import './utils/polyfills'; // Sciter / QuickJS compatibility — must be first
import { h, Component, ComponentChildren, render } from 'preact';
import App from './App';
import { AccentColorProvider } from './contexts/AccentColorContext';
import { I18nProvider } from './lib/i18n';
import { ipc } from './lib/ipc';

import './index.css';

// Error boundary to catch crashes
class ErrorBoundary extends Component<{ children: ComponentChildren }, { hasError: boolean, error: Error | null }> {
  constructor(props: { children: ComponentChildren }) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error: Error) {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, errorInfo: any) {
    console.error('Preact Error Boundary caught:', error, errorInfo);
  }

  render() {
    if (this.state.hasError) {
      return (
        <div style={{ padding: 20, color: 'white', background: '#1a1a1a', height: '100vh' }}>
          <h1>Something went wrong</h1>
          <pre style={{ whiteSpace: 'pre-wrap' }}>{this.state.error?.toString()}</pre>
          <button onClick={() => ipc.windowCtl.restart()}>Reload</button>
        </div>
      );
    }
    return this.props.children;
  }
}

render(
  <ErrorBoundary>
    <I18nProvider>
      <AccentColorProvider>
        <App />
      </AccentColorProvider>
    </I18nProvider>
  </ErrorBoundary>,
  document.getElementById('root')!
);
