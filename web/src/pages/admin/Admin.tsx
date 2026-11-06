// Company administration: settings, people & access, trade partner directory, employees & roles,
// the template library and the audit trail. Administrators only (the route guard checks).
import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { useNavigate, useParams } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api/client'
import { useAuth } from '../../app/auth'
import { ADMIN_PAGES, PageHead, Shell, type AdminKey } from '../../components/Shell'
import { Icon } from '../../components/Icon'
import { Drawer, EditCell, Kpi, Loading, Modal, Tabs, toNum, useAction } from '../../components/ui'
import { useLookups } from '../../app/hooks'
import { date, money } from '../../app/format'
import { describe } from '../../app/verbs'
import { useToast } from '../../app/toast'

export default function Admin() {
  const { section } = useParams(); const nav = useNavigate()
  const key = (ADMIN_PAGES.some(p => p.key === section) ? section : 'company') as AdminKey
  const meta = ADMIN_PAGES.find(p => p.key === key)!
  return <Shell title={meta.label} crumbs={<><span>Administration</span><span>›</span><b>{meta.label}</b></>}>
    <PageHead eyebrow="Administration" title={meta.label} desc={DESC[key]} />
    <div className="card">
      <Tabs<AdminKey> value={key} onChange={k => nav(`/admin/${k}`)} items={ADMIN_PAGES.map(p => [p.key, p.label])} />
      <div className="cb">
        {key === 'company' && <Company />}
        {key === 'people' && <People />}
        {key === 'partners' && <Partners />}
        {key === 'staff' && <Staff />}
        {key === 'templates' && <Templates />}
        {key === 'audit' && <Audit />}
      </div>
    </div>
  </Shell>
}
const DESC: Record<AdminKey, string> = {
  company: 'Your company name, project numbering and brand colours. Everything in Gridline belongs to your company and only your people can see it.',
  people: 'Who can sign in and what they can do. Invite estimators, managers, viewers and other administrators by email.',
  partners: 'The subcontractors and suppliers you invite to bid. New names typed on a bid board are added here automatically.',
  staff: 'Employees for crew plans and precon teams, and the employee roles with their default posting rates.',
  templates: 'Scope kits, base requirement checklists, cost kits, qualification sets and precon tasks that new projects start from.',
  audit: 'Everything that changed across the company: who did what, when, on which project.',
}

