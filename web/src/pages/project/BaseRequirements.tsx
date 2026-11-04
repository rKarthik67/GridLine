// Base requirements (was: General scope). The checklist every trade partner must answer; each line appears
// (text locked) at the top of every trade in Scope match. Editing here updates every trade at once; answers stay.
import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { ProjectDetail, Version } from '../../api/types'
import { PageHead } from '../../components/Shell'
import { Icon } from '../../components/Icon'
import { AskText, ContextMenu, Failed, Loading, Menu, useAction, type CtxItem } from '../../components/ui'
import { DataGrid, type GCol, type GridEdit } from '../../components/DataGrid'
import { useToast } from '../../app/toast'
import { vkey } from '../../app/hooks'

interface ReqLine { requirementLineId: number; sortKey: number; sectionName: string | null; isHeading: boolean; lineText: string; desired: string | null; trades: number }
interface ReqData { set: { setId: number; setName: string; isTemplate: boolean } | null; lines: ReqLine[]; templates: { setId: number; setName: string; lines: number }[] }

export default function BaseRequirements({ project, version }: { project: ProjectDetail; version: Version }) {
  const vid = version.versionId; const qc = useQueryClient(); const { run } = useAction(); const toast = useToast()
  const key = vkey('requirements', vid)
  const { data, error, isLoading } = useQuery({ queryKey: key, queryFn: () => api.get<ReqData>(`/versions/${vid}/requirements`) })
  const [ctx, setCtx] = useState<{ x: number; y: number; items: CtxItem[] } | null>(null); const [ask, setAsk] = useState<{ after: ReqLine | null } | null>(null)
  const canEdit = project.access !== 'viewer' && version.status !== 'locked'
  const put = (d?: ReqData) => { if (d) { qc.setQueryData(key, d); qc.invalidateQueries({ predicate: q => q.queryKey[0] === 'scope' }) } }
  const patch = async (l: ReqLine, body: object, ok?: string) => put(await run(() => api.patch<ReqData>(`/requirements/lines/${l.requirementLineId}?versionId=${vid}`, body), ok))
  const add = async (after: ReqLine | null, body: object, ok?: string) => put(await run(() => api.post<ReqData>(`/versions/${vid}/requirements/lines`, { afterId: after?.requirementLineId ?? null, sectionName: after?.sectionName, ...body }), ok))
  if (isLoading) return <Loading what="base requirements" />
  if (error || !data) return <Failed error={error} />
  const lines = data.lines

  const DESIRED: Record<string, string> = { included: 'Included', excluded: 'Excluded', price: 'Price', text: 'Text' }
  const cols: GCol<ReqLine>[] = [
    { prop: 'no', name: '#', size: 50, pin: 'start', value: l => l.isHeading ? '' : lines.slice(0, lines.indexOf(l) + 1).filter(x => !x.isHeading).length, cls: () => 'mut' },
    { prop: 'sectionName', name: 'Section', size: 200, edit: l => !l.isHeading, value: l => l.isHeading ? '' : l.sectionName ?? '' },
    { prop: 'lineText', name: 'Requirement', size: 620, edit: true, value: l => l.lineText, show: (l, h) => l.lineText || h('span', { class: 'mut' }, l.isHeading ? '(empty heading)' : 'Type the requirement') },
    { prop: 'desired', name: 'Desired answer', size: 160, edit: l => !l.isHeading, value: l => l.isHeading ? '' : DESIRED[l.desired ?? ''] ?? '',
      show: (l, h) => l.isHeading ? '' : l.desired ? h('span', { class: `ch ${l.desired === 'included' ? 'in' : l.desired === 'excluded' ? 'ex' : l.desired === 'price' ? 'pr' : 'tx'}` }, DESIRED[l.desired]) : h('span', { class: 'mut' }, '–') },
    { prop: 'trades', name: 'Trades', size: 90, align: 'r', value: l => l.trades, cls: () => 'mut' },
  ]
  /** Typed, pasted from Excel or filled down: one save per line, every trade updates. */
  async function edit(ch: GridEdit<ReqLine>[]) {
    const per = new Map<number, { l: ReqLine; body: Record<string, unknown> }>(); const bad: string[] = []
    for (const c of ch) { const e = per.get(c.row.requirementLineId) ?? { l: c.row, body: {} }; per.set(c.row.requirementLineId, e); const v = c.value.trim()
      if (c.prop === 'lineText') { if (!v) { bad.push('A requirement needs text'); continue } e.body.lineText = v; if (c.row.isHeading) e.body.sectionName = v }
      else if (c.prop === 'sectionName') e.body.sectionName = v
      else if (c.prop === 'desired') { const d = Object.entries(DESIRED).find(([k, n]) => n.toLowerCase() === v.toLowerCase() || k === v.toLowerCase())?.[0]; if (!d) bad.push(`"${v}" should be Included, Excluded, Price or Text`); else e.body.desired = d } }
    if (bad.length) toast(bad[0] + (bad.length > 1 ? ` (+${bad.length - 1} more)` : ''), 'alert')
    let last: ReqData | undefined
    for (const e of per.values()) if (Object.keys(e.body).length) last = await run(() => api.patch<ReqData>(`/requirements/lines/${e.l.requirementLineId}?versionId=${vid}`, e.body)) ?? last
    if (last) { put(last); toast(per.size > 1 ? `${per.size} rows updated on every trade` : 'Updated on every trade') }
  }

  function menu(e: MouseEvent, l: ReqLine | null) {
    if (!canEdit || !l) return; const i = lines.indexOf(l)
    setCtx({ x: e.clientX, y: e.clientY, items: [
      { label: 'Insert row below', icon: 'plus', onClick: () => add(l, { lineText: '' }, 'Row added to every trade') },
      { label: 'Insert rows below…', icon: 'plus', onClick: () => setAsk({ after: l }) },
      { label: 'Insert section heading below', icon: 'list', onClick: () => add(l, { isHeading: true, lineText: 'New section' }) }, null,
      { label: 'Change to heading', onClick: () => patch(l, { isHeading: true, clearDesired: true }), disabled: l.isHeading },
      { label: 'Change to normal row', onClick: () => patch(l, { isHeading: false, desired: 'included' }), disabled: !l.isHeading },
      { label: 'Move up', icon: 'chevl', onClick: () => patch(l, { moveAfterId: i > 1 ? lines[i - 2].requirementLineId : 0 }), disabled: i === 0 },
      { label: 'Move down', icon: 'chevr', onClick: () => patch(l, { moveAfterId: lines[i + 1].requirementLineId }), disabled: i === lines.length - 1 }, null,
      { label: 'Delete row (from every trade)', icon: 'x', danger: true, onClick: () => run(() => api.del<ReqData>(`/requirements/lines/${l.requirementLineId}?versionId=${vid}`), 'Row deleted').then(put) }] })
  }

  return <>
    <PageHead eyebrow={`${project.projectName} · v${version.versionNo}`} title="Base requirements" was="General scope"
      desc="Requirements every trade partner must answer. Each line shows at the top of every trade in Scope match; changes here update every trade, and answers are kept."
      actions={canEdit ? <><Menu label="Add from checklist" icon="copy" items={data.templates.map(t => ({ label: `${t.setName} · ${t.lines} lines`, onClick: async () => put(await run(() => api.post<ReqData>(`/versions/${vid}/requirements/apply-template`, { setId: t.setId }), `Lines added from ${t.setName}`)) }))} />
        <button className="btn p" onClick={() => add(lines[lines.length - 1] ?? null, { lineText: 'New requirement' }, 'Row added to every trade')}><Icon name="plus" />Add requirement</button></> : null} />
    <div className="card">
      <div className="tb"><span className="muted">{data.set ? <>Checklist: <b>{data.set.setName}</b>{data.set.isTemplate ? ' (company template — your first change makes a copy for this version)' : ''}</> : 'No checklist yet — add a requirement or copy one from a checklist.'}</span>
        <span className="sp" /><span className="muted" style={{ fontSize: 12 }}>Right-click a row to insert, move or delete</span></div>
      {lines.length ? <DataGrid<ReqLine> rows={lines} rowKey={l => l.requirementLineId} cols={cols} readOnly={!canEdit} onEdit={edit} onMenu={menu} rowCls={l => l.isHeading ? 'grp' : undefined} height="calc(100vh - 260px)" />
        : <div className="empty">No base requirements.</div>}
      <p className="muted" style={{ padding: '8px 12px' }}>Type in a cell or paste a block from Excel (Ctrl+V); Desired answer takes Included, Excluded, Price or Text. Drag across cells and Ctrl+C to copy for Excel.</p></div>
    {ctx && <ContextMenu {...ctx} onClose={() => setCtx(null)} />}
    {ask && <AskText title="Insert rows" label="How many rows? (1–50)" initial="10" number onClose={() => setAsk(null)} onSave={v => { const n = Math.max(1, Math.min(50, Math.round(+v || 1))); const a = ask.after; setAsk(null); add(a, { count: n, lineText: '' }, `${n} rows added`) }} />}
  </>
}
