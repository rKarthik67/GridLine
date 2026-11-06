import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { Me } from '../../api/types'
import { useAuth } from '../../app/auth'
import { AuthError, AuthShell } from './AuthShell'

interface InviteInfo { email: string; givenName: string | null; familyName: string | null; roleCode: string; companyName: string; invitedBy: string | null; expiresAt: string }
export default function Invite() {
  const { token } = useParams(); const { setSession } = useAuth(); const nav = useNavigate()
  const { data, error, isLoading } = useQuery({ queryKey: ['invite', token], queryFn: () => api.get<InviteInfo>(`/auth/invite/${token}`), retry: false })
  const [f, setF] = useState({ givenName: '', familyName: '', password: '', again: '' })
  const [err, setErr] = useState<string | null>(null); const [busy, setBusy] = useState(false)
  async function submit(e: FormEvent) {
    e.preventDefault(); if (f.password !== f.again) return setErr('The two passwords are not the same.')
    setBusy(true); setErr(null)
    try {
      const r = await api.post<{ accessToken: string; me: Me }>('/auth/invite/accept', { token, password: f.password, givenName: f.givenName || data?.givenName, familyName: f.familyName || data?.familyName })
      setSession(r.accessToken, r.me); nav('/projects', { replace: true })
    } catch (x) { setErr((x as Error).message) } finally { setBusy(false) }
  }
  if (isLoading) return <AuthShell title="Opening your invitation…"><div className="empty">One moment.</div></AuthShell>
  if (error || !data) return <AuthShell title="This invitation isn't valid" lead="It may have expired or already been used. Ask your administrator to send a new one." footer={<Link to="/signin">Sign in</Link>}><div /></AuthShell>
  return (
    <AuthShell title={`Join ${data.companyName}`} lead={<>{data.invitedBy ? `${data.invitedBy} invited you` : 'You are invited'} as <b>{data.roleCode}</b>. Choose a password for <b>{data.email}</b>.</>} footer={<Link to="/signin">Already have an account? Sign in</Link>}>
      <form onSubmit={submit}>
        <div className="fgrid" style={{ gridTemplateColumns: '1fr 1fr' }}>
          <div className="field"><label>First name</label><input className="inp" value={f.givenName || data.givenName || ''} onChange={e => setF({ ...f, givenName: e.target.value })} required /></div>
          <div className="field"><label>Last name</label><input className="inp" value={f.familyName || data.familyName || ''} onChange={e => setF({ ...f, familyName: e.target.value })} required /></div>
        </div>
        <div className="field"><label>Password</label><input className="inp" type="password" autoFocus autoComplete="new-password" value={f.password} onChange={e => setF({ ...f, password: e.target.value })} required minLength={10} /><small>At least 10 characters, mixing letters with numbers or symbols.</small></div>
        <div className="field"><label>Again, to be sure</label><input className="inp" type="password" autoComplete="new-password" value={f.again} onChange={e => setF({ ...f, again: e.target.value })} required /></div>
        <AuthError error={err} />
        <button className="btn auth-btn" disabled={busy}>{busy ? 'Joining…' : `Join ${data.companyName}`}</button>
      </form>
    </AuthShell>
  )
}
