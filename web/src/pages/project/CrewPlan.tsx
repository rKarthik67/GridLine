// Crew plan (was: Project team). Two tabs like today: Team members (rates, weeks, per diem, actual vs quoted, columns you pick)
// and Resource loader (hours per week or per month). Right-click a person for: Note, Set hours/budget, Set per diem,
// Change employee, Add team member, Delete team member. The weeks come from the dates in Project profile.
// The crew's quoted total is "General conditions" on the Price roll-up.
import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api/client'
import { TEAM_GROUPS, type CrewData, type CrewMember, type CrewWeek, type PerDiemPlace, type ProjectDetail, type TeamGroup, type Version } from '../../api/types'
import { PageHead } from '../../components/Shell'
import { Icon } from '../../components/Icon'
import { AskText, ContextMenu, EditCell, ExportMenu, Failed, Loading, Modal, Seg, Tabs, toNum, useAction, type CtxItem } from '../../components/ui'
import { StaffPicker } from '../../components/StaffPicker'
import { date, money, num } from '../../app/format'
import { useLookups, useOthersFocus, vkey } from '../../app/hooks'
import { perDiem, phaseCost, phaseHours, phaseWeeks } from '../../app/crewCalc'

type Tab = 'members' | 'loader'
interface Col { key: string; label: string; was?: string; get(m: CrewMember): number | null; fmt: 'money' | 'money2' | 'num' | 'pct'; total?: boolean; edit?: 'weeksBuild' | 'weeksClose' | 'reloc' | 'pct'; click?: 'perdiem' | 'budget' }
const COLS: Col[] = [
  { key: 'postingRate', label: 'Posting rate', get: m => m.hourlyCost, fmt: 'money2' },
  { key: 'constructionWeeks', label: 'Contr weeks', get: m => m.constructionWeeks, fmt: 'num', edit: 'weeksBuild' },
  { key: 'closeoutWeeks', label: 'Closeout weeks', get: m => m.closeoutWeeks, fmt: 'num', edit: 'weeksClose' },
  { key: 'constructionHours', label: 'Contr hrs', get: m => m.constructionHours, fmt: 'num', total: true },
  { key: 'closeoutHours', label: 'Closeout hrs', get: m => m.closeoutHours, fmt: 'num', total: true },
  { key: 'totalHours', label: 'Total hrs', get: m => m.totalHours, fmt: 'num', total: true },
  { key: 'constructionTimePct', label: '% constr time', get: m => m.constructionTimePct, fmt: 'pct' },
  { key: 'perDiem', label: 'Per diem', was: 'Temp living / per diem', get: m => m.perDiemCost, fmt: 'money', total: true, click: 'perdiem' },
  { key: 'relocation', label: 'Relocation', get: m => m.relocationCost, fmt: 'money', total: true, edit: 'reloc' },
  { key: 'finalRate', label: 'Act final rate', get: m => m.finalRate, fmt: 'money2' },
  { key: 'weeklyRate', label: 'Act wkly rate', get: m => m.weeklyRate, fmt: 'money2' },
  { key: 'quotedPct', label: '% quoted', was: 'RateDiscountPC', get: m => m.quotedPct, fmt: 'pct', edit: 'pct' },
  { key: 'quotedRate', label: 'Quoted rate', get: m => m.quotedRate, fmt: 'money2' },
  { key: 'quotedWeeklyRate', label: 'Quoted wkly rate', get: m => m.quotedWeeklyRate, fmt: 'money2' },
  { key: 'constructionCost', label: 'Constr actual', get: m => m.constructionCost, fmt: 'money', total: true, click: 'budget' },
  { key: 'closeoutCost', label: 'Closeout actual', get: m => m.closeoutCost, fmt: 'money', total: true, click: 'budget' },
  { key: 'totalCost', label: 'Total actual', get: m => m.totalCost, fmt: 'money', total: true },
  { key: 'constructionQuoted', label: 'Constr quoted', get: m => m.constructionQuoted, fmt: 'money', total: true },
  { key: 'closeoutQuoted', label: 'Closeout quoted', get: m => m.closeoutQuoted, fmt: 'money', total: true },
  { key: 'totalQuoted', label: 'Total quoted', get: m => m.totalQuoted, fmt: 'money', total: true },
  { key: 'quotedOfCost', label: 'Quoted ÷ actual', get: m => m.quotedOfCostPct, fmt: 'pct' },
]
const DEFAULT_COLS = ['postingRate', 'constructionWeeks', 'closeoutWeeks', 'totalHours', 'perDiem', 'relocation', 'quotedPct', 'totalCost', 'totalQuoted']
const f = (v: number | null, t: Col['fmt']) => v == null ? '–' : t === 'money' ? money(v) : t === 'money2' ? money(v, 2) : t === 'pct' ? `${num(v, v % 1 ? 1 : 0)}%` : num(v, v % 1 ? 1 : 0)
const groupName = (g: TeamGroup) => TEAM_GROUPS.find(x => x[0] === g)![1]
const fmtWeek = (s: string) => new Date(s + 'T00:00:00').toLocaleDateString('en-US', { month: 'short', day: 'numeric' })
const store = { get: (k: string) => { try { return localStorage.getItem(k) } catch { return null } }, set: (k: string, v: string) => { try { localStorage.setItem(k, v) } catch { /* ignore */ } } }
type ModalState = null | { kind: 'add'; group?: TeamGroup } | { kind: 'change' | 'budget' | 'perdiem' | 'note'; m: CrewMember } | { kind: 'cols' }

