import { useState, useEffect, useRef, useCallback } from 'react';

const STAGE_ICONS = {
    pending: <circle cx="6" cy="6" r="3" fill="currentColor" opacity="0.3" />,
    running: <><circle cx="6" cy="6" r="3" fill="none" stroke="currentColor" strokeWidth="1.5" /><circle cx="6" cy="6" r="1.5" fill="currentColor" /></>,
    completed: <polyline points="3,6 5.5,8.5 9,4" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />,
    failed: <><line x1="4" y1="4" x2="8" y2="8" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" /><line x1="8" y1="4" x2="4" y2="8" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" /></>,
};

function Pipeline({ stages }) {
    if (!stages || stages.length === 0) return null;
    return (
        <div className="pipeline">
            {stages.map((s, i) => (
                <div key={s.name} style={{ display: 'flex', alignItems: 'stretch', flex: 1, gap: 2 }}>
                    <div className="pipe-stage" data-status={s.status}>
                        <div className="pipe-icon">
                            {s.status === 'running' ? (
                                <div className="spinner spinner-sm" />
                            ) : (
                                <svg viewBox="0 0 12 12">{STAGE_ICONS[s.status] || STAGE_ICONS.pending}</svg>
                            )}
                        </div>
                        <div className="pipe-label">{s.label}</div>
                        {s.durationMs > 0 && <div className="pipe-time">{fmtMs(s.durationMs)}</div>}
                    </div>
                    {i < stages.length - 1 && (
                        <div className={`pipe-connector ${s.status === 'completed' ? 'done' : ''}`} />
                    )}
                </div>
            ))}
        </div>
    );
}

function LogTerminal({ logs, streaming }) {
    const endRef = useRef(null);
    useEffect(() => { endRef.current?.scrollIntoView({ behavior: 'smooth' }); }, [logs.length]);

    const visible = logs.filter(l => l.level !== 'stage_update' && l.level !== 'done' && l.stage !== '_system');

    return (
        <div className="terminal">
            {visible.map((l, i) => (
                <div key={i} className="terminal-line">
                    <span className="terminal-time">{fmtTime(l.timestamp)}</span>
                    <span className={`terminal-msg ${l.level}`}>{l.message}</span>
                </div>
            ))}
            {streaming && visible.length === 0 && (
                <div className="terminal-line">
                    <span className="terminal-time" />
                    <span className="terminal-msg info" style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                        <div className="spinner spinner-sm" /> Waiting for output...
                    </span>
                </div>
            )}
            <div ref={endRef} />
        </div>
    );
}

