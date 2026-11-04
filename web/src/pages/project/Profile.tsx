// Project profile (was: Project setup / Project information): details, zones, versions, rates, members, documents, activity.
import { useEffect, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { AreaUnit, CarveOuts, ProjectDetail, RatesData, RollUpData, UnitLabel, Version, Zone } from '../../api/types'
import { calcSummary } from '@gridline/calc'
import { IndirectModal } from './BidBoard'
import { PageHead } from '../../components/Shell'
import { Icon } from '../../components/Icon'
import { EditCell, Kpi, Loading, Modal, Seg, Tabs, toNum, useAction } from '../../components/ui'
import { cap, date, money, num } from '../../app/format'
import { useLookups, vkey } from '../../app/hooks'
import { weeksBetween } from '../../app/crewCalc'
import { useAuth } from '../../app/auth'
import { describe } from '../../app/verbs'

type Tab = 'details' | 'zones' | 'versions' | 'rates' | 'fees' | 'members' | 'documents' | 'activity'
const ZCOL = ['#3341c9', '#7a5cf0', '#f2a516', '#11806b', '#e11d74', '#0ea5e9', '#65a30d', '#ea580c']

export default function Profile({ project, version }: { project: ProjectDetail; version: Version }) {
  const [sp] = useSearchParams()
  const [tab, setTab] = useState<Tab>(() => (['details', 'zones', 'versions', 'rates', 'fees', 'members', 'documents', 'activity'].includes(sp.get('tab') ?? '') ? sp.get('tab') : 'details') as Tab)
  return <>
    <PageHead eyebrow={`${project.projectNo} · v${version.versionNo}`} title="Project profile" was="Project setup / Project information"
      desc="Project details, zones, versions and the rates used in pricing." />
    <div className="card"><Tabs<Tab> value={tab} onChange={setTab} items={[['details', 'Details'], ['zones', 'Zones'], ['rates', 'Rates & indirects'], ['fees', 'Fee carve-outs'], ['versions', 'Versions', project.versions.length], ['members', 'Members'], ['documents', 'Documents'], ['activity', 'Activity']]} />
      <div className="cb">
        {tab === 'details' && <Details project={project} />}
        {tab === 'zones' && <Zones project={project} version={version} />}
        {tab === 'versions' && <Versions project={project} version={version} />}
        {tab === 'rates' && <Rates project={project} version={version} />}
        {tab === 'fees' && <FeeCarveOuts project={project} version={version} />}
        {tab === 'members' && <Members project={project} />}
        {tab === 'documents' && <Documents project={project} />}
        {tab === 'activity' && <Activity project={project} />}
      </div></div></>
}

/**
 * Schedule dates (was: Project setup → Preconstruction start/end, Est construction start, Construction substantial completion,
 * Closeout date) and salary escalation. The crew plan's precon, construction and closeout weeks follow these dates; changing a
 * date first shows how many weeks are added or removed (and the hours that would go) and asks to confirm, like today.
 */
function ScheduleDates({ project }: { project: ProjectDetail }) {
  const qc = useQueryClient(); const { run, busy } = useAction()
  const init = () => ({ preconStartOn: project.preconStartOn ?? '', preconEndOn: project.preconEndOn ?? '', buildStartOn: project.buildStartOn ?? '', buildEndOn: project.buildEndOn ?? '',
    closeoutOn: project.closeoutOn ?? '', salaryEscalationPct: String(project.salaryEscalationPct ?? 3) })
  const [f, setF] = useState(init); useEffect(() => setF(init()), [project.rowRev, project.buildStartOn, project.buildEndOn, project.closeoutOn, project.preconStartOn, project.preconEndOn, project.salaryEscalationPct]) // eslint-disable-line react-hooks/exhaustive-deps
  const [confirmFor, setConfirmFor] = useState<{ changes: { phase: string; added: number; removed: number; hoursLost: number }[] } | null>(null)
  const canEdit = project.access !== 'viewer'
  const dirty = JSON.stringify(f) !== JSON.stringify(init())
  const errs: Record<string, string> = {}
  if (f.preconStartOn && f.preconEndOn && f.preconEndOn < f.preconStartOn) errs.preconEndOn = 'Must be after the start date'
  if (!!f.preconStartOn !== !!f.preconEndOn) errs[f.preconStartOn ? 'preconEndOn' : 'preconStartOn'] = 'Give both precon dates, or neither'
  if (f.buildStartOn && f.buildEndOn && f.buildEndOn < f.buildStartOn) errs.buildEndOn = 'Must be after Est construction start'
  if (!!f.buildStartOn !== !!f.buildEndOn) errs[f.buildStartOn ? 'buildEndOn' : 'buildStartOn'] = 'Needed with the other construction date'
  if (f.closeoutOn && f.buildEndOn && f.closeoutOn < f.buildEndOn) errs.closeoutOn = 'Must be after substantial completion'
  if (f.closeoutOn && !f.buildEndOn) errs.closeoutOn = 'Set substantial completion first'
  const esc = toNum(f.salaryEscalationPct); if (esc == null || esc < 0 || esc > 25) errs.salaryEscalationPct = '0 to 25% a year'
  const body = (preview: boolean) => ({ preconStartOn: f.preconStartOn || null, preconEndOn: f.preconEndOn || null, buildStartOn: f.buildStartOn || null, buildEndOn: f.buildEndOn || null,
    closeoutOn: f.closeoutOn || null, salaryEscalationPct: esc, preview })
  async function check() {
    const r = await run(() => api.put<{ changes: { phase: string; added: number; removed: number; hoursLost: number }[] }>(`/projects/${project.projectId}/dates`, body(true)))
    if (!r) return
    if (r.changes.some(c => c.added || c.removed)) setConfirmFor(r); else await save()
  }
  async function save() {
    setConfirmFor(null)
    if (await run(() => api.put(`/projects/${project.projectId}/dates`, body(false)), 'Schedule dates saved · crew plan weeks updated'))
      qc.invalidateQueries({ predicate: q => q.queryKey[0] === 'project' || q.queryKey[0] === 'crew' || q.queryKey[0] === 'price-roll-up' || q.queryKey[0] === 'bid-board' })
  }
  const D = (k: keyof typeof f, label: string, weeksFrom?: string, was?: string) => { const w = weeksBetween(weeksFrom, f[k]); return <div className="field" key={k} data-was={was}>
    <label>{label}{w != null && w >= 0 && <span className="wk-suffix"> ({w} weeks)</span>}</label>
    <input className={`inp ${errs[k] ? 'bad' : ''}`} type="date" disabled={!canEdit} value={f[k]} onChange={e => setF({ ...f, [k]: e.target.value })} />{errs[k] && <small className="err">! {errs[k]}</small>}</div> }
  const phaseName: Record<string, string> = { precon: 'precon', build: 'construction', closeout: 'closeout' }
  return <section className="sched" id="schedule"><h3 className="sec-h">Schedule <small className="mut">the crew plan's weeks follow these dates</small></h3>
    <div className="fgrid">
      {D('preconStartOn', 'Preconstruction start date', undefined, 'PreConstructionStartDate')}{D('preconEndOn', 'Preconstruction end date', f.preconStartOn, 'PreConstructionEndDate')}
      {D('buildStartOn', 'Est construction start date', undefined, 'CRM_EstConstructionStartDate')}{D('buildEndOn', 'Construction substantial completion date', f.buildStartOn, 'CRM_EstConstructionEndDate')}
      {D('closeoutOn', 'Closeout date', f.buildEndOn, 'CloseOutDate')}
      <div className="field" data-was="SalaryEscalationRate"><label>Salary escalation rate (% a year)</label><input className={`inp num ${errs.salaryEscalationPct ? 'bad' : ''}`} disabled={!canEdit} value={f.salaryEscalationPct} onChange={e => setF({ ...f, salaryEscalationPct: e.target.value })} />
        {errs.salaryEscalationPct ? <small className="err">! {errs.salaryEscalationPct}</small> : <small>Crew pay rises this much each calendar year.</small>}</div>
    </div>
    {canEdit && dirty && <div className="tb" style={{ border: 0, paddingLeft: 0 }}><button className="btn p" disabled={busy || Object.keys(errs).length > 0} onClick={check}><Icon name="check" />Save dates</button><button className="btn g" onClick={() => setF(init())}>Undo</button></div>}
    {confirmFor && <Modal title="Change the schedule?" onClose={() => setConfirmFor(null)} footer={<><button className="btn" onClick={() => setConfirmFor(null)}>Cancel</button><button className="btn p" disabled={busy} onClick={save}><Icon name="check" />Change dates</button></>}>
      <p>Changing these dates will update the crew plan's weeks:</p>
      <ul>{confirmFor.changes.filter(c => c.added || c.removed).map(c => <li key={c.phase}>
        {c.added > 0 && <>add <b>{c.added}</b> {phaseName[c.phase]} week{c.added === 1 ? '' : 's'}</>}{c.added > 0 && c.removed > 0 && ' and '}
        {c.removed > 0 && <>remove <b>{c.removed}</b> {phaseName[c.phase]} week{c.removed === 1 ? '' : 's'}{c.hoursLost > 0 && <> — <b className="bad">{num(c.hoursLost)} hours</b> entered in them will be deleted</>}</>}</li>)}</ul>
    </Modal>}
  </section>
}

function Details({ project }: { project: ProjectDetail }) {
  const lk = useLookups(); const qc = useQueryClient(); const { run, busy } = useAction(); const nav = useNavigate()
  const init = () => ({ projectName: project.projectName, projectNo: project.projectNo, clientName: project.clientName ?? '', stage: project.stage, projectType: project.projectType ?? '', contractType: project.contractType ?? '',
    divisionId: project.divisionId ?? '', branchId: project.branchId ?? '', street: project.street ?? '', city: project.city ?? '', stateCode: project.stateCode ?? '', countyName: project.countyName ?? '',
    postalCode: project.postalCode ?? '', bidDueOn: project.bidDueOn ?? '', crmRef: project.crmRef ?? '', estimatorId: project.estimatorId ?? '', managerId: project.managerId ?? '' })
  const [f, setF] = useState(init); useEffect(() => setF(init()), [project.rowRev])  // eslint-disable-line react-hooks/exhaustive-deps
  const canEdit = project.access !== 'viewer'
  const dirty = JSON.stringify(f) !== JSON.stringify(init())
  const T = (k: keyof typeof f, l: string, type = 'text') => <div className="field" key={k}><label>{l}</label><input className="inp" type={type} disabled={!canEdit || k === 'projectNo'} value={String(f[k])} onChange={e => setF({ ...f, [k]: e.target.value })} /></div>
  const S = (k: keyof typeof f, l: string, opts: [string | number, string][]) => <div className="field" key={k}><label>{l}</label><select className="inp" disabled={!canEdit} value={String(f[k])} onChange={e => setF({ ...f, [k]: e.target.value })}><option value="">–</option>{opts.map(([v, t]) => <option key={v} value={v}>{t}</option>)}</select></div>
  async function save() {
    const body = { ...f, divisionId: f.divisionId ? +f.divisionId : null, branchId: f.branchId ? +f.branchId : null, estimatorId: f.estimatorId ? +f.estimatorId : null, managerId: f.managerId ? +f.managerId : null, bidDueOn: f.bidDueOn || null, projectNo: undefined, rowRev: project.rowRev }
    if (await run(() => api.patch(`/projects/${project.projectId}`, body), 'Project saved')) qc.invalidateQueries({ queryKey: ['project', project.projectNo] })
  }
  return <><ScheduleDates project={project} />
    <h3 className="sec-h">Project details</h3><div className="fgrid">
    {T('projectName', 'Project name')}{T('projectNo', 'Project number')}{T('clientName', 'Client')}
    {S('stage', 'Stage', (lk.data?.stages ?? []).map(s => [s, cap(s)]))}{T('projectType', 'Project type')}{T('contractType', 'Contract type')}
    {S('divisionId', 'Business unit', (lk.data?.divisions ?? []).map(d => [d.divisionId, d.divisionName]))}{S('branchId', 'Office', (lk.data?.branches ?? []).map(b => [b.branchId, b.branchName]))}
    {T('street', 'Address')}{T('city', 'City')}{S('stateCode', 'State', (lk.data?.states ?? []).map(s => [s.stateCode, `${s.stateCode} · ${s.stateName}`]))}{T('countyName', 'County')}{T('postalCode', 'ZIP')}
    {T('bidDueOn', 'Bid due', 'date')}{T('crmRef', 'CRM reference')}
    {S('estimatorId', 'Estimator', (lk.data?.users ?? []).map(u => [u.userId, u.name]))}{S('managerId', 'Project manager', (lk.data?.users ?? []).map(u => [u.userId, u.name]))}</div>
    {canEdit && <div className="tb" style={{ border: 0, paddingLeft: 0 }}><button className="btn p" disabled={!dirty || busy} onClick={save}><Icon name="check" />Save changes</button>{dirty && <button className="btn g" onClick={() => setF(init())}>Undo</button>}
      <span className="sp" />{project.access === 'owner' && <button className="btn" style={{ color: 'var(--bad,#d1344b)' }} onClick={async () => { if (confirm(`Archive ${project.projectName}? It disappears from the portfolio.`) && await run(() => api.del(`/projects/${project.projectId}`), 'Project archived')) nav('/projects') }}><Icon name="x" />Archive project</button>}</div>}</>
}

const AREA_UNITS: AreaUnit[] = ['SF', 'RSF', 'GSF', 'USF']
const UNIT_LABELS: [UnitLabel, string][] = [['N/A', 'N/A'], ['Unit', 'Units'], ['MW', 'MW'], ['Key', 'Keys'], ['Space', 'Spaces']]
/** Zones (was: Breakout areas). Same options as today's Areas & metrics: area + unit of area, quantity + unit of quantity,
 *  include in total SF, reserve % per zone, "remainder of base bid", and "zones equal total project area". */
function Zones({ project, version }: { project: ProjectDetail; version: Version }) {
  const vid = version.versionId; const qc = useQueryClient(); const { run, busy } = useAction()
  const { data } = useQuery({ queryKey: vkey('zones', vid), queryFn: () => api.get<Zone[]>(`/versions/${vid}/zones`) })
  const [rows, setRows] = useState<Partial<Zone>[] | null>(null); useEffect(() => { if (data) setRows(data.map(z => ({ ...z }))) }, [data])
  const initSet = () => ({ zonesSetArea: version.zonesSetArea, primaryAreaUnit: version.primaryAreaUnit, primaryUnitLabel: version.primaryUnitLabel, grossArea: version.grossArea })
  const [vs, setVs] = useState(initSet); useEffect(() => setVs(initSet()), [version.rowRev, version.grossArea, version.zonesSetArea, version.primaryAreaUnit, version.primaryUnitLabel])
  const [open, setOpen] = useState<Set<number>>(new Set()); const [drag, setDrag] = useState<number | null>(null)
  const canEdit = project.access !== 'viewer' && version.status !== 'locked'
  if (!rows) return <Loading what="zones" />
  const included = rows.filter(z => !z.excludeFromTotal)
  const totalShare = rows.reduce((s, z) => s + (z.defaultShare ?? 0), 0), totalSf = included.reduce((s, z) => s + (z.floorArea ?? 0), 0)
  const totalUnits = vs.primaryUnitLabel === 'N/A' ? 0 : rows.filter(z => z.unitLabel === vs.primaryUnitLabel).reduce((s, z) => s + (z.unitCount ?? 0), 0)
  const allUnits = rows.reduce((s, z) => s + (z.unitCount ?? 0), 0)
  const set = (i: number, k: keyof Zone, v: unknown) => setRows(r => r!.map((z, j) => j === i ? { ...z, [k]: v } : z))
  const move = (from: number, to: number) => setRows(r => { const a = [...r!]; const [x] = a.splice(from, 1); a.splice(to, 0, x); return a })
  const remIdx = rows.findIndex(z => z.isRemainder)
  const setRemainder = (i: number) => setRows(r => r!.map((z, j) => ({ ...z, isRemainder: j === i })))
  const pct = (v: number | undefined) => v ? String(+v) : ''
  async function save() {
    const r = await run(() => api.put<Zone[]>(`/versions/${vid}/zones`, { zones: rows, zonesSetArea: vs.zonesSetArea, primaryAreaUnit: vs.primaryAreaUnit, primaryUnitLabel: vs.primaryUnitLabel, grossArea: vs.zonesSetArea ? null : vs.grossArea }), 'Zones saved · prices recalculated')
    if (r) qc.invalidateQueries({ predicate: q => JSON.stringify(q.queryKey).includes(`"v${vid}"`) || q.queryKey[0] === 'project' })
  }
  return <>
    <div className="zsum">
      <div className="field"><label>Total area size ({vs.primaryAreaUnit})</label>
        {vs.zonesSetArea ? <div className="inp ro num">{num(totalSf)}<small className="mut"> · from the zones</small></div>
          : <input className="inp num" disabled={!canEdit} value={vs.grossArea ?? ''} onChange={e => setVs({ ...vs, grossArea: toNum(e.target.value) ?? 0 })} />}</div>
      <div className="field"><label>Primary unit of area</label><select className="inp" disabled={!canEdit} value={vs.primaryAreaUnit} onChange={e => setVs({ ...vs, primaryAreaUnit: e.target.value as AreaUnit })}>{AREA_UNITS.map(u => <option key={u}>{u}</option>)}</select></div>
      <div className="field"><label>Primary unit of quantity</label><select className="inp" disabled={!canEdit} value={vs.primaryUnitLabel} onChange={e => setVs({ ...vs, primaryUnitLabel: e.target.value as UnitLabel })}>{UNIT_LABELS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select></div>
      <div className="field"><label>Total units</label><div className="inp ro num">{vs.primaryUnitLabel === 'N/A' ? '—' : num(totalUnits)}<small className="mut">{vs.primaryUnitLabel === 'N/A' ? ' · pick a primary unit' : ` · zones counted in ${UNIT_LABELS.find(u => u[0] === vs.primaryUnitLabel)![1]}`}</small></div></div>
    </div>
    <p className="muted">Zones break the price out by area (building, phase, site). In Scope match every bidder can give an amount per zone; the <b>remainder of base bid</b> zone gets whatever is left of the bid total. Trades without zone amounts use the default split.</p>
    <div className="gw"><table className="dg ztab" style={{ width: '100%' }}><thead><tr><th style={{ width: 22 }} /><th style={{ width: 34 }} /><th style={{ width: 80 }}>Code</th><th>Zone name</th>
      <th className="r" style={{ width: 110 }}>Area</th><th style={{ width: 76 }}>Unit</th><th className="r" style={{ width: 90 }}>Quantity</th><th style={{ width: 90 }}>Unit type</th>
      <th style={{ width: 66 }} title="Include this zone's area in the total project area">Include</th><th style={{ width: 84 }} title="Remainder of base bid">Remainder</th><th className="r" style={{ width: 90 }}>Default %</th><th style={{ width: 92 }} /></tr></thead>
      <tbody>{rows.map((z, i) => { const k = z.zoneId ?? -i - 1; const ex = open.has(k)
        return [<tr key={k} draggable={canEdit} onDragStart={() => setDrag(i)} onDragOver={e => { if (drag != null) e.preventDefault() }} onDrop={() => { if (drag != null && drag !== i) move(drag, i); setDrag(null) }} onDragEnd={() => setDrag(null)} style={drag === i ? { opacity: .4 } : undefined}>
          <td>{canEdit && <span title="Drag to change the order (the order zones show in reports)" style={{ cursor: 'grab', color: 'var(--faint)' }}>⠿</span>}</td>
          <td><input type="color" disabled={!canEdit} value={z.colour ?? ZCOL[i % ZCOL.length]} onChange={e => set(i, 'colour', e.target.value)} style={{ width: 28, height: 22, border: 0, padding: 0 }} /></td>
          <td><input className="inp cellin" disabled={!canEdit} value={z.zoneCode ?? ''} onChange={e => set(i, 'zoneCode', e.target.value.toUpperCase())} /></td>
          <td><input className="inp cellin" style={{ width: '100%' }} disabled={!canEdit} value={z.zoneName ?? ''} placeholder="Zone name…" onChange={e => set(i, 'zoneName', e.target.value)} /></td>
          <td><input className="inp cellin num" disabled={!canEdit} value={z.floorArea ?? ''} onChange={e => set(i, 'floorArea', Math.max(0, Math.round(toNum(e.target.value) ?? 0)))} /></td>
          <td><select className="inp cellin" disabled={!canEdit} value={z.areaUnit ?? 'SF'} onChange={e => set(i, 'areaUnit', e.target.value)}>{AREA_UNITS.map(u => <option key={u}>{u}</option>)}</select></td>
          <td><input className="inp cellin num" disabled={!canEdit} value={z.unitCount ?? ''} onChange={e => set(i, 'unitCount', toNum(e.target.value))} /></td>
          <td><select className="inp cellin" disabled={!canEdit} value={z.unitLabel ?? 'N/A'} onChange={e => set(i, 'unitLabel', e.target.value)}>{UNIT_LABELS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select></td>
          <td style={{ textAlign: 'center' }}><input type="checkbox" disabled={!canEdit} checked={!z.excludeFromTotal} onChange={e => set(i, 'excludeFromTotal', !e.target.checked)} aria-label={`Include ${z.zoneName || 'zone'} in total area`} /></td>
          <td style={{ textAlign: 'center' }}><input type="radio" name="zrem" disabled={!canEdit} checked={!!z.isRemainder} onChange={() => setRemainder(i)} aria-label={`${z.zoneName || 'zone'} is the remainder of base bid`} /></td>
          <td><input className="inp cellin num" disabled={!canEdit} value={z.defaultShare != null ? +(z.defaultShare * 100).toFixed(2) : ''} onChange={e => set(i, 'defaultShare', toNum(e.target.value) == null ? null : toNum(e.target.value)! / 100)} /></td>
          <td style={{ whiteSpace: 'nowrap' }}><button className="rm" style={{ opacity: 1 }} onClick={() => setOpen(o => { const n = new Set(o); n.has(k) ? n.delete(k) : n.add(k); return n })} title={ex ? 'Hide reserves' : 'Reserve % for this zone'}><span style={{ display: 'inline-flex', transform: ex ? 'rotate(180deg)' : undefined }}><Icon name="chev" size="s" /></span></button>
            {canEdit && <><button className="rm" style={{ opacity: 1 }} disabled={i === 0} onClick={() => move(i, i - 1)} title="Move up"><Icon name="chevl" size="s" /></button><button className="rm" style={{ opacity: 1 }} disabled={i === rows.length - 1} onClick={() => move(i, i + 1)} title="Move down"><Icon name="chevr" size="s" /></button>
              <button className="rm" style={{ opacity: 1 }} onClick={() => setRows(r => r!.filter((_, j) => j !== i))} title="Remove zone"><Icon name="x" size="s" /></button></>}</td></tr>,
          ex ? <tr key={k + 'x'} className="zx"><td /><td /><td colSpan={10}><div className="zres">
            {([['projectReservePct', 'Project reserve', 'was: project contingency'], ['designReservePct', 'Design reserve', 'was: design contingency'], ['escalationReservePct', 'Escalation reserve', 'was: escalation contingency']] as const).map(([f, l, was]) =>
              <div className="field" key={f} data-was={was}><label>{l} %</label><input className="inp num" disabled={!canEdit} value={pct(z[f])} placeholder="0" onChange={e => set(i, f, toNum(e.target.value) ?? 0)} /></div>)}
            <small className="mut">When any zone has a reserve %, that reserve on the Price roll-up uses the zones' rates, weighted by each zone's share of the trades.</small></div></td></tr> : null] })}</tbody>
      <tfoot><tr><td /><td /><td /><td>Total{rows.some(z => z.excludeFromTotal) ? ' (included zones)' : ''}</td><td className="n">{num(totalSf)}</td><td /><td className="n">{num(allUnits)}</td><td /><td /><td /><td className="n" style={{ color: Math.abs(totalShare - 1) > 0.001 && rows.length ? 'var(--bad,#d1344b)' : undefined }}>{(totalShare * 100).toFixed(2)}%</td><td /></tr></tfoot></table></div>
    <div className="zopts">
      <label className="check"><input type="checkbox" disabled={!canEdit} checked={vs.zonesSetArea} onChange={e => setVs({ ...vs, zonesSetArea: e.target.checked })} /> Zones equal total project area</label>
      <div className="field" style={{ minWidth: 220 }}><label>Remainder of base bid</label><select className="inp" disabled={!canEdit} value={remIdx} onChange={e => setRemainder(Number(e.target.value))}>
        <option value={-1}>None</option>{rows.map((z, i) => <option key={i} value={i}>{z.zoneName || 'Unnamed zone'}</option>)}</select></div>
      <span className="mut" style={{ fontSize: 12 }}>Drag rows to change report order.</span>
    </div>
    {canEdit && <div className="tb" style={{ border: 0, paddingLeft: 0 }}><button className="btn sm" onClick={() => setRows(r => [...r!, { zoneCode: '', zoneName: '', floorArea: 0, areaUnit: 'SF', unitLabel: 'N/A', colour: ZCOL[r!.length % ZCOL.length] }])}><Icon name="plus" size="s" />Add zone</button><span className="sp" />
      <button className="btn p" disabled={busy} onClick={save}><Icon name="check" />Save zones</button></div>}
    <p className="muted">Gross area of this version: {num(version.grossArea)} {version.primaryAreaUnit}{version.zonesSetArea ? ' (the sum of the included zones)' : ' (entered by hand)'}{version.primaryUnitLabel !== 'N/A' ? ` · ${num(version.totalUnits)} ${UNIT_LABELS.find(u => u[0] === version.primaryUnitLabel)![1]}` : ''}.</p></>
}

function Versions({ project, version }: { project: ProjectDetail; version: Version }) {
  const qc = useQueryClient(); const nav = useNavigate(); const { run, busy } = useAction(); const { can } = useAuth()
  const [newOpen, setNewOpen] = useState(false); const [a, setA] = useState(project.versions[0]?.versionId); const [b, setB] = useState(version.versionId)
  const cmp = useQuery({ queryKey: ['compare', a, b], queryFn: () => api.get<{ trades: { csiCode: string; tradeName: string; a: number | null; b: number | null; delta: number; change: string }[]; summary: { name: string; a: number | null; b: number | null; delta: number }[] }>(`/versions/compare?a=${a}&b=${b}`), enabled: !!a && !!b && a !== b })
  const refresh = () => qc.invalidateQueries({ queryKey: ['project', project.projectNo] })
  const canEdit = project.access !== 'viewer'
  return <><div className="gw"><table className="dg" style={{ width: '100%' }}><thead><tr><th>Version</th><th>Label</th><th>Status</th><th className="r">Gross SF</th><th>Created</th><th /></tr></thead>
    <tbody>{project.versions.map(v => <tr key={v.versionId} className={v.versionId === version.versionId ? 'on' : ''}><td><b>v{v.versionNo}</b> {v.isPrimary && <span className="tag g">Primary</span>}</td>
      <td>{canEdit ? <EditCell value={v.versionLabel} onSave={x => run(() => api.patch(`/versions/${v.versionId}`, { versionLabel: x }).then(refresh), 'Renamed')} /> : v.versionLabel}</td>
      <td><span className={`tag ${v.status === 'locked' ? 'r' : v.status === 'submitted' ? 'b' : 'n'}`}>{cap(v.status)}</span></td><td className="n">{num(v.grossArea)}</td><td>{date(v.createdAt?.slice(0, 10))}</td>
      <td style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
        <button className="btn sm" onClick={() => nav(`/p/${encodeURIComponent(project.projectNo)}/v/${v.versionNo}/bid-board`)}>Open</button>
        <button className="btn sm g ic" title="Open in a new tab" onClick={() => window.open(`/p/${encodeURIComponent(project.projectNo)}/v/${v.versionNo}/bid-board`, '_blank')}><Icon name="expand" size="s" /></button>
        {canEdit && !v.isPrimary && <button className="btn sm" onClick={() => run(() => api.post(`/versions/${v.versionId}/primary`).then(refresh), `v${v.versionNo} is now primary`)}>Make primary</button>}
        {can('version.lock') && <button className="btn sm" onClick={() => run(() => api.patch(`/versions/${v.versionId}`, { status: v.status === 'locked' ? 'active' : 'locked' }).then(refresh), v.status === 'locked' ? 'Unlocked' : 'Locked')}><Icon name="lock" size="s" />{v.status === 'locked' ? 'Unlock' : 'Lock'}</button>}</td></tr>)}</tbody></table></div>
    {canEdit && <div className="tb" style={{ border: 0, paddingLeft: 0 }}><button className="btn p" onClick={() => setNewOpen(true)}><Icon name="plus" />New version</button>
      <button className="btn" disabled={busy} onClick={async () => { const name = prompt('Name for the copy', `${project.projectName} (copy)`); if (!name) return; const r = await run(() => api.post<{ projectNo: string }>(`/projects/${project.projectId}/copy`, { versionId: version.versionId, projectName: name }), 'Project copied'); if (r) nav(`/p/${encodeURIComponent(r.projectNo)}/v/1/bid-board`) }}><Icon name="copy" />Copy as a new project</button></div>}
    {project.versions.length > 1 && <div className="card" style={{ marginTop: 14 }}><div className="ch"><h3>Compare versions</h3>
      <select className="inp" style={{ marginLeft: 'auto' }} value={a} onChange={e => setA(+e.target.value)}>{project.versions.map(v => <option key={v.versionId} value={v.versionId}>v{v.versionNo}</option>)}</select>
      <span className="muted">with</span><select className="inp" value={b} onChange={e => setB(+e.target.value)}>{project.versions.map(v => <option key={v.versionId} value={v.versionId}>v{v.versionNo}</option>)}</select></div>
      {a === b ? <div className="empty">Pick two different versions.</div> : !cmp.data ? <Loading what="the comparison" /> :
        <div className="grid2"><div className="gw" style={{ maxHeight: 420 }}><table className="dg" style={{ width: '100%' }}><thead><tr><th>Trade</th><th className="r">A</th><th className="r">B</th><th className="r">Δ</th></tr></thead>
          <tbody>{cmp.data.trades.filter(t => t.change !== 'same').map(t => <tr key={t.csiCode}><td><span className="code">{t.csiCode}</span> {t.tradeName} <span className={`tag ${t.change === 'added' ? 'g' : t.change === 'removed' ? 'r' : 'y'}`}>{t.change}</span></td>
            <td className="n">{money(t.a)}</td><td className="n">{money(t.b)}</td><td className="n" style={{ color: t.delta > 0 ? 'var(--bad,#d1344b)' : 'var(--good)' }}>{money(t.delta)}</td></tr>)}
            {cmp.data.trades.every(t => t.change === 'same') && <tr><td colSpan={4} className="mut">Every trade has the same value.</td></tr>}</tbody></table></div>
          <div className="gw"><table className="dg" style={{ width: '100%' }}><thead><tr><th>Price roll-up</th><th className="r">A</th><th className="r">B</th><th className="r">Δ</th></tr></thead>
            <tbody>{cmp.data.summary.map(s => <tr key={s.name}><td>{s.name}</td><td className="n">{money(s.a)}</td><td className="n">{money(s.b)}</td><td className="n">{s.delta ? money(s.delta) : '–'}</td></tr>)}</tbody></table></div></div>}</div>}
    {newOpen && <NewVersion project={project} from={version} onClose={() => setNewOpen(false)} onDone={no => { setNewOpen(false); refresh(); nav(`/p/${encodeURIComponent(project.projectNo)}/v/${no}/profile`) }} />}</>
}
function NewVersion({ project, from, onClose, onDone }: { project: ProjectDetail; from: Version; onClose(): void; onDone(no: number): void }) {
  const { run, busy } = useAction(); const [src, setSrc] = useState(from.versionId); const [label, setLabel] = useState('')
  return <Modal title="New version" onClose={onClose} footer={<><button className="btn" onClick={onClose}>Cancel</button><button className="btn p" disabled={busy} onClick={async () => { const r = await run(() => api.post<{ versionNo: number }>(`/projects/${project.projectId}/versions`, { fromVersionId: src, versionLabel: label || null }), 'Version created'); if (r) onDone(r.versionNo) }}><Icon name="plus" />Create</button></>}>
    <div className="fgrid"><div className="field"><label>Copy everything from</label><select className="inp" value={src} onChange={e => setSrc(+e.target.value)}>{project.versions.map(v => <option key={v.versionId} value={v.versionId}>v{v.versionNo} · {v.versionLabel}</option>)}</select></div>
      <div className="field"><label>Label</label><input className="inp" value={label} onChange={e => setLabel(e.target.value)} placeholder={`Version ${project.versions.length + 1}`} /></div></div>
    <p className="muted">Zones, rates, trades with their bids and scope answers, estimates, indirect items and cost lines are copied. The original is not changed.</p>
  </Modal>
}

/** On/off switch used on the Rates page: off = counts as $0 in the roll-up (the rate or amount is kept). */
function OnOff({ on, disabled, onChange, label }: { on: boolean; disabled?: boolean; onChange(v: boolean): void; label: string }) {
  return <button className="tog" role="switch" aria-checked={on} aria-label={label} title={on ? `${label}: on (click to switch off — it then counts as $0)` : `${label}: off (counts as $0)`} disabled={disabled} onClick={() => onChange(!on)} />
}
function Rates({ project, version }: { project: ProjectDetail; version: Version }) {
  const vid = version.versionId; const qc = useQueryClient(); const { run } = useAction()
  const { data } = useQuery({ queryKey: vkey('markups', vid), queryFn: () => api.get<RatesData>(`/versions/${vid}/markups`) })
  const ru = useQuery({ queryKey: vkey('price-roll-up', vid), queryFn: () => api.get<RollUpData>(`/versions/${vid}/price-roll-up`) })
  const [addInd, setAddInd] = useState(false)
  const canEdit = project.access !== 'viewer' && version.status !== 'locked'
  if (!data || !ru.data) return <Loading what="rates" />
  const refresh = () => { qc.invalidateQueries({ queryKey: vkey('markups', vid) }); qc.invalidateQueries({ queryKey: vkey('price-roll-up', vid) }); qc.invalidateQueries({ queryKey: vkey('bid-board', vid) }) }
  const save = async (code: string, name: string, body: object) => { if (await run(() => api.put(`/versions/${vid}/markups`, [{ code, ...body }]), `${name} saved`)) refresh() }
  const saveInd = async (id: number, body: object, ok = 'Indirect item saved') => { if (await run(() => api.patch(`/indirect/${id}`, body), ok)) refresh() }
  const amountOf = (code: string) => ru.data!.lines.find(l => l.code === code)?.amount
  const base: Record<string, string> = { subtotal: '% of direct cost total', contract: '% of subtotal', tax: '% of contract total' }
  const co = ru.data.carveOuts
  return <><p className="muted">Reserves, bonds & cover, insurance, fee and sales tax for this version, and the indirect items. The Price roll-up uses these. A fixed amount replaces the rate. Switch an item <b>off</b> to make it count as $0 without losing what you typed.</p>
    <div className="gw"><table className="dg rates" style={{ width: '100%' }}><thead><tr><th style={{ width: 56 }}>On</th><th>Item</th><th>Applied to</th><th className="r" style={{ width: 110 }}>Rate %</th><th className="r">Company default</th><th className="r" style={{ width: 140 }}>Fixed amount</th><th className="r" style={{ width: 140 }}>Budget</th><th className="r" style={{ width: 130 }}>In roll-up</th></tr></thead>
      <tbody>
        {data.costs.map(c => <tr key={c.code} className={c.isOff ? 'off' : ''}><td><OnOff label={c.name} on={!c.isOff} disabled={!canEdit} onChange={v => save(c.code, c.name, { isOff: !v })} /></td>
          <td><b>{c.name}</b></td><td className="mut">total of the {c.name} page</td><td /><td /><td /><td /><td className="n">{c.isOff ? <span className="tag n">off · $0</span> : money(amountOf(c.code) ?? 0)}</td></tr>)}
        {data.markups.map(m => <tr key={m.code} className={m.isOff ? 'off' : ''}><td><OnOff label={m.name} on={!m.isOff} disabled={!canEdit} onChange={v => save(m.code, m.name, { isOff: !v })} /></td>
          <td><b>{m.name}</b>{m.code === 'fee' && co.applyFeeExcludes && co.feeExcludes.length > 0 && <span className="tag b" style={{ marginLeft: 6 }} title="Some costs are carved out of the fee base (Fee carve-outs tab)">{co.feeExcludes.length} carved out</span>}</td>
          <td className="mut">{m.code === 'fee' ? 'subtotal + bonds, cover, insurance and in-fee indirect items, minus carve-outs' : m.code.startsWith('reserve') && co.reservePrecon.includes(m.code) ? '% of direct cost total + precon services' : base[m.appliesTo] ?? m.appliesTo}</td>
          <td className="n">{canEdit ? <EditCell number value={m.ratePct} disabled={m.isOff} onSave={v => { const n = toNum(v); if (n != null) save(m.code, m.name, { ratePct: n }) }} style={{ textAlign: 'right' }} /> : `${m.ratePct}%`}</td>
          <td className="n mut">{m.defaultRatePct != null ? `${(+m.defaultRatePct).toFixed(3)}%` : '–'}</td>
          <td className="n">{canEdit ? <EditCell number value={m.fixedAmount} disabled={m.isOff} placeholder="use rate" onSave={v => { const n = toNum(v); save(m.code, m.name, n == null ? { clearFixed: true } : { fixedAmount: n }) }} style={{ textAlign: 'right' }} /> : money(m.fixedAmount)}</td>
          <td className="n">{canEdit ? <EditCell number value={m.budgetAmount} placeholder="–" onSave={v => { const n = toNum(v); if (n != null) save(m.code, m.name, { budgetAmount: n }) }} style={{ textAlign: 'right' }} /> : money(m.budgetAmount)}</td>
          <td className="n">{m.isOff ? <span className="tag n">off · $0</span> : money(amountOf(m.code) ?? 0)}</td></tr>)}
      </tbody></table></div>

    <div className="tb" style={{ border: 0, paddingLeft: 0, marginTop: 18 }}><h3 style={{ margin: 0, fontSize: 14 }} data-was="User-defined indirect line items">Indirect items</h3>
      <span className="muted" style={{ fontSize: 12 }}>Added after the fee calculation lines; tick “In fee base” to charge fee on it.</span><span className="sp" />
      {canEdit && <button className="btn sm" onClick={() => setAddInd(true)}><Icon name="plus" size="s" />Add indirect item</button>}</div>
    <div className="gw"><table className="dg rates" style={{ width: '100%' }}><thead><tr><th style={{ width: 56 }}>On</th><th style={{ width: 260 }}>Item</th><th>Description</th><th style={{ width: 110 }} title="Fee is charged on this item">In fee base</th><th className="r" style={{ width: 150 }}>Amount</th><th className="r" style={{ width: 130 }}>In roll-up</th><th style={{ width: 40 }} /></tr></thead>
      <tbody>{ru.data.indirect.map(i => <tr key={i.indirectId} className={i.isOff ? 'off' : ''}>
        <td><OnOff label={i.itemName} on={!i.isOff} disabled={!canEdit} onChange={v => saveInd(i.indirectId, { isOff: !v }, v ? `${i.itemName} switched on` : `${i.itemName} switched off`)} /></td>
        <td>{canEdit ? <EditCell value={i.itemName} onSave={v => v.trim() && saveInd(i.indirectId, { itemName: v })} /> : i.itemName}</td>
        <td>{canEdit ? <EditCell value={i.description} placeholder="Add a description" onSave={v => saveInd(i.indirectId, { description: v })} /> : i.description}</td>
        <td style={{ textAlign: 'center' }}><input type="checkbox" disabled={!canEdit} checked={!i.outsideFee} onChange={e => saveInd(i.indirectId, { outsideFee: !e.target.checked })} aria-label={`${i.itemName} in fee base`} /></td>
        <td className="n">{canEdit ? <EditCell number value={i.amount} disabled={i.isOff} onSave={v => { const n = toNum(v); if (n != null) saveInd(i.indirectId, { amount: n }) }} style={{ textAlign: 'right' }} /> : money(i.amount)}</td>
        <td className="n">{i.isOff ? <span className="tag n">off · $0</span> : money(i.amount)}</td>
        <td>{canEdit && <button className="rm" style={{ opacity: 1 }} title="Delete" onClick={async () => { if (await run(() => api.del(`/indirect/${i.indirectId}`), 'Indirect item deleted')) refresh() }}><Icon name="x" size="s" /></button>}</td></tr>)}
        {!ru.data.indirect.length && <tr><td colSpan={7}><div className="empty">No indirect items. Add one for owner-requested costs such as permits, IT or testing.</div></td></tr>}</tbody></table></div>
    {addInd && <IndirectModal versionId={vid} onClose={() => setAddInd(false)} onDone={() => { setAddInd(false); refresh() }} />}</>
}

const FEE_PARTS: [string, string, string][] = [['genreq', 'Project overheads', 'General requirements'], ['precon', 'Precon services', 'Preconstruction'], ['genconds', 'General conditions (crew plan)', 'General conditions'], ['reserves', 'Reserves (project, design, escalation)', 'Contingency'], ['bond', 'P & P bond', 'P & P Bond'], ['cover', 'Works cover', 'Builders risk']]
const RESERVES: [string, string][] = [['reserveproject', 'Project reserve'], ['reservedesign', 'Design reserve'], ['reserveescalation', 'Escalation reserve']]
/** Fee carve-outs (was: Fee exclusions): costs left out of the fee base, the quoted fee %, the reserve base and bid total options. Every change recalculates the totals. */
function FeeCarveOuts({ project, version }: { project: ProjectDetail; version: Version }) {
  const vid = version.versionId; const qc = useQueryClient(); const { run, busy } = useAction()
  const ru = useQuery({ queryKey: vkey('price-roll-up', vid), queryFn: () => api.get<RollUpData>(`/versions/${vid}/price-roll-up`) })
  const [fee, setFee] = useState<string | null>(null); const [pending, setPending] = useState<Partial<CarveOuts>>({})   // shown at once, until the server answers
  const canEdit = project.access !== 'viewer' && version.status !== 'locked'
  if (!ru.data) return <Loading what="fee carve-outs" />
  const co = { ...ru.data.carveOuts, ...pending }, L = ru.data.lines
  const amt = (n: string) => L.find(l => l.name === n)?.amount ?? 0
  const feeLine = L.find(l => l.code === 'fee'), ct = amt('Contract total')
  const feeBase = feeLine && feeLine.ratePct ? feeLine.amount / (feeLine.ratePct / 100) : null
  // what the fee would be with no carve-outs (browser preview, same rules as the server)
  const noCarve = calcSummary({ ...ru.data.input, zoneShares: ru.data.input.zoneShares ?? undefined, options: { ...(ru.data.input.options ?? {}), feeExcludes: [] } })
  const feeNoCarve = noCarve.find(l => l.code === 'fee')?.amount ?? 0
  async function put(body: Partial<CarveOuts>, ok = 'Fee carve-outs saved · totals recalculated') {
    setPending(p => ({ ...p, ...body }))
    const r = await run(() => api.put<RollUpData>(`/versions/${vid}/fee-carve-outs`, body), ok)
    if (r) qc.setQueryData(vkey('price-roll-up', vid), r)
    setPending({})
    qc.invalidateQueries({ queryKey: vkey('markups', vid) }); qc.invalidateQueries({ queryKey: vkey('bid-board', vid) })
  }
  const toggle = (list: string[], k: string, on: boolean) => on ? [...new Set([...list, k])] : list.filter(x => x !== k)
  const carvedAmt = (k: string) => k === 'reserves' ? L.filter(l => l.code?.startsWith('reserve')).reduce((s, l) => s + l.amount, 0) : amt(k === 'genreq' ? 'Project overheads' : k === 'precon' ? 'Precon services' : k === 'genconds' ? 'General conditions' : '') || (L.find(l => l.code === k)?.amount ?? 0)
  return <div className="feeco" data-was="Fee exclusions">
    <div className="feeco-head"><div><h3>Fee carve-outs</h3><p className="muted">Choose the costs left out of the fee calculation. Changes update the fee and the contract total straight away.</p></div>
      {co.applyFeeExcludes && co.feeExcludes.length > 0 && <span className="tag g">Carve-outs active</span>}</div>
    <div className="feeco-row">
      <div className="field"><label>Apply fee carve-outs?</label><Seg<'y' | 'n'> value={co.applyFeeExcludes ? 'y' : 'n'} onChange={v => canEdit && put({ applyFeeExcludes: v === 'y' })} items={[['y', 'Yes'], ['n', 'No']]} />
        <small className="mut">With No, the ticks below are kept but don't apply.</small></div>
      <div className="field" style={{ maxWidth: 200 }}><label>Quoted fee (% )</label><input className="inp num" disabled={!canEdit || busy} value={fee ?? String(co.feePct ?? 0)} onChange={e => setFee(e.target.value)}
        onBlur={() => { const n = toNum(fee ?? ''); if (fee != null && n != null && n !== co.feePct) put({ feePct: n }, 'Fee % saved'); setFee(null) }} onKeyDown={e => e.key === 'Enter' && (e.target as HTMLInputElement).blur()} />
        {co.feeOff && <small style={{ color: 'var(--bad,#d1344b)' }}>The fee is switched off on the Rates tab.</small>}</div>
      <div className="kpis" style={{ flex: 1 }}>
        <Kpi k="Fee base" v={feeBase != null ? money(Math.round(feeBase)) : '—'} />
        <Kpi k="Fee" v={money(feeLine?.amount ?? 0)} d={feeNoCarve !== (feeLine?.amount ?? 0) ? `${money((feeLine?.amount ?? 0) - feeNoCarve)} vs no carve-outs` : 'no carve-outs applied'} dc={(feeLine?.amount ?? 0) < feeNoCarve ? 'good' : ''} />
        <Kpi k="Contract total" v={money(ct)} /></div>
    </div>
    <div className={`feeco-cols ${co.applyFeeExcludes ? '' : 'dim'}`}>
      <section><h4>Left out of the fee base</h4>
        {FEE_PARTS.map(([k, l, was]) => <label key={k} className="check" data-was={was}><input type="checkbox" disabled={!canEdit} checked={co.feeExcludes.includes(k)} onChange={e => put({ feeExcludes: toggle(co.feeExcludes, k, e.target.checked) })} /> {l}<span className="mut num" style={{ marginLeft: 'auto' }}>{money(carvedAmt(k))}</span></label>)}
        <small className="mut">Trades and in-fee indirect items are always in the fee base. Indirect items are set on the Rates tab.</small></section>
      <section><h4 data-was="Indirect cost inclusions / contingency base">Reserve base</h4>
        <small className="mut">Each reserve is a % of the direct cost total. Tick to add precon services or general conditions to its base.</small>
        <table className="resbase"><thead><tr><th /><th>+ Precon services</th><th>+ General conditions</th><th className="r">Amount</th></tr></thead><tbody>
          {RESERVES.map(([k, l]) => <tr key={k}><td>{l}</td>
            <td><input type="checkbox" disabled={!canEdit} checked={co.reservePrecon.includes(k)} onChange={e => put({ reservePrecon: toggle(co.reservePrecon, k, e.target.checked) })} aria-label={`${l}: include precon services`} /></td>
            <td><input type="checkbox" disabled={!canEdit} checked={(co.reserveGenconds ?? []).includes(k)} onChange={e => put({ reserveGenconds: toggle(co.reserveGenconds ?? [], k, e.target.checked) })} aria-label={`${l}: include general conditions`} /></td>
            <td className="n mut">{money(L.find(x => x.code === k)?.amount ?? 0)}</td></tr>)}</tbody></table></section>
      <section><h4 data-was="Bid total options">Bid total options</h4>
        <label className="check" data-was="Exclude General Requirements From Direct Costs"><input type="checkbox" disabled={!canEdit} checked={co.excludeGenreqFromDirect} onChange={e => put({ excludeGenreqFromDirect: e.target.checked })} /> Leave project overheads out of the direct cost total</label>
        <small className="mut">The direct cost total is then trades only, so reserves are worked out without project overheads. Project overheads still count in the subtotal.</small></section>
    </div>
  </div>
}

function Members({ project }: { project: ProjectDetail }) {
  const lk = useLookups(); const qc = useQueryClient(); const { run, busy } = useAction()
  const key = ['members', project.projectId]
  const { data } = useQuery({ queryKey: key, queryFn: () => api.get<{ userId: number; name: string; email: string; access: string; colour: string; initials: string }[]>(`/projects/${project.projectId}/members`) })
  const [rows, setRows] = useState<{ userId: number; access: string }[] | null>(null); useEffect(() => { if (data) setRows(data.map(m => ({ userId: m.userId, access: m.access }))) }, [data])
  const owner = project.access === 'owner'
  if (!rows) return <Loading what="members" />
  const user = (id: number) => lk.data?.users.find(u => u.userId === id)
  const free = (lk.data?.users ?? []).filter(u => !rows.some(r => r.userId === u.userId))
  return <><p className="muted">People who can open this project and what they can do. Without a row here, admins are owners, estimators and managers can edit, and read-only users can view.</p>
    <div className="list">{rows.map((r, i) => { const u = user(r.userId); return <div className="li" key={r.userId}><span className="pa" style={{ background: u?.colour }}>{u?.initials}</span><div className="t"><b>{u?.name}</b><small>{u?.email}</small></div>
      <select className="inp" disabled={!owner} value={r.access} onChange={e => setRows(rows.map((x, j) => j === i ? { ...x, access: e.target.value } : x))}><option value="viewer">Viewer</option><option value="editor">Editor</option><option value="owner">Owner</option></select>
      {owner && <button className="rm" style={{ opacity: 1 }} onClick={() => setRows(rows.filter((_, j) => j !== i))}><Icon name="x" size="s" /></button>}</div> })}</div>
    {owner && <div className="tb" style={{ border: 0, paddingLeft: 0 }}><select className="inp" defaultValue="" onChange={e => { if (e.target.value) { setRows([...rows, { userId: +e.target.value, access: 'editor' }]); e.target.value = '' } }}>
      <option value="">Add a person…</option>{free.map(u => <option key={u.userId} value={u.userId}>{u.name}</option>)}</select><span className="sp" />
      <button className="btn p" disabled={busy} onClick={async () => { if (await run(() => api.put(`/projects/${project.projectId}/members`, rows), 'Members saved')) qc.invalidateQueries({ queryKey: key }) }}><Icon name="check" />Save members</button></div>}</>
}

function Documents({ project }: { project: ProjectDetail }) {
  const qc = useQueryClient(); const { run, busy } = useAction(); const key = ['documents', project.projectId]
  const { data } = useQuery({ queryKey: key, queryFn: () => api.get<{ documentId: number; docKind: string; fileName: string; byteSize: number; uploadedAt: string; uploadedBy: string | null }[]>(`/projects/${project.projectId}/documents`) })
  const canEdit = project.access !== 'viewer'
  async function upload(files: FileList | null) { if (!files?.length) return; const fd = new FormData(); [...files].forEach(f => fd.append('file', f)); fd.append('kind', 'other'); if (await run(() => api.post(`/projects/${project.projectId}/documents`, fd), `${files.length} file(s) uploaded`)) qc.invalidateQueries({ queryKey: key }) }
  return <>{canEdit && <div className="drop" style={{ marginBottom: 12 }} onDragOver={e => e.preventDefault()} onDrop={e => { e.preventDefault(); upload(e.dataTransfer.files) }}>
      <Icon name="upload" size="l" /><div><b>Drop files here</b> or <label className="lnk" style={{ cursor: 'pointer' }}>browse<input type="file" multiple hidden onChange={e => upload(e.target.files)} /></label> — drawings, specs, bid forms (50 MB each)</div>{busy && <p className="muted">Uploading…</p>}</div>}
    {!data ? <Loading what="documents" /> : data.length === 0 ? <div className="empty">No documents yet.</div> :
      <div className="gw"><table className="dg" style={{ width: '100%' }}><thead><tr><th>File</th><th>Type</th><th className="r">Size</th><th>Uploaded</th><th /></tr></thead>
        <tbody>{data.map(d => <tr key={d.documentId}><td><Icon name="file" size="s" /> <b>{d.fileName}</b></td><td><span className="tag n">{d.docKind}</span></td><td className="n">{(d.byteSize / 1024).toLocaleString(undefined, { maximumFractionDigits: 0 })} KB</td>
          <td>{date(d.uploadedAt.slice(0, 10))}{d.uploadedBy ? ` · ${d.uploadedBy}` : ''}</td>
          <td style={{ textAlign: 'right' }}><button className="btn sm g ic" title="Download" onClick={() => run(() => api.download(`/documents/${d.documentId}/file`, d.fileName))}><Icon name="down" size="s" /></button>
            {canEdit && <button className="btn sm g ic" title="Delete" onClick={async () => { if (await run(() => api.del(`/documents/${d.documentId}`), 'Document deleted')) qc.invalidateQueries({ queryKey: key }) }}><Icon name="x" size="s" /></button>}</td></tr>)}</tbody></table></div>}</>
}

function Activity({ project }: { project: ProjectDetail }) {
  const { data } = useQuery({ queryKey: ['activity', project.projectId], queryFn: () => api.get<{ eventId: number; happenedAt: string; verb: string; subjectType: string; personName: string | null; initials: string | null; colour: string | null; versionId: number | null }[]>(`/projects/${project.projectId}/activity`), refetchInterval: 15000 })
  if (!data) return <Loading what="activity" />
  return <div className="timeline">{data.map(e => <div key={e.eventId} className="li" style={{ display: 'flex', gap: 10, alignItems: 'center', padding: '6px 0', borderBottom: '1px solid var(--line)' }}>
    <span className="pa" style={{ background: e.colour ?? '#888' }}>{e.initials ?? 'SY'}</span><div><b>{e.personName ?? 'Gridline'}</b> {describe(e.verb)}
      <div className="muted" style={{ fontSize: 11.5 }}>{new Date(e.happenedAt).toLocaleString()}{e.versionId ? ` · v${project.versions.find(v => v.versionId === e.versionId)?.versionNo ?? '?'}` : ''}</div></div></div>)}
    {!data.length && <div className="empty">Nothing yet.</div>}</div>
}
