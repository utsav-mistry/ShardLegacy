import { useState, useEffect } from 'react';

export default function Deployments() {
    const [deployments, setDeployments] = useState([]);
    const [loading, setLoading] = useState(true);
    const [filter, setFilter] = useState('all');

    useEffect(() => {
        fetch('/api/projects')
            .then(r => r.ok ? r.json() : [])
            .then(data => { setDeployments(data); setLoading(false); })
            .catch(() => setLoading(false));
    }, []);

    const filtered = filter === 'all'
        ? deployments
        : deployments.filter(d => d.status === filter);

    const handleTeardown = (id) => {
        if (!confirm('Tear down this deployment?')) return;
        fetch(`/api/projects/${id}/teardown`, { method: 'POST' })
            .then(() => fetch('/api/projects'))
            .then(r => r.json())
            .then(setDeployments);
    };

    const handleRemove = (id) => {
        if (!confirm('Remove this deployment record?')) return;
        fetch(`/api/projects/${id}`, { method: 'DELETE' })
            .then(() => fetch('/api/projects'))
            .then(r => r.json())
            .then(setDeployments);
    };

    if (loading) return <p style={{ color: 'var(--text-4)', padding: 20 }}>Loading...</p>;
    if (deployments.length === 0) return (
        <div className="card">
            <div style={{ padding: 40, textAlign: 'center', color: 'var(--text-4)' }}>
                <p style={{ marginBottom: 8 }}>No deployments yet.</p>
                <p style={{ fontSize: 12 }}>Create your first deployment from the Deploy page.</p>
            </div>
        </div>
    );

    return (
        <div>
            <div style={{ display: 'flex', gap: 8, marginBottom: 16 }}>
                {['all', 'running', 'deploying', 'failed', 'stopped'].map(f => (
                    <button
                        key={f}
                        className={`btn btn-sm ${filter === f ? 'btn-primary' : 'btn-ghost'}`}
                        onClick={() => setFilter(f)}
                    >
                        {f.charAt(0).toUpperCase() + f.slice(1)}
                    </button>
                ))}
            </div>

            <div className="card">
                <div className="card-head">
                    <span className="card-title">Deployments</span>
                    <span className="card-count">{filtered.length}</span>
                </div>
                <div style={{ overflowX: 'auto' }}>
                    <table className="tbl">
                        <thead>
                            <tr>
                                <th>Project</th>
                                <th>Source</th>
                                <th>Type</th>
                                <th>Status</th>
                                <th>Container</th>
                                <th>Port</th>
                                <th>URL</th>
                                <th>Duration</th>
                                <th>Deployed</th>
                                <th style={{ textAlign: 'right' }}>Actions</th>
                            </tr>
                        </thead>
                        <tbody>
                            {filtered.map(d => (
                                <tr key={d.id}>
                                    <td style={{ fontWeight: 600 }}>{d.projectName || '-'}</td>
                                    <td className="mono" style={{ fontSize: 11, maxWidth: 200, overflow: 'hidden', textOverflow: 'ellipsis' }}>{d.source}</td>
                                    <td><span className="tag">{d.sourceType}</span></td>
                                    <td>
                                        <span className={`badge ${d.status === 'running' ? 'badge-green' : d.status === 'deploying' ? 'badge-blue' : d.status === 'failed' ? 'badge-red' : 'badge-gray'}`}>
                                            <span className="badge-dot" />{d.status}
                                        </span>
                                    </td>
                                    <td className="mono" style={{ fontSize: 11 }}>{d.containerId?.slice(0, 8) || '-'}</td>
                                    <td className="mono">{d.assignedPort || '-'}</td>
                                    <td>
                                        {d.directUrl ? (
                                            <a href={d.directUrl} target="_blank" rel="noreferrer" className="url-link" style={{ fontSize: 11 }}>{d.directUrl.replace('http://', '')}</a>
                                        ) : '-'}
                                    </td>
                                    <td className="mono" style={{ fontSize: 11 }}>{d.durationMs ? formatDuration(d.durationMs) : '-'}</td>
                                    <td style={{ fontSize: 12, color: 'var(--text-3)' }}>{formatDate(d.deployedAt)}</td>
                                    <td style={{ textAlign: 'right' }}>
                                        <div style={{ display: 'flex', gap: 4, justifyContent: 'flex-end' }}>
                                            {d.directUrl && (
                                                <a href={d.directUrl} target="_blank" rel="noreferrer" className="btn btn-sm btn-ghost" title="Open">→</a>
                                            )}
                                            {d.status === 'running' && (
                                                <button className="btn btn-sm btn-ghost" onClick={() => handleTeardown(d.id)} title="Tear down">⊗</button>
                                            )}
                                            <button className="btn btn-sm btn-danger" onClick={() => handleRemove(d.id)} title="Remove">✕</button>
                                        </div>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
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