export default function Deploy() {
    const [source, setSource] = useState('');
    const [scanResult, setScanResult] = useState(null);
    const [scanning, setScanning] = useState(false);
    const [deploying, setDeploying] = useState(false);
    const [selectedFile, setSelectedFile] = useState('');
    const [projectName, setProjectName] = useState('');
    const [envVars, setEnvVars] = useState([]);

    const [activeDeployment, setActiveDeployment] = useState(null);
    const [liveLogs, setLiveLogs] = useState([]);
    const [liveStages, setLiveStages] = useState([]);
    const [streaming, setStreaming] = useState(false);

    const [deployments, setDeployments] = useState([]);
    const [nginxConfig, setNginxConfig] = useState(null);

    const eventSourceRef = useRef(null);

    const fetchDeployments = useCallback(() => {
        fetch('/api/projects').then(r => r.ok ? r.json() : []).then(setDeployments).catch(() => { });
    }, []);

    useEffect(() => { fetchDeployments(); }, [fetchDeployments]);

    // Clean up SSE on unmount
    useEffect(() => { return () => { eventSourceRef.current?.close(); }; }, []);

    const connectSSE = useCallback((deployId) => {
        eventSourceRef.current?.close();
        setLiveLogs([]);
        setLiveStages([]);
        setStreaming(true);

        const es = new EventSource(`/api/projects/${deployId}/logs/stream`);
        eventSourceRef.current = es;

        es.onmessage = (ev) => {
            try {
                const entry = JSON.parse(ev.data);

                if (entry.level === 'done') {
                    es.close();
                    setStreaming(false);
                    fetchDeployments();
                    fetch(`/api/projects/${deployId}`).then(r => r.ok ? r.json() : null).then(d => {
                        if (d) setActiveDeployment(d);
                    });
                    return;
                }

                if (entry.level === 'stage_update') {
                    try { setLiveStages(JSON.parse(entry.message)); } catch { }
                    return;
                }

                setLiveLogs(prev => [...prev, entry]);
            } catch { }
        };

        es.onerror = () => {
            es.close();
            setStreaming(false);
            fetchDeployments();
        };
    }, [fetchDeployments]);

    const handleScan = () => {
        if (!source.trim()) return;
        setScanning(true);
        setScanResult(null);
        setSelectedFile('');
        setProjectName('');

        fetch('/api/projects/scan', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ source: source.trim() })
        })
            .then(r => r.json())
            .then(data => {
                setScanResult(data);
                if (data.detectedFiles?.length > 0) setSelectedFile(data.detectedFiles[0]);
                if (data.projectName) setProjectName(data.projectName);
                setScanning(false);
            })
            .catch(() => { setScanResult({ success: false, error: 'Network error.' }); setScanning(false); });
    };

    const handleDeploy = () => {
        if (!source.trim() || !selectedFile) return;
        setDeploying(true);

        const body = {
            source: source.trim(),
            projectName: projectName || undefined,
            selectedFile,
            environmentVariables: envVars.filter(e => e.key.trim()).map(e => ({
                key: e.key.trim(), value: e.value, isSecret: e.isSecret || false
            }))
        };
        if (body.environmentVariables.length === 0) delete body.environmentVariables;

        fetch('/api/projects/deploy', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        })
            .then(r => r.json())
            .then(data => {
                setDeploying(false);
                setScanResult(null);
                setActiveDeployment(data);
                setLiveStages(data.stages || []);
                if (data.id) connectSSE(data.id);
            })
            .catch(() => setDeploying(false));
    };

    const handleTeardown = (id) => {
        fetch(`/api/projects/${id}/teardown`, { method: 'POST' }).then(() => fetchDeployments());
    };

    const handleRemove = (id) => {
        fetch(`/api/projects/${id}`, { method: 'DELETE' }).then(() => {
            if (activeDeployment?.id === id) { setActiveDeployment(null); setLiveLogs([]); setLiveStages([]); }
            fetchDeployments();
        });
    };

    const showLogs = (dep) => {
        eventSourceRef.current?.close();
        setActiveDeployment(dep);
        setLiveLogs(dep.logs || []);
        setLiveStages(dep.stages || []);
        if (dep.status === 'deploying') {
            connectSSE(dep.id);
        } else {
            setStreaming(false);
        }
    };

    const viewNginx = (id) => {
        fetch(`/api/projects/${id}/nginx-config`).then(r => r.ok ? r.json() : null).then(d => setNginxConfig(d?.config));
    };

    const addEnvVar = () => setEnvVars(prev => [...prev, { key: '', value: '', isSecret: false }]);
    const removeEnvVar = (i) => setEnvVars(prev => prev.filter((_, idx) => idx !== i));
    const updateEnvVar = (i, field, val) => setEnvVars(prev => prev.map((e, idx) => idx === i ? { ...e, [field]: val } : e));

    const displayStages = liveStages.length > 0 ? liveStages : (activeDeployment?.stages || []);

    return (
        <div>
            {/* ── DEPLOY FORM ──────────────────────────────────────── */}
            <div className="deploy-form">
                <div className="deploy-form-title">Deploy a Project</div>
                <div className="deploy-form-desc">
                    Enter a GitHub URL or local folder path. ShardLegacy scans for Docker files,
                    builds the image, deploys a container, and routes it to <code>projectname.localhost</code>.
                </div>

                <div className="form-row">
                    <input
                        className="input" style={{ flex: 1 }}
                        placeholder="https://github.com/user/repo  or  C:\path\to\project"
                        value={source} onChange={e => setSource(e.target.value)}
                        onKeyDown={e => e.key === 'Enter' && handleScan()}
                        id="source-input"
                    />
                    <button className="btn btn-primary" onClick={handleScan} disabled={scanning || !source.trim()} id="scan-btn">
                        {scanning ? <><div className="spinner spinner-sm" /> Scanning</> : 'Scan'}
                    </button>
                </div>

                {scanResult && (
                    <div className="scan-result">
                        {scanResult.success ? (
                            <>
                                <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 10 }}>
                                    <span className="badge badge-green"><span className="badge-dot" />
                                        {scanResult.detectedFiles.length} Docker file{scanResult.detectedFiles.length > 1 ? 's' : ''} found
                                    </span>
                                    {scanResult.detectedFramework && (
                                        <span className="badge badge-purple">{scanResult.detectedFramework}</span>
                                    )}
                                </div>

                                <div style={{ marginBottom: 10 }}>
                                    <span className="form-label">Select Docker file</span>
                                    <div style={{ display: 'flex', gap: 4, flexWrap: 'wrap' }}>
                                        {scanResult.detectedFiles.map(f => (
                                            <button key={f}
                                                className={`btn btn-sm ${selectedFile === f ? 'btn-primary' : ''}`}
                                                onClick={() => setSelectedFile(f)}>{f}</button>
                                        ))}
                                    </div>
                                </div>

                                <div className="form-row">
                                    <div style={{ flex: 1 }}>
                                        <span className="form-label">Project name</span>
                                        <input className="input" style={{ width: '100%' }}
                                            value={projectName} onChange={e => setProjectName(e.target.value)}
                                            placeholder={scanResult.projectName} id="project-name" />
                                    </div>
                                </div>

                                {projectName && (
                                    <div style={{ fontSize: 11, color: 'var(--text-4)', marginBottom: 10 }}>
                                        Site URL: <span className="mono" style={{ color: 'var(--accent)' }}>
                                            http://{projectName.toLowerCase().replace(/[^a-z0-9-]/g, '-')}.localhost
                                        </span>
                                    </div>
                                )}

                                {/* ENV VARS */}
                                <div style={{ marginBottom: 12 }}>
                                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 6 }}>
                                        <span className="form-label" style={{ margin: 0 }}>Environment Variables</span>
                                        <button className="btn btn-sm btn-ghost" onClick={addEnvVar}>+ Add</button>
                                    </div>
                                    {envVars.map((ev, i) => (
                                        <div key={i} style={{ display: 'flex', gap: 4, marginBottom: 4 }}>
                                            <input className="input" style={{ width: 160 }} placeholder="KEY"
                                                value={ev.key} onChange={e => updateEnvVar(i, 'key', e.target.value)} />
                                            <input className="input" style={{ flex: 1 }} placeholder="value"
                                                type={ev.isSecret ? 'password' : 'text'}
                                                value={ev.value} onChange={e => updateEnvVar(i, 'value', e.target.value)} />
                                            <button className="btn btn-sm" title={ev.isSecret ? 'Secret' : 'Visible'}
                                                onClick={() => updateEnvVar(i, 'isSecret', !ev.isSecret)}>
                                                {ev.isSecret ? 'S' : 'V'}
                                            </button>
                                            <button className="btn btn-sm btn-danger" onClick={() => removeEnvVar(i)}>x</button>
                                        </div>
                                    ))}
                                </div>

                                <button className="btn btn-primary" onClick={handleDeploy}
                                    disabled={deploying || !selectedFile} id="deploy-btn"
                                    style={{ width: '100%', justifyContent: 'center', padding: '8px 16px' }}>
                                    {deploying ? <><div className="spinner spinner-sm" /> Starting deployment...</> : 'Deploy'}
                                </button>
                            </>
                        ) : (
                            <div style={{ color: 'var(--red)', fontSize: 13 }}>{scanResult.error}</div>
                        )}
                    </div>
                )}
            </div>

            {/* ── ACTIVE DEPLOYMENT ────────────────────────────────── */}
            {activeDeployment && (
                <div className="card" style={{ marginBottom: 20 }}>
                    <div className="card-head">
                        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                            <span className="card-title">{activeDeployment.projectName || 'Deploying...'}</span>
                            <span className={`badge ${activeDeployment.status === 'running' ? 'badge-green' :
                                    activeDeployment.status === 'deploying' ? 'badge-blue' :
                                        activeDeployment.status === 'failed' ? 'badge-red' : 'badge-gray'
                                }`}>
                                {streaming && <div className="spinner spinner-sm" />}
                                {!streaming && <span className="badge-dot" />}
                                {activeDeployment.status}
                            </span>
                            {activeDeployment.durationMs > 0 && (
                                <span style={{ fontSize: 11, color: 'var(--text-4)' }}>{fmtMs(activeDeployment.durationMs)}</span>
                            )}
                        </div>
                        <div className="actions">
                            {activeDeployment.status === 'running' && activeDeployment.fullUrl && (
                                <a href={activeDeployment.fullUrl} target="_blank" rel="noreferrer"
                                    className="btn btn-sm btn-success">Open Site</a>
                            )}
                            <button className="btn btn-sm" onClick={() => {
                                eventSourceRef.current?.close();
                                setActiveDeployment(null); setLiveLogs([]); setLiveStages([]);
                            }}>Close</button>
                        </div>
                    </div>
                    <div className="card-body">
                        <Pipeline stages={displayStages} />

                        {activeDeployment.status === 'running' && (
                            <div style={{
                                background: 'var(--green-dim)', border: '1px solid var(--green)',
                                borderRadius: 'var(--radius)', padding: '10px 14px', marginBottom: 14
                            }}>
                                <div style={{ fontSize: 12, fontWeight: 600, color: 'var(--green)', marginBottom: 2 }}>
                                    Deployment Successful
                                </div>
                                <div className="mono" style={{ fontSize: 12, color: 'var(--text-2)' }}>
                                    <a href={activeDeployment.fullUrl} target="_blank" rel="noreferrer" className="url-link">
                                        {activeDeployment.fullUrl}
                                    </a>
                                    <span style={{ color: 'var(--text-4)', margin: '0 8px' }}>|</span>
                                    <span style={{ color: 'var(--text-3)' }}>Direct: </span>
                                    <a href={activeDeployment.directUrl} target="_blank" rel="noreferrer" className="url-link">
                                        {activeDeployment.directUrl}
                                    </a>
                                </div>
                            </div>
                        )}

                        <LogTerminal logs={liveLogs.length > 0 ? liveLogs : (activeDeployment.logs || [])} streaming={streaming} />
                    </div>
                </div>
            )}

            {/* ── NGINX CONFIG ─────────────────────────────────────── */}
            {nginxConfig && (
                <div className="card" style={{ marginBottom: 20 }}>
                    <div className="card-head">
                        <span className="card-title">Nginx Configuration</span>
                        <button className="btn btn-sm" onClick={() => setNginxConfig(null)}>Close</button>
                    </div>
                    <div className="card-body" style={{ padding: 0 }}>
                        <pre className="nginx-pre">{nginxConfig}</pre>
                    </div>
                </div>
            )}

            {/* ── ALL DEPLOYMENTS ──────────────────────────────────── */}
            <div className="card">
                <div className="card-head">
                    <div>
                        <span className="card-title">Deployments</span>
                        <span className="card-count">{deployments.length}</span>
                    </div>
                    <button className="btn btn-sm" onClick={fetchDeployments}>Refresh</button>
                </div>
                <table className="tbl">
                    <thead>
                        <tr>
                            <th>Project</th>
                            <th>Source</th>
                            <th>Site URL</th>
                            <th>Status</th>
                            <th>Duration</th>
                            <th>Actions</th>
                        </tr>
                    </thead>
                    <tbody>
                        {deployments.map(dep => (
                            <tr key={dep.id}>
                                <td style={{ fontWeight: 600, color: 'var(--text-0)' }}>{dep.projectName || '...'}</td>
                                <td><span className="tag">{dep.sourceType}</span></td>
                                <td>
                                    {dep.status === 'running' && dep.fullUrl ? (
                                        <a href={dep.fullUrl} target="_blank" rel="noreferrer" className="url-link">
                                            {dep.subdomain}
                                        </a>
                                    ) : (
                                        <span className="mono" style={{ color: 'var(--text-4)', fontSize: 11 }}>
                                            {dep.subdomain || '-'}
                                        </span>
                                    )}
                                </td>
                                <td>
                                    <span className={`badge ${dep.status === 'running' ? 'badge-green' :
                                            dep.status === 'deploying' ? 'badge-blue' :
                                                dep.status === 'failed' ? 'badge-red' : 'badge-gray'
                                        }`}>
                                        {dep.status === 'deploying' && <div className="spinner spinner-sm" />}
                                        {dep.status !== 'deploying' && <span className="badge-dot" />}
                                        {dep.status}
                                    </span>
                                </td>
                                <td className="mono" style={{ fontSize: 11, color: 'var(--text-3)' }}>
                                    {dep.durationMs ? fmtMs(dep.durationMs) : '-'}
                                </td>
                                <td>
                                    <div className="actions">
                                        <button className="btn btn-sm" onClick={() => showLogs(dep)}>Logs</button>
                                        {dep.status === 'running' && (
                                            <>
                                                <button className="btn btn-sm" onClick={() => viewNginx(dep.id)}>Nginx</button>
                                                <button className="btn btn-sm btn-warn" onClick={() => handleTeardown(dep.id)}>Stop</button>
                                            </>
                                        )}
                                        {(dep.status === 'stopped' || dep.status === 'failed') && (
                                            <button className="btn btn-sm btn-danger" onClick={() => handleRemove(dep.id)}>Remove</button>
                                        )}
                                    </div>
                                </td>
                            </tr>
                        ))}
                        {deployments.length === 0 && (
                            <tr><td colSpan={6} className="empty">No deployments. Use the form above to deploy your first project.</td></tr>
                        )}
                    </tbody>
                </table>
            </div>
        </div>
    );
}

function fmtMs(ms) {
    if (ms < 1000) return ms + 'ms';
    if (ms < 60000) return (ms / 1000).toFixed(1) + 's';
    return Math.floor(ms / 60000) + 'm ' + Math.floor((ms % 60000) / 1000) + 's';
}

function fmtTime(ts) {
    if (!ts) return '';
    return new Date(ts).toLocaleTimeString('en-US', { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' });
}
