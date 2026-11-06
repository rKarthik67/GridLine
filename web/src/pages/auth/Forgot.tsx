import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { api } from '../../api/client'
import { AuthError, AuthShell } from './AuthShell'

export default function Forgot() {
  const [email, setEmail] = useState(''); const [done, setDone] = useState<{ message: string; devLink?: string | null } | null>(null)
  const [err, setErr] = useState<string | null>(null); const [busy, setBusy] = useState(false)
  async function submit(e: FormEvent) {
    e.preventDefault(); setBusy(true); setErr(null)
    try { setDone(await api.post('/auth/forgot', { email })) } catch (x) { setErr((x as Error).message) } finally { setBusy(false) }
  }
  return (
    <AuthShell title="Reset your password" lead="Enter your work email and we'll send a link to choose a new password." footer={<Link to="/signin">Back to sign in</Link>}>
      {done ? <div className="auth-ok"><b>Check your email.</b><p className="muted">{done.message} The link works for 2 hours.</p>
          {done.devLink && <p className="muted" style={{ fontSize: 12 }}>No email server is configured on this installation, so here is the link: <a href={done.devLink}>open the reset page</a>.</p>}</div>
        : <form onSubmit={submit}>
          <div className="field"><label>Work email</label><input className="inp" type="email" autoFocus autoComplete="username" value={email} onChange={e => setEmail(e.target.value)} required /></div>
          <AuthError error={err} />
          <button className="btn auth-btn" disabled={busy}>{busy ? 'Sending…' : 'Send reset link'}</button>
        </form>}
    </AuthShell>
  )
}
