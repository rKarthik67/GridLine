import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { useAuth } from '../../app/auth'
import { AuthError, AuthShell } from './AuthShell'

export default function SignIn() {
  const { signIn } = useAuth(); const nav = useNavigate(); const [sp] = useSearchParams()
  const [email, setEmail] = useState(''); const [password, setPassword] = useState(''); const [remember, setRemember] = useState(true)
  const [err, setErr] = useState<string | null>(null); const [busy, setBusy] = useState(false)
  async function submit(e: FormEvent) {
    e.preventDefault(); setBusy(true); setErr(null)
    try { await signIn(email, password, remember); nav(sp.get('next') || '/projects', { replace: true }) }
    catch (x) { setErr((x as Error).message) } finally { setBusy(false) }
  }
  return (
    <AuthShell title="Sign in" lead="Welcome back. Sign in with your work email." footer={<>New here? <Link to="/signup">Create your company</Link></>}>
      <form onSubmit={submit}>
        <div className="field"><label htmlFor="em">Work email</label><input id="em" className="inp" type="email" autoComplete="username" autoFocus value={email} onChange={e => setEmail(e.target.value)} required /></div>
        <div className="field"><label htmlFor="pw">Password <Link to="/forgot" className="auth-side">Forgot it?</Link></label><input id="pw" className="inp" type="password" autoComplete="current-password" value={password} onChange={e => setPassword(e.target.value)} required /></div>
        <label className="check" style={{ fontSize: 12.5 }}><input type="checkbox" checked={remember} onChange={e => setRemember(e.target.checked)} /> Keep me signed in on this computer for 30 days</label>
        <AuthError error={err} />
        <button className="btn auth-btn" disabled={busy}>{busy ? 'Signing in…' : 'Sign in'}</button>
      </form>
    </AuthShell>
  )
}
