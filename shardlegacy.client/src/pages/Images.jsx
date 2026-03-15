import { useState, useEffect } from 'react';

export default function Images() {
    const [images, setImages] = useState([]);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        const loadImages = () => {
            fetch('/api/projects')
                .then(r => r.ok ? r.json() : [])
                .then(data => {
                    const imageSet = new Map();
                    data.forEach(p => {
                        if (p.imageName) {
                            imageSet.set(p.imageName, {
                                name: p.imageName,
                                deployments: (imageSet.get(p.imageName)?.deployments || 0) + 1,
                                lastUsed: new Date(p.deployedAt),
                            });
                        }
                    });
                    setImages(Array.from(imageSet.values()).sort((a, b) => b.lastUsed - a.lastUsed));
                    setLoading(false);
                })
                .catch(() => setLoading(false));
        };
        loadImages();
        const interval = setInterval(loadImages, 10000);
        return () => clearInterval(interval);
    }, []);

    if (loading) return <p style={{ color: 'var(--text-4)', padding: 20 }}>Loading Docker images...</p>;
    if (images.length === 0) return (
        <div className="card">
            <div style={{ padding: 40, textAlign: 'center', color: 'var(--text-4)' }}>
                <p style={{ marginBottom: 8 }}>No Docker images built yet.</p>
                <p style={{ fontSize: 12 }}>Deploy a project to build a Docker image.</p>
            </div>
        </div>
    );

    return (
        <div>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(300px, 1fr))', gap: 12, marginBottom: 20 }}>
                {images.map(img => (
                    <div key={img.name} className="card" style={{ padding: 16, display: 'flex', flexDirection: 'column' }}>
                        <div style={{ fontWeight: 600, marginBottom: 8, wordBreak: 'break-all' }}>{img.name}</div>
                        <div style={{ display: 'flex', gap: 16, fontSize: 12 }}>
                            <div>
                                <div style={{ color: 'var(--text-4)', marginBottom: 2 }}>Deployments</div>
                                <div style={{ fontSize: 16, fontWeight: 600, color: 'var(--accent)' }}>{img.deployments}</div>
                            </div>
                            <div>
                                <div style={{ color: 'var(--text-4)', marginBottom: 2 }}>Last Used</div>
                                <div style={{ color: 'var(--text-3)' }}>{timeAgo(img.lastUsed)}</div>
                            </div>
                        </div>
                    </div>
                ))}
            </div>
        </div>
    );
}

function timeAgo(date) {
    const ms = Date.now() - date.getTime();
    const mins = Math.floor(ms / 60000);
    const hours = Math.floor(ms / 3600000);
    const days = Math.floor(ms / 86400000);
    if (days > 0) return `${days}d ago`;
    if (hours > 0) return `${hours}h ago`;
    if (mins > 0) return `${mins}m ago`;
    return 'now';
}