export default function CrewPlan({ project, version }: { project: ProjectDetail; version: Version }) {
  const pid = project.projectId, vid = version.versionId; const qc = useQueryClient(); const { run, busy } = useAction(); const lk = useLookups(); const nav = useNavigate()
  const key = vkey('crew', vid, `p${pid}`)
  const { data, error, isLoading } = useQuery({ queryKey: key, queryFn: () => api.get<CrewData>(`/projects/${pid}/crew`) })
  const [tab, setTab] = useState<Tab>('members'); const [q, setQ] = useState(''); const [collapsed, setCollapsed] = useState<Set<string>>(new Set())
  const [period, setPeriod] = useState<'week' | 'month'>(() => store.get('gridline.crew.period') === 'week' ? 'week' : 'month')
  const [ctx, setCtx] = useState<{ x: number; y: number; items: CtxItem[] } | null>(null)
  const [modal, setModal] = useState<ModalState>(null)
  const [drag, setDrag] = useState<number | null>(null)
  const others = useOthersFocus('crew')
  const canEdit = project.access !== 'viewer'
  useEffect(() => store.set('gridline.crew.period', period), [period])
  const put = (d: CrewData | undefined) => { if (d) { qc.setQueryData(key, d); qc.invalidateQueries({ queryKey: vkey('price-roll-up', vid) }) } return d }
  const months = useMemo(() => { const m = new Map<number, CrewWeek[]>(); for (const w of data?.weeks ?? []) m.set(w.monthNo, [...(m.get(w.monthNo) ?? []), w]); return [...m.entries()] }, [data?.weeks])
  if (isLoading) return <Loading what="the crew plan" />
  if (error || !data) return <Failed error={error} />
  const cols = (data.columns ?? DEFAULT_COLS).map(k => COLS.find(c => c.key === k)).filter(Boolean) as Col[]
  const members = data.members.filter(m => !q || `${m.staffName ?? 'TBA'} ${m.roleLabel}`.toLowerCase().includes(q.toLowerCase()))
  const patch = async (m: CrewMember, body: object, ok?: string) => put(await run(() => api.patch<CrewData>(`/crew/${m.teamMemberId}`, { ...body, rowRev: m.rowRev }), ok))
  const phaseSet = async (m: CrewMember, body: { phase: 'build' | 'closeout'; hours: number; weeks?: number }[], ok?: string) => put(await run(() => api.put<CrewData>(`/crew/${m.teamMemberId}/phase-hours`, body), ok))
  const del = async (m: CrewMember) => put(await run(() => api.del<CrewData>(`/crew/${m.teamMemberId}`), `${m.staffName ?? m.roleLabel} removed`))
  const buckets: [number, CrewWeek[]][] = period === 'week' ? data.weeks.map(w => [w.weekId, [w]]) : months
  function menu(e: React.MouseEvent, m: CrewMember) {
    e.preventDefault(); if (!canEdit) return
    setCtx({ x: e.clientX, y: e.clientY, items: [
      { label: 'Note', icon: 'note', onClick: () => setModal({ kind: 'note', m }) },
      { label: 'Set hours/budget', icon: 'calc', onClick: () => setModal({ kind: 'budget', m }) },
      ...(tab === 'members' ? [{ label: 'Set per diem', icon: 'map', onClick: () => setModal({ kind: 'perdiem', m }) } as CtxItem] : []),
      { label: 'Change employee', icon: 'team', onClick: () => setModal({ kind: 'change', m }) },
      { label: 'Add team member', icon: 'plus', onClick: () => setModal({ kind: 'add', group: m.teamGroup }) },
      null,
      { label: 'Delete team member', icon: 'x', danger: true, onClick: () => del(m) }] })
  }
  function dropOn(target: CrewMember) {
    const id = drag; setDrag(null); if (!id || id === target.teamMemberId) return
    const list = data!.members.filter(x => x.teamMemberId !== id), i = list.findIndex(x => x.teamMemberId === target.teamMemberId)
    const m = data!.members.find(x => x.teamMemberId === id)!
    patch(m, { moveAfterId: i > 0 ? list[i - 1].teamMemberId : 0, ...(m.teamGroup !== target.teamGroup ? { teamGroup: target.teamGroup } : {}) }, 'Moved')
  }
  const T = data.totals, sc = data.schedule
  const name = (m: CrewMember) => { const o = others.get(String(m.teamMemberId))
    return <span className="crew-name">{m.isTba ? <><span className="tag y">TBA</span> {m.roleLabel}</> : m.staffName}{m.note && <span className="nt" title={m.note}><Icon name="note" size="s" /></span>}
      {o && <span className="tag" style={{ background: o.colour, color: '#fff' }}>{o.initials}</span>}</span> }
  const groups = TEAM_GROUPS.map(([g]) => [g, members.filter(m => m.teamGroup === g)] as const)

  function memberCell(m: CrewMember, c: Col) {
    const v = c.get(m)
    if (canEdit && c.edit) {
      const save = (s: string) => { const x = toNum(s); if (x == null) return
        if (c.edit === 'reloc') patch(m, { relocationCost: Math.max(0, x) }, 'Relocation saved')
        else if (c.edit === 'pct') patch(m, { quotedPct: Math.max(0, x) }, '% quoted saved')
        else { const phase = c.edit === 'weeksBuild' ? 'build' : 'closeout' as const, cur = phase === 'build' ? m.constructionHours / (m.constructionWeeks || 1) : m.closeoutHours / (m.closeoutWeeks || 1)
          phaseSet(m, [{ phase, hours: Math.round((cur || 40) * 100) / 100, weeks: Math.max(0, Math.round(x)) }], 'Weeks saved') } }
      return <EditCell number value={v} onSave={save} style={{ textAlign: 'right', width: 90 }} />
    }
    if (c.click && canEdit) return <button className="lnk cellbtn" onClick={() => setModal({ kind: c.click === 'perdiem' ? 'perdiem' : 'budget', m })} title={c.click === 'perdiem' ? 'Set per diem' : 'Set hours/budget'}>{f(v, c.fmt)}</button>
    return f(v, c.fmt)
  }
  const toDates = () => nav('../profile?tab=details#schedule', { relative: 'path' })

  return <>
    <PageHead eyebrow={project.projectName} title="Crew plan" was="Project team"
      desc="Who works on the project, their rates and hours. The quoted total is General conditions on the Price roll-up."
      actions={<><button className="btn" onClick={toDates} title="Construction and closeout weeks follow the dates in Project profile"><Icon name="calendar" />Schedule dates</button>
        <ExportMenu items={[['Crew plan (Excel)', `/exports/crew-plan.xlsx?versionId=${vid}`]]} />
        {canEdit && data.weeks.length > 0 && <button className="btn p" onClick={() => setModal({ kind: 'add' })}><Icon name="plus" />Add team member</button>}</>} />
    {data.weeks.length === 0 ? <div className="card pad"><div className="empty"><Icon name="calendar" size="l" /><h3>No schedule yet</h3>
      <p>Enter the Est construction start, Substantial completion and Closeout dates in Project profile. The construction and closeout weeks come from those dates.</p>
      <button className="btn p" onClick={toDates}><Icon name="calendar" />Set the dates</button></div></div> : <>
    <div className="crew-cards">
      {([['Construction', T.construction], ['Closeout', T.closeout], ['Total', T.total]] as const).map(([k, t]) =>
        <div key={k} className={`crew-card ${k === 'Total' ? 'tot' : ''}`}><div className="h"><h5>{k}</h5>{t.cost > t.quoted && <span className="var" title="Actual is more than quoted"><Icon name="alert" size="s" />{money(t.cost - t.quoted)}</span>}</div>
          <div className="r"><span>Hours</span><b>{num(t.hours)}</b></div><div className="r"><span>Actual</span><b>{money(t.cost)}</b></div><div className="r"><span>Quoted</span><b>{money(t.quoted)}</b></div></div>)}
      <div className="crew-card"><div className="h"><h5>Schedule</h5><button className="lnk" onClick={toDates}>Edit dates</button></div>
        <div className="r"><span>Construction</span><b>{date(sc.buildStartOn)} – {date(sc.buildEndOn)} · {sc.buildWeeks} wks</b></div>
        <div className="r"><span>Closeout</span><b>to {date(sc.closeoutOn)} · {sc.closeoutWeeks} wks</b></div>
        <div className="r"><span>Salary escalation</span><b>{data.salaryEscalationPct}% / year</b></div></div>
    </div>
    <div className="card"><Tabs<Tab> value={tab} onChange={setTab} items={[['members', 'Team members', data.members.length], ['loader', 'Resource loader', data.members.length]]} />
      <div className="tb">
        <label className="sbox"><Icon name="search" size="s" /><input placeholder="Search team members" value={q} onChange={e => setQ(e.target.value)} /></label>
        {tab === 'loader' && <><span className="muted" style={{ fontSize: 11.5, fontWeight: 800, textTransform: 'uppercase' }}>Organize by</span><Seg<'week' | 'month'> value={period} onChange={setPeriod} items={[['week', 'Weeks'], ['month', 'Months']]} />
          <span className="muted" style={{ fontSize: 12 }}>{period === 'month' ? 'Hours per month · typing spreads them evenly over the month\'s weeks' : 'Hours per week (0–80)'}</span></>}
        <span className="sp" />
        {tab === 'members' && canEdit && <button className="btn sm" onClick={() => run(() => api.post<CrewData>(`/projects/${pid}/crew/reset-rates`), 'Posting rates updated').then(put)} title="Set everyone back to their current posting rate" data-was="Update Posting Rates"><Icon name="sync" size="s" />Update posting rates</button>}
        {tab === 'members' && <button className="btn sm" onClick={() => setModal({ kind: 'cols' })} data-was="Add Columns"><Icon name="cols" size="s" />Columns</button>}
        <span className="muted" style={{ fontSize: 12 }}>Right-click a person for more · drag rows to reorder</span>
      </div>
      {tab === 'members' && cols.length === 0 ? <div className="empty"><h3>No columns chosen</h3><button className="btn" onClick={() => setModal({ kind: 'cols' })}>Add columns</button></div> :
      <div className="gw"><table className="dg crew">
        <thead>
          {tab === 'loader' && <tr className="phase-row"><th className="st" style={{ left: 0 }} /><th className="st edge" style={{ left: 240 }} />
            {buckets.map(([k, ws], i) => i > 0 && buckets[i - 1][1][0].phase === ws[0].phase ? null :
              <th key={k} colSpan={(() => { let n = 0; for (let j = i; j < buckets.length && buckets[j][1][0].phase === ws[0].phase; j++) n++; return n })()} className={`phz ph-${ws[0].phase}`}>{ws[0].phase === 'build' ? 'Construction' : 'Closeout'}</th>)}<th /></tr>}
          <tr><th className="st" style={{ left: 0, minWidth: 240 }}>Team member</th><th className="st edge" style={{ left: 240, minWidth: 190 }}>Employee role</th>
            {tab === 'members' ? cols.map(c => <th key={c.key} className="r" data-was={c.was}>{c.label}</th>)
              : buckets.map(([k, ws]) => <th key={k} className={`r wk ph-${ws[0].phase}`} title={period === 'week' ? `Week ${ws[0].weekNo} · ×${ws[0].multiplier.toFixed(3)} escalation` : undefined}>
                {period === 'week' ? <>W{ws[0].weekNo}<div className="muted">{fmtWeek(ws[0].startsOn)}</div></> : <>{new Date(ws[0].startsOn + 'T00:00:00').toLocaleDateString('en-US', { month: 'short', year: '2-digit' })}<div className="muted">{ws.length} wks</div></>}</th>)}
            {tab === 'loader' && <th className="r">Total hrs</th>}</tr></thead>
        <tbody>{groups.map(([g, list]) => { const col = collapsed.has(g), span = 2 + (tab === 'members' ? cols.length : buckets.length + 1)
          return [<tr key={g} className="grp"><td colSpan={span}><button className={`tgl ${col ? 'c' : ''}`} onClick={() => setCollapsed(s => { const n = new Set(s); n.has(g) ? n.delete(g) : n.add(g); return n })}><Icon name="chev" size="s" /></button>
              <b>{groupName(g)}</b> <span className="cnt">({list.length})</span></td></tr>,
            ...(col ? [] : list.map(m => <tr key={m.teamMemberId} onContextMenu={e => menu(e, m)} draggable={canEdit} onDragStart={() => setDrag(m.teamMemberId)} onDragOver={e => { if (drag) e.preventDefault() }} onDrop={() => dropOn(m)} style={drag === m.teamMemberId ? { opacity: .4 } : undefined}>
              <td className="st" style={{ left: 0 }}>{canEdit && <span className="grip" title="Drag to reorder">⠿</span>}{name(m)}</td><td className="st edge mut" style={{ left: 240 }}>{m.isTba ? (m.roleName ?? m.roleLabel) : m.roleLabel}</td>
              {tab === 'members' ? cols.map(c => <td key={c.key} className="n">{memberCell(m, c)}</td>)
                : <>{buckets.map(([k, ws]) => { const h = ws.reduce((s, w) => s + (m.hours[w.weekId] ?? 0), 0)
                  return <td key={k} className={`n hc ph-${ws[0].phase}`} style={{ background: h ? `rgba(39,98,77,${Math.min(0.4, (h / ws.length / 40) * 0.32)})` : undefined }}>
                    {canEdit ? <EditCell number value={h || ''} style={{ width: period === 'week' ? 48 : 60, textAlign: 'right', background: 'transparent', border: 0 }}
                      onSave={v => { const x = Math.max(0, toNum(v) ?? 0)
                        if (period === 'week') run(() => api.put<CrewData>(`/crew/${m.teamMemberId}/hours`, { cells: [{ weekId: ws[0].weekId, hours: Math.min(80, x) }] })).then(put)
                        else run(() => api.put<CrewData>(`/crew/${m.teamMemberId}/month-hours`, { monthNo: k, hours: x })).then(put) }} /> : (h || '')}</td> })}
                  <td className="n"><b>{num(m.totalHours)}</b></td></>}
            </tr>)),
            ...(canEdit && !col ? [<tr key={g + 'add'} className="addrow"><td colSpan={span}><button className="btn sm g" onClick={() => setModal({ kind: 'add', group: g })}><Icon name="plus" size="s" />Add team member</button></td></tr>] : [])] })}
        </tbody>
        <tfoot><tr><td className="st" style={{ left: 0 }}><b>Σ Total</b></td><td className="st edge" style={{ left: 240 }} />
          {tab === 'members' ? cols.map(c => <td key={c.key} className="n">{c.total ? f(members.reduce((s, m) => s + (c.get(m) ?? 0), 0), c.fmt) : ''}</td>)
            : <>{buckets.map(([k, ws]) => <td key={k} className="n">{num(members.reduce((s, m) => s + ws.reduce((t, w) => t + (m.hours[w.weekId] ?? 0), 0), 0))}</td>)}
              <td className="n">{num(members.reduce((s, m) => s + m.totalHours, 0))}</td></>}</tr></tfoot>
      </table></div>}
    </div></>}
    {ctx && <ContextMenu {...ctx} onClose={() => setCtx(null)} />}
    {modal?.kind === 'note' && <AskText title={`Note · ${modal.m.staffName ?? modal.m.roleLabel}`} label="Note" multiline initial={modal.m.note ?? ''} onClose={() => setModal(null)} onSave={v => { const m = modal.m; setModal(null); patch(m, { note: v }, 'Note saved') }} />}
    {modal?.kind === 'add' && <AddMember pid={pid} group={modal.group} busy={busy} onClose={() => setModal(null)} onDone={d => { put(d); setModal(null) }} />}
    {modal?.kind === 'change' && <StaffPicker title={`Change employee · ${modal.m.staffName ?? 'TBA ' + modal.m.roleLabel}`} saveLabel="Change employee" busy={busy}
      initial={{ teamGroup: modal.m.teamGroup, staffRoleId: modal.m.staffRoleId, staffId: modal.m.staffId, rate: modal.m.hourlyCost }} onClose={() => setModal(null)}
      onSave={async p => { const m = modal.m; const d = await patch(m, { ...(p.staffId ? { staffId: p.staffId } : { clearStaff: true }), staffRoleId: p.staffRoleId, teamGroup: p.teamGroup, hourlyCost: p.rate ?? undefined,
        roleLabel: lk.data?.staffRoles.find(r => r.staffRoleId === p.staffRoleId)?.roleName }, 'Employee changed'); if (d) setModal(null) }} />}
    {modal?.kind === 'budget' && <BudgetModal data={data} m={modal.m} busy={busy} onClose={() => setModal(null)} onSave={async body => { const d = await phaseSet(modal.m, body, 'Hours saved'); if (d) setModal(null) }} />}
    {modal?.kind === 'perdiem' && <PerDiemModal data={data} m={modal.m} stateCode={project.stateCode} busy={busy} onClose={() => setModal(null)} onDone={d => { put(d); setModal(null) }} />}
    {modal?.kind === 'cols' && <ColumnsModal pid={pid} current={cols.map(c => c.key)} onClose={() => setModal(null)} onDone={() => { qc.invalidateQueries({ queryKey: key }); setModal(null) }} />}
  </>
}

