using MongoDB.Driver;
using ShardLegacy.Server.Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;

namespace ShardLegacy.Server.Services
{
    public class DeploymentService
    {
        private static readonly List<ProjectDeployment> _deployments = new();
        private static int _nextPort = 9001;
        private static readonly object _lock = new();
        private static bool _nginxRunning = false;

        private static readonly ConcurrentDictionary<string, List<Channel<LogEntry>>> _logChannels = new();
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> _logStreamTokens = new();

        private static readonly string[] Adjectives =
        {
            "nimbus", "electron", "stellar", "quantum", "nebula",
            "photon", "vertex", "cipher", "prism", "aurora",
            "zenith", "helix", "cobalt", "ember", "frost",
            "onyx", "pulse", "sigma", "vortex", "apex",
            "nova", "echo", "flux", "ion", "lynx"
        };

        private static readonly string[] StageNames = { "clone", "environment", "build", "deploy", "healthcheck", "proxy" };
        private static readonly string[] StageLabels = { "Clone Repository", "Environment Setup", "Docker Build", "Container Deploy", "Health Check", "Nginx Proxy" };

        private readonly string _workspacePath;
        private readonly string _nginxConfigPath;
        private readonly string _nginxMainConf;
        private readonly ILogger<DeploymentService> _logger;
        private readonly MongoDbService? _mongo;
        private readonly SemaphoreSlim _persistLock = new(1, 1);

        private const string NginxContainerName = "shard-nginx-proxy";

        public DeploymentService(ILogger<DeploymentService> logger, IConfiguration config, MongoDbService? mongo = null)
        {
            _logger = logger;
            _mongo = mongo;

            _workspacePath = config.GetValue<string>("Deployment:WorkspacePath")
                ?? Path.Combine(Path.GetTempPath(), "shardlegacy-deployments");
            _nginxConfigPath = Path.Combine(Path.GetTempPath(), "shardlegacy-nginx", "conf.d");
            _nginxMainConf = Path.Combine(Path.GetTempPath(), "shardlegacy-nginx");

            Directory.CreateDirectory(_workspacePath);
            Directory.CreateDirectory(_nginxConfigPath);

            // Write the main nginx.conf
            WriteMainNginxConf();
        }

