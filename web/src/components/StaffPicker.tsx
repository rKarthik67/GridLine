// Pick a person for a team (was: Add Team Member / Change Employee): search, employee group, role, team member or TBA.
import { useMemo, useState, type ReactNode } from 'react'
import { TEAM_GROUPS, type TeamGroup } from '../api/types'
import { useLookups } from '../app/hooks'
import { money } from '../app/format'
import { Icon } from './Icon'
import { Modal } from './ui'

export interface Picked { teamGroup: TeamGroup; staffRoleId: number | null; staffId: number | null; rate: number | null }
export function StaffPicker({ title, initial, saveLabel, busy, onClose, onSave, children, rateLabel = 'Avg. posting rate' }: {
  title: string; initial?: Partial<Picked>; saveLabel: string; busy?: boolean; onClose(): void; onSave(p: Picked): void; children?: ReactNode; rateLabel?: string
}) {
  const lk = useLookups()
  const [p, setP] = useState<Picked>({ teamGroup: initial?.teamGroup ?? 'operations', staffRoleId: initial?.staffRoleId ?? null, staffId: initial?.staffId ?? null, rate: initial?.rate ?? null })
  const [q, setQ] = useState('')
  const staff = lk.data?.staff ?? [], roles = lk.data?.staffRoles ?? []
  const roleCount = (id: number) => staff.filter(s => s.staffRoleId === id && s.teamGroup === p.teamGroup).length
  const people = staff.filter(s => s.teamGroup === p.teamGroup && (p.staffRoleId == null || s.staffRoleId === p.staffRoleId))
  const hits = useMemo(() => q.trim() ? staff.filter(s => s.fullName.toLowerCase().includes(q.trim().toLowerCase())).slice(0, 8) : [], [q, staff])
  const roleRate = (id: number | null) => roles.find(r => r.staffRoleId === id)?.defaultRate ?? null
  const pickPerson = (id: number | null) => { const s = staff.find(x => x.staffId === id)
    setP(o => ({ ...o, staffId: id, staffRoleId: s?.staffRoleId ?? o.staffRoleId, teamGroup: s?.teamGroup ?? o.teamGroup, rate: s?.hourlyCost ?? roleRate(s?.staffRoleId ?? o.staffRoleId) })) }
  const ok = p.staffRoleId != null || p.staffId != null
  return <Modal title={title} onClose={onClose} footer={<><button className="btn" onClick={onClose}>Cancel</button><button className="btn p" disabled={!ok || busy} onClick={() => onSave(p)}><Icon name="check" />{saveLabel}</button></>}>
    <div className="field"><label>Search</label><input className="inp" placeholder="Type a name to find someone" value={q} onChange={e => setQ(e.target.value)} autoFocus />
      {hits.length > 0 && <div className="pick-hits">{hits.map(s => <button key={s.staffId} className={p.staffId === s.staffId ? 'on' : ''} onClick={() => { pickPerson(s.staffId); setQ('') }}>
        <b>{s.fullName}</b><small>{roles.find(r => r.staffRoleId === s.staffRoleId)?.roleName} · {TEAM_GROUPS.find(g => g[0] === s.teamGroup)?.[1]} · {money(s.hourlyCost, 2)}/h</small></button>)}</div>}</div>
    <div className="field"><label>Employee group</label><div className="seg">{TEAM_GROUPS.map(([g, l]) =>
      <button key={g} aria-pressed={p.teamGroup === g} onClick={() => setP(o => ({ ...o, teamGroup: g, staffId: null }))}>{l}</button>)}</div></div>
    <div className="fgrid">
      <div className="field"><label>Employee role</label><select className="inp" value={p.staffRoleId ?? ''} onChange={e => { const id = e.target.value ? +e.target.value : null; setP(o => ({ ...o, staffRoleId: id, staffId: null, rate: roleRate(id) })) }}>
        <option value="">Choose a role…</option>{roles.map(r => <option key={r.staffRoleId} value={r.staffRoleId}>{r.roleName} ({roleCount(r.staffRoleId)})</option>)}</select></div>
      <div className="field"><label>Team member</label><select className="inp" value={p.staffId ?? ''} onChange={e => pickPerson(e.target.value ? +e.target.value : null)}>
        <option value="">TBA (not named yet)</option>{people.map(s => <option key={s.staffId} value={s.staffId}>{s.fullName}</option>)}</select></div>
      <div className="field"><label>{rateLabel}</label><input className="inp num" value={p.rate ?? ''} placeholder="$/h" onChange={e => { const v = e.target.value.replace(/[$,]/g, ''); setP(o => ({ ...o, rate: v === '' || isNaN(+v) ? null : +v })) }} /></div>
      {children}
    </div>
  </Modal>
}