function AddMember({ pid, group, busy, onClose, onDone }: { pid: number; group?: TeamGroup; busy: boolean; onClose(): void; onDone(d: CrewData): void }) {
  const { run } = useAction(); const [b, setB] = useState('40'); const [c, setC] = useState('0'); const [qp, setQp] = useState('100')
  return <StaffPicker title="Add team member to project" saveLabel="Add team member" busy={busy} initial={{ teamGroup: group ?? 'operations' }} onClose={onClose}
    onSave={async p => { const d = await run(() => api.post<CrewData>(`/projects/${pid}/crew`, { teamGroup: p.teamGroup, staffId: p.staffId, staffRoleId: p.staffRoleId, hourlyCost: p.rate,
      buildHours: Math.min(80, toNum(b) ?? 0), closeoutHours: Math.min(80, toNum(c) ?? 0), quotedPct: toNum(qp) ?? 100 }), 'Team member added'); if (d) onDone(d) }}>
    <div className="field"><label>Construction hours / week</label><input className="inp num" value={b} onChange={e => setB(e.target.value)} /><small>Filled into every construction week.</small></div>
    <div className="field"><label>Closeout hours / week</label><input className="inp num" value={c} onChange={e => setC(e.target.value)} /></div>
    <div className="field"><label>% quoted</label><input className="inp num" value={qp} onChange={e => setQp(e.target.value)} /><small>Quoted = actual × this %. 100% bills at cost.</small></div>
  </StaffPicker>
}

