import { h, ComponentType } from 'preact';
import { useState } from 'preact/hooks';
import { ipc } from './lib/ipc';
import {
  IconHome,
  IconNews,
  IconProfiles,
  IconInstances,
  IconSettings,
  IconLogs,
} from './components/Icons';

// ─── Page IDs ────────────────────────────────────────────────────────────────
type PageId = 'dashboard' | 'news' | 'profiles' | 'instances' | 'settings' | 'logs';

// ─── Sidebar navigation entries ──────────────────────────────────────────────
const NAV_ITEMS: Array<{ id: PageId; label: string; Icon: ComponentType<{ size?: number }> }> = [
  { id: 'dashboard',  label: 'Dashboard',  Icon: IconHome },
  { id: 'news',       label: 'News',       Icon: IconNews },
  { id: 'profiles',   label: 'Profiles',   Icon: IconProfiles },
  { id: 'instances',  label: 'Instances',  Icon: IconInstances },
  { id: 'settings',   label: 'Settings',   Icon: IconSettings },
  { id: 'logs',       label: 'Logs',       Icon: IconLogs },
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

// ─── Dashboard page ──────────────────────────────────────────────────────────
function DashboardPage() {
  return (
    <div class="page">
      <div class="dashboard-hero">
        <div class="dashboard-logo">
          <span class="dashboard-logo-letter">H</span>
        </div>
        <h1 class="dashboard-title">HyPrism</h1>
        <p class="dashboard-subtitle">Hytale Launcher</p>
      </div>
      <div class="dashboard-actions">
        <button class="btn btn-primary btn-lg" onClick={() => ipc.game.launch()}>
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
  function handleMouseDown(e: MouseEvent) {
    // Only drag from the bar background, not from control buttons
    if ((e.target as Element).closest('.titlebar-btn')) return;
    // Window.this.move() starts the native interactive window drag in Sciter
    const w = (globalThis as any).Window?.this;
    if (w?.move) w.move();
  }

  return (
    <div class="titlebar" onMouseDown={handleMouseDown}>
      <div class="titlebar-brand">
        <span class="titlebar-brand-dot"/>
        <span class="titlebar-title">HyPrism</span>
      </div>
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
      <div class="sidebar-logo" title="HyPrism">
        <span class="sidebar-logo-glyph">H</span>
      </div>
      <div class="sidebar-nav">
        {NAV_ITEMS.map(({ id, label, Icon }) => (
          <button
            key={id}
            class={`sidebar-item${current === id ? ' active' : ''}`}
            title={label}
            onClick={() => onNavigate(id)}
          >
            <Icon size={18} />
            <span class="sidebar-label">{label}</span>
          </button>
        ))}
      </div>
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