// ------------------------------------------------------------------ company
interface CompanyDto { companyId: number; slug: string; displayName: string; legalName: string | null; timeZone: string; currency: string; brandPrimary: string; brandAccent: string; helpEmail: string | null; projectNoPrefix: string; nextProjectNo: number; plan: string; status: string; trialEndsOn: string | null; createdAt: string; users: number; projects: number; partners: number }
function Company() {
  const qc = useQueryClient(); const { run, busy } = useAction(); const { refresh } = useAuth()
  const { data } = useQuery({ queryKey: ['admin', 'company'], queryFn: () => api.get<CompanyDto>('/admin/company') })
  const [f, setF] = useState<Partial<CompanyDto> | null>(null)
  useEffect(() => { if (data) setF({ displayName: data.displayName, legalName: data.legalName ?? '', helpEmail: data.helpEmail ?? '', timeZone: data.timeZone, projectNoPrefix: data.projectNoPrefix, nextProjectNo: data.nextProjectNo, brandPrimary: data.brandPrimary, brandAccent: data.brandAccent }) }, [data])
  if (!data || !f) return <Loading what="company settings" />
  async function save(e: FormEvent) {
    e.preventDefault()
    if (await run(() => api.patch('/admin/company', f), 'Company settings saved')) { qc.invalidateQueries({ queryKey: ['admin', 'company'] }); refresh() }
  }
  const T = (k: keyof CompanyDto, l: string, extra?: React.InputHTMLAttributes<HTMLInputElement>) => <div className="field" key={k}><label>{l}</label><input className="inp" value={String(f![k] ?? '')} onChange={e => setF({ ...f, [k]: extra?.type === 'number' ? +e.target.value : e.target.value })} {...extra} /></div>
  return <form onSubmit={save}>
    <div className="kpis"><Kpi k="Plan" v={data.plan} d={data.trialEndsOn ? `trial ends ${date(data.trialEndsOn)}` : `since ${date(data.createdAt.slice(0, 10))}`} /><Kpi k="People" v={data.users} /><Kpi k="Projects" v={data.projects} /><Kpi k="Trade partners" v={data.partners} /></div>
    <div className="grid2">
      <section className="stack"><h3 className="sec-h">Identity</h3>
        {T('displayName', 'Company name')}{T('legalName', 'Legal name')}{T('helpEmail', 'Help email (shown to trade partners on bid links)', { type: 'email' })}
        <div className="field"><label>Time zone</label><select className="inp" value={f.timeZone} onChange={e => setF({ ...f, timeZone: e.target.value })}>{TZ.map(z => <option key={z} value={z}>{z}</option>)}</select></div>
        <div className="field"><label>Sign-in handle</label><input className="inp ro" readOnly value={data.slug} /><small>Used in links; contact support to change it.</small></div></section>
      <section className="stack"><h3 className="sec-h">Project numbering and brand</h3>
        <div className="fgrid">{T('projectNoPrefix', 'Project number prefix', { maxLength: 8 })}{T('nextProjectNo', 'Next number', { type: 'number', min: 1 })}</div>
        <small className="muted">The next project will be <b>{f.projectNoPrefix}{f.nextProjectNo}</b>. Imported projects can bring their own number.</small>
        <div className="fgrid">
          <div className="field"><label>Primary colour</label><div style={{ display: 'flex', gap: 8 }}><input type="color" value={f.brandPrimary} onChange={e => setF({ ...f, brandPrimary: e.target.value })} style={{ width: 44, height: 34, border: '1px solid var(--line2)', borderRadius: 6, background: '#fff' }} /><input className="inp mono" value={f.brandPrimary} onChange={e => setF({ ...f, brandPrimary: e.target.value })} /></div></div>
          <div className="field"><label>Accent colour</label><div style={{ display: 'flex', gap: 8 }}><input type="color" value={f.brandAccent} onChange={e => setF({ ...f, brandAccent: e.target.value })} style={{ width: 44, height: 34, border: '1px solid var(--line2)', borderRadius: 6, background: '#fff' }} /><input className="inp mono" value={f.brandAccent} onChange={e => setF({ ...f, brandAccent: e.target.value })} /></div></div>
        </div>
        <small className="muted">Brand colours appear on the bid portal your trade partners see and on exported PDFs.</small></section>
    </div>
    <div className="tb" style={{ border: 0, paddingLeft: 0, marginTop: 10 }}><button className="btn p" disabled={busy}><Icon name="check" />Save settings</button></div>
    <SecurityAndData />
  </form>
}
/** What a CIO asks before signing: how the data is protected, and how to get it out. */
function SecurityAndData() {
  const { run, busy } = useAction(); const { me } = useAuth()
  const items: [string, string, boolean][] = [
    ['Company isolation', 'Every project, bid, partner and template belongs to your company; other companies can never read it, not even benchmarks.', true],
    ['Passwords', 'PBKDF2-SHA256, at least 10 mixed characters. 8 wrong attempts lock an account for 15 minutes.', true],
    ['Sessions', '12 hours, or 30 days with "keep me signed in". Revocable per person; a password change signs everything else out.', true],
    ['Audit trail', 'Every change is recorded with who, when and on what. Administrators see the full company trail.', true],
    ['Encryption in transit', 'HTTPS is terminated at the reverse proxy in front of Gridline; HSTS is on when enabled by the host.', true],
    ['Single sign-on (Entra ID / Okta)', 'On the roadmap. Password sign-in stays available.', false],
    ['Two-factor authentication', 'On the roadmap.', false],
  ]
  return <section style={{ marginTop: 26 }}><h3 className="sec-h">Security &amp; your data</h3>
    <div className="sec-list">{items.map(([t, d, ok]) => <div key={t} className={ok ? '' : 'todo'}><Icon name={ok ? 'shield' : 'clock'} size="s" /><span><b>{t}</b><small>{d}</small></span></div>)}</div>
    <div className="tb" style={{ border: 0, paddingLeft: 0, marginTop: 12, gap: 12 }}>
      <button type="button" className="btn" disabled={busy} onClick={() => run(() => api.download('/admin/export.json', `${me?.company?.slug ?? 'company'}-export.json`), 'Company data exported')}><Icon name="down" />Export all company data (JSON)</button>
      <span className="muted" style={{ fontSize: 12 }}>People, trade partners, employees, every project with its versions, trades, bids and roll-up, and the template library. Your data is yours; take it with you any time.</span></div>
  </section>
}
const TZ = ['America/New_York', 'America/Chicago', 'America/Denver', 'America/Phoenix', 'America/Los_Angeles', 'America/Anchorage', 'Pacific/Honolulu', 'America/Toronto', 'America/Vancouver', 'Europe/London', 'Asia/Kolkata', 'Asia/Dubai', 'Australia/Sydney']

