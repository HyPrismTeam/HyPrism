import { h } from 'preact';
import { useState } from 'preact/hooks';
import { ipc } from './lib/ipc';

// ─── Page IDs ────────────────────────────────────────────────────────────────
type PageId = 'dashboard' | 'news' | 'profiles' | 'instances' | 'settings' | 'logs';

// ─── Sidebar navigation entries ──────────────────────────────────────────────
const NAV_ITEMS: Array<{ id: PageId; label: string; icon: string }> = [
  { id: 'dashboard',  label: 'Dashboard',  icon: '⊞' },
  { id: 'news',       label: 'News',        icon: '◈' },
  { id: 'profiles',   label: 'Profiles',    icon: '◉' },
  { id: 'instances',  label: 'Instances',   icon: '⬡' },
  { id: 'settings',   label: 'Settings',    icon: '⚙' },
  { id: 'logs',       label: 'Logs',        icon: '≡' },
];

// ─── Placeholder page component ──────────────────────────────────────────────
function PlaceholderPage({ title }: { title: string }) {
  return (
    <div class="page">
      <div class="page-placeholder">
        <h2>{title}</h2>
        <p>This page is under construction.</p>
      </div>
    </div>
  );
}

// ─── Dashboard page (first to be implemented) ────────────────────────────────
function DashboardPage() {
  return (
    <div class="page">
      <div class="card" style={{ maxWidth: 600, margin: '0 auto', textAlign: 'center', padding: '40px 24px' }}>
        <h2 style={{ fontSize: 24, marginBottom: 12, color: 'var(--accent)' }}>HyPrism</h2>
        <p style={{ color: 'var(--text-secondary)', marginBottom: 24 }}>
          Launcher is ready. Connect the backend to continue.
        </p>
        <button
          class="btn btn-primary"
          onClick={() => ipc.game.launch()}
        >
          Launch Game
        </button>
      </div>
    </div>
  );
}

// ─── Page router ─────────────────────────────────────────────────────────────
function renderPage(page: PageId) {
  switch (page) {
    case 'dashboard':  return <DashboardPage />;
    case 'news':       return <PlaceholderPage title="News" />;
    case 'profiles':   return <PlaceholderPage title="Profiles" />;
    case 'instances':  return <PlaceholderPage title="Instances" />;
    case 'settings':   return <PlaceholderPage title="Settings" />;
    case 'logs':       return <PlaceholderPage title="Logs" />;
    default:           return <DashboardPage />;
  }
}

// ─── Titlebar ─────────────────────────────────────────────────────────────────
function Titlebar() {
  return (
    <div class="titlebar">
      <span class="titlebar-title">HyPrism</span>
      <div class="titlebar-controls">
        <button
          class="titlebar-btn"
          title="Minimize"
          onClick={() => ipc.windowCtl.minimize()}
        >
          −
        </button>
        <button
          class="titlebar-btn"
          title="Maximize"
          onClick={() => ipc.windowCtl.maximize()}
        >
          □
        </button>
        <button
          class="titlebar-btn close"
          title="Close"
          onClick={() => ipc.windowCtl.close()}
        >
          ×
        </button>
      </div>
    </div>
  );
}

// ─── Sidebar ──────────────────────────────────────────────────────────────────
interface SidebarProps {
  current: PageId;
  onNavigate: (page: PageId) => void;
}

function Sidebar({ current, onNavigate }: SidebarProps) {
  return (
    <nav class="sidebar">
      {NAV_ITEMS.map(({ id, label, icon }) => (
        <button
          key={id}
          class={`sidebar-item${current === id ? ' active' : ''}`}
          title={label}
          onClick={() => onNavigate(id)}
        >
          {icon}
        </button>
      ))}
    </nav>
  );
}

// ─── App root ─────────────────────────────────────────────────────────────────
export function App() {
  const [page, setPage] = useState<PageId>('dashboard');

  return (
    <div class="app-shell">
      <Titlebar />
      <div class="app-body">
        <Sidebar current={page} onNavigate={setPage} />
        <main class="content-pane">
          {renderPage(page)}
        </main>
      </div>
    </div>
  );
}
