// Catches a page that throws while drawing, so one broken page shows a message instead of a blank app.
import { Component, type ReactNode } from 'react'
import { Icon } from './Icon'

export class ErrorBoundary extends Component<{ children: ReactNode; where?: string }, { error: Error | null }> {
  state = { error: null as Error | null }
  static getDerivedStateFromError(error: Error) { return { error } }
  componentDidCatch(error: Error, info: { componentStack?: string | null }) { console.error('[gridline] page crashed', this.props.where, error, info.componentStack) }
  render() {
    const e = this.state.error
    if (!e) return this.props.children
    return <div className="card crash" role="alert">
      <Icon name="alert" size="l" />
      <h3>Something went wrong on {this.props.where ?? 'this page'}</h3>
      <p className="muted">Your saved work is safe. Try again, or reload the page. If it keeps happening, send this message to support.</p>
      <pre>{e.message}</pre>
      <div style={{ display: 'flex', gap: 8, justifyContent: 'center' }}>
        <button className="btn" onClick={() => this.setState({ error: null })}>Try again</button>
        <button className="btn p" onClick={() => location.reload()}>Reload page</button>
      </div>
    </div>
  }
}