        public async Task InitializeAsync()
        {
            if (_mongo?.IsConnected != true) return;

            try
            {
                var saved = await _mongo.Deployments!.Find(_ => true).ToListAsync();
                lock (_lock)
                {
                    _deployments.Clear();
                    _deployments.AddRange(saved);

                    var maxPort = _deployments.Any() ? _deployments.Max(d => d.AssignedPort) : 0;
                    if (maxPort >= _nextPort) _nextPort = maxPort + 1;
                }

                _logger.LogInformation("Loaded {Count} deployments from MongoDB.", _deployments.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load deployments from MongoDB.");
            }
        }

        private async Task PersistDeploymentAsync(ProjectDeployment dep)
        {
            if (_mongo?.IsConnected != true) return;

            await _persistLock.WaitAsync();
            try
            {
                await _mongo.Deployments!.ReplaceOneAsync(
                    d => d.Id == dep.Id,
                    dep,
                    new ReplaceOptions { IsUpsert = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist deployment {Id}.", dep.Id);
            }
            finally
            {
                _persistLock.Release();
            }
        }

        // ── Public API ─────────────────────────────────────────────────

        public List<ProjectDeployment> GetAll() => _deployments.OrderByDescending(d => d.DeployedAt).ToList();
        public ProjectDeployment? GetById(string id) => _deployments.FirstOrDefault(d => d.Id == id);

        /// <summary>
        /// Creates and registers a deployment, returns it immediately.
        /// Call RunDeployment() afterward in a background task.
        /// </summary>
        public ProjectDeployment CreateDeployment(DeployRequest request)
        {
            var deployment = new ProjectDeployment
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                Source = request.Source,
                SourceType = IsGitHubUrl(request.Source) ? "github" : "local",
                DeployedAt = DateTime.UtcNow,
                Status = "deploying",
                EnvironmentVariables = request.EnvironmentVariables ?? new(),
                Stages = new List<DeploymentStage>()
            };

            for (int i = 0; i < StageNames.Length; i++)
            {
                deployment.Stages.Add(new DeploymentStage
                {
                    Order = i,
                    Name = StageNames[i],
                    Label = StageLabels[i],
                    Status = "pending"
                });
            }

            _deployments.Add(deployment);
            _ = PersistDeploymentAsync(deployment);
            return deployment;
        }

        /// <summary>
        /// Runs the full deployment pipeline. Call in a background task.
        /// </summary>
        public async Task RunDeployment(ProjectDeployment deployment, DeployRequest request)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                // ── STAGE 0: Clone / Scan ──────────────────────────────
                SetStage(deployment, 0, "running");
                await Emit(deployment, "step", "Scanning project source...", "clone");

                var scan = await ScanSource(request.Source);
                if (!scan.Success)
                {
                    await Emit(deployment, "error", scan.Error, "clone");
                    SetStage(deployment, 0, "failed");
                    deployment.Status = "failed";
                    await Finish(deployment, sw);
                    return;
                }

                var projectName = (request.ProjectName ?? scan.ProjectName)
                    .ToLowerInvariant().Replace(" ", "-").Replace(".", "-");
                deployment.ProjectName = projectName;

                var selectedFile = request.SelectedFile ?? scan.DetectedFiles.First();
                deployment.DetectedFile = selectedFile;

                await Emit(deployment, "info", $"Project: {projectName}", "clone");
                await Emit(deployment, "info", $"Framework: {scan.DetectedFramework}", "clone");
                await Emit(deployment, "info", $"Docker file: {selectedFile}", "clone");
                await Emit(deployment, "success", "Source analyzed successfully.", "clone");
                SetStage(deployment, 0, "completed");

                var projectPath = scan.LocalPath;
                var isCompose = selectedFile.Contains("compose", StringComparison.OrdinalIgnoreCase);

                // ── STAGE 1: Environment ───────────────────────────────
                SetStage(deployment, 1, "running");
                await Emit(deployment, "step", "Configuring environment...", "environment");

                var adjective = Adjectives[Random.Shared.Next(Adjectives.Length)];
                var subdomain = $"{projectName}.localhost";
                deployment.Subdomain = subdomain;
                deployment.ImageName = $"shardlegacy/{projectName}:latest";

                int port;
                lock (_lock) { port = _nextPort++; }
                deployment.AssignedPort = port;
                deployment.FullUrl = $"http://{subdomain}";
                deployment.DirectUrl = $"http://localhost:{port}";

                await Emit(deployment, "info", $"Subdomain: {subdomain}", "environment");
                await Emit(deployment, "info", $"Container port: {port}", "environment");

                if (deployment.EnvironmentVariables.Count > 0)
                {
                    var envContent = string.Join("\n",
                        deployment.EnvironmentVariables.Select(e => $"{e.Key}={e.Value}"));
                    await File.WriteAllTextAsync(Path.Combine(projectPath, ".env"), envContent);
                    await Emit(deployment, "info",
                        $"Injected {deployment.EnvironmentVariables.Count} env var(s).", "environment");
                }

                await Emit(deployment, "success", "Environment ready.", "environment");
                SetStage(deployment, 1, "completed");

                // ── STAGE 2: Docker Build ──────────────────────────────
                SetStage(deployment, 2, "running");
                await Emit(deployment, "step", "Building Docker image...", "build");

                if (isCompose)
                {
                    var composePath = Path.Combine(projectPath, selectedFile);
                    var composeDir = Path.GetDirectoryName(composePath) ?? projectPath;

                    await Emit(deployment, "info", "docker compose build", "build");
                    var build = await RunCmd("docker",
                        $"compose -f \"{composePath}\" -p {projectName} build", composeDir);

                    if (build.ExitCode != 0)
                    {
                        await Emit(deployment, "error", $"Build failed: {Tail(build.StdErr)}", "build");
                        SetStage(deployment, 2, "failed");
                        deployment.Status = "failed";
                        await Finish(deployment, sw);
                        return;
                    }
                    await Emit(deployment, "success", "Compose images built.", "build");
                }
                else
                {
                    var dockerfilePath = Path.Combine(projectPath, selectedFile);
                    var ctx = Path.GetDirectoryName(dockerfilePath) ?? projectPath;

                    await Emit(deployment, "info",
                        $"docker build -t {deployment.ImageName}", "build");
                    var build = await RunCmd("docker",
                        $"build -t {deployment.ImageName} -f \"{dockerfilePath}\" \"{ctx}\"");

                    if (build.ExitCode != 0)
                    {
                        await Emit(deployment, "error", $"Build failed: {Tail(build.StdErr)}", "build");
                        SetStage(deployment, 2, "failed");
                        deployment.Status = "failed";
                        await Finish(deployment, sw);
                        return;
                    }
                    await Emit(deployment, "success", "Image built successfully.", "build");
                }
                SetStage(deployment, 2, "completed");

                // ── STAGE 3: Container Deploy ──────────────────────────
                SetStage(deployment, 3, "running");
                await Emit(deployment, "step", "Starting container...", "deploy");

                if (isCompose)
                {
                    var composePath = Path.Combine(projectPath, selectedFile);
                    var composeDir = Path.GetDirectoryName(composePath) ?? projectPath;

                    var up = await RunCmd("docker",
                        $"compose -f \"{composePath}\" -p {projectName} up -d", composeDir);

                    if (up.ExitCode != 0)
                    {
                        await Emit(deployment, "error", $"Start failed: {Tail(up.StdErr)}", "deploy");
                        SetStage(deployment, 3, "failed");
                        deployment.Status = "failed";
                        await Finish(deployment, sw);
                        return;
                    }
                    deployment.ContainerId = $"{projectName}-compose";
                    await Emit(deployment, "success", "Compose stack started.", "deploy");
                }
                else
                {
                    var cn = $"shard-{projectName}-{deployment.Id}";
                    await Emit(deployment, "info", $"Container: {cn}", "deploy");

                    // Parse Dockerfile to detect exposed port
                    var dockerfilePath = Path.Combine(projectPath, selectedFile);
                    var detectedPort = ParseDockerfilePort(dockerfilePath);
                    
                    // Fallback to common ports if detection fails
                    var portsToTry = detectedPort.HasValue
                        ? new[] { detectedPort.Value }.Concat(new[] { 80, 8080, 3000, 5000 }).Distinct().ToArray()
                        : new[] { 80, 8080, 3000, 5000 };

                    if (detectedPort.HasValue)
                        await Emit(deployment, "info", $"Detected port in Dockerfile: {detectedPort}", "deploy");

                    string? containerId = null;
                    int? actualInternalPort = null;

                    foreach (var iPort in portsToTry)
                    {
                        var run = await RunCmd("docker",
                            $"run -d --name {cn} -p {port}:{iPort} {deployment.ImageName}");

                        if (run.ExitCode == 0)
                        {
                            containerId = run.StdOut.Trim();
                            if (containerId.Length > 12) containerId = containerId[..12];
                            actualInternalPort = iPort;
                            await Emit(deployment, "info",
                                $"Mapped external :{port} -> internal :{iPort}", "deploy");
                            break;
                        }

                        // Clean up failed attempt
                        await RunCmd("docker", $"rm -f {cn}");
                        
                        if (iPort == detectedPort)
                            await Emit(deployment, "warn",
                                $"Detected port {iPort} failed. Trying fallback ports...", "deploy");
                        else
                            await Emit(deployment, "warn",
                                $"Port {iPort} failed, trying next...", "deploy");
                    }

                    if (containerId == null)
                    {
                        await Emit(deployment, "error", "Could not start container on any port.", "deploy");
                        SetStage(deployment, 3, "failed");
                        deployment.Status = "failed";
                        await Finish(deployment, sw);
                        return;
                    }

                    deployment.ContainerId = containerId;
                    deployment.InternalPort = actualInternalPort ?? 0;
                    await Emit(deployment, "success", $"Container running: {containerId}", "deploy");
                }
                SetStage(deployment, 3, "completed");

                // ── STAGE 4: Health Check ──────────────────────────────
                SetStage(deployment, 4, "running");
                await Emit(deployment, "step", "Running health checks...", "healthcheck");

                var healthy = false;
                using var http = new HttpClient(new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                })
                { Timeout = TimeSpan.FromSeconds(5) };

                for (int i = 1; i <= 6; i++)
                {
                    await Emit(deployment, "info", $"Attempt {i}/6...", "healthcheck");
                    await Task.Delay(2000);

                    try
                    {
                        var resp = await http.GetAsync($"http://localhost:{port}");
                        if ((int)resp.StatusCode < 500)
                        {
                            healthy = true;
                            await Emit(deployment, "success",
                                $"HTTP {(int)resp.StatusCode} — container is responding.", "healthcheck");
                            break;
                        }
                        await Emit(deployment, "warn",
                            $"HTTP {(int)resp.StatusCode}, retrying...", "healthcheck");
                    }
                    catch
                    {
                        await Emit(deployment, "warn", "Not ready yet...", "healthcheck");
                    }
                }

                if (!healthy)
                    await Emit(deployment, "warn",
                        "Health checks inconclusive. Container may need more startup time.", "healthcheck");

                SetStage(deployment, 4, "completed");

                // ── STAGE 5: Nginx Proxy ───────────────────────────────
                SetStage(deployment, 5, "running");
                await Emit(deployment, "step", "Setting up reverse proxy...", "proxy");

                await EnsureNginxRunning(deployment);
                WriteNginxSiteConfig(deployment);
                await ReloadNginx(deployment);

                await Emit(deployment, "success",
                    $"Reverse proxy configured: {subdomain}", "proxy");
                SetStage(deployment, 5, "completed");

                // ── DONE ───────────────────────────────────────────────
                deployment.Status = "running";
                deployment.CompletedAt = DateTime.UtcNow;
                sw.Stop();
                deployment.DurationMs = sw.ElapsedMilliseconds;

                await Emit(deployment, "success",
                    $"Deployment complete in {FmtMs(deployment.DurationMs)}.", "");
                await Emit(deployment, "info", $"Site URL: {deployment.FullUrl}", "");
                await Emit(deployment, "info", $"Direct:   {deployment.DirectUrl}", "");
                
                // Signal client to open WebSocket for live container logs
                await Emit(deployment, "info", "🔌 Opening live container log stream...", "logs");

                // Start container log streaming in background
                _ = Task.Run(() => StreamContainerLogsAsync(deployment));
            }
            catch (Exception ex)
            {
                deployment.Status = "failed";
                await Emit(deployment, "error", $"Unexpected error: {ex.Message}", "");
                _logger.LogError(ex, "Deploy failed: {Source}", request.Source);
            }