// ------------------------------------------------------------------ people & access
interface UserRow { userId: number; givenName: string; familyName: string; name: string; initials: string; colour: string; email: string; jobTitle: string | null; isActive: boolean; lastSeenAt: string | null; createdAt: string; lockedUntil: string | null; role: string; hasPassword: boolean; projects: number }
interface InviteRow { inviteId: number; email: string; givenName: string | null; familyName: string | null; role: string; createdAt: string; expiresAt: string; invitedBy: string | null }
interface PeopleData { users: UserRow[]; invites: InviteRow[]; roles: { code: string; name: string; summary: string }[] }
function People() {
  const qc = useQueryClient(); const { run, busy } = useAction(); const { me } = useAuth(); const toast = useToast()
  const { data } = useQuery({ queryKey: ['admin', 'users'], queryFn: () => api.get<PeopleData>('/admin/users') })
  const [invite, setInvite] = useState(false); const [link, setLink] = useState<{ email: string; link: string; emailSent: boolean } | null>(null)
  const reload = () => qc.invalidateQueries({ queryKey: ['admin', 'users'] })
  if (!data) return <Loading what="people" />
  const patch = async (u: UserRow, body: object, ok: string) => { if (await run(() => api.patch(`/admin/users/${u.userId}`, body), ok)) reload() }
  return <>
    <div className="tb" style={{ border: 0, padding: '0 0 10px' }}><span className="muted">{data.users.filter(u => u.isActive).length} active · {data.invites.length} invitation{data.invites.length === 1 ? '' : 's'} pending</span><span className="sp" />
      <button className="btn p" onClick={() => setInvite(true)}><Icon name="send" />Invite someone</button></div>
    <div className="gw auto"><table className="dg" style={{ width: '100%' }}><thead><tr><th>Person</th><th>Role</th><th>Projects</th><th>Last seen</th><th>Status</th><th /></tr></thead>
      <tbody>{data.users.map(u => <tr key={u.userId} style={u.isActive ? undefined : { opacity: .55 }}>
        <td><div style={{ display: 'flex', gap: 10, alignItems: 'center' }}><span className="pa" style={{ background: u.colour, width: 28, height: 28, borderRadius: '50%', display: 'grid', placeItems: 'center', color: '#fff', fontSize: 11, fontWeight: 800 }}>{u.initials}</span>
          <div><b>{u.name}</b>{u.userId === me?.userId && <span className="tag n" style={{ marginLeft: 6 }}>you</span>}<div className="code">{u.email}{u.jobTitle ? ` · ${u.jobTitle}` : ''}</div></div></div></td>
        <td><select className="inp" style={{ height: 30 }} value={u.role} disabled={u.userId === me?.userId} onChange={e => patch(u, { role: e.target.value }, `${u.name} is now ${e.target.value}`)} title={data.roles.find(r => r.code === u.role)?.summary}>
          {data.roles.map(r => <option key={r.code} value={r.code}>{r.name}</option>)}</select></td>
        <td className="n">{u.projects}</td><td>{u.lastSeenAt ? date(u.lastSeenAt.slice(0, 10)) : 'never'}</td>
        <td>{!u.isActive ? <span className="tag n">Deactivated</span> : u.lockedUntil && new Date(u.lockedUntil) > new Date() ? <span className="tag r">Locked</span> : !u.hasPassword ? <span className="tag y">No password yet</span> : <span className="tag g">Active</span>}</td>
        <td style={{ textAlign: 'right' }}>{u.userId !== me?.userId && (u.isActive
          ? <button className="btn sm" onClick={() => patch(u, { isActive: false }, `${u.name} deactivated`)}>Deactivate</button>
          : <button className="btn sm" onClick={() => patch(u, { isActive: true }, `${u.name} reactivated`)}>Reactivate</button>)}</td></tr>)}</tbody></table></div>
    {data.invites.length > 0 && <><h3 className="sec-h" style={{ marginTop: 18 }}>Pending invitations</h3>
      <div className="gw auto"><table className="dg" style={{ width: '100%' }}><thead><tr><th>Email</th><th>Name</th><th>Role</th><th>Invited</th><th>Expires</th><th /></tr></thead>
        <tbody>{data.invites.map(i => <tr key={i.inviteId}><td>{i.email}</td><td>{[i.givenName, i.familyName].filter(Boolean).join(' ') || '–'}</td><td>{i.role}</td><td>{date(i.createdAt.slice(0, 10))}{i.invitedBy ? ` by ${i.invitedBy}` : ''}</td><td>{date(i.expiresAt.slice(0, 10))}</td>
          <td style={{ textAlign: 'right' }}><button className="btn sm" onClick={async () => { if (await run(() => api.del(`/admin/users/invite/${i.inviteId}`), 'Invitation cancelled')) reload() }}>Cancel</button></td></tr>)}</tbody></table></div></>}
    <div className="roles-help">{data.roles.map(r => <div key={r.code}><b>{r.name}</b><small>{r.summary}</small></div>)}</div>
    {invite && <InviteModal roles={data.roles} busy={busy} onClose={() => setInvite(false)} onSend={async body => {
      const r = await run(() => api.post<{ link: string; emailSent: boolean; showLink: boolean; email: string }>('/admin/users/invite', body), 'Invitation created')
      if (r) { setInvite(false); reload(); if (r.showLink) setLink({ email: r.email, link: r.link, emailSent: r.emailSent }); else toast(`Invitation emailed to ${r.email}`) } }} />}
    {link && <Modal title="Invitation link" onClose={() => setLink(null)} footer={<button className="btn p" onClick={() => setLink(null)}>Done</button>}>
      <p>{link.emailSent ? `An email went to ${link.email}. You can also share this link:` : `No email server is set up on this installation, so share this link with ${link.email} yourself:`}</p>
      <div style={{ display: 'flex', gap: 8 }}><input className="inp" readOnly value={link.link} style={{ flex: 1 }} onFocus={e => e.target.select()} />
        <button className="btn" onClick={() => navigator.clipboard.writeText(link.link).then(() => toast('Link copied'))}><Icon name="copy" />Copy</button></div>
      <p className="muted" style={{ fontSize: 12 }}>The link works for 7 days and can be used once.</p></Modal>}
  </>
}
function InviteModal({ roles, busy, onClose, onSend }: { roles: { code: string; name: string; summary: string }[]; busy: boolean; onClose(): void; onSend(b: object): void }) {
  const [f, setF] = useState({ email: '', givenName: '', familyName: '', role: 'estimator' })
  return <Modal title="Invite someone to your company" onClose={onClose} footer={<><button className="btn" onClick={onClose}>Cancel</button><button className="btn p" disabled={busy || !f.email.includes('@')} onClick={() => onSend(f)}><Icon name="send" />Send invitation</button></>}>
    <div className="stack" style={{ gap: 10 }}>
      <div className="field"><label>Work email</label><input className="inp" type="email" autoFocus value={f.email} onChange={e => setF({ ...f, email: e.target.value })} /></div>
      <div className="fgrid"><div className="field"><label>First name</label><input className="inp" value={f.givenName} onChange={e => setF({ ...f, givenName: e.target.value })} /></div><div className="field"><label>Last name</label><input className="inp" value={f.familyName} onChange={e => setF({ ...f, familyName: e.target.value })} /></div></div>
      <div className="field"><label>Role</label><select className="inp" value={f.role} onChange={e => setF({ ...f, role: e.target.value })}>{roles.map(r => <option key={r.code} value={r.code}>{r.name} · {r.summary}</option>)}</select></div>
    </div>
  </Modal>
}

