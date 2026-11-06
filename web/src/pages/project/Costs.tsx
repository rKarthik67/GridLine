// Precon services (was: Preconstruction) and Project overheads (was: General requirements), like today's pages.
// Precon services: Team members (hours per precon task, right-click for Change employee / Add / Delete) and Expenses.
// Both: expenses by category with CSI code, Billable, Qty, Unit, Rate → Subtotal, and the client quote on the right.
// Quoted % (was: allowable markup): quoted rate = rate × % on billable lines; Precon quoted = (labour + billable) × %.
// Typing a Quoted total back-calculates the % (capped at 100%).
import { useMemo, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api/client'
import { TEAM_GROUPS, type CostLineRow, type CostsData, type PreconMemberRow, type PreconTeam, type ProjectDetail, type TeamGroup, type Version } from '../../api/types'
import { PageHead } from '../../components/Shell'
import { Icon } from '../../components/Icon'
import { ContextMenu, ExportMenu, Failed, Loading, Menu, Modal, Tabs, toNum, useAction, type CtxItem } from '../../components/ui'
import { StaffPicker } from '../../components/StaffPicker'
import { DataGrid, pasteNum, type GCol, type GridEdit } from '../../components/DataGrid'
import { useToast } from '../../app/toast'
import { money, num } from '../../app/format'
import { useLookups, vkey } from '../../app/hooks'

type Tab = 'team' | 'expenses'
type Reply = { costs?: CostsData; team?: PreconTeam }
const groupName = (g: TeamGroup) => TEAM_GROUPS.find(x => x[0] === g)![1]
const signed = (v: number) => v < 0 ? `(${money(Math.abs(v))})` : money(v)

export default function Costs({ project, version, pageKey }: { project: ProjectDetail; version: Version; pageKey: string }) {
  const area = pageKey === 'precon-services' ? 'precon' : 'genreq'
  const vid = version.versionId; const qc = useQueryClient(); const { run } = useAction(); const lk = useLookups()
  const key = vkey('costs', vid, area), tkey = vkey('precon-team', vid)
  const { data, error, isLoading } = useQuery({ queryKey: key, queryFn: () => api.get<CostsData>(`/versions/${vid}/costs?area=${area}`) })
  const team = useQuery({ queryKey: tkey, queryFn: () => api.get<PreconTeam>(`/versions/${vid}/precon-team`), enabled: area === 'precon' })
  const [tab, setTab] = useState<Tab>(area === 'precon' ? 'team' : 'expenses')
  const [qPct, setQPct] = useState<string | null>(null); const [qTot, setQTot] = useState<string | null>(null)
  const [modal, setModal] = useState<null | { kind: 'tasks' | 'missing' } | { kind: 'addExpense'; category?: string } | { kind: 'addMember'; group?: TeamGroup } | { kind: 'change'; m: PreconMemberRow }>(null)
  const canEdit = project.access !== 'viewer' && version.status !== 'locked'
  const title = area === 'precon' ? 'Precon services' : 'Project overheads', was = area === 'precon' ? 'Preconstruction' : 'General requirements'
  const after = (r?: Reply) => {
    if (r?.costs) qc.setQueryData(key, r.costs); else qc.invalidateQueries({ queryKey: key })   // team changes move the labour → refetch the totals
    if (r?.team) qc.setQueryData(tkey, r.team)
    qc.invalidateQueries({ queryKey: vkey('price-roll-up', vid) }); return r
  }
  if (isLoading || (area === 'precon' && team.isLoading)) return <Loading what={title.toLowerCase()} />
  if (error || !data) return <Failed error={error} />
  const T = data.totals
  const saveQuoted = (body: object) => run(() => api.put<Reply>(`/versions/${vid}/costs/quoted`, { area, ...body }), 'Quoted saved · roll-up recalculated').then(after)
  const kits = (lk.data?.costKits ?? []).filter(k => k.area === area)
  const missingCsi = data.lines.filter(l => !l.csiCode).length, missingDesc = data.lines.filter(l => !l.itemName?.trim()).length

  const summary = <div className="cost-cards">
    {area === 'precon' ? <div className="cost-card"><h5>Actual</h5>
      <div className="r"><span>Labour actual</span><b>{money(T.labourActual)}</b></div><div className="r"><span>Expenses actual</span><b>{money(T.expensesActual)}</b></div>
      <div className="r tot"><span>Total actual</span><b>{money(T.actual)}</b></div></div>
      : <div className="cost-card"><h5>Actual</h5><div className="r"><span>Total actual</span><b>{money(T.actual)}</b></div><div className="r"><span>Billable actual</span><b>{money(T.billableActual)}</b></div></div>}
    <div className="cost-card q"><h5>Client quote</h5>
      <div className="r"><span>Total quoted</span>{canEdit ? <span className="qin">$<input className="inp num" value={qTot ?? String(Math.round(T.quoted))} onChange={e => setQTot(e.target.value)}
        onBlur={() => { const v = toNum(qTot ?? ''); if (qTot != null && v != null && Math.round(v) !== Math.round(T.quoted)) saveQuoted({ quotedTotal: v }); setQTot(null) }} onKeyDown={e => e.key === 'Enter' && (e.target as HTMLInputElement).blur()} /></span> : <b>{money(T.quoted)}</b>}</div>
      <div className="r"><span>or Quoted %</span>{canEdit ? <span className="qin"><input className="inp num" value={qPct ?? String(+data.quotedPct.toFixed(3))} onChange={e => setQPct(e.target.value)}
        onBlur={() => { const v = toNum(qPct ?? ''); if (qPct != null && v != null && v !== data.quotedPct) saveQuoted({ quotedPct: v }); setQPct(null) }} onKeyDown={e => e.key === 'Enter' && (e.target as HTMLInputElement).blur()} />%</span> : <b>{data.quotedPct}%</b>}</div>
      <div className="r"><span>Differential</span><b className={Math.round(T.difference) === 0 ? 'good' : T.difference < 0 ? 'bad' : ''}>{signed(Math.round(T.difference))}</b></div>
      <small className="mut">{area === 'precon' ? 'Quoted = (labour + billable expenses) × Quoted %.' : 'Quoted = billable lines × Quoted %.'} A Quoted total back-calculates the % (max 100%).</small></div>
  </div>

  return <>
    <PageHead eyebrow={`${project.projectName} · v${version.versionNo}`} title={title} was={was}
      desc={area === 'precon' ? 'The precon team\'s hours by task and the precon expenses. Feeds Precon services on the Price roll-up.' : 'Project overhead expenses by category. Feeds Project overheads on the Price roll-up.'}
      actions={<>{area === 'precon' && canEdit && <><button className="btn" onClick={() => setModal({ kind: 'tasks' })} data-was="Change Tasks"><Icon name="list" />Change tasks</button>
          <button className="btn" onClick={() => setModal({ kind: 'addMember' })}><Icon name="team" />Add team member</button></>}
        {canEdit && kits.length > 0 && <Menu label="Copy expense template" icon="copy" items={kits.map(k => ({ label: `${k.kitName} · ${k.items} items`, onClick: () => run(() => api.post<Reply>(`/versions/${vid}/costs/apply-kit`, { costKitId: k.costKitId }), `Items copied from ${k.kitName}`).then(after) }))} />}
        {canEdit && <button className="btn p" onClick={() => setModal({ kind: 'addExpense' })}><Icon name="plus" />Add expense</button>}
        <ExportMenu items={[[`${title} (Excel)`, `/exports/${area === 'precon' ? 'precon-services' : 'project-overheads'}.xlsx?versionId=${vid}`]]} /></>} />
    {(missingCsi > 0 || missingDesc > 0) && <div className="warnbar"><Icon name="alert" />
      <div>{missingCsi > 0 && <><b>{missingCsi} line{missingCsi === 1 ? ' has' : 's have'} no CSI code.</b> </>}{missingDesc > 0 && <><b>{missingDesc} line{missingDesc === 1 ? ' has' : 's have'} no description.</b> </>}Lines without a CSI code can't be grouped by division on the roll-up or exported to accounting.</div>
      <button className="btn sm" onClick={() => setModal({ kind: 'missing' })}>View</button></div>}
    {summary}
    <div className="card">
      {area === 'precon' && <Tabs<Tab> value={tab} onChange={setTab} items={[['team', 'Team members', team.data?.members.length ?? 0], ['expenses', 'Expenses', data.lines.length]]} />}
      {tab === 'team' && area === 'precon' && team.data ? <PreconTeamGrid vid={vid} data={team.data} canEdit={canEdit} onChange={after} onAdd={g => setModal({ kind: 'addMember', group: g })} onChangeEmployee={m => setModal({ kind: 'change', m })} />
        : <ExpensesGrid vid={vid} area={area} data={data} canEdit={canEdit} onChange={after} onAdd={c => setModal({ kind: 'addExpense', category: c })} />}
    </div>
    {modal?.kind === 'tasks' && <TasksModal vid={vid} onClose={() => setModal(null)} onDone={r => { after(r); setModal(null) }} />}
    {modal?.kind === 'addExpense' && <AddExpense vid={vid} area={area} category={modal.category} categories={[...new Set(data.lines.map(l => l.categoryName))]} onClose={() => setModal(null)} onDone={r => { after(r); setModal(null) }} />}
    {modal?.kind === 'addMember' && <AddPreconMember vid={vid} group={modal.group} onClose={() => setModal(null)} onDone={r => { after(r); setModal(null) }} />}
    {modal?.kind === 'change' && <StaffPicker title="Change employee" saveLabel="Change employee" initial={{ teamGroup: modal.m.teamGroup, staffRoleId: modal.m.staffRoleId, staffId: modal.m.staffId, rate: modal.m.postingRate }}
      onClose={() => setModal(null)} onSave={async p => { const r = await run(() => api.patch<Reply>(`/precon-team/${modal.m.preconMemberId}`, { staffId: p.staffId, staffRoleId: p.staffRoleId, teamGroup: p.teamGroup, postingRate: p.rate, rowRev: modal.m.rowRev }), 'Employee changed'); if (r) { after(r); setModal(null) } }} />}
    {modal?.kind === 'missing' && <Modal title="Missing information" onClose={() => setModal(null)} footer={<button className="btn p" onClick={() => setModal(null)}>Close</button>}>
      <table className="dg" style={{ width: '100%' }}><thead><tr><th>Category</th><th>Item</th><th>Missing</th></tr></thead><tbody>
        {data.lines.filter(l => !l.csiCode || !l.itemName?.trim()).map(l => <tr key={l.costLineId}><td>{l.categoryName}</td><td>{l.itemName || <span className="mut">(no description)</span>}</td>
          <td>{[!l.csiCode && 'CSI code', !l.itemName?.trim() && 'Description'].filter(Boolean).join(', ')}</td></tr>)}</tbody></table></Modal>}
  </>
}

// ------------------------------------------------------------------ precon team: hours per task
type PRow = { kind: 'grp'; g: TeamGroup; n: number } | { kind: 'm'; m: PreconMemberRow; i: number }
function PreconTeamGrid({ vid, data, canEdit, onChange, onAdd, onChangeEmployee }: { vid: number; data: PreconTeam; canEdit: boolean; onChange(r: Reply): void; onAdd(g?: TeamGroup): void; onChangeEmployee(m: PreconMemberRow): void }) {
  const { run } = useAction(); const toast = useToast(); const [ctx, setCtx] = useState<{ x: number; y: number; items: CtxItem[] } | null>(null)
  const [sel, setSel] = useState<Set<number>>(new Set()); const [confirm, setConfirm] = useState(false); const [collapsed, setCollapsed] = useState<Set<string>>(new Set())
  const go = async (p: Promise<Reply | undefined>) => { const r = await p; if (r) onChange(r); return r }
  const rows = useMemo(() => { const out: PRow[] = []; let i = 0
    for (const [g] of TEAM_GROUPS) { const list = data.members.filter(m => m.teamGroup === g); out.push({ kind: 'grp', g, n: list.length })
      if (!collapsed.has(g)) for (const m of list) out.push({ kind: 'm', m, i: ++i }) }
    return out }, [data.members, collapsed])
  const toggle = (id: number, on: boolean) => setSel(s => { const x = new Set(s); on ? x.add(id) : x.delete(id); return x })
  const cols = useMemo<GCol<PRow>[]>(() => [
    { prop: 'sel', name: '#', size: 58, pin: 'start', value: r => r.kind === 'm' && r.i ? r.i : '', show: (r, h) => r.kind === 'm' && !r.i ? '' : r.kind === 'grp'
        ? h('button', { class: `tgl ${collapsed.has(r.g) ? 'c' : ''}`, onClick: () => setCollapsed(s => { const x = new Set(s); x.has(r.g) ? x.delete(r.g) : x.add(r.g); return x }) }, '▾')
        : h('label', { class: 'rowsel' }, [h('span', {}, String(r.i)), h('input', { type: 'checkbox', checked: sel.has(r.m.preconMemberId), onClick: (e: MouseEvent) => { e.stopPropagation(); toggle(r.m.preconMemberId, (e.target as HTMLInputElement).checked) } })]) },
    { prop: 'name', name: 'Team member', size: 270, pin: 'start', value: r => r.kind === 'm' ? (r.m.staffName ?? `TBA ${r.m.roleLabel}`) : groupName(r.g),
      show: (r, h) => r.kind === 'grp' ? h('span', { class: 'gname' }, [h('b', {}, groupName(r.g)), h('span', { class: 'cnt' }, ` (${r.n})`), canEdit ? h('button', { class: 'gadd', onClick: () => onAdd(r.g) }, '+ Add') : ''])
        : r.m.isTba ? [h('span', { class: 'tag y' }, 'TBA'), ' ' + r.m.roleLabel] : r.m.staffName },
    { prop: 'role', name: 'Employee role', size: 180, value: r => r.kind === 'm' ? r.m.roleLabel : '', cls: () => 'mut' },
    { prop: 'postingRate', name: 'Act posting rate', size: 150, align: 'r', edit: r => r.kind === 'm', value: r => r.kind === 'm' ? r.m.postingRate : '', show: r => r.kind === 'm' && r.i ? money(r.m.postingRate, 2) : '' },
    { prop: 'quotedRate', name: 'Quoted posting rate', size: 170, align: 'r', value: r => r.kind === 'm' ? r.m.quotedRate : '', show: r => r.kind === 'm' && r.i ? money(r.m.quotedRate, 2) : '', cls: () => 'mut' },
    { prop: 'totalHours', name: 'Total hours', size: 120, align: 'r', value: r => r.kind === 'm' ? r.m.totalHours : '', show: r => r.kind === 'm' ? num(r.m.totalHours) : '', cls: () => 'b' },
    ...data.tasks.map<GCol<PRow>>(t => ({ prop: 't' + t.activityId, name: t.taskName + (t.isAdhoc ? ' (ad-hoc)' : ''), size: 160, align: 'r',
      edit: r => r.kind === 'm', value: r => r.kind === 'm' ? (r.m.hours[t.activityId] || '') : '', show: r => r.kind === 'm' ? (r.m.hours[t.activityId] ? num(r.m.hours[t.activityId]) : '') : '' })),
  ], [data.tasks, collapsed, sel, canEdit]) // eslint-disable-line react-hooks/exhaustive-deps
  async function edit(ch: GridEdit<PRow>[]) {
    const byMember = new Map<number, { m: PreconMemberRow; tasks: { activityId: number; hours: number }[]; rate?: number }>()
    for (const c of ch) { if (c.row.kind !== 'm') continue; const m = c.row.m; const e = byMember.get(m.preconMemberId) ?? { m, tasks: [] }; byMember.set(m.preconMemberId, e)
      const n = pasteNum(c.value)
      if (c.prop === 'postingRate') { if (n == null || n < 0) { toast('Posting rate must be a number', 'alert'); continue } e.rate = n }
      else if (c.prop.startsWith('t')) { if (c.value.trim() && (n == null || n < 0)) { toast(`"${c.value}" is not a number of hours`, 'alert'); continue } e.tasks.push({ activityId: +c.prop.slice(1), hours: n ?? 0 }) } }
    let last: Reply | undefined
    for (const e of byMember.values()) {
      if (e.rate != null) last = await run(() => api.patch<Reply>(`/precon-team/${e.m.preconMemberId}`, { postingRate: e.rate, rowRev: e.m.rowRev })) ?? last
      if (e.tasks.length) last = await run(() => api.put<Reply>(`/precon-team/${e.m.preconMemberId}/tasks`, e.tasks)) ?? last
    }
    if (last) { onChange(last); if (ch.length > 1) toast(`${ch.length} cells saved`) }
  }
  const menu = (e: MouseEvent, r: PRow | null) => { if (!canEdit || !r || r.kind !== 'm') return; const m = r.m
    setCtx({ x: e.clientX, y: e.clientY, items: [{ label: 'Change employee', icon: 'team', onClick: () => onChangeEmployee(m) }, { label: 'Add team member', icon: 'plus', onClick: () => onAdd(m.teamGroup) }, null,
      { label: 'Delete team member', icon: 'x', danger: true, onClick: () => go(run(() => api.del<Reply>(`/precon-team/${m.preconMemberId}`), 'Team member deleted')) }] }) }
  if (!data.members.length) return <div className="empty"><Icon name="team" size="l" /><h3>No team members defined</h3><p>Add the people who work on preconstruction and enter their hours per task.</p>
    {canEdit && <button className="btn p" onClick={() => onAdd()}><Icon name="plus" />Add team member</button>}</div>
  const foot = [{ kind: 'm', i: 0, m: { preconMemberId: -1, staffName: 'Σ Total', roleLabel: `Labour actual ${money(data.totals.labour)}`, isTba: false, postingRate: null, quotedRate: null, totalHours: data.totals.hours,
    hours: Object.fromEntries(data.tasks.map(t => [t.activityId, data.members.reduce((s, m) => s + (m.hours[t.activityId] ?? 0), 0)])) } as unknown as PreconMemberRow } as PRow]
  return <>
    <DataGrid<PRow> rows={rows} rowKey={r => r.kind === 'grp' ? 'g' + r.g : r.m.preconMemberId} cols={cols} readOnly={!canEdit} onEdit={edit} onMenu={menu}
      rowCls={r => r.kind === 'grp' ? 'grp' : !r.i ? 'tot' : sel.has(r.m.preconMemberId) ? 'on' : undefined} bottom={foot} height="calc(100vh - 420px)" />
    <p className="muted" style={{ padding: '8px 12px' }}>Type hours, or copy a block from Excel and paste it (Ctrl+V). Drag across cells to select; Ctrl+C copies them for Excel. Right-click a person to change the employee, add or delete. Labour = hours × act posting rate.</p>
    {sel.size > 0 && <div className="selbar"><span>{sel.size} selected</span><button className="btn sm" onClick={() => setSel(new Set())}>Cancel</button><button className="btn sm danger" onClick={() => setConfirm(true)}>Delete selected</button></div>}
    {confirm && <Modal title={`Delete (${sel.size} people)`} onClose={() => setConfirm(false)} footer={<><button className="btn" onClick={() => setConfirm(false)}>Cancel</button>
      <button className="btn danger" onClick={async () => { setConfirm(false); const r = await run(() => api.post<Reply>(`/versions/${vid}/precon-team/delete`, { ids: [...sel] }), 'Deleted'); if (r) { onChange(r); setSel(new Set()) } }}>Delete</button></>}>
      <p>Delete the selected team members and their task hours? This can't be undone.</p></Modal>}
    {ctx && <ContextMenu {...ctx} onClose={() => setCtx(null)} />}
  </>
}

// ------------------------------------------------------------------ expenses: actual (left) and client quote (right)
type ERow = { kind: 'cat'; c: string; list: CostLineRow[] } | { kind: 'l'; l: CostLineRow; i: number } | { kind: 'tot' }
function ExpensesGrid({ vid, area, data, canEdit, onChange, onAdd }: { vid: number; area: string; data: CostsData; canEdit: boolean; onChange(r: Reply): void; onAdd(category?: string): void }) {
  const { run } = useAction(); const lk = useLookups(); const toast = useToast()
  const [sel, setSel] = useState<Set<number>>(new Set()); const [confirm, setConfirm] = useState(false); const [collapsed, setCollapsed] = useState<Set<string>>(new Set())
  const [ctx, setCtx] = useState<{ x: number; y: number; items: CtxItem[] } | null>(null)
  const cats = useMemo(() => { const m = new Map<string, CostLineRow[]>(); for (const l of data.lines) m.set(l.categoryName, [...(m.get(l.categoryName) ?? []), l]); return [...m.entries()] }, [data.lines])
  const rows = useMemo(() => { const out: ERow[] = []; let i = 0
    for (const [c, list] of cats) { out.push({ kind: 'cat', c, list }); if (!collapsed.has(c)) for (const l of list) out.push({ kind: 'l', l, i: ++i }) }
    return out }, [cats, collapsed])
  const units = lk.data?.units ?? [], codes = lk.data?.csiCodes ?? []
  const sum = (list: CostLineRow[], f: (l: CostLineRow) => number) => list.reduce((s, l) => s + f(l), 0)
  const L = (r: ERow) => r.kind === 'l' ? r.l : null
  const cols = useMemo<GCol<ERow>[]>(() => {
    const line = (r: ERow) => r.kind === 'l'
    const agg = (r: ERow, f: (l: CostLineRow) => number) => r.kind === 'cat' ? sum(r.list, f) : r.kind === 'tot' ? sum(data.lines, f) : null
    return [
    { prop: 'sel', name: '#', size: 58, pin: 'start', value: r => r.kind === 'l' ? r.i : '', show: (r, h) => {
        if (r.kind === 'tot') return ''
        const ids = r.kind === 'cat' ? r.list.map(l => l.costLineId) : [r.l.costLineId], on = ids.every(id => sel.has(id))
        const box = h('input', { type: 'checkbox', checked: on, onClick: (e: MouseEvent) => { e.stopPropagation(); const v = (e.target as HTMLInputElement).checked; setSel(s => { const x = new Set(s); ids.forEach(id => v ? x.add(id) : x.delete(id)); return x }) } })
        return r.kind === 'cat' ? [h('button', { class: `tgl ${collapsed.has(r.c) ? 'c' : ''}`, onClick: () => setCollapsed(s => { const x = new Set(s); x.has(r.c) ? x.delete(r.c) : x.add(r.c); return x }) }, '▾'), box]
          : h('label', { class: 'rowsel' }, [h('span', {}, String(r.i)), box]) } },
    { prop: 'itemName', name: 'Item', size: 320, pin: 'start', edit: line, value: r => r.kind === 'l' ? r.l.itemName : r.kind === 'cat' ? r.c : 'Σ Total',
      cls: r => r.kind === 'l' && !r.l.itemName?.trim() ? 'miss' : undefined,
      show: (r, h) => r.kind === 'cat' ? h('span', { class: 'gname' }, [h('b', {}, r.c), h('span', { class: 'cnt' }, ` (${r.list.length})`), canEdit ? h('button', { class: 'gadd', onClick: () => onAdd(r.c) }, '+ Add') : ''])
        : r.kind === 'tot' ? h('b', {}, 'Σ Total') : (r.l.itemName || h('span', { class: 'mut' }, 'Add a description')) },
    { prop: 'csiCode', name: 'CSI code', size: 110, edit: line, value: r => L(r)?.csiCode ?? '', cls: r => r.kind === 'l' && !r.l.csiCode ? 'miss' : undefined, tip: r => L(r)?.csiTitle ?? undefined,
      show: (r, h) => r.kind === 'l' ? (r.l.csiCode || h('span', { class: 'mut' }, 'Add CSI')) : '' },
    { prop: 'isBillable', name: 'Billable', size: 96, align: 'c', edit: line, value: r => r.kind === 'l' ? (r.l.isBillable ? 'Yes' : 'No') : '',
      show: (r, h) => r.kind === 'l' ? h('input', { type: 'checkbox', checked: r.l.isBillable, disabled: !canEdit, onClick: (e: MouseEvent) => { e.stopPropagation(); edit([{ row: r, prop: 'isBillable', value: (e.target as HTMLInputElement).checked ? 'Yes' : 'No' }]) } }) : '' },
    { prop: 'qty', name: 'Qty', size: 80, align: 'r', edit: line, value: r => L(r)?.qty ?? '', show: r => r.kind === 'l' ? num(r.l.qty, 2) : '' },
    { prop: 'uomCode', name: 'Unit', size: 76, edit: line, value: r => L(r)?.uomCode ?? '' },
    { prop: 'rate', name: 'Rate', size: 110, align: 'r', edit: line, value: r => L(r)?.rate ?? '', show: r => r.kind === 'l' ? money(r.l.rate, 2) : '' },
    { prop: 'actualTotal', name: 'Subtotal', size: 120, align: 'r', value: r => L(r)?.actualTotal ?? agg(r, l => l.actualTotal), show: r => money((L(r)?.actualTotal ?? agg(r, l => l.actualTotal)) ?? 0), cls: () => 'b' },
    { prop: 'qQty', name: 'Quoted qty', size: 120, align: 'r', headCls: 'q', value: r => L(r)?.qty ?? '', show: r => r.kind === 'l' ? num(r.l.qty, 2) : '', cls: () => 'q' },
    { prop: 'qRate', name: 'Quoted rate', size: 130, align: 'r', headCls: 'q', value: r => L(r)?.quotedRate ?? '', show: r => r.kind === 'l' ? money(r.l.quotedRate ?? 0, 2) : '', cls: () => 'q' },
    { prop: 'quotedTotal', name: 'Quoted subtotal', size: 160, align: 'r', headCls: 'q', value: r => L(r)?.quotedTotal ?? agg(r, l => l.quotedTotal), show: r => money((L(r)?.quotedTotal ?? agg(r, l => l.quotedTotal)) ?? 0), cls: () => 'q b' },
    { prop: 'delta', name: 'Delta', size: 110, align: 'r', headCls: 'q', value: r => Math.round(L(r) ? L(r)!.quotedTotal - L(r)!.actualTotal : agg(r, l => l.quotedTotal - l.actualTotal) ?? 0),
      show: r => signed(Math.round(L(r) ? L(r)!.quotedTotal - L(r)!.actualTotal : agg(r, l => l.quotedTotal - l.actualTotal) ?? 0)),
      cls: r => { const d = Math.round(L(r) ? L(r)!.quotedTotal - L(r)!.actualTotal : agg(r, l => l.quotedTotal - l.actualTotal) ?? 0); return `q ${d === 0 ? '' : d < 0 ? 'bad' : 'good'}` } },
  ] }, [data.lines, collapsed, sel, canEdit]) // eslint-disable-line react-hooks/exhaustive-deps

  /** Typing, pasting from Excel or dragging the fill handle: check every value, then save each line once. */
  async function edit(ch: GridEdit<ERow>[]) {
    const per = new Map<number, { l: CostLineRow; body: Record<string, unknown> }>(); const bad: string[] = []
    for (const c of ch) { if (c.row.kind !== 'l') continue; const l = c.row.l; const e = per.get(l.costLineId) ?? { l, body: {} }; per.set(l.costLineId, e); const v = c.value.trim()
      if (c.prop === 'itemName') e.body.itemName = v
      else if (c.prop === 'csiCode') { if (v && !codes.some(x => x.csiCode === v)) bad.push(`CSI code "${v}" isn't in the code list`); else e.body.csiCode = v }
      else if (c.prop === 'isBillable') { const b = /^(y|yes|true|1|x|✓)$/i.test(v) ? true : /^(n|no|false|0|)$/i.test(v) ? false : null; if (b == null) bad.push(`Billable "${v}" should be Yes or No`); else e.body.isBillable = b }
      else if (c.prop === 'uomCode') { const u = units.find(x => x.uomCode.toLowerCase() === v.toLowerCase()); if (v && !u) bad.push(`Unit "${v}" isn't a known unit`); else e.body.uomCode = u?.uomCode ?? '' }
      else if (c.prop === 'qty' || c.prop === 'rate') { const n = pasteNum(v); if (n == null) bad.push(`"${v}" is not a number`); else e.body[c.prop] = n } }
    if (bad.length) toast(bad.slice(0, 2).join(' · ') + (bad.length > 2 ? ` (+${bad.length - 2} more)` : ''), 'alert')
    let last: Reply | undefined
    for (const e of per.values()) if (Object.keys(e.body).length) last = await run(() => api.patch<Reply>(`/costs/${e.l.costLineId}`, { ...e.body, rowRev: e.l.rowRev })) ?? last
    if (last) { onChange(last); if (per.size > 1) toast(`${per.size} rows saved`) }
  }
  const menu = (e: MouseEvent, r: ERow | null) => { if (!canEdit || !r || r.kind === 'tot') return
    const c = r.kind === 'cat' ? r.c : r.l.categoryName
    setCtx({ x: e.clientX, y: e.clientY, items: [{ label: `Add item to ${c}`, icon: 'plus', onClick: () => onAdd(c) },
      ...(r.kind === 'l' ? [null, { label: 'Delete item', icon: 'x', danger: true, onClick: async () => { const x = await run(() => api.post<Reply>(`/versions/${vid}/costs/delete?area=${area}`, { ids: [r.l.costLineId] }), 'Item deleted'); if (x) onChange(x) } } as CtxItem] : [])] }) }
  if (!data.lines.length) return <div className="empty"><Icon name="cone" size="l" /><h3>No expenses defined</h3><p>{area === 'precon' ? 'The direct costs of preconstruction: materials, equipment, travel and other expenses.' : 'Project overhead costs by category.'}</p>
    {canEdit && <button className="btn p" onClick={() => onAdd()}><Icon name="plus" />Add an expense or template</button>}</div>
  return <>
    <DataGrid<ERow> rows={rows} rowKey={r => r.kind === 'cat' ? 'c' + r.c : r.kind === 'l' ? r.l.costLineId : 'tot'} cols={cols} readOnly={!canEdit} onEdit={edit} onMenu={menu}
      rowCls={r => r.kind === 'cat' ? 'grp' : r.kind === 'l' && sel.has(r.l.costLineId) ? 'on' : undefined} bottom={[{ kind: 'tot' }]} height={area === 'precon' ? 'calc(100vh - 420px)' : 'calc(100vh - 380px)'} />
    <p className="muted" style={{ padding: '8px 12px' }}>Type in a cell, or paste a block copied from Excel (Ctrl+V); drag across cells and Ctrl+C to copy them for Excel. The quote columns on the right are worked out from the Quoted %.</p>
    {sel.size > 0 && <div className="selbar"><span>{sel.size} row{sel.size === 1 ? '' : 's'} selected</span><button className="btn sm" onClick={() => setSel(new Set())}>Cancel</button><button className="btn sm danger" onClick={() => setConfirm(true)}>Delete selected</button></div>}
    {confirm && <Modal title={`Delete (${sel.size} items)`} onClose={() => setConfirm(false)} footer={<><button className="btn" onClick={() => setConfirm(false)}>Cancel</button>
      <button className="btn danger" onClick={async () => { setConfirm(false); const r = await run(() => api.post<Reply>(`/versions/${vid}/costs/delete?area=${area}`, { ids: [...sel] }), 'Items deleted'); if (r) { onChange(r); setSel(new Set()) } }}>Delete items</button></>}>
      <p>Are you sure you want to delete the selected items? This action cannot be undone.</p></Modal>}
    {ctx && <ContextMenu {...ctx} onClose={() => setCtx(null)} />}
  </>
}

function AddExpense({ vid, area, category, categories, onClose, onDone }: { vid: number; area: string; category?: string; categories: string[]; onClose(): void; onDone(r: Reply): void }) {
  const { run, busy } = useAction(); const lk = useLookups()
  const [f, setF] = useState({ categoryName: category ?? categories[0] ?? 'General', itemName: '', csiCode: '', qty: '1', uomCode: 'LS', rate: '', isBillable: true })
  const set = (k: keyof typeof f, v: string | boolean) => setF(o => ({ ...o, [k]: v }))
  return <Modal title="Add expense" onClose={onClose} footer={<><button className="btn" onClick={onClose}>Cancel</button>
    <button className="btn p" disabled={busy || !f.itemName.trim()} onClick={async () => { const r = await run(() => api.post<Reply>(`/versions/${vid}/costs`, { area, ...f, qty: toNum(f.qty) ?? 1, rate: toNum(f.rate) ?? 0 }), 'Expense added'); if (r) onDone(r) }}><Icon name="plus" />Add</button></>}>
    <datalist id="cats">{categories.map(c => <option key={c} value={c} />)}</datalist><datalist id="csi-codes-add">{lk.data?.csiCodes.map(c => <option key={c.codeId} value={c.csiCode}>{c.codeTitle}</option>)}</datalist>
    <div className="fgrid">
      <div className="field"><label>Category</label><input className="inp" list="cats" value={f.categoryName} onChange={e => set('categoryName', e.target.value)} /><small>Pick one or type a new category.</small></div>
      <div className="field" style={{ gridColumn: 'span 2' }}><label>Description</label><input className="inp" autoFocus value={f.itemName} onChange={e => set('itemName', e.target.value)} placeholder="e.g. Estimating software licence" /></div>
      <div className="field"><label>CSI code</label><input className="inp" list="csi-codes-add" value={f.csiCode} onChange={e => set('csiCode', e.target.value)} /></div>
      <div className="field"><label>Qty</label><input className="inp num" value={f.qty} onChange={e => set('qty', e.target.value)} /></div>
      <div className="field"><label>Unit</label><select className="inp" value={f.uomCode} onChange={e => set('uomCode', e.target.value)}>{lk.data?.units.map(u => <option key={u.uomCode} value={u.uomCode}>{u.uomCode} · {u.uomName}</option>)}</select></div>
      <div className="field"><label>Rate</label><input className="inp num" value={f.rate} onChange={e => set('rate', e.target.value)} placeholder="$0" /></div>
    </div>
    <label className="check" style={{ marginTop: 10 }}><input type="checkbox" checked={f.isBillable} onChange={e => set('isBillable', e.target.checked)} /> Billable (quoted to the client at the Quoted %)</label>
  </Modal>
}

function AddPreconMember({ vid, group, onClose, onDone }: { vid: number; group?: TeamGroup; onClose(): void; onDone(r: Reply): void }) {
  const { run, busy } = useAction()
  return <StaffPicker title="Add team member in Precon services" saveLabel="Add team member" busy={busy} initial={{ teamGroup: group ?? 'corporate' }} onClose={onClose}
    onSave={async p => { const r = await run(() => api.post<Reply>(`/versions/${vid}/precon-team`, { teamGroup: p.teamGroup, staffId: p.staffId, staffRoleId: p.staffRoleId, postingRate: p.rate }), 'Team member added'); if (r) onDone(r) }} />
}

/** Change Precon Dept Tasks: library ⇄ this version's tasks, plus ad-hoc tasks for this project. */
function TasksModal({ vid, onClose, onDone }: { vid: number; onClose(): void; onDone(r: Reply): void }) {
  const { run, busy } = useAction()
  const { data } = useQuery({ queryKey: ['precon-tasks', vid], queryFn: () => api.get<{ activityId: number; taskName: string; isDefault: boolean; isAdhoc: boolean; selected: boolean }[]>(`/versions/${vid}/precon-tasks`) })
  const [right, setRight] = useState<number[] | null>(null); const [checked, setChecked] = useState<Set<number>>(new Set()); const [adhoc, setAdhoc] = useState<string[]>([]); const [txt, setTxt] = useState('')
  if (!data) return <Modal title="Change precon tasks" onClose={onClose}><Loading what="tasks" /></Modal>
  const R = right ?? data.filter(t => t.selected).map(t => t.activityId), L = data.filter(t => !R.includes(t.activityId))
  const nameOf = (id: number) => data.find(t => t.activityId === id)!
  const addAdhoc = () => { if (txt.trim()) { setAdhoc(a => [txt.trim(), ...a.filter(x => x.toLowerCase() !== txt.trim().toLowerCase())]); setTxt('') } }
  const list = (title: string, items: number[], side: 'l' | 'r') => { const on = items.filter(i => checked.has(i)).length
    return <div className="xfer"><div className="xh"><input type="checkbox" checked={items.length > 0 && on === items.length} ref={el => { if (el) el.indeterminate = on > 0 && on < items.length }}
      onChange={e => setChecked(s => { const x = new Set(s); items.forEach(i => e.target.checked ? x.add(i) : x.delete(i)); return x })} /><b>{title}</b><small>{on}/{items.length + (side === 'r' ? adhoc.length : 0)} selected</small></div>
      <div className="xl">{side === 'r' && adhoc.map(a => <div key={a} className="xi new"><span className="tag b">new</span>{a}<button className="rm" style={{ opacity: 1 }} onClick={() => setAdhoc(x => x.filter(y => y !== a))}><Icon name="x" size="s" /></button></div>)}
        {items.map(i => { const t = nameOf(i); return <label key={i} className="xi"><input type="checkbox" checked={checked.has(i)} onChange={e => setChecked(s => { const x = new Set(s); e.target.checked ? x.add(i) : x.delete(i); return x })} />{t.taskName}
          {t.isAdhoc && <small className="tag n">ad-hoc</small>}{t.isDefault && <small className="mut">default</small>}</label> })}</div></div> }
  return <Modal wide title="Change precon tasks" onClose={onClose} footer={<><button className="btn" onClick={onClose}>Cancel</button>
    <button className="btn p" disabled={busy || (R.length === 0 && adhoc.length === 0)} title={R.length === 0 && adhoc.length === 0 ? 'Select a task from the library or add an ad-hoc one' : undefined}
      onClick={async () => { const r = await run(() => api.put<Reply>(`/versions/${vid}/precon-tasks`, { activityIds: R, adhoc }), 'Tasks saved'); if (r) onDone(r) }}><Icon name="check" />Save</button></>}>
    <div className="xfer-wrap">{list('Library', L.map(t => t.activityId), 'l')}
      <div className="xbtns"><button className="btn sm" disabled={!L.some(t => checked.has(t.activityId))} onClick={() => { setRight([...R, ...L.filter(t => checked.has(t.activityId)).map(t => t.activityId)]); setChecked(new Set()) }} aria-label="Move selected right"><Icon name="chevr" /></button>
        <button className="btn sm" disabled={!R.some(i => checked.has(i))} onClick={() => { setRight(R.filter(i => !checked.has(i))); setChecked(new Set()) }} aria-label="Move selected left"><Icon name="chevl" /></button></div>
      {list('Project tasks', R, 'r')}</div>
    <div className="field" style={{ marginTop: 14 }}><label>Add ad-hoc task for this project</label><div style={{ display: 'flex', gap: 8 }}>
      <input className="inp" value={txt} onChange={e => setTxt(e.target.value)} onKeyDown={e => { if (e.key === 'Enter') addAdhoc() }} placeholder="e.g. Site logistics plan" />
      <button className="btn" disabled={!txt.trim()} onClick={addAdhoc}><Icon name="plus" />Add</button></div></div>
    <p className="muted">Removing a task hides its column; hours already entered come back if you add the task again.</p>
  </Modal>
}