            await Finish(deployment, sw);
        }

        // ── Nginx Management ───────────────────────────────────────────

        private void WriteMainNginxConf()
        {
            var mainConf = @"
worker_processes auto;
events { worker_connections 1024; }

http {
    include       /etc/nginx/mime.types;
    default_type  application/octet-stream;

    sendfile    on;
    keepalive_timeout 65;

    # Increase buffer for proxied responses
    proxy_buffer_size   128k;
    proxy_buffers       4 256k;
    proxy_busy_buffers_size 256k;

    # Default server — returns 404 for unknown subdomains
    server {
        listen 80 default_server;
        server_name _;
        return 404;
    }

    # Include all site configs
    include /etc/nginx/conf.d/*.conf;
}
";
            File.WriteAllText(Path.Combine(_nginxMainConf, "nginx.conf"), mainConf.Trim());
        }

        private void WriteNginxSiteConfig(ProjectDeployment dep)
        {
            var config = $@"# ShardLegacy: {dep.ProjectName} ({dep.Id})
server {{
    listen 80;
    server_name {dep.Subdomain};

    location / {{
        proxy_pass         http://host.docker.internal:{dep.AssignedPort};
        proxy_set_header   Host $host;
        proxy_set_header   X-Real-IP $remote_addr;
        proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
        proxy_http_version 1.1;
        proxy_set_header   Upgrade $http_upgrade;
        proxy_set_header   Connection ""upgrade"";
        proxy_read_timeout 86400;
    }}
}}
";
            File.WriteAllText(Path.Combine(_nginxConfigPath, $"{dep.ProjectName}.conf"), config);
        }

        private async Task EnsureNginxRunning(ProjectDeployment dep)
        {
            if (_nginxRunning)
            {
                await Emit(dep, "info", "Nginx proxy already running.", "proxy");
                return;
            }

            // Check if container already exists
            var inspect = await RunCmd("docker", $"inspect {NginxContainerName}");
            if (inspect.ExitCode == 0 && inspect.StdOut.Contains("\"Running\": true"))
            {
                _nginxRunning = true;
                await Emit(dep, "info", "Nginx proxy container found.", "proxy");
                return;
            }

            // Remove stale container if exists
            await RunCmd("docker", $"rm -f {NginxContainerName}");

            await Emit(dep, "info", "Starting Nginx proxy container on port 80...", "proxy");

            var confDir = _nginxMainConf.Replace("\\", "/");
            var siteDir = _nginxConfigPath.Replace("\\", "/");

            var run = await RunCmd("docker",
                $"run -d " +
                $"--name {NginxContainerName} " +
                $"-p 80:80 " +
                $"-v \"{confDir}/nginx.conf:/etc/nginx/nginx.conf:ro\" " +
                $"-v \"{siteDir}:/etc/nginx/conf.d:ro\" " +
                $"--add-host=host.docker.internal:host-gateway " +
                $"nginx:alpine");

            if (run.ExitCode != 0)
            {
                await Emit(dep, "warn",
                    $"Could not start Nginx: {Tail(run.StdErr)}. " +
                    $"Sites still accessible via direct URL.", "proxy");
                return;
            }

            _nginxRunning = true;
            await Emit(dep, "success", "Nginx proxy container started on port 80.", "proxy");
        }

        private async Task ReloadNginx(ProjectDeployment dep)
        {
            if (!_nginxRunning) return;

            var reload = await RunCmd("docker", $"exec {NginxContainerName} nginx -s reload");
            if (reload.ExitCode != 0)
            {
                await Emit(dep, "warn", $"Nginx reload failed: {Tail(reload.StdErr)}", "proxy");
            }
            else
            {
                await Emit(dep, "info", "Nginx reloaded with new config.", "proxy");
            }
        }

        private void RemoveNginxSiteConfig(string projectName)
        {
            var path = Path.Combine(_nginxConfigPath, $"{projectName}.conf");
            if (File.Exists(path)) File.Delete(path);
        }

        // ── Container Log Streaming ────────────────────────────────────

        private async Task StreamContainerLogsAsync(ProjectDeployment deployment)
        {
            var cts = new CancellationTokenSource();
            if (!_logStreamTokens.TryAdd(deployment.Id, cts))
            {
                // Another log streamer is already running
                cts.Dispose();
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"logs -f {deployment.ContainerId}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = new Process { StartInfo = psi };
                proc.Start();

                using var reader = proc.StandardOutput;
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    // Check if token is cancelled and not disposed
                    if (_logStreamTokens.TryGetValue(deployment.Id, out var tokenSource))
                    {
                        if (tokenSource.IsCancellationRequested) break;
                    }
                    else
                    {
                        // Token source was removed/disposed
                        break;
                    }
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        await Emit(deployment, "log", line, "logs");
                    }
                }

                // Wait for process to complete
                if (_logStreamTokens.TryGetValue(deployment.Id, out var waitTokenSource))
                {
                    await proc.WaitForExitAsync(waitTokenSource.Token);
                }
                else
                {
                    await proc.WaitForExitAsync();
                }
            }
            catch (OperationCanceledException)
            {
                // Streaming stopped
            }
            catch (ObjectDisposedException)
            {
                // Token was disposed, safe to ignore
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Container log streaming failed for {Id}", deployment.Id);
            }
            finally
            {
                _logStreamTokens.TryRemove(deployment.Id, out var removedCts);
                // Only dispose if this is the same instance
                removedCts?.Dispose();
            }
        }

        private void StopContainerLogStreaming(string deploymentId)
        {
            if (_logStreamTokens.TryRemove(deploymentId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        // ── Scan ───────────────────────────────────────────────────────

        public async Task<ScanResult> ScanSource(string source)
        {
            var result = new ScanResult();
            try
            {
                string localPath;

                if (IsGitHubUrl(source))
                {
                    var repoName = ExtractRepoName(source);
                    var id = Guid.NewGuid().ToString("N")[..8];
                    localPath = Path.Combine(_workspacePath, $"{repoName}-{id}");

                    var clone = await RunCmd("git", $"clone --depth 1 \"{source}\" \"{localPath}\"");
                    if (clone.ExitCode != 0)
                    {
                        result.Error = $"Clone failed: {Tail(clone.StdErr)}";
                        return result;
                    }
                    result.ProjectName = repoName;
                }
                else
                {
                    localPath = source.Trim('"').Trim();
                    if (!Directory.Exists(localPath))
                    {
                        result.Error = $"Directory not found: {localPath}";
                        return result;
                    }
                    result.ProjectName = new DirectoryInfo(localPath).Name;
                }

                result.DetectedFramework = DetectFramework(localPath);

                var files = new List<string>();
                var patterns = new[] { "Dockerfile", "dockerfile", "docker-compose.yml",
                    "docker-compose.yaml", "compose.yml", "compose.yaml" };

                foreach (var p in patterns)
                {
                    files.AddRange(Directory.GetFiles(localPath, p, SearchOption.TopDirectoryOnly)
                        .Select(f => Path.GetRelativePath(localPath, f)));
                }

                foreach (var dir in Directory.GetDirectories(localPath))
                {
                    var dn = new DirectoryInfo(dir).Name;
                    if (dn.StartsWith(".") || dn == "node_modules" || dn == "bin" || dn == "obj") continue;
                    foreach (var p in new[] { "Dockerfile", "dockerfile" })
                    {
                        files.AddRange(Directory.GetFiles(dir, p, SearchOption.TopDirectoryOnly)
                            .Select(f => Path.GetRelativePath(localPath, f)));
                    }
                }

                result.DetectedFiles = files.Distinct().ToList();
                result.LocalPath = localPath;
                result.Success = result.DetectedFiles.Count > 0;
                if (!result.Success) result.Error = "No Dockerfile or compose file found.";
            }
            catch (Exception ex)
            {
                result.Error = $"Scan error: {ex.Message}";
                _logger.LogError(ex, "Scan failed: {S}", source);
            }
            return result;
        }

        // ── Teardown / Remove ──────────────────────────────────────────

        public async Task<bool> Teardown(string id)
        {
            var dep = GetById(id);
            if (dep == null) return false;

            try
            {
                // Stop log streaming
                StopContainerLogStreaming(id);

                if (dep.DetectedFile.Contains("compose", StringComparison.OrdinalIgnoreCase))
                    await RunCmd("docker", $"compose -p {dep.ProjectName} down");
                else
                {
                    var cn = $"shard-{dep.ProjectName}-{dep.Id}";
                    await RunCmd("docker", $"stop {cn}");
                    await RunCmd("docker", $"rm {cn}");
                }

                RemoveNginxSiteConfig(dep.ProjectName);
                if (_nginxRunning)
                    await RunCmd("docker", $"exec {NginxContainerName} nginx -s reload");

                dep.Status = "stopped";
                _ = PersistDeploymentAsync(dep);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Teardown failed: {Id}", id);
                return false;
            }
        }

        public bool Remove(string id)
        {
            var dep = GetById(id);
            if (dep == null) return false;
            _deployments.Remove(dep);

            if (_mongo?.IsConnected == true)
            {
                _ = _mongo.Deployments!.DeleteOneAsync(d => d.Id == id);
            }

            return true;
        }

        public string? GetNginxConfig(string id)
        {
            var dep = GetById(id);
            if (dep == null) return null;
            var f = Path.Combine(_nginxConfigPath, $"{dep.ProjectName}.conf");
            return File.Exists(f) ? File.ReadAllText(f) : null;
        }

        // ── SSE Log Streaming ──────────────────────────────────────────

        public Channel<LogEntry> Subscribe(string deploymentId)
        {
            var ch = Channel.CreateUnbounded<LogEntry>();
            var list = _logChannels.GetOrAdd(deploymentId, _ => new List<Channel<LogEntry>>());
            lock (list) { list.Add(ch); }

            // --- Ensure log streaming is running for active containers ---
            var dep = GetById(deploymentId);
            if (dep != null && dep.Status == "running" && !string.IsNullOrEmpty(dep.ContainerId))
            {
                // If not already streaming logs for this deployment, start it
                if (!_logStreamTokens.ContainsKey(deploymentId))
                {
                    _ = Task.Run(() => StreamContainerLogsAsync(dep));
                }
            }

            return ch;
        }

        public void Unsubscribe(string deploymentId, Channel<LogEntry> ch)
        {
            if (_logChannels.TryGetValue(deploymentId, out var list))
                lock (list) { list.Remove(ch); }
        }

        private async Task Emit(ProjectDeployment dep, string level, string msg, string stage)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = level,
                Message = msg,
                Stage = stage
            };
            dep.Logs.Add(entry);

            // Also emit stage snapshot
            if (level == "step" || level == "success" || level == "error")
            {
                var stageEntry = new LogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    Level = "stage_update",
                    Message = System.Text.Json.JsonSerializer.Serialize(dep.Stages),
                    Stage = "_system"
                };
                dep.Logs.Add(stageEntry);
                await Broadcast(dep.Id, stageEntry);
            }

            await Broadcast(dep.Id, entry);
            await Task.Delay(30);
        }

        private async Task Broadcast(string depId, LogEntry entry)
        {
            if (!_logChannels.TryGetValue(depId, out var list)) return;
            List<Channel<LogEntry>> snapshot;
            lock (list) { snapshot = list.ToList(); }
            foreach (var ch in snapshot)
                await ch.Writer.WriteAsync(entry);
        }

        private async Task Finish(ProjectDeployment dep, Stopwatch sw)
        {
            if (sw.IsRunning) { sw.Stop(); dep.DurationMs = sw.ElapsedMilliseconds; }
            dep.CompletedAt ??= DateTime.UtcNow;

            var end = new LogEntry { Level = "done", Message = "__STREAM_END__", Stage = "_system" };
            await Broadcast(dep.Id, end);

            _ = PersistDeploymentAsync(dep);

            if (_logChannels.TryGetValue(dep.Id, out var list))
                lock (list) { foreach (var ch in list) ch.Writer.TryComplete(); }
        }

        // ── Helpers ────────────────────────────────────────────────────

        private void SetStage(ProjectDeployment dep, int i, string status)
        {
            var s = dep.Stages[i];
            s.Status = status;
            if (status == "running") s.StartedAt = DateTime.UtcNow;
            if (status is "completed" or "failed")
            {
                s.CompletedAt = DateTime.UtcNow;
                if (s.StartedAt.HasValue)
                    s.DurationMs = (long)(s.CompletedAt.Value - s.StartedAt.Value).TotalMilliseconds;
            }

            _ = PersistDeploymentAsync(dep);
        }

        private static int? ParseDockerfilePort(string dockerfilePath)
        {
            try
            {
                if (!File.Exists(dockerfilePath)) return null;

                var lines = File.ReadAllLines(dockerfilePath);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    
                    // Match EXPOSE instruction
                    if (trimmed.StartsWith("EXPOSE", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            // Extract port number (handle "EXPOSE 5000" or "EXPOSE 5000/tcp")
                            var portStr = parts[1].Split('/')[0];
                            if (int.TryParse(portStr, out var port) && port > 0 && port < 65536)
                                return port;
                        }
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string DetectFramework(string path)
        {
            if (File.Exists(Path.Combine(path, "package.json")))
            {
                var pkg = File.ReadAllText(Path.Combine(path, "package.json"));
                if (pkg.Contains("\"next\"")) return "Next.js";
                if (pkg.Contains("\"react\"")) return "React";
                if (pkg.Contains("\"vue\"")) return "Vue.js";
                if (pkg.Contains("\"express\"")) return "Express.js";
                if (pkg.Contains("\"nuxt\"")) return "Nuxt.js";
                return "Node.js";
            }
            if (File.Exists(Path.Combine(path, "requirements.txt")) || File.Exists(Path.Combine(path, "Pipfile")))
                return File.Exists(Path.Combine(path, "manage.py")) ? "Django" : "Flask/Python";
            if (Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories).Length > 0) return "ASP.NET";
            if (File.Exists(Path.Combine(path, "go.mod"))) return "Go";
            if (File.Exists(Path.Combine(path, "Cargo.toml"))) return "Rust";
            if (File.Exists(Path.Combine(path, "pom.xml"))) return "Java/Spring";
            return "Unknown";
        }

        private static bool IsGitHubUrl(string s) =>
            s.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("http://github.com/", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase);

        private static string ExtractRepoName(string url)
        {
            var name = url.TrimEnd('/').Split('/').Last();
            return name.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        }

        private static string Tail(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "(no output)";
            var lines = s.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            return string.Join("\n", lines.TakeLast(4));
        }

        private static string FmtMs(long ms)
        {
            if (ms < 1000) return $"{ms}ms";
            if (ms < 60000) return $"{ms / 1000.0:F1}s";
            return $"{ms / 60000}m {(ms % 60000) / 1000}s";
        }

        private static async Task<ProcResult> RunCmd(string file, string args, string? cwd = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (cwd != null) psi.WorkingDirectory = cwd;

            using var proc = new Process { StartInfo = psi };
            proc.Start();
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            return new ProcResult { ExitCode = proc.ExitCode, StdOut = stdout, StdErr = stderr };
        }
    }

    public class ProcResult
    {
        public int ExitCode { get; set; }
        public string StdOut { get; set; } = "";
        public string StdErr { get; set; } = "";
    }
}
