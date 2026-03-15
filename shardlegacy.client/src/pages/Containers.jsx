import { useState, useEffect } from 'react';

export default function Containers() {
    const [containers, setContainers] = useState([]);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        const loadContainers = () => {
            fetch('/api/projects')
                .then(r => r.ok ? r.json() : [])
                .then(data => {
                    const activeContainers = data.filter(p => p.status === 'running' && p.containerId);
                    setContainers(activeContainers);
                    setLoading(false);
                })
                .catch(() => setLoading(false));
        };
        loadContainers();
        const interval = setInterval(loadContainers, 5000);
        return () => clearInterval(interval);
    }, []);

    if (loading) return <p style={{ color: 'var(--text-4)', padding: 20 }}>Loading running containers...</p>;
    if (containers.length === 0) return (
        <div className="card">
            <div style={{ padding: 40, textAlign: 'center', color: 'var(--text-4)' }}>
                <p style={{ marginBottom: 8 }}>No running containers.</p>
                <p style={{ fontSize: 12 }}>Deploy a project to start a container.</p>
            </div>
        </div>
    );

    return (
        <div>
            <div className="card">
                <div className="card-head">
                    <span className="card-title">Active Containers</span>
                    <span className="card-count">{containers.length}</span>
                </div>
                <div style={{ overflowX: 'auto' }}>
                    <table className="tbl">
                        <thead>
                            <tr>
                                <th>Project</th>
                                <th>Container ID</th>
                                <th>Image</th>
                                <th>Port</th>
                                <th>Status</th>
                                <th>Uptime</th>
                                <th>Access</th>
                            </tr>
                        </thead>
                        <tbody>
                            {containers.map(c => (
                                <tr key={c.id}>
                                    <td style={{ fontWeight: 600 }}>{c.projectName}</td>
                                    <td className="mono" style={{ fontSize: 11 }}>{c.containerId.slice(0, 12)}</td>
                                    <td className="mono" style={{ fontSize: 11 }}>{c.imageName?.split(':')[0].split('/')[1] || '-'}</td>
                                    <td className="mono">{c.assignedPort}</td>
                                    <td>
                                        <span className="badge badge-green">
                                            <span className="badge-dot" />running
                                        </span>
                                    </td>
                                    <td style={{ fontSize: 12, color: 'var(--text-3)' }}>{getUptime(c.deployedAt)}</td>
                                    <td>
                                        {c.directUrl && (
                                            <a href={c.directUrl} target="_blank" rel="noreferrer" className="url-link">→ Open</a>
                                        )}
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

function getUptime(deployedAt) {
    if (!deployedAt) return '-';
    const ms = Date.now() - new Date(deployedAt).getTime();
    const days = Math.floor(ms / 86400000);
    const hours = Math.floor((ms % 86400000) / 3600000);
    const mins = Math.floor((ms % 3600000) / 60000);
    if (days > 0) return `${days}d ${hours}h`;
    if (hours > 0) return `${hours}h ${mins}m`;
    return `${mins}m`;
}
