import { useState } from 'react';
import Dashboard from './pages/Dashboard';
import Deploy from './pages/Deploy';
import './App.css';

const NAV = [
    { section: 'Overview', items: [{ id: 'dashboard', label: 'Dashboard', icon: 'grid' }] },
    { section: 'Deployment', items: [{ id: 'deploy', label: 'Deploy', icon: 'rocket' }] },
];

const TITLES = { dashboard: 'Dashboard', deploy: 'Deploy Project' };

function Icon({ name }) {
    const d = {
        grid: <><rect x="3" y="3" width="7" height="7" /><rect x="14" y="3" width="7" height="7" /><rect x="14" y="14" width="7" height="7" /><rect x="3" y="14" width="7" height="7" /></>,
        rocket: <><path d="M4.5 16.5c-1.5 1.26-2 5-2 5s3.74-.5 5-2c.71-.84.7-2.13-.09-2.91a2.18 2.18 0 00-2.91-.09z" /><path d="M12 15l-3-3a22 22 0 012-3.95A12.88 12.88 0 0122 2c0 2.72-.78 7.5-6 11a22.35 22.35 0 01-4 2z" /><path d="M9 12H4s.55-3.03 2-4c1.62-1.08 5 0 5 0" /><path d="M12 15v5s3.03-.55 4-2c1.08-1.62 0-5 0-5" /></>,
    };
    return <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">{d[name]}</svg>;
}

export default function App() {
    const [page, setPage] = useState('dashboard');

    return (
        <div className="layout">
            <aside className="sidebar">
                <div className="sidebar-brand">
                    <svg viewBox="0 0 24 24" fill="currentColor"><path d="M21 16V8a2 2 0 00-1-1.73l-7-4a2 2 0 00-2 0l-7 4A2 2 0 003 8v8a2 2 0 001 1.73l7 4a2 2 0 002 0l7-4A2 2 0 0021 16z" /></svg>
                    <span>ShardLegacy</span>
                </div>
                <nav className="sidebar-nav">
                    {NAV.map(s => (
                        <div key={s.section}>
                            <div className="nav-group">{s.section}</div>
                            {s.items.map(it => (
                                <button key={it.id} className={`nav-btn ${page === it.id ? 'active' : ''}`} onClick={() => setPage(it.id)} id={`nav-${it.id}`}>
                                    <Icon name={it.icon} />{it.label}
                                </button>
                            ))}
                        </div>
                    ))}
                </nav>
                <div className="sidebar-foot">ShardLegacy v1.0.0</div>
            </aside>
            <main className="main">
                <header className="topbar">
                    <h1>{TITLES[page]}</h1>
                    <div className="topbar-right">
                        <span className="badge badge-green"><span className="badge-dot" />System Ready</span>
                    </div>
                </header>
                <div className="content">
                    {page === 'dashboard' ? <Dashboard /> : <Deploy />}
                </div>
            </main>
        </div>
    );
}