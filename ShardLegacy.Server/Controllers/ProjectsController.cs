using Microsoft.AspNetCore.Mvc;
using ShardLegacy.Server.Models;
using ShardLegacy.Server.Services;
using System.Net.WebSockets;
using System.Text.Json;

namespace ShardLegacy.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProjectsController : ControllerBase
    {
        private readonly DeploymentService _svc;

        public ProjectsController(DeploymentService svc) => _svc = svc;

        [HttpGet]
        public ActionResult<IEnumerable<ProjectDeployment>> GetAll() => Ok(_svc.GetAll());

        [HttpGet("{id}")]
        public ActionResult<ProjectDeployment> GetById(string id)
        {
            var d = _svc.GetById(id);
            return d == null ? NotFound() : Ok(d);
        }

        [HttpPost("scan")]
        public async Task<ActionResult<ScanResult>> Scan([FromBody] ScanRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Source))
                return BadRequest(new { error = "Source is required." });
            return Ok(await _svc.ScanSource(req.Source));
        }

        /// <summary>
        /// Create deployment record immediately, run pipeline in background.
        /// Returns the deployment object with ID so client can connect SSE.
        /// </summary>
        [HttpPost("deploy")]
        public ActionResult<ProjectDeployment> Deploy([FromBody] DeployRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Source))
                return BadRequest(new { error = "Source is required." });

            // Register deployment synchronously — guaranteed available for SSE
            var deployment = _svc.CreateDeployment(req);

            // Run the pipeline in background
            _ = Task.Run(() => _svc.RunDeployment(deployment, req));

            return Ok(deployment);
        }

        /// <summary>
        /// SSE endpoint: streams real-time deployment logs.
        /// If deployment is already finished, replays stored logs.
        /// </summary>
        [HttpGet("{id}/logs/stream")]
        public async Task StreamLogs(string id, CancellationToken ct)
        {
            Response.ContentType = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["Connection"] = "keep-alive";
            Response.Headers["X-Accel-Buffering"] = "no";

            var jsonOpts = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var dep = _svc.GetById(id);

            // If already finished, replay stored logs
            if (dep != null && dep.Status is "running" or "failed" or "stopped")
            {
                foreach (var log in dep.Logs)
                {
                    var json = JsonSerializer.Serialize(log, jsonOpts);
                    await Response.WriteAsync($"data: {json}\n\n", ct);
                }
                await Response.WriteAsync(
                    "data: {\"level\":\"done\",\"message\":\"__STREAM_END__\"}\n\n", ct);
                await Response.Body.FlushAsync(ct);
                return;
            }

            // Live stream — subscribe to channel
            var channel = _svc.Subscribe(id);
            try
            {
                await foreach (var entry in channel.Reader.ReadAllAsync(ct))
                {
                    var json = JsonSerializer.Serialize(entry, jsonOpts);
                    await Response.WriteAsync($"data: {json}\n\n", ct);
                    await Response.Body.FlushAsync(ct);

                    if (entry.Level == "done") break;
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                _svc.Unsubscribe(id, channel);
            }
        }

        /// <summary>
        /// WebSocket endpoint: streams deployment logs over a WS connection.
        /// </summary>
        [HttpGet("{id}/logs/ws")]
        public async Task GetLogsWebSocket(string id)
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
                return;

            using var ws = await HttpContext.WebSockets.AcceptWebSocketAsync();

            var jsonOpts = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            // Start streaming container logs directly (no replay of pipeline logs)
            var channel = _svc.Subscribe(id);
            try
            {
                while (!HttpContext.RequestAborted.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    var hasItem = await channel.Reader.WaitToReadAsync(HttpContext.RequestAborted);
                    if (!hasItem) break;

                    while (channel.Reader.TryRead(out var entry))
                    {
                        var bytes = JsonSerializer.SerializeToUtf8Bytes(entry, jsonOpts);
                        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, HttpContext.RequestAborted);
                        if (entry.Level == "done")
                            break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                _svc.Unsubscribe(id, channel);
                if (ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
            }
        }

        [HttpPost("{id}/teardown")]
        public async Task<ActionResult> Teardown(string id)
        {
            return await _svc.Teardown(id) ? Ok(new { message = "Torn down." }) : NotFound();
        }

        [HttpDelete("{id}")]
        public ActionResult Delete(string id)
        {
            return _svc.Remove(id) ? NoContent() : NotFound();
        }

        [HttpGet("{id}/nginx-config")]
        public ActionResult GetNginxConfig(string id)
        {
            var cfg = _svc.GetNginxConfig(id);
            return cfg == null ? NotFound() : Ok(new { config = cfg });
        }
    }

    public class ScanRequest
    {
        public string Source { get; set; } = string.Empty;
    }
}
