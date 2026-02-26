import { h, Component, render } from 'preact';
import type { ComponentChildren } from 'preact';
import { App } from './App';
import './index.css';

// ─── Error boundary ───────────────────────────────────────────────────────────
// Catches render errors so we don't get a completely blank screen on crash.
class ErrorBoundary extends Component<
  { children: ComponentChildren },
  { hasError: boolean; error: Error | null }
> {
  constructor(props: { children: ComponentChildren }) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error: Error) {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, info: { componentStack?: string }) {
    console.error('[HyPrism] Render error:', error, info?.componentStack ?? '');
  }

  render() {
    if (this.state.hasError) {
      return (
        <div style={{ padding: 24, color: '#f0f0f0', background: '#090909', height: '100vh', fontFamily: 'sans-serif' }}>
          <h2 style={{ color: '#FFA845', marginBottom: 12 }}>Render error</h2>
          <pre style={{ whiteSpace: 'pre-wrap', color: '#aaa', fontSize: 12 }}>
            {this.state.error?.toString()}
          </pre>
          <br />
          <button
            onClick={() => this.setState({ hasError: false, error: null })}
            style={{ padding: '8px 16px', cursor: 'pointer', background: '#FFA845', border: 'none', borderRadius: 6, fontWeight: 600 }}
          >
            Retry
          </button>
        </div>
      );
    }
    return this.props.children;
  }
}

// ─── Mount ────────────────────────────────────────────────────────────────────
const root = document.getElementById('root');
if (root) {
  render(
    <ErrorBoundary>
      <App />
    </ErrorBoundary>,
    root
  );
} else {
  console.error('[HyPrism] #root element not found');
}
