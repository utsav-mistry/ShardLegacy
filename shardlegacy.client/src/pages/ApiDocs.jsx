export default function ApiDocs() {
    const endpoints = [
        { method: 'GET', path: '/api/projects', desc: 'List all deployments', response: '[ProjectDeployment]' },
        { method: 'GET', path: '/api/projects/{id}', desc: 'Get deployment details', response: 'ProjectDeployment' },
        { method: 'POST', path: '/api/projects/scan', desc: 'Scan GitHub URL or local path', body: '{ source: string }', response: 'ScanResult' },
        { method: 'POST', path: '/api/projects/deploy', desc: 'Start deployment (background)', body: 'DeployRequest', response: 'ProjectDeployment' },
        { method: 'GET', path: '/api/projects/{id}/logs/stream', desc: 'SSE: real-time deployment logs', response: 'Server-Sent Events' },
        { method: 'POST', path: '/api/projects/{id}/teardown', desc: 'Stop container, remove config', response: '200 OK' },
        { method: 'DELETE', path: '/api/projects/{id}', desc: 'Remove deployment record', response: '204 No Content' },
        { method: 'GET', path: '/api/projects/{id}/nginx-config', desc: 'Get generated Nginx config', response: '{ config: string }' },
    ];

    return (
        <div>
            <div className="card" style={{ marginBottom: 20 }}>
                <div style={{ padding: 16, borderBottom: '1px solid var(--border-0)' }}>
                    <div style={{ fontWeight: 600, marginBottom: 8 }}>Base URL</div>
                    <code style={{ padding: '8px 12px', background: 'var(--bg-2)', borderRadius: 'var(--radius)', display: 'inline-block', fontSize: 12 }}>
                        https://localhost:55730/api
                    </code>
                </div>
                <div style={{ padding: 16 }}>
                    <div style={{ fontWeight: 600, marginBottom: 8 }}>Authentication</div>
                    <p style={{ fontSize: 12, color: 'var(--text-3)' }}>Currently no authentication required. Secure before production use.</p>
                </div>
            </div>

            <div className="card">
                <div className="card-head"><span className="card-title">Endpoints</span></div>
                <div style={{ overflowX: 'auto' }}>
                    <table className="tbl">
                        <thead>
                            <tr>
                                <th>Method</th>
                                <th>Path</th>
                                <th>Description</th>
                                <th>Body/Response</th>
                            </tr>
                        </thead>
                        <tbody>
                            {endpoints.map((ep, i) => (
                                <tr key={i}>
                                    <td>
                                        <span className="tag" style={{
                                            background: ep.method === 'GET' ? 'var(--cyan-dim)' : ep.method === 'POST' ? 'var(--green-dim)' : 'var(--red-dim)',
                                            color: ep.method === 'GET' ? 'var(--cyan)' : ep.method === 'POST' ? 'var(--green)' : 'var(--red)',
                                        }}>{ep.method}</span>
                                    </td>
                                    <td className="mono" style={{ fontSize: 11 }}>{ep.path}</td>
                                    <td style={{ fontSize: 12 }}>{ep.desc}</td>
                                    <td style={{ fontSize: 11, color: 'var(--text-3)' }}>
                                        {ep.body && <div>Body: {ep.body}</div>}
                                        {ep.response && <div>Response: {ep.response}</div>}
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            </div>

            <div className="card" style={{ marginTop: 20 }}>
                <div style={{ padding: 16 }}>
                    <div style={{ fontWeight: 600, marginBottom: 12 }}>Example: Deploy a Project</div>
                    <pre style={{
                        background: 'var(--bg-2)',
                        padding: 12,
                        borderRadius: 'var(--radius)',
                        overflow: 'auto',
                        fontSize: 11,
                        lineHeight: 1.6
                    }}>{`curl -X POST https://localhost:55730/api/projects/deploy \
  -H "Content-Type: application/json" \
  -d '{
    "source": "https://github.com/user/repo",
    "projectName": "my-app",
    "selectedFile": "Dockerfile",
    "environmentVariables": [
      {"key": "NODE_ENV", "value": "production", "isSecret": false}
    ]
  }'`}</pre>
                </div>
            </div>
        </div>
    );
}