/** Set team member budget: construction / closeout — budget, hours per week or weekly budget; each one fills the others. */
function BudgetModal({ data, m, busy, onClose, onSave }: { data: CrewData; m: CrewMember; busy: boolean; onClose(): void; onSave(b: { phase: 'build' | 'closeout'; hours: number }[]): void }) {
  const rate = m.hourlyCost ?? 0, esc = data.salaryEscalationPct, W = data.weeks
  const init = (p: 'build' | 'closeout') => { const n = phaseWeeks(W, p), h = Math.round(((p === 'build' ? m.constructionHours : m.closeoutHours) / (n || 1)) * 10) / 10; return { h, budget: phaseCost(W, p, rate, h, esc) } }
  const [v, setV] = useState({ build: init('build'), closeout: init('closeout') })
  const set = (p: 'build' | 'closeout', field: 'h' | 'budget' | 'weekly', raw: string) => {
    const x = Math.max(0, toNum(raw) ?? 0), n = phaseWeeks(W, p) || 1
    let h = field === 'h' ? x : phaseHours(W, p, rate, field === 'budget' ? x : x * n, esc)
    let budget = field === 'budget' ? x : field === 'weekly' ? x * n : phaseCost(W, p, rate, h, esc)
    if (h > 40) { h = 40; budget = phaseCost(W, p, rate, h, esc) }   // the original caps at 40 h / week
    if (field === 'h') budget = phaseCost(W, p, rate, h, esc)
    setV(o => ({ ...o, [p]: { h, budget } }))
  }
  const row = (p: 'build' | 'closeout', label: string) => { const n = phaseWeeks(W, p); return <div className="budget-row"><h4>{label} <small className="mut">{n} weeks</small></h4><div className="fgrid">
    <div className="field"><label>Budget</label><input className="inp num" key={'b' + v[p].budget} defaultValue={Math.round(v[p].budget)} onBlur={e => set(p, 'budget', e.target.value)} /></div>
    <div className="field"><label>Hours / week</label><input className="inp num" key={'h' + v[p].h} defaultValue={v[p].h} onBlur={e => set(p, 'h', e.target.value)} /></div>
    <div className="field"><label>Weekly budget</label><input className="inp num" key={'w' + v[p].budget} defaultValue={n ? Math.round(v[p].budget / n) : 0} onBlur={e => set(p, 'weekly', e.target.value)} /></div></div></div> }
  return <Modal title="Set team member budget" onClose={onClose} footer={<><button className="btn" onClick={onClose}>Cancel</button><button className="btn p" disabled={busy} onClick={() => onSave([{ phase: 'build', hours: v.build.h }, { phase: 'closeout', hours: v.closeout.h }])}><Icon name="check" />Save</button></>}>
    <div className="field"><label>Team member</label><input className="inp" readOnly value={`${m.staffName ?? 'TBA'} · ${m.roleLabel} · ${money(rate, 2)}/h`} /></div>
    {row('build', 'Construction')}{row('closeout', 'Closeout')}
    <p className="muted">Costs include {esc}% salary escalation a year (set in Project profile). Saving sets those hours in every week of the phase.</p>
  </Modal>
}

