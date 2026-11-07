// Allowances (was: Allowances). Same model as the original app: an owner prescribed allowance puts a scope line in every trade,
// a miscellaneous one in a single trade. Each trade partner prices that line on Scope match like any other line, so it counts in
// the bids; the allowance shows what the picked bid carries for it. Included trades add up to the allowance total.
import { Fragment, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { AllowanceItem, AllowancesData, BidBoardData, ProjectDetail, Version } from '../../api/types'
import { PageHead } from '../../components/Shell'
import { Icon } from '../../components/Icon'
import { Failed, Kpi, Loading, Modal, Seg, Tabs, Toggle, toNum, useAction } from '../../components/ui'
import { date, money } from '../../app/format'
import { vkey } from '../../app/hooks'

type TabK = 'all' | 'ownerprescribed' | 'miscellaneous'
type St = 'all' | 'excluded' | 'included'

export default function Allowances({ project, version }: { project: ProjectDetail; version: Version }) {
  const vid = version.versionId; const qc = useQueryClient(); const { run, busy } = useAction()
  const key = vkey('allowances', vid)
  const { data, error, isLoading } = useQuery({ queryKey: key, queryFn: () => api.get<AllowancesData>(`/versions/${vid}/allowances`) })
  const board = useQuery({ queryKey: vkey('bid-board', vid), queryFn: () => api.get<BidBoardData>(`/versions/${vid}/bid-board`) })
  const [tab, setTab] = useState<TabK>('all'); const [st, setSt] = useState<St>('all'); const [q, setQ] = useState(''); const [tf, setTf] = useState<number[]>([])
  const [open, setOpen] = useState<number | null>(null); const [sel, setSel] = useState<Set<number>>(new Set()); const [add, setAdd] = useState(false)
  const [confirmDel, setConfirmDel] = useState<number[] | null>(null)
  const canEdit = project.access !== 'viewer' && version.status !== 'locked'
  const put = (d?: AllowancesData) => { if (d) { qc.setQueryData(key, d); qc.invalidateQueries({ queryKey: vkey('bid-board', vid) }); qc.invalidateQueries({ queryKey: vkey('price-roll-up', vid) }) } }

  const rows = useMemo(() => (data?.items ?? []).filter(i => (tab === 'all' || i.kind === tab)
    && (st === 'all' || i.trades.some(t => t.included === (st === 'included')))
    && (!tf.length || i.trades.some(t => tf.includes(t.tradeId)))
    && (!q || `${i.description} ${i.trades.map(t => t.tradeName).join(' ')}`.toLowerCase().includes(q.toLowerCase()))), [data, tab, st, q, tf])
  if (isLoading) return <Loading what="allowances" />
  if (error || !data) return <Failed error={error} />

  const owner = rows.filter(r => r.kind === 'ownerprescribed')
  const misc = rows.filter(r => r.kind === 'miscellaneous')
  const miscByTrade = [...misc.reduce((m, r) => m.set(r.tradeName ?? '', [...(m.get(r.tradeName ?? '') ?? []), r]), new Map<string, AllowanceItem[]>())].sort(([a], [b]) => a.localeCompare(b))
  const allSel = rows.length > 0 && rows.every(r => sel.has(r.sumId))
  const cols = canEdit ? 5 : 4
  async function bulkStatus(included: boolean) {
    const items = data!.items.filter(i => sel.has(i.sumId)).flatMap(i => i.trades.map(t => ({ sumId: i.sumId, tradeId: t.tradeId })))
    if (items.length) put(await run(() => api.post<AllowancesData>(`/versions/${vid}/allowances/status`, { items, included }), `${sel.size} allowance${sel.size > 1 ? 's' : ''} ${included ? 'included' : 'excluded'}`))
  }
  const row = (i: AllowanceItem) => <Fragment key={i.sumId}>
    <tr className={open === i.sumId ? 'open' : ''} onClick={() => setOpen(o => o === i.sumId ? null : i.sumId)} style={{ cursor: 'pointer' }}>
      {canEdit && <td onClick={e => e.stopPropagation()}><input type="checkbox" aria-label={`Select ${i.description}`} checked={sel.has(i.sumId)} onChange={() => setSel(s => { const n = new Set(s); n.has(i.sumId) ? n.delete(i.sumId) : n.add(i.sumId); return n })} /></td>}
      <td><Icon name={open === i.sumId ? 'chev' : 'chevr'} size="s" /></td>
      <td style={{ whiteSpace: 'normal' }}>{i.description}<span className="muted" style={{ fontSize: 11.5, marginLeft: 8 }}>{i.trades.filter(t => t.included).length} of {i.trades.length} trade{i.trades.length === 1 ? '' : 's'} included</span></td>
      <td className="n"><b className="num">{money(i.total)}</b></td>
      <td className="mut" style={{ fontSize: 12 }}>{date(i.changedAt.slice(0, 10))}{i.changedByName ? ` · ${i.changedByName}` : ''}</td></tr>
    {open === i.sumId && <tr className="xrow"><td colSpan={cols}><AllowancePanel item={i} vid={vid} canEdit={canEdit} onData={put} onDelete={() => setConfirmDel([i.sumId])} /></td></tr>}
  </Fragment>

  return <>
    <PageHead eyebrow={`${project.projectName} · v${version.versionNo}`} title="Allowances" was="Allowances"
      desc="Owner prescribed allowances add a line to every trade; miscellaneous ones to a single trade. Trade partners price the line on Scope match, and it counts in their bids."
      actions={canEdit && <button className="btn p" onClick={() => setAdd(true)} data-was="Add Allowance"><Icon name="plus" />Add allowance</button>} />
    <div className="kpis">
      <Kpi k="Owner prescribed allowances" v={money(data.summary.ownerPrescribed)} />
      <Kpi k="Miscellaneous allowances" v={money(data.summary.miscellaneous)} />
      <Kpi k="Total project allowances" v={money(data.summary.total)} cls="hl" />
    </div>
    <div className="card">
      <Tabs<TabK> value={tab} onChange={setTab} items={[['all', 'All', data.items.length], ['ownerprescribed', 'Owner prescribed'], ['miscellaneous', 'Miscellaneous']]} />
      <div className="tb" style={{ flexWrap: 'wrap', gap: 8 }}>
        <label className="sbox srch3"><Icon name="search" size="s" /><input placeholder="Search allowance or trade" value={q} onChange={e => setQ(e.target.value)} /></label>
        <Seg<St> value={st} onChange={setSt} items={[['all', 'All'], ['excluded', 'Excluded'], ['included', 'Included']]} />
        <TradePicker trades={(board.data?.trades ?? []).map(t => ({ id: t.tradeId, label: `${t.csiCode} ${t.tradeName}` }))} value={tf} onChange={setTf} />
        <div className="sp" />
        {canEdit && sel.size > 0 && <span className="bulkbar"><b>{sel.size} selected</b>
          <button className="btn sm" onClick={() => setSel(new Set())}>Deselect</button>
          <button className="btn sm" disabled={busy} onClick={() => bulkStatus(true)}>Include</button>
          <button className="btn sm" disabled={busy} onClick={() => bulkStatus(false)}>Exclude</button>
          <button className="btn sm" style={{ color: 'var(--bad)' }} onClick={() => setConfirmDel([...sel])}><Icon name="trash" size="s" />Delete</button></span>}
      </div>
      <div className="gw"><table className="dg altlist" style={{ width: '100%' }}>
        <thead><tr>{canEdit && <th style={{ width: 34 }}><input type="checkbox" aria-label="Select all" checked={allSel} onChange={() => setSel(allSel ? new Set() : new Set(rows.map(r => r.sumId)))} /></th>}
          <th style={{ width: 28 }} /><th>Allowance</th><th className="r" style={{ width: 150 }}>Total</th><th style={{ width: 190 }}>Updated</th></tr></thead>
        <tbody>
          {owner.length > 0 && <tr className="grp2"><td colSpan={cols}>Owner prescribed <span className="muted" style={{ fontWeight: 400 }}>· a line in every trade</span></td></tr>}
          {owner.map(row)}
          {miscByTrade.map(([trade, items]) => <Fragment key={trade}><tr className="grp2"><td colSpan={cols}>{trade} <span className="muted" style={{ fontWeight: 400 }}>· miscellaneous</span></td></tr>{items.map(row)}</Fragment>)}
          {!rows.length && <tr><td colSpan={cols} className="empty">{data.items.length ? 'Nothing matches the filters.' : 'No allowances yet.'}</td></tr>}
        </tbody></table></div>
    </div>
    {add && <AddAllowanceModal vid={vid} trades={board.data?.trades.map(t => ({ id: t.tradeId, label: `${t.csiCode} ${t.tradeName}` })) ?? []} onClose={() => setAdd(false)}
      onDone={d => { put(d); setAdd(false); const n = [...d.items].sort((a, b) => b.sumId - a.sumId)[0]; if (n) setOpen(n.sumId) }} />}
    {confirmDel && <Modal title="Delete allowances" onClose={() => setConfirmDel(null)} footer={<><button className="btn" onClick={() => setConfirmDel(null)}>Cancel</button>
      <button className="btn p" style={{ background: 'var(--bad)' }} disabled={busy} onClick={async () => { const ids = confirmDel; const d = await run(() => api.post<AllowancesData>(`/versions/${vid}/allowances/delete`, { ids }), `${ids.length} deleted`); if (d) { put(d); setSel(new Set()); setConfirmDel(null); if (open && ids.includes(open)) setOpen(null) } }}><Icon name="trash" />Delete</button></>}>
      <p>Delete {confirmDel.length === 1 ? `"${data.items.find(x => x.sumId === confirmDel[0])?.description}"` : `${confirmDel.length} allowances`}? The allowance line{confirmDel.length === 1 ? '' : 's'} and the amounts bidders entered on {confirmDel.length === 1 ? 'it' : 'them'} are removed from every trade, and the bids are recalculated.</p></Modal>}
  </>
}

function AllowancePanel({ item, vid, canEdit, onData, onDelete }: { item: AllowanceItem; vid: number; canEdit: boolean; onData(d?: AllowancesData): void; onDelete(): void }) {
  const { run, busy } = useAction(); const nav = useNavigate()
  const [desc, setDesc] = useState(item.description); const [amt, setAmt] = useState(''); const [tid, setTid] = useState('')
  useEffect(() => setDesc(item.description), [item.description])
  const status = (tradeIds: number[], included: boolean) => run(() => api.post<AllowancesData>(`/versions/${vid}/allowances/status`, { items: tradeIds.map(tradeId => ({ sumId: item.sumId, tradeId })), included })).then(onData)
  return <div className="xpanel">
    <div className="xform">
      <div className="field"><label>Description</label><textarea className="inp" rows={3} disabled={!canEdit} value={desc} onChange={e => setDesc(e.target.value)} />
        <small>{item.kind === 'ownerprescribed' ? 'Owner prescribed · a line in every trade' : `Miscellaneous · ${item.tradeName}`}. The line text on Scope match follows this description.</small></div>
      {item.kind === 'ownerprescribed' && canEdit && <div className="field"><label>Budget amount → all trade partners of a trade</label>
        <div className="applyrow"><input className="inp num" style={{ width: 120 }} placeholder="e.g. 20000" value={amt} onChange={e => setAmt(e.target.value)} />
          <select className="inp" style={{ flex: 1, minWidth: 140 }} value={tid} onChange={e => setTid(e.target.value)}><option value="">Select trade…</option>{item.trades.map(t => <option key={t.tradeId} value={t.tradeId}>{t.csiCode} {t.tradeName}</option>)}</select>
          <button className="btn sm" disabled={busy || toNum(amt) == null || !tid} onClick={async () => { const d = await run(() => api.post<AllowancesData>(`/allowances/${item.sumId}/apply`, { tradeId: Number(tid), amount: toNum(amt) }), 'Amount entered for every bidder of the trade'); if (d) { onData(d); setAmt('') } }}>Apply</button></div>
        <small>Enters the amount on the allowance line for every bidder of that trade.</small></div>}
      {canEdit && <div className="tb" style={{ border: 0, padding: 0 }}><button className="btn sm" style={{ color: 'var(--bad)' }} onClick={onDelete}><Icon name="trash" size="s" />Delete</button><span className="sp" />
        <button className="btn sm" disabled={desc === item.description} onClick={() => setDesc(item.description)}>Cancel</button>
        <button className="btn p sm" disabled={busy || desc === item.description || !desc.trim()} onClick={async () => onData(await run(() => api.patch<AllowancesData>(`/allowances/${item.sumId}`, { description: desc, rowRev: item.rowRev }), 'Allowance saved'))}><Icon name="check" size="s" />Save</button></div>}
    </div>
    <div className="xdetail">
      <div className="tb" style={{ border: 0 }}><b style={{ fontSize: 12.5 }}>Trade entered values</b><span className="sp" />
        {canEdit && item.trades.length > 1 && <><button className="btn sm" disabled={busy} onClick={() => status(item.trades.map(t => t.tradeId), true)}>Include all</button><button className="btn sm" disabled={busy} onClick={() => status(item.trades.map(t => t.tradeId), false)}>Exclude all</button></>}</div>
      <table className="dg" style={{ width: '100%' }}>
        <thead><tr><th>Trade</th><th>Picked bid</th><th className="r" style={{ width: 130 }}>Total</th><th style={{ width: 150 }}>Status</th></tr></thead>
        <tbody>
          {item.trades.map(t => <tr key={t.tradeId} className={t.included ? '' : 'off'}>
            <td><span className="code">{t.csiCode}</span> <a className="lnk" onClick={() => nav(`../scope-match?trade=${t.tradeId}`, { relative: 'path' })}>{t.tradeName}</a></td>
            <td className="mut" style={{ fontSize: 12 }}>{t.carried ?? <span className="tag y">Nothing picked</span>}<span style={{ marginLeft: 6 }} title="Bidders who priced the allowance line">· {t.priced}/{t.bidders} priced</span></td>
            <td className="n">{money(t.contribution)}</td>
            <td><Toggle label={t.included ? 'Included' : 'Excluded'} on={t.included} onChange={v => canEdit && status([t.tradeId], v)} /></td></tr>)}
          <tr className="tot"><td><b>Total</b></td><td /><td className="n"><b className="num">{money(item.total)}</b></td><td className="mut" style={{ fontSize: 11.5 }}>included trades</td></tr>
        </tbody></table>
    </div>
  </div>
}

function TradePicker({ trades, value, onChange }: { trades: { id: number; label: string }[]; value: number[]; onChange(v: number[]): void }) {
  const [open, setOpen] = useState(false); const ref = useRef<HTMLDivElement>(null)
  useEffect(() => { if (!open) return; const c = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) setOpen(false) }; addEventListener('mousedown', c); return () => removeEventListener('mousedown', c) }, [open])
  const label = !value.length ? 'All trades' : value.length === 1 ? trades.find(t => t.id === value[0])?.label ?? '1 trade' : `${value.length} trades`
  return <div ref={ref} style={{ position: 'relative' }}>
    <button className="btn" onClick={() => setOpen(o => !o)} aria-expanded={open}><Icon name="filter" size="s" />{label}<Icon name="chev" size="s" /></button>
    {open && <div className="menu" style={{ position: 'absolute', top: '100%', left: 0, zIndex: 60, marginTop: 4, minWidth: 260, maxHeight: 340, overflow: 'auto' }}>
      <label className="check" style={{ padding: '6px 10px' }}><input type="checkbox" checked={!value.length} onChange={() => onChange([])} /><em>All trades</em></label>
      {trades.map(t => <label key={t.id} className="check" style={{ padding: '6px 10px' }}><input type="checkbox" checked={value.includes(t.id)} onChange={() => onChange(value.includes(t.id) ? value.filter(x => x !== t.id) : [...value, t.id])} />{t.label}</label>)}
    </div>}
  </div>
}