// ------------------------------------------------------------------ trade partners
interface PartnerRow { partnerId: number; companyName: string; website: string | null; isPrequalified: boolean; prequalExpires: string | null; diverseTags: string[]; addedByUser: boolean; contactName: string | null; contactEmail: string | null; contactPhone: string | null; city: string | null; stateCode: string | null; bids: number; picked: number; csiCodes: string | null }
function Partners() {
  const qc = useQueryClient(); const { run, busy } = useAction(); const { can } = useAuth()
  const [q, setQ] = useState(''); const [page, setPage] = useState(1); const [edit, setEdit] = useState<PartnerRow | 'new' | null>(null)
  const { data } = useQuery({ queryKey: ['admin', 'partners', q, page], queryFn: () => api.get<{ rows: PartnerRow[]; total: number; size: number }>(`/admin/partners?search=${encodeURIComponent(q)}&page=${page}`) })
  const reload = () => qc.invalidateQueries({ queryKey: ['admin', 'partners'] })
  const canEdit = can('project.edit')
  return <>
    <div className="tb" style={{ border: 0, padding: '0 0 10px' }}><label className="sbox"><Icon name="search" size="s" /><input placeholder="Search company, contact or email" value={q} onChange={e => { setQ(e.target.value); setPage(1) }} style={{ width: 280 }} /></label>
      <span className="muted">{data ? `${data.total.toLocaleString()} trade partners` : ''}</span><span className="sp" />{canEdit && <button className="btn p" onClick={() => setEdit('new')}><Icon name="plus" />Add trade partner</button>}</div>
    {!data ? <Loading what="trade partners" /> : <div className="gw auto"><table className="dg" style={{ width: '100%' }}><thead><tr><th>Company</th><th>Contact</th><th>Location</th><th>Trades (CSI)</th><th className="r">Bids</th><th className="r">Picked</th><th>Prequal</th><th /></tr></thead>
      <tbody>{data.rows.map(p => <tr key={p.partnerId}><td><b>{p.companyName}</b>{p.addedByUser && <sup title="Added from a bid board, not the directory" style={{ color: 'var(--accent-d)' }}>*</sup>}{p.diverseTags.length > 0 && <span className="tag b" style={{ marginLeft: 6 }}>{p.diverseTags.join(', ')}</span>}</td>
        <td>{p.contactName ?? ''}<div className="code">{[p.contactEmail, p.contactPhone].filter(Boolean).join(' · ')}</div></td><td>{[p.city, p.stateCode].filter(Boolean).join(', ')}</td><td className="code" style={{ whiteSpace: 'normal', maxWidth: 220 }}>{p.csiCodes ?? ''}</td>
        <td className="n">{p.bids}</td><td className="n">{p.picked}</td><td>{p.isPrequalified ? <span className="tag g">Yes{p.prequalExpires ? ` · ${date(p.prequalExpires)}` : ''}</span> : <span className="tag n">No</span>}</td>
        <td style={{ textAlign: 'right' }}>{canEdit && <button className="btn sm g" onClick={() => setEdit(p)}><Icon name="edit" size="s" />Edit</button>}</td></tr>)}
        {!data.rows.length && <tr><td colSpan={8}><div className="empty">No trade partners match.</div></td></tr>}</tbody></table>
      {data.total > data.size && <div className="pager"><span>Page {page} of {Math.ceil(data.total / data.size)}</span><span className="sp" /><button className="btn sm" disabled={page <= 1} onClick={() => setPage(p => p - 1)}>Previous</button><button className="btn sm" disabled={page * data.size >= data.total} onClick={() => setPage(p => p + 1)}>Next</button></div>}</div>}
    {edit && <PartnerDrawer p={edit === 'new' ? null : edit} busy={busy} onClose={() => setEdit(null)} onSave={async body => {
      const ok = edit === 'new' ? await run(() => api.post('/admin/partners', body), 'Trade partner added') : await run(() => api.patch(`/admin/partners/${edit.partnerId}`, body), 'Trade partner saved')
      if (ok) { setEdit(null); reload() } }} onDelete={edit === 'new' ? undefined : async () => { if (await run(() => api.del(`/admin/partners/${edit.partnerId}`), 'Trade partner deleted')) { setEdit(null); reload() } }} />}
  </>
}
function PartnerDrawer({ p, busy, onClose, onSave, onDelete }: { p: PartnerRow | null; busy: boolean; onClose(): void; onSave(b: object): void; onDelete?(): void }) {
  const { isAdmin } = useAuth(); const lk = useLookups()
  const [f, setF] = useState({ companyName: p?.companyName ?? '', website: p?.website ?? '', isPrequalified: p?.isPrequalified ?? false, prequalExpires: p?.prequalExpires ?? '', diverse: p?.diverseTags.includes('diverse') ?? false,
    contactName: p?.contactName ?? '', contactEmail: p?.contactEmail ?? '', contactPhone: p?.contactPhone ?? '', city: p?.city ?? '', stateCode: p?.stateCode ?? '', csi: p?.csiCodes ?? '' })
  const codes = f.csi.split(/[,;\n]/).map(s => s.trim()).filter(Boolean)
  return <Drawer onClose={onClose} head={<div><h3 style={{ margin: 0 }}>{p ? p.companyName : 'New trade partner'}</h3><small className="muted">{p ? `${p.bids} bids · ${p.picked} picked` : 'Added to your company directory'}</small></div>}>
    <div className="stack" style={{ gap: 12 }}>
      <div className="field"><label>Company name</label><input className="inp" autoFocus value={f.companyName} onChange={e => setF({ ...f, companyName: e.target.value })} /></div>
      <div className="fgrid"><div className="field"><label>Website</label><input className="inp" value={f.website} onChange={e => setF({ ...f, website: e.target.value })} placeholder="https://" /></div>
        <div className="field"><label>Diverse business</label><label className="check"><input type="checkbox" checked={f.diverse} onChange={e => setF({ ...f, diverse: e.target.checked })} /> MBE / WBE / DBE etc.</label></div></div>
      <h4 className="sec-h" style={{ margin: '6px 0 0' }}>Primary contact</h4>
      <div className="fgrid"><div className="field"><label>Name</label><input className="inp" value={f.contactName} onChange={e => setF({ ...f, contactName: e.target.value })} /></div>
        <div className="field"><label>Email</label><input className="inp" type="email" value={f.contactEmail} onChange={e => setF({ ...f, contactEmail: e.target.value })} /></div>
        <div className="field"><label>Phone</label><input className="inp" value={f.contactPhone} onChange={e => setF({ ...f, contactPhone: e.target.value })} /></div></div>
      <div className="fgrid"><div className="field"><label>City</label><input className="inp" value={f.city} onChange={e => setF({ ...f, city: e.target.value })} /></div>
        <div className="field"><label>State</label><select className="inp" value={f.stateCode} onChange={e => setF({ ...f, stateCode: e.target.value })}><option value="">–</option>{(lk.data?.states ?? []).map(s => <option key={s.stateCode} value={s.stateCode}>{s.stateCode} · {s.stateName}</option>)}</select></div></div>
      <h4 className="sec-h" style={{ margin: '6px 0 0' }}>Prequalification</h4>
      <div className="fgrid"><div className="field"><label>Prequalified</label><label className="check"><input type="checkbox" checked={f.isPrequalified} onChange={e => setF({ ...f, isPrequalified: e.target.checked })} /> Yes</label></div>
        <div className="field"><label>Expires</label><input className="inp" type="date" value={f.prequalExpires} onChange={e => setF({ ...f, prequalExpires: e.target.value })} /></div></div>
      <div className="field"><label>Trades bid (CSI codes, comma separated)</label><textarea className="inp" rows={2} value={f.csi} onChange={e => setF({ ...f, csi: e.target.value })} placeholder="03 30 00, 05 12 00" /><small>{codes.length} code{codes.length === 1 ? '' : 's'}</small></div>
      <div style={{ display: 'flex', gap: 8, marginTop: 6 }}>
        <button className="btn p" disabled={busy || !f.companyName.trim()} onClick={() => onSave({ companyName: f.companyName, website: f.website || null, isPrequalified: f.isPrequalified, prequalExpires: f.prequalExpires || null, diverseTags: f.diverse ? ['diverse'] : [], contactName: f.contactName, contactEmail: f.contactEmail, contactPhone: f.contactPhone, city: f.city, stateCode: f.stateCode || null, csiCodes: codes })}><Icon name="check" />{p ? 'Save' : 'Add trade partner'}</button>
        <button className="btn" onClick={onClose}>Cancel</button><span style={{ flex: 1 }} />
        {onDelete && isAdmin && <button className="btn d" disabled={busy} onClick={onDelete}><Icon name="trash" />Delete</button>}</div>
    </div>
  </Drawer>
}

