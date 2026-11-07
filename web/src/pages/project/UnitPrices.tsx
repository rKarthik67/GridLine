// Unit prices (was: Unit prices). Same model as the original app: one list per version, every trade enters a price on
// Scope match → Unit prices, one trade's price is selected. Discounted price = price × Quoted %, each indirect = discounted
// price × that roll-up line's share of the contract (when switched on), Quoted price = discounted + indirects.
// Unit prices never change the contract total.
import { Fragment, useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { ProjectDetail, UnitPriceItem, UnitPricesData, Version } from '../../api/types'
import { PageHead } from '../../components/Shell'
import { Icon } from '../../components/Icon'
import { Failed, Loading, Modal, Seg, toNum, useAction } from '../../components/ui'
import { date, money, pct } from '../../app/format'
import { vkey } from '../../app/hooks'

export const UNITS = ['Cubic Yard', 'Day', 'Each', 'Gallon', 'Hour', 'Linear Foot', 'Lump Sum', 'Month', 'Pound', 'Square Foot', 'Square Yard', 'Ton', 'Week']
type St = 'all' | 'proposed' | 'selected'
const statusOf = (i: UnitPriceItem) => i.selectedTradeId ? 'selected' : 'proposed'

export default function UnitPrices({ project, version }: { project: ProjectDetail; version: Version }) {
  const vid = version.versionId; const qc = useQueryClient(); const { run, busy } = useAction()
  const key = vkey('unit-prices', vid)
  const { data, error, isLoading } = useQuery({ queryKey: key, queryFn: () => api.get<UnitPricesData>(`/versions/${vid}/unit-prices`) })
  const [st, setSt] = useState<St>('all'); const [q, setQ] = useState('')
  const [open, setOpen] = useState<number | null>(null); const [sel, setSel] = useState<Set<number>>(new Set()); const [add, setAdd] = useState(false)
  const [confirmDel, setConfirmDel] = useState<number[] | null>(null)
  const canEdit = project.access !== 'viewer' && version.status !== 'locked'
  const put = (d?: UnitPricesData) => { if (d) qc.setQueryData(key, d) }
  const rows = useMemo(() => (data?.items ?? []).filter(i => (st === 'all' || statusOf(i) === st)
    && (!q || `${i.description} ${i.unit ?? ''} ${i.comments ?? ''}`.toLowerCase().includes(q.toLowerCase()))), [data, st, q])
  if (isLoading) return <Loading what="unit prices" />
  if (error || !data) return <Failed error={error} />
  const allSel = rows.length > 0 && rows.every(r => sel.has(r.unitRateId)); const cols = canEdit ? 8 : 7

  return <>
    <PageHead eyebrow={`${project.projectName} · v${version.versionNo}`} title="Unit prices" was="Unit prices"
      desc="Rates quoted per unit (per cubic yard, per hour…). Each trade enters its price on Scope match; pick the one to carry. They don't change the contract total."
      actions={canEdit && <button className="btn p" onClick={() => setAdd(true)}><Icon name="plus" />Add unit price</button>} />
    <div className="card">
      <div className="tb" style={{ flexWrap: 'wrap', gap: 8 }}>
        <label className="sbox srch3"><Icon name="search" size="s" /><input placeholder="Search" value={q} onChange={e => setQ(e.target.value)} /></label>
        <Seg<St> value={st} onChange={setSt} items={[['all', 'Any status'], ['proposed', 'Proposed'], ['selected', 'Selected']]} />
        <div className="sp" />
        {canEdit && sel.size > 0 && <span className="bulkbar"><b>{sel.size} selected</b><button className="btn sm" onClick={() => setSel(new Set())}>Deselect</button>
          <button className="btn sm" style={{ color: 'var(--bad)' }} onClick={() => setConfirmDel([...sel])}><Icon name="trash" size="s" />Delete</button></span>}
      </div>
      <div className="gw"><table className="dg altlist" style={{ width: '100%' }}>
        <thead><tr>{canEdit && <th style={{ width: 34 }}><input type="checkbox" aria-label="Select all" checked={allSel} onChange={() => setSel(allSel ? new Set() : new Set(rows.map(r => r.unitRateId)))} /></th>}
          <th style={{ width: 28 }} /><th>Description</th><th style={{ width: 120 }}>Unit</th><th className="r" style={{ width: 130 }}>Price</th><th className="r" style={{ width: 140 }}>Quoted price</th><th style={{ width: 100 }}>Status</th><th style={{ width: 170 }}>Updated</th></tr></thead>
        <tbody>
          {rows.map(i => <Fragment key={i.unitRateId}>
            <tr className={open === i.unitRateId ? 'open' : ''} onClick={() => setOpen(o => o === i.unitRateId ? null : i.unitRateId)} style={{ cursor: 'pointer' }}>
              {canEdit && <td onClick={e => e.stopPropagation()}><input type="checkbox" aria-label={`Select ${i.description}`} checked={sel.has(i.unitRateId)} onChange={() => setSel(s => { const n = new Set(s); n.has(i.unitRateId) ? n.delete(i.unitRateId) : n.add(i.unitRateId); return n })} /></td>}
              <td><Icon name={open === i.unitRateId ? 'chev' : 'chevr'} size="s" /></td>
              <td style={{ whiteSpace: 'normal' }}>{i.description}{i.comments && <div className="muted" style={{ fontSize: 11.5 }}>{i.comments}</div>}</td>
              <td>{i.unit ?? <span className="mut">–</span>}</td>
              <td className="n">{i.selectedTradeId ? money(i.price, 2) : <span className="mut">–</span>}</td>
              <td className="n"><b className="num">{i.selectedTradeId ? money(i.quotedPrice, 2) : '–'}</b></td>
              <td><span className={`tag ${i.selectedTradeId ? 'g' : 'b'}`}>{i.selectedTradeId ? 'Selected' : 'Proposed'}</span></td>
              <td className="mut" style={{ fontSize: 12 }}>{date(i.changedAt.slice(0, 10))}{i.changedByName ? ` · ${i.changedByName}` : ''}</td></tr>
            {open === i.unitRateId && <tr className="xrow"><td colSpan={cols}><UnitPanel item={i} canEdit={canEdit} onData={put} onDelete={() => setConfirmDel([i.unitRateId])} /></td></tr>}
          </Fragment>)}
          {!rows.length && <tr><td colSpan={cols} className="empty">{data.items.length ? 'Nothing matches the filters.' : 'No unit prices yet. Add one, then each trade enters its price on Scope match → Unit prices.'}</td></tr>}
        </tbody></table></div>
    </div>
    {add && <AddUnitModal vid={vid} onClose={() => setAdd(false)} onDone={d => { put(d); setAdd(false); const n = [...d.items].sort((a, b) => b.unitRateId - a.unitRateId)[0]; if (n) setOpen(n.unitRateId) }} />}
    {confirmDel && <Modal title="Delete unit prices" onClose={() => setConfirmDel(null)} footer={<><button className="btn" onClick={() => setConfirmDel(null)}>Cancel</button>
      <button className="btn p" style={{ background: 'var(--bad)' }} disabled={busy} onClick={async () => { const ids = confirmDel; const d = await run(() => api.post<UnitPricesData>(`/versions/${vid}/unit-prices/delete`, { ids }), `${ids.length} deleted`); if (d) { put(d); setSel(new Set()); setConfirmDel(null) } }}><Icon name="trash" />Delete</button></>}>
      <p>Delete {confirmDel.length === 1 ? `"${data.items.find(x => x.unitRateId === confirmDel[0])?.description}"` : `${confirmDel.length} unit prices`}? The prices the trades entered are deleted too.</p></Modal>}
  </>
}

function UnitPanel({ item, canEdit, onData, onDelete }: { item: UnitPriceItem; canEdit: boolean; onData(d?: UnitPricesData): void; onDelete(): void }) {
  const { run, busy } = useAction(); const nav = useNavigate()
  const init = { description: item.description, unit: item.unit ?? '', quotedPct: String(item.quotedPct), comments: item.comments ?? '' }
  const [f, setF] = useState(init)
  const dirty = f.description !== init.description || f.unit !== init.unit || toNum(f.quotedPct) !== item.quotedPct || f.comments !== init.comments
  const patch = (body: object, ok?: string) => run(() => api.patch<UnitPricesData>(`/unit-prices/${item.unitRateId}`, { ...body, rowRev: item.rowRev }), ok).then(onData)
  return <div className="xpanel">
    <div className="xform">
      <div className="field"><label>Description</label><textarea className="inp" rows={3} disabled={!canEdit} value={f.description} onChange={e => setF({ ...f, description: e.target.value })} /></div>
      <div className="field"><label>Unit</label><select className="inp" disabled={!canEdit} value={f.unit} onChange={e => setF({ ...f, unit: e.target.value })}><option value="">Select unit</option>{[...new Set([...UNITS, ...(f.unit ? [f.unit] : [])])].map(u => <option key={u}>{u}</option>)}</select></div>
      <div className="field"><label>Quoted %</label><input className="inp num" style={{ width: 120 }} disabled={!canEdit} value={f.quotedPct} onChange={e => setF({ ...f, quotedPct: e.target.value })} /></div>
      <div className="field"><label>Comments</label><textarea className="inp" rows={3} disabled={!canEdit} value={f.comments} onChange={e => setF({ ...f, comments: e.target.value })} /></div>
      {canEdit && <div className="tb" style={{ border: 0, padding: 0 }}><button className="btn sm" style={{ color: 'var(--bad)' }} onClick={onDelete}><Icon name="trash" size="s" />Delete</button><span className="sp" />
        <button className="btn sm" disabled={!dirty} onClick={() => setF(init)}>Cancel</button>
        <button className="btn p sm" disabled={!dirty || busy || !f.description.trim() || toNum(f.quotedPct) == null} onClick={() => patch({ description: f.description, unit: f.unit, quotedPct: toNum(f.quotedPct), comments: f.comments }, 'Unit price saved')}><Icon name="check" size="s" />Save</button></div>}
    </div>
    <div className="xdetail">
      <div className="tb" style={{ border: 0 }}><b style={{ fontSize: 12.5 }}>Trade unit prices</b></div>
      <table className="dg" style={{ width: '100%' }}>
        <thead><tr><th>Trade</th><th className="r" style={{ width: 120 }}>Price</th><th>Note</th><th style={{ width: 120 }}>Status</th></tr></thead>
        <tbody>
          {item.trades.map(t => <tr key={t.tradeId}>
            <td><span className="code">{t.csiCode}</span> <a className="lnk" onClick={() => nav(`../scope-match?trade=${t.tradeId}&tab=unitprices`, { relative: 'path' })}>{t.tradeName}</a></td>
            <td className="n">{t.price ? money(t.price, 2) : <span className="mut">–</span>}</td><td className="mut" style={{ whiteSpace: 'normal' }}>{t.note}</td>
            <td>{t.isSelected ? <span className="tag g">Selected{canEdit && <button className="rm" style={{ opacity: 1, marginLeft: 4 }} title="Clear the selection" disabled={busy} onClick={() => patch({ clearSelection: true }, 'Selection cleared')}><Icon name="x" size="s" /></button>}</span>
              : canEdit && t.price > 0 ? <button className="btn sm" disabled={busy} onClick={() => patch({ selectTradeId: t.tradeId }, `${t.tradeName} selected`)}>Select</button> : <span className="mut">Proposed</span>}</td></tr>)}
          {!item.trades.length && <tr><td colSpan={4} className="mut">No trades on this version.</td></tr>}
          <tr className="sub"><td><b>Selected price</b></td><td className="n"><b className="num">{money(item.price, 2)}</b></td><td colSpan={2} className="mut">{item.unit ? `per ${item.unit}` : ''}</td></tr>
          <tr className="sub"><td><b>Discounted price</b> <span className="muted">at {pct(item.quotedPct, 2)}</span></td><td className="n"><b className="num">{money(item.discountedPrice, 2)}</b></td><td colSpan={2} /></tr>
          {item.indirects.map(x => <tr key={x.code} className={x.included ? '' : 'off'}>
            <td style={{ paddingLeft: 18 }}><label className="check" style={{ fontSize: 12.5 }}><input type="checkbox" disabled={!canEdit || busy} checked={x.included} onChange={e => patch({ includeCode: x.code, included: e.target.checked })} />{x.name}</label></td>
            <td className="n">{x.included ? money(x.amount, 2) : <span className="mut">excluded</span>}</td><td colSpan={2} className="mut" style={{ fontSize: 11.5 }}>{pct(x.pct ?? 0, 4)} of the contract</td></tr>)}
          <tr className="tot"><td><b>Quoted price</b></td><td className="n"><b className="num">{money(item.quotedPrice, 2)}</b></td><td colSpan={2} /></tr>
        </tbody></table>
    </div>
  </div>
}

function AddUnitModal({ vid, onClose, onDone }: { vid: number; onClose(): void; onDone(d: UnitPricesData): void }) {
  const { run, busy } = useAction()
  const [f, setF] = useState({ description: '', unit: '', quotedPct: '100', comments: '' })
  async function save() {
    const d = await run(() => api.post<UnitPricesData>(`/versions/${vid}/unit-prices`, { ...f, quotedPct: toNum(f.quotedPct) ?? 100 }), 'Unit price added')
    if (d) onDone(d)
  }
  return <Modal title="Add unit price" onClose={onClose} footer={<><button className="btn" onClick={onClose}>Cancel</button><button className="btn p" disabled={busy || !f.description.trim()} onClick={save}><Icon name="check" />Add</button></>}>
    <div style={{ display: 'grid', gap: 12 }}>
      <div className="field"><label>Description</label><textarea className="inp" rows={3} autoFocus placeholder="e.g. Additional rock excavation" value={f.description} onChange={e => setF({ ...f, description: e.target.value })} /></div>
      <div className="field"><label>Unit <small>optional</small></label><select className="inp" value={f.unit} onChange={e => setF({ ...f, unit: e.target.value })}><option value="">Select unit</option>{UNITS.map(u => <option key={u}>{u}</option>)}</select></div>
      <div className="field"><label>Quoted %</label><input className="inp num" style={{ width: 120 }} value={f.quotedPct} onChange={e => setF({ ...f, quotedPct: e.target.value })} /></div>
      <div className="field"><label>Comments <small>optional</small></label><textarea className="inp" rows={2} value={f.comments} onChange={e => setF({ ...f, comments: e.target.value })} /></div>
    </div>
  </Modal>
}