function PerDiemModal({ data, m, stateCode, busy, onClose, onDone }: { data: CrewData; m: CrewMember; stateCode: string | null; busy: boolean; onClose(): void; onDone(d: CrewData): void }) {
  const lk = useLookups(); const { run } = useAction()
  const [city, setCity] = useState(''); const [zip, setZip] = useState(''); const [state, setState] = useState(m.perDiemPlaceId ? '' : stateCode ?? '')
  const [placeId, setPlaceId] = useState<number | null>(m.perDiemPlaceId)
  // like today: travel defaults to Est construction start → Closeout date
  const [start, setStart] = useState(m.perDiemStart ?? data.schedule.buildStartOn ?? ''); const [end, setEnd] = useState(m.perDiemEnd ?? data.schedule.closeoutOn ?? data.schedule.buildEndOn ?? '')
  const [days, setDays] = useState<'weekdays' | 'alldays'>(m.perDiemDays); const [kind, setKind] = useState<'hotel' | 'meals' | 'both'>(m.perDiemKind)
  const places = useQuery({ queryKey: ['perdiem', city, zip, state], queryFn: () => api.get<PerDiemPlace[]>(`/per-diem/places?q=${encodeURIComponent(city)}&state=${encodeURIComponent(state)}&zip=${encodeURIComponent(zip)}`) })
  const list = places.data ?? [], place = list.find(p => p.placeId === placeId) ?? (placeId == null ? list[0] : undefined)
  const prev = place ? perDiem(start, end, days === 'alldays', kind, place.lodging, place.meals) : { days: 0, cost: 0 }
  const month = start ? new Date(start + 'T00:00:00').getMonth() : 0
  const outside = (start && data.schedule.buildStartOn && start < data.schedule.buildStartOn) || (end && data.schedule.closeoutOn && end > data.schedule.closeoutOn)
  return <Modal wide title={`Set per diem · ${m.staffName ?? 'TBA ' + m.roleLabel}`} onClose={onClose} footer={<>
    {m.perDiemCost > 0 && <button className="btn" style={{ color: 'var(--bad,#d1344b)' }} onClick={async () => { const d = await run(() => api.put<CrewData>(`/crew/${m.teamMemberId}/per-diem`, { clear: true }), 'Per diem cleared'); if (d) onDone(d) }}>Clear per diem</button>}
    <span style={{ flex: 1 }} /><button className="btn" onClick={onClose}>Cancel</button>
    <button className="btn p" disabled={busy || !place || !start || !end || end < start} onClick={async () => { const d = await run(() => api.put<CrewData>(`/crew/${m.teamMemberId}/per-diem`, { placeId: place!.placeId, start, end, days, kind }), 'Per diem saved'); if (d) onDone(d) }}><Icon name="check" />Save</button></>}>
    <div className="fgrid">
      <div className="field"><label>City or county</label><input className="inp" value={city} onChange={e => { setCity(e.target.value); setPlaceId(null) }} placeholder="e.g. Austin" /></div>
      <div className="field"><label>Zip code</label><input className="inp" value={zip} onChange={e => { setZip(e.target.value); setPlaceId(null) }} placeholder="needs the GSA zip list" /></div>
      <div className="field"><label>State</label><select className="inp" value={state} onChange={e => { setState(e.target.value); setPlaceId(null) }}><option value="">Any</option>{lk.data?.states.map(s => <option key={s.stateCode} value={s.stateCode}>{s.stateCode} · {s.stateName}</option>)}</select></div>
      <div className="field" style={{ gridColumn: 'span 2' }}><label>{list.length} matching rates</label><select className="inp" value={place?.placeId ?? ''} onChange={e => setPlaceId(+e.target.value)}>
        {list.map(p => <option key={p.placeId} value={p.placeId}>{p.city}{p.stateCode ? `, ${p.stateCode}` : ''}{p.locationDefined ? ` · ${p.locationDefined}` : ''} (FY{p.fiscalYear})</option>)}</select></div>
      <div className="field"><label>Travel start date</label><input className="inp" type="date" value={start} onChange={e => setStart(e.target.value)} /></div>
      <div className="field"><label>Travel end date</label><input className="inp" type="date" value={end} onChange={e => setEnd(e.target.value)} /></div>
      <div className="field"><label>Per diem days</label><Seg<'weekdays' | 'alldays'> value={days} onChange={setDays} items={[['weekdays', 'Week days'], ['alldays', 'All days']]} /></div>
      <div className="field"><label>Expense type</label><Seg<'hotel' | 'meals' | 'both'> value={kind} onChange={setKind} items={[['hotel', 'Hotel'], ['meals', 'Meals'], ['both', 'Both']]} /></div>
    </div>
    {outside && <p className="warn-note"><Icon name="alert" size="s" /> The travel dates are outside construction start → closeout.</p>}
    {end && start && end < start && <p className="warn-note"><Icon name="alert" size="s" /> The travel end date is before the start date.</p>}
    <div className="kpis" style={{ marginTop: 12 }}>
      <div className="kpi"><span className="k">Destination</span><span className="v" style={{ fontSize: 14 }}>{place ? `${place.city}${place.county ? ' · ' + place.county : ''}` : 'No matching rate'}</span></div>
      <div className="kpi"><span className="k">Hotel rate ({new Date(2000, month, 1).toLocaleDateString('en-US', { month: 'short' })})</span><span className="v">{place ? money(place.lodging[month]) : '–'}</span></div>
      <div className="kpi"><span className="k">Meals rate</span><span className="v">{place ? money(place.meals) : '–'}</span></div>
      <div className="kpi"><span className="k">Per diem days</span><span className="v">{num(prev.days)}</span></div>
      <div className="kpi"><span className="k">Total per diem cost</span><span className="v">{money(prev.cost)}</span></div>
    </div>
    <p className="muted">Rates are the GSA per diem rates from today's app (fiscal year shown). Hotel uses each night's month; week days skip Saturdays and Sundays.</p>
  </Modal>
}