function AddAllowanceModal({ vid, trades, onClose, onDone }: { vid: number; trades: { id: number; label: string }[]; onClose(): void; onDone(d: AllowancesData): void }) {
  const { run, busy } = useAction()
  const [f, setF] = useState<{ kind: 'ownerprescribed' | 'miscellaneous'; description: string; tradeId: string }>({ kind: 'ownerprescribed', description: '', tradeId: '' })
  const ok = f.description.trim() && (f.kind === 'ownerprescribed' || f.tradeId)
  async function save() {
    const d = await run(() => api.post<AllowancesData>(`/versions/${vid}/allowances`, { kind: f.kind, description: f.description, tradeId: f.kind === 'miscellaneous' ? Number(f.tradeId) : null }), 'Allowance added')
    if (d) onDone(d)
  }
  return <Modal title="Add allowance" onClose={onClose} footer={<><button className="btn" onClick={onClose}>Cancel</button><button className="btn p" disabled={busy || !ok} onClick={save}><Icon name="check" />Add</button></>}>
    <div style={{ display: 'grid', gap: 12 }}>
      <div className="field"><label>Type</label><Seg value={f.kind} onChange={kind => setF({ ...f, kind })} items={[['ownerprescribed', 'Owner prescribed'], ['miscellaneous', 'Miscellaneous']]} /></div>
      <div className="field"><label>Description</label><textarea className="inp" rows={3} autoFocus placeholder="e.g. Special equipment allowance" value={f.description} onChange={e => setF({ ...f, description: e.target.value })} /></div>
      {f.kind === 'miscellaneous' && <div className="field"><label>Trade</label><select className="inp" value={f.tradeId} onChange={e => setF({ ...f, tradeId: e.target.value })}><option value="">Select trade…</option>{trades.map(t => <option key={t.id} value={t.id}>{t.label}</option>)}</select></div>}
      <small className="muted">{f.kind === 'ownerprescribed' ? 'A line for it is added to the scope of every trade on this version (and to trades added later).' : 'A line for it is added to the scope of the trade you pick.'} Trades start as Excluded, as in the original app.</small>
    </div>
  </Modal>
}