// ------------------------------------------------------------------ employees & roles
interface StaffRow { staffId: number; fullName: string; workEmail: string | null; staffNo: string | null; staffRoleId: number | null; roleName: string | null; hourlyCost: number | null; billRate: number | null; teamGroup: string; isActive: boolean; projects: number }
interface RoleRow { staffRoleId: number; roleName: string; shortName: string | null; seniority: number | null; defaultRate: number | null; people: number }
function Staff() {
  const qc = useQueryClient(); const { run, busy } = useAction()
  const { data } = useQuery({ queryKey: ['admin', 'staff'], queryFn: () => api.get<{ staff: StaffRow[]; roles: RoleRow[] }>('/admin/staff') })
  const [add, setAdd] = useState(false); const [addRole, setAddRole] = useState(false); const [showInactive, setShowInactive] = useState(false)
  const reload = () => { qc.invalidateQueries({ queryKey: ['admin', 'staff'] }); qc.invalidateQueries({ queryKey: ['lookups'] }) }
  if (!data) return <Loading what="employees" />
  const patch = async (id: number, body: object, ok = 'Saved') => { if (await run(() => api.patch(`/admin/staff/${id}`, body), ok)) reload() }
  const patchRole = async (id: number, body: object) => { if (await run(() => api.patch(`/admin/staff-roles/${id}`, body), 'Role saved')) reload() }
  const GROUPS: [string, string][] = [['operations', 'Operations'], ['siteops', 'Site operations'], ['corporate', 'Corporate resources']]
  const rows = data.staff.filter(s => showInactive || s.isActive)
  return <div className="stack" style={{ gap: 20 }}>
    <div><div className="tb" style={{ border: 0, padding: '0 0 10px' }}><h3 className="sec-h" style={{ margin: 0 }}>Employees <small>{rows.length}</small></h3><label className="check" style={{ fontSize: 12 }}><input type="checkbox" checked={showInactive} onChange={e => setShowInactive(e.target.checked)} /> show inactive</label><span className="sp" /><button className="btn p sm" onClick={() => setAdd(true)}><Icon name="plus" size="s" />Add employee</button></div>
      <div className="gw auto"><table className="dg" style={{ width: '100%' }}><thead><tr><th>Name</th><th>Role</th><th>Team</th><th className="r">Posting rate</th><th className="r">Bill rate</th><th className="r">Projects</th><th /></tr></thead>
        <tbody>{rows.map(s => <tr key={s.staffId} style={s.isActive ? undefined : { opacity: .55 }}>
          <td><EditCell value={s.fullName} onSave={v => v.trim() && patch(s.staffId, { fullName: v })} /><div className="code">{s.workEmail ?? ''}{s.staffNo ? ` · #${s.staffNo}` : ''}</div></td>
          <td><select className="inp" style={{ height: 28 }} value={s.staffRoleId ?? ''} onChange={e => patch(s.staffId, { staffRoleId: +e.target.value })}><option value="">–</option>{data.roles.map(r => <option key={r.staffRoleId} value={r.staffRoleId}>{r.roleName}</option>)}</select></td>
          <td><select className="inp" style={{ height: 28 }} value={s.teamGroup} onChange={e => patch(s.staffId, { teamGroup: e.target.value })}>{GROUPS.map(([k, l]) => <option key={k} value={k}>{l}</option>)}</select></td>
          <td className="n"><EditCell number value={s.hourlyCost} placeholder="$/h" onSave={v => { const n = toNum(v); if (n != null) patch(s.staffId, { hourlyCost: n }) }} style={{ textAlign: 'right', width: 90 }} /></td>
          <td className="n"><EditCell number value={s.billRate} placeholder="$/h" onSave={v => { const n = toNum(v); if (n != null) patch(s.staffId, { billRate: n }) }} style={{ textAlign: 'right', width: 90 }} /></td>
          <td className="n">{s.projects}</td>
          <td style={{ textAlign: 'right' }}><button className="btn sm g" onClick={() => patch(s.staffId, { isActive: !s.isActive }, s.isActive ? 'Marked inactive' : 'Marked active')}>{s.isActive ? 'Deactivate' : 'Activate'}</button></td></tr>)}</tbody></table></div></div>
    <div style={{ maxWidth: 720 }}><div className="tb" style={{ border: 0, padding: '0 0 10px' }}><h3 className="sec-h" style={{ margin: 0 }}>Employee roles</h3><span className="sp" /><button className="btn sm" onClick={() => setAddRole(true)}><Icon name="plus" size="s" />Add role</button></div>
      <div className="gw auto"><table className="dg" style={{ width: '100%' }}><thead><tr><th>Role</th><th className="r">Default rate</th><th className="r">People</th><th /></tr></thead>
        <tbody>{data.roles.map(r => <tr key={r.staffRoleId}><td><EditCell value={r.roleName} onSave={v => v.trim() && patchRole(r.staffRoleId, { roleName: v })} /></td>
          <td className="n"><EditCell number value={r.defaultRate} placeholder="$/h" onSave={v => { const n = toNum(v); if (n != null) patchRole(r.staffRoleId, { defaultRate: n }) }} style={{ textAlign: 'right', width: 90 }} /></td><td className="n">{r.people}</td>
          <td style={{ textAlign: 'right' }}>{r.people === 0 && <button className="rm" style={{ opacity: 1 }} title="Delete" onClick={async () => { if (await run(() => api.del(`/admin/staff-roles/${r.staffRoleId}`), 'Role deleted')) reload() }}><Icon name="x" size="s" /></button>}</td></tr>)}</tbody></table></div>
      <p className="muted" style={{ fontSize: 12 }}>The default rate is the posting rate a crew member starts with when no person is named yet (TBA).</p></div>
    {add && <Modal title="Add employee" onClose={() => setAdd(false)}><StaffForm roles={data.roles} busy={busy} onSave={async b => { if (await run(() => api.post('/admin/staff', b), 'Employee added')) { setAdd(false); reload() } }} /></Modal>}
    {addRole && <Modal title="Add employee role" onClose={() => setAddRole(false)}><RoleForm busy={busy} onSave={async b => { if (await run(() => api.post('/admin/staff-roles', b), 'Role added')) { setAddRole(false); reload() } }} /></Modal>}
  </div>
}
function StaffForm({ roles, busy, onSave }: { roles: RoleRow[]; busy: boolean; onSave(b: object): void }) {
  const [f, setF] = useState({ fullName: '', workEmail: '', staffNo: '', staffRoleId: String(roles[0]?.staffRoleId ?? ''), hourlyCost: '', billRate: '', teamGroup: 'operations' })
  return <div className="stack" style={{ gap: 10 }}>
    <div className="field"><label>Full name</label><input className="inp" autoFocus value={f.fullName} onChange={e => setF({ ...f, fullName: e.target.value })} /></div>
    <div className="fgrid"><div className="field"><label>Work email</label><input className="inp" value={f.workEmail} onChange={e => setF({ ...f, workEmail: e.target.value })} /></div><div className="field"><label>Employee no.</label><input className="inp" value={f.staffNo} onChange={e => setF({ ...f, staffNo: e.target.value })} /></div></div>
    <div className="fgrid"><div className="field"><label>Role</label><select className="inp" value={f.staffRoleId} onChange={e => setF({ ...f, staffRoleId: e.target.value })}>{roles.map(r => <option key={r.staffRoleId} value={r.staffRoleId}>{r.roleName}</option>)}</select></div>
      <div className="field"><label>Team</label><select className="inp" value={f.teamGroup} onChange={e => setF({ ...f, teamGroup: e.target.value })}><option value="operations">Operations</option><option value="siteops">Site operations</option><option value="corporate">Corporate resources</option></select></div></div>
    <div className="fgrid"><div className="field"><label>Posting rate ($/h)</label><input className="inp num" value={f.hourlyCost} onChange={e => setF({ ...f, hourlyCost: e.target.value })} /></div><div className="field"><label>Bill rate ($/h)</label><input className="inp num" value={f.billRate} onChange={e => setF({ ...f, billRate: e.target.value })} /></div></div>
    <div><button className="btn p" disabled={busy || !f.fullName.trim()} onClick={() => onSave({ ...f, staffRoleId: f.staffRoleId ? +f.staffRoleId : null, hourlyCost: toNum(f.hourlyCost), billRate: toNum(f.billRate) })}><Icon name="plus" />Add</button></div>
  </div>
}
function RoleForm({ busy, onSave }: { busy: boolean; onSave(b: object): void }) {
  const [f, setF] = useState({ roleName: '', shortName: '', seniority: '', defaultRate: '' })
  return <div className="stack" style={{ gap: 10 }}>
    <div className="field"><label>Role name</label><input className="inp" autoFocus value={f.roleName} onChange={e => setF({ ...f, roleName: e.target.value })} placeholder="Senior estimator" /></div>
    <div className="fgrid"><div className="field"><label>Short name</label><input className="inp" value={f.shortName} onChange={e => setF({ ...f, shortName: e.target.value })} /></div><div className="field"><label>Seniority (1–9)</label><input className="inp num" value={f.seniority} onChange={e => setF({ ...f, seniority: e.target.value })} /></div><div className="field"><label>Default rate ($/h)</label><input className="inp num" value={f.defaultRate} onChange={e => setF({ ...f, defaultRate: e.target.value })} /></div></div>
    <div><button className="btn p" disabled={busy || !f.roleName.trim()} onClick={() => onSave({ roleName: f.roleName, shortName: f.shortName || null, seniority: toNum(f.seniority), defaultRate: toNum(f.defaultRate) })}><Icon name="plus" />Add role</button></div>
  </div>
}

