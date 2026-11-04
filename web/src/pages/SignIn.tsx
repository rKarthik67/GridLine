import { useState, type FormEvent } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { useAuth } from '../app/auth'

export default function SignIn() {
  const { signIn } = useAuth(); const nav = useNavigate(); const [sp] = useSearchParams()
  const [email, setEmail] = useState('demo@northshore.example'); const [password, setPassword] = useState('')
  const [err, setErr] = useState<string | null>(null); const [busy, setBusy] = useState(false)
  async function submit(e: FormEvent) {
    e.preventDefault(); setBusy(true); setErr(null)
    try { await signIn(email, password); nav(sp.get('next') || '/projects', { replace: true }) }
    catch (x) { setErr((x as Error).message) } finally { setBusy(false) }
  }
  return (
    <div className="signin">
      <div className="art"><div className="brand" style={{ padding: 0 }}><span className="mark">G</span><b>Gridline<i>.</i></b></div>
        <h2 style={{ marginTop: 40 }}>Level every bid.<br />Price every project.</h2></div>
      <form className="card pad" onSubmit={submit} style={{ maxWidth: 380, margin: 'auto' }}>
        <h3 style={{ marginTop: 0 }}>Sign in</h3>
        <div className="field"><label htmlFor="em">Email</label><input id="em" className="inp" type="email" autoComplete="username" value={email} onChange={e => setEmail(e.target.value)} required /></div>
        <div className="field" style={{ marginTop: 10 }}><label htmlFor="pw">Password</label><input id="pw" className="inp" type="password" autoComplete="current-password" value={password} onChange={e => setPassword(e.target.value)} required /></div>
        {err && <p className="muted" role="alert" style={{ color: 'var(--bad, #d1344b)' }}>{err}</p>}
        <button className="btn p" style={{ width: '100%', marginTop: 14, justifyContent: 'center' }} disabled={busy}>{busy ? 'Signing in…' : 'Sign in'}</button>
      </form>
    </div>
  )
}
