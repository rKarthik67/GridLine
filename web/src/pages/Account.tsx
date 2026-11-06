// Your account: who you are, change your password, and the devices you're signed in on.
import { useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { useAuth } from '../app/auth'
import { Shell, PageHead } from '../components/Shell'
import { Icon } from '../components/Icon'
import { useAction } from '../components/ui'

interface Session { tokenId: number; issuedAt: string; expiresAt: string; lastUsedAt: string | null; userAgent: string | null }
export default function Account() {
  const { me } = useAuth(); const qc = useQueryClient(); const { run, busy } = useAction()
  const sessions = useQuery({ queryKey: ['sessions'], queryFn: () => api.get<Session[]>('/auth/sessions') })
  const [cur, setCur] = useState(''); const [p1, setP1] = useState(''); const [p2, setP2] = useState(''); const [msg, setMsg] = useState<string | null>(null)
  async function change(e: FormEvent) {
    e.preventDefault(); setMsg(null)
    if (p1 !== p2) return setMsg('The two new passwords are not the same.')
    if (await run(() => api.post('/auth/change-password', { currentPassword: cur, newPassword: p1 }), 'Password changed · other sessions signed out')) { setCur(''); setP1(''); setP2(''); qc.invalidateQueries({ queryKey: ['sessions'] }) }
  }
  const browser = (ua: string | null) => !ua ? 'Unknown browser' : /Edg\//.test(ua) ? 'Microsoft Edge' : /Chrome\//.test(ua) ? 'Chrome' : /Firefox\//.test(ua) ? 'Firefox' : /Safari\//.test(ua) ? 'Safari' : ua.slice(0, 40)
  return <Shell title="Account">
    <PageHead eyebrow="My desk" title="Account" desc="Your profile, password and the places you're signed in." noRefresh />
    <div className="grid2">
      <div className="card pad">
        <div className="pm-head" style={{ padding: 0, marginBottom: 12 }}><span className="avatar lg" style={{ background: me?.colour, color: '#fff' }}>{me?.initials}</span><div><b style={{ fontSize: 15 }}>{me?.name}</b><small className="muted" style={{ display: 'block' }}>{me?.email}</small></div></div>
        <dl className="estdl"><dt>Company</dt><dd>{me?.company?.name}</dd><dt>Role</dt><dd>{me?.roles.join(', ') || 'viewer'}</dd></dl>
        <p className="muted" style={{ fontSize: 12 }}>Name, role and colour are managed by your company administrator under People &amp; access.</p>
      </div>
      <form className="card pad" onSubmit={change}>
        <h3 style={{ margin: '0 0 10px' }}>Change password</h3>
        <div className="stack" style={{ gap: 10 }}>
          <div className="field"><label>Current password</label><input className="inp" type="password" autoComplete="current-password" value={cur} onChange={e => setCur(e.target.value)} required /></div>
          <div className="field"><label>New password</label><input className="inp" type="password" autoComplete="new-password" value={p1} onChange={e => setP1(e.target.value)} required minLength={10} /><small>At least 10 characters, mixing letters with numbers or symbols.</small></div>
          <div className="field"><label>New password again</label><input className="inp" type="password" autoComplete="new-password" value={p2} onChange={e => setP2(e.target.value)} required /></div>
          {msg && <p className="err">{msg}</p>}
          <div><button className="btn p" disabled={busy}><Icon name="lock" />Change password</button></div>
        </div>
      </form>
    </div>
    <div className="card" style={{ marginTop: 14 }}>
      <div className="ch"><h3>Where you're signed in</h3><span className="muted">Every browser tab shares one session per computer.</span><div className="acts">
        <button className="btn sm" onClick={async () => { if (await run(() => api.post('/auth/sessions/revoke-others'), 'Other sessions signed out')) qc.invalidateQueries({ queryKey: ['sessions'] }) }}><Icon name="logout" size="s" />Sign out everywhere else</button></div></div>
      <div className="gw auto"><table className="dg" style={{ width: '100%' }}><thead><tr><th>Browser</th><th>Signed in</th><th>Last used</th><th>Expires</th></tr></thead>
        <tbody>{(sessions.data ?? []).map(s => <tr key={s.tokenId}><td>{browser(s.userAgent)}</td><td>{new Date(s.issuedAt).toLocaleString()}</td><td>{s.lastUsedAt ? new Date(s.lastUsedAt).toLocaleString() : '–'}</td><td>{new Date(s.expiresAt).toLocaleString()}</td></tr>)}</tbody></table></div>
    </div>
  </Shell>
}