// ------------------------------------------------------------------ template library
interface Tpl { id: number; name: string; lines: number; isActive?: boolean; isTemplate?: boolean; isDefault?: boolean; area?: string; csiCode?: string | null; used?: number; sortOrder?: number }
interface TplData { scopeKits: Tpl[]; requirementSets: Tpl[]; costKits: Tpl[]; qualificationSets: Tpl[]; preconTasks: Tpl[] }
function Templates() {
  const qc = useQueryClient(); const { run } = useAction()
  const { data } = useQuery({ queryKey: ['admin', 'templates'], queryFn: () => api.get<TplData>('/admin/templates') })
  const [view, setView] = useState<{ kind: string; t: Tpl } | null>(null)
  const reload = () => { qc.invalidateQueries({ queryKey: ['admin', 'templates'] }); qc.invalidateQueries({ queryKey: ['lookups'] }) }
  if (!data) return <Loading what="templates" />
  const patch = async (kind: string, t: Tpl, body: object) => { if (await run(() => api.patch(`/admin/templates/${kind}/${t.id}`, body), 'Saved')) reload() }
  const Section = ({ kind, title, rows, desc, extra }: { kind: string; title: string; rows: Tpl[]; desc: string; extra?: (t: Tpl) => React.ReactNode }) => <div className="card" style={{ marginBottom: 12 }}>
    <div className="ch"><h3>{title}</h3><span className="muted">{desc}</span></div>
    <div className="gw auto"><table className="dg" style={{ width: '100%' }}><thead><tr><th>Name</th><th className="r">Lines</th>{extra && <th />}<th>Status</th><th /></tr></thead>
      <tbody>{rows.map(t => <tr key={t.id}><td><EditCell value={t.name} onSave={v => v.trim() && patch(kind, t, { name: v })} /></td><td className="n">{t.lines}</td>{extra && <td>{extra(t)}</td>}
        <td>{'isActive' in t ? <label className="check" style={{ fontSize: 12 }}><button className="tog" role="switch" aria-checked={t.isActive !== false} onClick={() => patch(kind, t, { isActive: t.isActive === false })} /> {t.isActive === false ? 'off' : 'on'}</label>
          : 'isDefault' in t ? <label className="check" style={{ fontSize: 12 }}><input type="checkbox" checked={!!t.isDefault} onChange={e => patch(kind, t, { isDefault: e.target.checked })} /> applied to new projects</label> : <span className="tag g">template</span>}</td>
        <td style={{ textAlign: 'right' }}>{kind !== 'precon-task' && <button className="btn sm g" onClick={() => setView({ kind, t })}><Icon name="eye" size="s" />View lines</button>}</td></tr>)}
        {!rows.length && <tr><td colSpan={5}><div className="empty">None yet.</div></td></tr>}</tbody></table></div></div>
  return <>
    <Section kind="scope-kit" title="Scope kits" rows={data.scopeKits} desc="Trade scope line templates applied to a trade in Scope match." extra={t => <span className="code">{t.csiCode ?? ''}</span>} />
    <Section kind="requirement-set" title="Base requirement checklists" rows={data.requirementSets} desc="The general scope every trade partner answers; a new project starts from the first one." extra={t => <span className="muted">{t.used} version{t.used === 1 ? '' : 's'} use it</span>} />
    <Section kind="cost-kit" title="Cost kits" rows={data.costKits} desc="Starter lines for Precon services and Project overheads." extra={t => <span className="tag n">{t.area === 'precon' ? 'Precon services' : 'Project overheads'}</span>} />
    <Section kind="qualification-set" title="Qualification sets" rows={data.qualificationSets} desc="Assumptions, exclusions and qualifications added to a trade in one go." />
    <Section kind="precon-task" title="Precon tasks" rows={data.preconTasks} desc="The task columns on Precon services; ticked ones are on every new version." />
    {view && <TemplateLines kind={view.kind} t={view.t} onClose={() => setView(null)} />}
  </>
}
function TemplateLines({ kind, t, onClose }: { kind: string; t: Tpl; onClose(): void }) {
  const { data } = useQuery({ queryKey: ['admin', 'template-lines', kind, t.id], queryFn: () => api.get<{ lineId: number; sortKey: number; section?: string | null; isHeading?: boolean; text: string; desired?: string | null; qty?: number | null; uom?: string | null; rate?: number | null }[]>(`/admin/templates/${kind}/${t.id}/lines`) })
  return <Drawer onClose={onClose} head={<div><h3 style={{ margin: 0 }}>{t.name}</h3><small className="muted">{t.lines} lines · edit lines on a project and save back as a template in a later release</small></div>}>
    {!data ? <Loading what="lines" /> : <div className="gw auto"><table className="dg" style={{ width: '100%' }}><thead><tr><th>#</th><th>{kind === 'cost-kit' ? 'Category' : 'Section'}</th><th>Text</th><th>{kind === 'cost-kit' ? 'Qty · unit · rate' : 'Desired'}</th></tr></thead>
      <tbody>{data.map((l, i) => <tr key={l.lineId} className={l.isHeading ? 'grp' : ''}><td className="n mut">{l.isHeading ? '' : i + 1}</td><td>{l.section ?? ''}</td><td style={{ whiteSpace: 'normal' }}>{l.text}</td>
        <td>{kind === 'cost-kit' ? `${l.qty ?? ''} ${l.uom ?? ''} ${l.rate != null ? money(l.rate, 2) : ''}` : l.desired ?? ''}</td></tr>)}</tbody></table></div>}
  </Drawer>
}

