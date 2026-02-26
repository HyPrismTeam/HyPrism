import { Component, render, type ComponentChildren } from 'preact';
import App from './App';
import { AccentColorProvider } from './contexts/AccentColorContext';

import { initI18n } from './i18n';
import './index.css';
import 'flag-icons/css/flag-icons.min.css';

// Error boundary to catch crashes
class ErrorBoundary extends Component<{ children: ComponentChildren }, { hasError: boolean, error: Error | null }> {
  constructor(props: { children: ComponentChildren }) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error: Error) {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, errorInfo: { componentStack?: string }) {
    console.error('Preact Error Boundary caught:', error, errorInfo);
  }

  render() {
    if (this.state.hasError) {
      return (
        <div style={{ padding: 20, color: 'white', background: '#1a1a1a', height: '100vh' }}>
          <h1>Something went wrong</h1>
          <pre style={{ whiteSpace: 'pre-wrap' }}>{this.state.error?.toString()}</pre>
          <button onClick={() => window.location.reload()}>Reload</button>
        </div>
      );
    }
    return this.props.children;
  }
}

// Initialize i18n from backend, then render the app
initI18n().catch((err) => {
  console.warn('[i18n] init failed, rendering with fallback keys:', err);
}).finally(() => {
  render(
    <ErrorBoundary>
      <AccentColorProvider>
        <App />
      </AccentColorProvider>
    </ErrorBoundary>,
    document.getElementById('root')!
  );
});