function ColumnsModal({ pid, current, onClose, onDone }: { pid: number; current: string[]; onClose(): void; onDone(): void }) {
  const { run, busy } = useAction(); const [sel, setSel] = useState<string[]>(current); const [all, setAll] = useState(false)
  const toggle = (k: string) => setSel(s => s.includes(k) ? s.filter(x => x !== k) : COLS.map(c => c.key).filter(x => s.includes(x) || x === k))
  return <Modal title="Columns" onClose={onClose} footer={<><label className="check" style={{ marginRight: 'auto' }}><input type="checkbox" checked={all} onChange={e => setAll(e.target.checked)} /> Use for all my projects</label>
    <button className="btn" onClick={onClose}>Cancel</button><button className="btn p" disabled={busy} onClick={async () => { if (await run(() => api.put(`/columns/crew`, { columns: sel, projectId: pid, allProjects: all }), 'Columns saved')) onDone() }}><Icon name="check" />Save</button></>}>
    <div className="tb" style={{ border: 0, padding: 0 }}><b>{sel.length}/{COLS.length} selected</b><span className="sp" />
      <button className="btn sm g" onClick={() => setSel(COLS.map(c => c.key))}>Select all</button><button className="btn sm g" onClick={() => setSel([])}>Clear</button><button className="btn sm g" onClick={() => setSel(DEFAULT_COLS)}>Defaults</button></div>
    <div className="colpick">{COLS.map(c => <label key={c.key} className="check"><input type="checkbox" checked={sel.includes(c.key)} onChange={() => toggle(c.key)} /> {c.label}</label>)}</div>
    <p className="muted">Team member and Employee role are always shown. Only you see your choice.</p>
  </Modal>
}