// ------------------------------------------------------------------ audit
interface AuditRow { eventId: number; happenedAt: string; verb: string; subjectType: string; subjectKey: string; projectId: number | null; projectNo: string | null; projectName: string | null; personName: string | null; initials: string | null; colour: string | null }
function Audit() {
  const [verb, setVerb] = useState(''); const [pages, setPages] = useState<AuditRow[][]>([])
  const { data, isFetching } = useQuery({ queryKey: ['admin', 'audit', verb], queryFn: () => api.get<AuditRow[]>(`/admin/audit${verb ? `?verb=${encodeURIComponent(verb)}` : ''}`) })
  useEffect(() => { setPages([]) }, [verb])
  const rows = useMemo(() => [...(data ?? []), ...pages.flat()], [data, pages])
  async function more() { const last = rows[rows.length - 1]; if (!last) return; const next = await api.get<AuditRow[]>(`/admin/audit?before=${last.eventId}${verb ? `&verb=${encodeURIComponent(verb)}` : ''}`); setPages(p => [...p, next]) }
  const FILTERS: [string, string][] = [['', 'Everything'], ['bid.', 'Bids'], ['trade.', 'Trades'], ['answer.', 'Scope answers'], ['version.', 'Versions'], ['project.', 'Projects'], ['user.', 'People'], ['partner.', 'Trade partners'], ['company.', 'Company'], ['import.', 'Imports']]
  return <>
    <div className="pills" style={{ marginBottom: 10 }}>{FILTERS.map(([k, l]) => <button key={k} aria-pressed={verb === k} onClick={() => setVerb(k)}>{l}</button>)}</div>
    {!data ? <Loading what="the audit trail" /> : <div className="timeline">{rows.map(e => <div key={e.eventId} className="li" style={{ display: 'flex', gap: 10, alignItems: 'center', padding: '6px 0', borderBottom: '1px solid var(--line)' }}>
      <span className="pa" style={{ background: e.colour ?? '#888', width: 28, height: 28, borderRadius: '50%', display: 'grid', placeItems: 'center', color: '#fff', fontSize: 11, fontWeight: 800, flex: 'none' }}>{e.initials ?? 'SY'}</span>
      <div style={{ flex: 1, minWidth: 0 }}><b>{e.personName ?? 'Gridline'}</b> {describe(e.verb)}{e.projectNo && <> on <b>{e.projectName}</b> <span className="code">{e.projectNo}</span></>}
        <div className="muted" style={{ fontSize: 11.5 }}>{new Date(e.happenedAt).toLocaleString()} · {e.verb}</div></div></div>)}
      {!rows.length && <div className="empty">Nothing yet.</div>}
      {rows.length >= 100 && <div className="tb" style={{ border: 0, justifyContent: 'center' }}><button className="btn" disabled={isFetching} onClick={more}>Load older</button></div>}</div>}
  </>
}
