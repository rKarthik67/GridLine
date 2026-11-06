import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router'
import { api } from '../../api/client'
import type { Me } from '../../api/types'
import { useAuth } from '../../app/auth'
import { useConfig } from '../../app/config'
import { AuthError, AuthShell } from './AuthShell'

export default function SignUp() {
  const { setSession } = useAuth(); const nav = useNavigate(); const cfg = useConfig()
  const [f, setF] = useState({ companyName: '', givenName: '', familyName: '', email: '', password: '', projectNoPrefix: 'EST-' })
  const [err, setErr] = useState<string | null>(null); const [busy, setBusy] = useState(false)
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement>) => setF({ ...f, [k]: e.target.value })
  async function submit(e: FormEvent) {
    e.preventDefault(); setBusy(true); setErr(null)
    try { const r = await api.post<{ accessToken: string; me: Me }>('/auth/signup', f); setSession(r.accessToken, r.me); nav('/projects', { replace: true }) }
    catch (x) { setErr((x as Error).message) } finally { setBusy(false) }
  }
  if (cfg.data?.signupEnabled === false) return <AuthShell title="Sign-up is by invitation" lead="This Gridline installation is private. Ask your company administrator for an invitation, or sign in if you already have an account." footer={<Link to="/signin">Sign in</Link>}><div /></AuthShell>
  return (
    <AuthShell title="Create your company" lead="You become the first administrator. Invite your estimators once you're in. Free for 30 days, no card needed."
      footer={<>Already have an account? <Link to="/signin">Sign in</Link></>}>
      <form onSubmit={submit}>
        <div className="field"><label>Company name</label><input className="inp" autoFocus value={f.companyName} onChange={set('companyName')} placeholder="Northshore Builders" required /></div>
        <div className="fgrid" style={{ gridTemplateColumns: '1fr 1fr' }}>
          <div className="field"><label>First name</label><input className="inp" value={f.givenName} onChange={set('givenName')} required /></div>
          <div className="field"><label>Last name</label><input className="inp" value={f.familyName} onChange={set('familyName')} required /></div>
        </div>
        <div className="field"><label>Work email</label><input className="inp" type="email" autoComplete="username" value={f.email} onChange={set('email')} required /></div>
        <div className="field"><label>Password</label><input className="inp" type="password" autoComplete="new-password" value={f.password} onChange={set('password')} required minLength={10} /><small>At least 10 characters, mixing letters with numbers or symbols.</small></div>
        <div className="field"><label>Project number prefix <span className="muted" style={{ fontWeight: 500 }}>(you can change it later)</span></label><input className="inp" style={{ width: 120 }} value={f.projectNoPrefix} onChange={set('projectNoPrefix')} maxLength={8} /></div>
        <AuthError error={err} />
        <button className="btn auth-btn" disabled={busy}>{busy ? 'Creating…' : 'Create company'}</button>
        <p className="muted" style={{ fontSize: 11.5, margin: '10px 0 0' }}>Your company starts with Gridline's starter library: scope kits, base requirement checklists, cost kits, qualification sets and employee roles, all editable.{(cfg.data?.termsUrl || cfg.data?.privacyUrl) && <> By creating a company you agree to the {cfg.data?.termsUrl && <a href={cfg.data.termsUrl} target="_blank" rel="noreferrer">terms of service</a>}{cfg.data?.termsUrl && cfg.data?.privacyUrl && ' and '}{cfg.data?.privacyUrl && <a href={cfg.data.privacyUrl} target="_blank" rel="noreferrer">privacy policy</a>}.</>}</p>
      </form>
    </AuthShell>
  )
}
