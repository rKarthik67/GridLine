import { useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router'
import { api } from '../../api/client'
import { AuthError, AuthShell } from './AuthShell'

export default function Reset() {
  const { token } = useParams()
  const [p1, setP1] = useState(''); const [p2, setP2] = useState(''); const [done, setDone] = useState(false)
  const [err, setErr] = useState<string | null>(null); const [busy, setBusy] = useState(false)
  async function submit(e: FormEvent) {
    e.preventDefault(); if (p1 !== p2) return setErr('The two passwords are not the same.')
    setBusy(true); setErr(null)
    try { await api.post('/auth/reset', { token, password: p1 }); setDone(true) } catch (x) { setErr((x as Error).message) } finally { setBusy(false) }
  }
  return (
    <AuthShell title="Choose a new password" footer={<Link to="/signin">Back to sign in</Link>}>
      {done ? <div className="auth-ok"><b>Your password is changed.</b><p className="muted">Every other session was signed out. <Link to="/signin">Sign in</Link> with the new password.</p></div>
        : <form onSubmit={submit}>
          <div className="field"><label>New password</label><input className="inp" type="password" autoFocus autoComplete="new-password" value={p1} onChange={e => setP1(e.target.value)} required minLength={10} /><small>At least 10 characters, mixing letters with numbers or symbols.</small></div>
          <div className="field"><label>Again, to be sure</label><input className="inp" type="password" autoComplete="new-password" value={p2} onChange={e => setP2(e.target.value)} required /></div>
          <AuthError error={err} />
          <button className="btn auth-btn" disabled={busy}>{busy ? 'Saving…' : 'Set password'}</button>
        </form>}
    </AuthShell>
  )
}
