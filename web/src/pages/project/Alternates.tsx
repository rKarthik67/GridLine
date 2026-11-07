// Alternates & VE (was: Alternates / Value engineering). One list per version, the same model as the original app:
// every trade enters its amount (+ note) on Scope match → Alternates & VE, Total = Σ trade amounts,
// Subtotal quoted = Total × Quoted %, each indirect = roll-up line × (Subtotal quoted ÷ direct cost) when switched on,
// Total quoted = Subtotal quoted + indirects. Alternates never change the contract total.
import { Fragment, useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { AlternateItem, AlternatesData, ProjectDetail, Version } from '../../api/types'
import { PageHead } from '../../components/Shell'
import { Icon } from '../../components/Icon'
import { Failed, Kpi, Loading, Modal, Seg, toNum, useAction } from '../../components/ui'
import { date, money, pct } from '../../app/format'
import { vkey } from '../../app/hooks'

type Kind = 'all' | 'option' | 'saving'
type St = 'all' | 'proposed' | 'accepted' | 'rejected'
const plural = (n: number, w: string) => `${n} ${w}${n === 1 ? '' : 's'}`
export const ALT_STATUS: Record<string, [string, string]> = { proposed: ['Proposed', 'b'], accepted: ['Approved', 'g'], rejected: ['Rejected', 'r'] }

export default function Alternates({ project, version }: { project: ProjectDetail; version: Version }) {
  const vid = version.versionId; const qc = useQueryClient(); const { run, busy } = useAction()
  const key = vkey('alternates', vid)
  const { data, error, isLoading } = useQuery({ queryKey: key, queryFn: () => api.get<AlternatesData>(`/versions/${vid}/alternates`) })
  const [kind, setKind] = useState<Kind>('all'); const [st, setSt] = useState<St>('all'); const [q, setQ] = useState('')
  const [open, setOpen] = useState<number | null>(null); const [sel, setSel] = useState<Set<number>>(new Set()); const [add, setAdd] = useState<'option' | 'saving' | null>(null)
  const [confirmDel, setConfirmDel] = useState<number[] | null>(null)
  const canEdit = project.access !== 'viewer' && version.status !== 'locked'
  const put = (d?: AlternatesData) => { if (d) qc.setQueryData(key, d) }

  const rows = useMemo(() => (data?.items ?? []).filter(i => (kind === 'all' || i.kind === kind) && (st === 'all' || i.status === st)
    && (!q || `${i.code} ${i.description} ${i.comments ?? ''} ${i.trades.map(t => t.tradeName).join(' ')}`.toLowerCase().includes(q.toLowerCase()))), [data, kind, st, q])
  if (isLoading) return <Loading what="alternates" />
  if (error || !data) return <Failed error={error} />

  const sum = (k: 'option' | 'saving', s?: string) => data.items.filter(i => i.kind === k && (!s || i.status === s)).reduce((a, i) => a + i.totalQuoted, 0)
  const allSel = rows.length > 0 && rows.every(r => sel.has(r.optionId))
  async function bulk(status: string | null) {
    const ids = [...sel]; if (!ids.length) return
    const d = await run(() => api.post<AlternatesData>(`/versions/${vid}/alternates/bulk`, { ids, status }), status ? `${ids.length} set to ${ALT_STATUS[status][0]}` : `${ids.length} deleted`)
    if (d) { put(d); setSel(new Set()); setConfirmDel(null); if (!status && open && ids.includes(open)) setOpen(null) }
  }

  return <>
    <PageHead eyebrow={`${project.projectName} · v${version.versionNo}`} title="Alternates & VE" was="Alternates / Value engineering"
      desc="Add-on alternates and value-engineering savings, priced by the trades on Scope match. They are quoted to the client separately and don't change the contract total."
      actions={canEdit && <><button className="btn" onClick={() => setAdd('saving')} data-was="Value Engineering"><Icon name="bulb" />Add VE idea</button>
        <button className="btn p" onClick={() => setAdd('option')} data-was="Add Options"><Icon name="plus" />Add alternate</button></>} />
    <div className="kpis">
      <Kpi k="Alternates" v={money(sum('option'))} d={`${plural(data.items.filter(i => i.kind === 'option').length, 'item')} · ${money(sum('option', 'accepted'))} approved`} />
      <Kpi k="Value engineering" v={money(sum('saving'))} d={`${plural(data.items.filter(i => i.kind === 'saving').length, 'idea')} · ${money(sum('saving', 'accepted'))} approved`} />
      <Kpi k="Direct cost (for indirects)" v={money(data.directCost)} d="Trades + project overheads + precon + general conditions" />
    </div>
    <div className="card">
      <div className="tb" style={{ flexWrap: 'wrap', gap: 8 }}>
        <Seg<Kind> value={kind} onChange={setKind} items={[['all', 'All'], ['option', 'Alternates'], ['saving', 'VE']]} />
        <label className="sbox srch3"><Icon name="search" size="s" /><input placeholder="Search code, description or trade" value={q} onChange={e => setQ(e.target.value)} /></label>
        <Seg<St> value={st} onChange={setSt} items={[['all', 'Any status'], ['proposed', 'Proposed'], ['accepted', 'Approved'], ['rejected', 'Rejected']]} />
        <div className="sp" />
        {canEdit && sel.size > 0 && <span className="bulkbar"><b>{sel.size} selected</b>
          <button className="btn sm" onClick={() => setSel(new Set())}>Deselect</button>
          {Object.entries(ALT_STATUS).map(([k, [l]]) => <button key={k} className="btn sm" disabled={busy} onClick={() => bulk(k)}>{l}</button>)}
          <button className="btn sm" style={{ color: 'var(--bad)' }} onClick={() => setConfirmDel([...sel])}><Icon name="trash" size="s" />Delete</button></span>}
      </div>
      <div className="gw"><table className="dg altlist" style={{ width: '100%' }}>
        <thead><tr>{canEdit && <th style={{ width: 34 }}><input type="checkbox" aria-label="Select all" checked={allSel} onChange={() => setSel(allSel ? new Set() : new Set(rows.map(r => r.optionId)))} /></th>}
          <th style={{ width: 28 }} /><th style={{ width: 80 }}>Code</th><th style={{ width: 110 }}>Type</th><th>Description</th><th className="r" style={{ width: 150 }}>Total quoted</th><th style={{ width: 110 }}>Status</th><th style={{ width: 170 }}>Updated</th></tr></thead>
        <tbody>
          {rows.map(i => <Fragment key={i.optionId}>
            <tr className={open === i.optionId ? 'open' : ''} onClick={() => setOpen(o => o === i.optionId ? null : i.optionId)} style={{ cursor: 'pointer' }}>
              {canEdit && <td onClick={e => e.stopPropagation()}><input type="checkbox" aria-label={`Select ${i.code}`} checked={sel.has(i.optionId)} onChange={() => setSel(s => { const n = new Set(s); n.has(i.optionId) ? n.delete(i.optionId) : n.add(i.optionId); return n })} /></td>}
              <td><Icon name={open === i.optionId ? 'chev' : 'chevr'} size="s" /></td>
              <td><span className="code">{i.code}</span></td>
              <td><span className={`tag ${i.kind === 'saving' ? 'o' : 'k'}`}>{i.kind === 'saving' ? 'VE' : 'Alternate'}</span></td>
              <td style={{ whiteSpace: 'normal' }}>{i.description}{i.comments && <div className="muted" style={{ fontSize: 11.5 }}>{i.comments}</div>}</td>
              <td className="n"><b className="num">{money(i.totalQuoted, 2)}</b></td>
              <td><span className={`tag ${ALT_STATUS[i.status]?.[1] ?? 'n'}`}>{ALT_STATUS[i.status]?.[0] ?? i.status}</span></td>
              <td className="mut" style={{ fontSize: 12 }}>{date(i.changedAt.slice(0, 10))}{i.changedByName ? ` · ${i.changedByName}` : ''}</td></tr>
            {open === i.optionId && <tr className="xrow"><td colSpan={canEdit ? 8 : 7}><AltPanel item={i} canEdit={canEdit} onData={put} onDelete={() => setConfirmDel([i.optionId])} /></td></tr>}
          </Fragment>)}
          {!rows.length && <tr><td colSpan={8} className="empty">{data.items.length ? 'Nothing matches the filters.' : 'No alternates or VE ideas yet. Add one, then each trade prices it on Scope match → Alternates & VE.'}</td></tr>}
        </tbody></table></div>
    </div>
    {add && <AddAltModal kind={add} vid={vid} onClose={() => setAdd(null)} onDone={d => { put(d); setAdd(null); const n = d.items.filter(x => x.kind === add).sort((a, b) => b.optionId - a.optionId)[0]; if (n) setOpen(n.optionId) }} />}
    {confirmDel && <Modal title="Delete alternates" onClose={() => setConfirmDel(null)} footer={<><button className="btn" onClick={() => setConfirmDel(null)}>Cancel</button>
      <button className="btn p" style={{ background: 'var(--bad)' }} disabled={busy} onClick={async () => { const ids = confirmDel; const d = await run(() => api.post<AlternatesData>(`/versions/${vid}/alternates/bulk`, { ids, status: null }), `${ids.length} deleted`); if (d) { put(d); setSel(new Set()); setConfirmDel(null) } }}><Icon name="trash" />Delete</button></>}>
      <p>Delete {confirmDel.length === 1 ? data.items.find(x => x.optionId === confirmDel[0])?.code : `${confirmDel.length} items`}? The trade amounts entered for {confirmDel.length === 1 ? 'it' : 'them'} are deleted too.</p></Modal>}
  </>
}

function AltPanel({ item, canEdit, onData, onDelete }: { item: AlternateItem; canEdit: boolean; onData(d?: AlternatesData): void; onDelete(): void }) {
  const { run, busy } = useAction(); const nav = useNavigate()
  const [f, setF] = useState({ description: item.description, quotedPct: String(item.quotedPct), status: item.status, comments: item.comments ?? '' })
  const dirty = f.description !== item.description || toNum(f.quotedPct) !== item.quotedPct || f.status !== item.status || f.comments !== (item.comments ?? '')
  async function save() {
    const qp = toNum(f.quotedPct); if (qp == null) return
    onData(await run(() => api.patch<AlternatesData>(`/alternates/${item.optionId}`, { description: f.description, quotedPct: qp, status: f.status, comments: f.comments, rowRev: item.rowRev }), `${item.code} saved`))
  }
  async function toggle(code: string, included: boolean) {
    onData(await run(() => api.patch<AlternatesData>(`/alternates/${item.optionId}`, { includeCode: code, included, rowRev: item.rowRev })))
  }
  return <div className="xpanel">
    <div className="xform">
      <div className="field"><label>Description</label><textarea className="inp" rows={3} disabled={!canEdit} value={f.description} onChange={e => setF({ ...f, description: e.target.value })} /></div>
      <div className="field"><label>Quoted %</label><input className="inp num" style={{ width: 120 }} disabled={!canEdit} value={f.quotedPct} onChange={e => setF({ ...f, quotedPct: e.target.value })} /><small>Share of the trade total quoted to the client (100% = as priced).</small></div>
      <div className="field"><label>Status</label><Seg value={f.status} onChange={s => canEdit && setF({ ...f, status: s })} items={Object.entries(ALT_STATUS).map(([k, [l]]) => [k, l]) as [AlternateItem['status'], string][]} /></div>
      <div className="field"><label>Comments</label><textarea className="inp" rows={3} disabled={!canEdit} value={f.comments} onChange={e => setF({ ...f, comments: e.target.value })} /></div>
      {canEdit && <div className="tb" style={{ border: 0, padding: 0 }}><button className="btn sm" style={{ color: 'var(--bad)' }} onClick={onDelete}><Icon name="trash" size="s" />Delete</button><span className="sp" />
        <button className="btn sm" disabled={!dirty} onClick={() => setF({ description: item.description, quotedPct: String(item.quotedPct), status: item.status, comments: item.comments ?? '' })}>Cancel</button>
        <button className="btn p sm" disabled={!dirty || busy || !f.description.trim()} onClick={save}><Icon name="check" size="s" />Save</button></div>}
    </div>
    <div className="xdetail">
      <table className="dg" style={{ width: '100%' }}>
        <thead><tr><th>Trade</th><th className="r" style={{ width: 140 }}>Amount</th><th>Comments</th></tr></thead>
        <tbody>
          {item.trades.map(t => <tr key={t.tradeId}><td><span className="code">{t.csiCode}</span> <a className="lnk" onClick={() => nav(`../scope-match?trade=${t.tradeId}&tab=alternates`, { relative: 'path' })}>{t.tradeName}</a>{t.carried && <span className="muted" style={{ fontSize: 11.5, marginLeft: 6 }}>{t.carried}</span>}</td>
            <td className="n">{money(t.amount, 2)}</td><td className="mut" style={{ whiteSpace: 'normal' }}>{t.note}</td></tr>)}
          {!item.trades.length && <tr><td colSpan={3} className="mut">No trade has priced this yet. Open a trade on Scope match → Alternates & VE to enter its amount.</td></tr>}
          <tr className="sub"><td><b>Trade total</b></td><td className="n"><b className="num">{money(item.total, 2)}</b></td><td /></tr>
          <tr className="sub"><td><b>Subtotal quoted</b> <span className="muted">at {pct(item.quotedPct, 2)}</span></td><td className="n"><b className="num">{money(item.subtotalQuoted, 2)}</b></td><td /></tr>
          {item.indirects.map(x => <tr key={x.code} className={x.included ? '' : 'off'}>
            <td style={{ paddingLeft: 18 }}><label className="check" style={{ fontSize: 12.5 }}><input type="checkbox" disabled={!canEdit || busy} checked={x.included} onChange={e => toggle(x.code, e.target.checked)} />{x.name}</label></td>
            <td className="n">{x.included ? money(x.amount, 2) : <span className="mut" title={`${money(x.budget ?? 0, 2)} if included`}>excluded</span>}</td><td /></tr>)}
          <tr className="tot"><td><b>Total quoted</b></td><td className="n"><b className="num">{money(item.totalQuoted, 2)}</b></td><td /></tr>
        </tbody></table>
    </div>
  </div>
}

function AddAltModal({ kind: k0, vid, onClose, onDone }: { kind: 'option' | 'saving'; vid: number; onClose(): void; onDone(d: AlternatesData): void }) {
  const { run, busy } = useAction()
  const [f, setF] = useState({ kind: k0, description: '', quotedPct: '100', status: 'proposed', comments: '' })
  async function save() {
    const d = await run(() => api.post<AlternatesData>(`/versions/${vid}/alternates`, { ...f, quotedPct: toNum(f.quotedPct) ?? 100 }), f.kind === 'saving' ? 'VE idea added' : 'Alternate added')
    if (d) onDone(d)
  }
  return <Modal title={f.kind === 'saving' ? 'Add VE idea' : 'Add alternate'} onClose={onClose} footer={<><button className="btn" onClick={onClose}>Cancel</button>
    <button className="btn p" disabled={busy || !f.description.trim()} onClick={save}><Icon name="check" />Add</button></>}>
    <div style={{ display: 'grid', gap: 12 }}>
      <div className="field"><label>Type</label><Seg value={f.kind} onChange={kind => setF({ ...f, kind })} items={[['option', 'Alternate'], ['saving', 'Value engineering']]} /></div>
      <div className="field"><label>Description</label><textarea className="inp" rows={3} autoFocus placeholder="e.g. Upgrade lobby floor to stone" value={f.description} onChange={e => setF({ ...f, description: e.target.value })} /></div>
      <div className="field"><label>Quoted %</label><input className="inp num" style={{ width: 120 }} value={f.quotedPct} onChange={e => setF({ ...f, quotedPct: e.target.value })} /></div>
      <div className="field"><label>Status</label><Seg value={f.status} onChange={status => setF({ ...f, status })} items={Object.entries(ALT_STATUS).map(([k, [l]]) => [k, l]) as [string, string][]} /></div>
      <div className="field"><label>Comments</label><textarea className="inp" rows={2} value={f.comments} onChange={e => setF({ ...f, comments: e.target.value })} /></div>
      <small className="muted">Every trade on this version gets a place to price it (Scope match → Alternates & VE).</small>
    </div>
  </Modal>
}
