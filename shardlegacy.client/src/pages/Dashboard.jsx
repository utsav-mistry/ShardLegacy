import { useState, useEffect } from 'react';

export default function Dashboard() {
    const [projects, setProjects] = useState([]);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        fetch('/api/projects')
            .then(r => r.ok ? r.json() : [])
            .then(data => { setProjects(data); setLoading(false); })
            .catch(() => setLoading(false));
    }, []);

    if (loading) return <p style={{ color: 'var(--text-4)', padding: 20 }}>Loading...</p>;

    const running = projects.filter(p => p.status === 'running');
    const failed = projects.filter(p => p.status === 'failed');
    const deploying = projects.filter(p => p.status === 'deploying');
    const stopped = projects.filter(p => p.status === 'stopped');

    return (
        <div>
            <div className="stats">
                <div className="stat">
                    <div className="stat-label">Total Projects</div>
                    <div className="stat-value">{projects.length}</div>
                </div>
                <div className="stat">
                    <div className="stat-label">Running</div>
                    <div className="stat-value" style={{ color: 'var(--green)' }}>{running.length}</div>
                </div>
                <div className="stat">
                    <div className="stat-label">Deploying</div>
                    <div className="stat-value" style={{ color: 'var(--accent)' }}>{deploying.length}</div>
                </div>
                <div className="stat">
                    <div className="stat-label">Failed</div>
                    <div className="stat-value" style={{ color: failed.length > 0 ? 'var(--red)' : undefined }}>{failed.length}</div>
                </div>
                <div className="stat">
                    <div className="stat-label">Stopped</div>
                    <div className="stat-value">{stopped.length}</div>
                </div>
            </div>

            <div className="card">
                <div className="card-head">
                    <div>
                        <span className="card-title">Recent Deployments</span>
                        <span className="card-count">{projects.length}</span>
                    </div>
                </div>
                <table className="tbl">
                    <thead>
                        <tr>
                            <th>Project</th>
                            <th>Source</th>
                            <th>URL</th>
                            <th>Port</th>
                            <th>Status</th>
                            <th>Duration</th>
                            <th>Deployed</th>
                        </tr>
                    </thead>
                    <tbody>
                        {projects.map(p => (
                            <tr key={p.id}>
                                <td style={{ fontWeight: 600, color: 'var(--text-0)' }}>{p.projectName || '-'}</td>
                                <td>
                                    <span className="tag">{p.sourceType}</span>
                                </td>
                                <td>
                                    {p.directUrl ? (
                                        <a href={p.directUrl} target="_blank" rel="noreferrer" className="url-link">{p.directUrl}</a>
                                    ) : '-'}
                                </td>
                                <td className="mono">{p.assignedPort || '-'}</td>
                                <td className="status-col">
                                    <span className={`badge ${p.status === 'running' ? 'badge-green' :
                                            p.status === 'deploying' ? 'badge-blue' :
                                                p.status === 'failed' ? 'badge-red' :
                                                    'badge-gray'
                                        }`}>
                                        <span className="badge-dot" />{p.status}
                                    </span>
                                </td>
                                <td className="mono" style={{ fontSize: 11, color: 'var(--text-3)' }}>
                                    {p.durationMs ? formatDuration(p.durationMs) : '-'}
                                </td>
                                <td style={{ fontSize: 12, color: 'var(--text-3)' }}>
                                    {formatDate(p.deployedAt)}
                                </td>
                            </tr>
                        ))}
                        {projects.length === 0 && (
                            <tr><td colSpan={7} className="empty">No deployments yet.</td></tr>
                        )}
                    </tbody>
                </table>
            </div>
        </div>
    );
}

function formatDuration(ms) {
    if (ms < 1000) return ms + 'ms';
    if (ms < 60000) return (ms / 1000).toFixed(1) + 's';
    return Math.floor(ms / 60000) + 'm ' + Math.floor((ms % 60000) / 1000) + 's';
}

function formatDate(d) {
    if (!d) return '-';
    return new Date(d).toLocaleString('en-US', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
}
