// Scope match (was: Scope leveling). Level every bid to the same scope, then pick one.
// Trade partners run side by side as lanes; type Included / Excluded / an amount / =formula / text into a cell.
import { lazy, Suspense, useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { calcTrade, parseAnswer } from '@gridline/calc'
import { api } from '../../api/client'
import type { BidBoardData, Bid, ProjectDetail, TradeAlternate, TradeUnitPrice, ScopeAnswer, ScopeData, ScopeLine, Trade, TradeReply, Version } from '../../api/types'
import { PageHead } from '../../components/Shell'
import { Icon } from '../../components/Icon'
import { AskText, ContextMenu, EditCell, ExportMenu, Failed, Loading, Menu, Modal, Seg, Tabs, Toggle, letterColour, toNum, useAction, type CtxItem } from '../../components/ui'
import { useNavigate } from 'react-router'
import { date, lane, money, pct } from '../../app/format'
import { useFeatures, useLookups, useOthersFocus, vkey } from '../../app/hooks'
import { ALT_STATUS } from './Alternates'
import { focus } from '../../live/hub'
import { useToast } from '../../app/toast'
import { AddBidModal, BidDrawer } from './BidBoard'
import { DataGrid, pasteNum, type GCol, type GridEdit, type H } from '../../components/DataGrid'
const ScratchPad = lazy(() => import('../../components/ScratchPad').then(m => ({ default: m.ScratchPad })))

type Tab = 'items' | 'estimate' | 'scratch' | 'files' | 'quals' | 'inbox' | 'alternates' | 'unitprices'
const SECTIONS: [ScopeLine['section'], string, string | null][] = [
  ['baserequirement', 'Base requirements', 'Text comes from the base requirements checklist'],
  ['tradeitem', 'Trade items', null],
  ['zone', 'Zones', 'Each bidder\'s amount per zone · names follow Project profile → Zones'],
]
const akey = (l: number, b: number) => `${l}:${b}`
const ITEM_W = 380, LANE_W = 170   // fixed column widths; rows grow taller to fit the text
/** What a stored answer looks like when you edit it. */
export function answerText(a?: ScopeAnswer | null) {
  if (!a) return ''
  switch (a.kind) { case 'included': return 'Included'; case 'excluded': return 'Excluded'; case 'price': return 'Price'
    case 'amount': return a.formula ?? String(a.amount ?? ''); case 'text': return a.answerText ?? ''; default: return '' }
}
function Chip({ a, desired }: { a?: { kind: string; amount?: number | null; answerText?: string | null } | null; desired?: string | null }) {
  if (desired !== undefined) {
    if (!desired) return <span className="mut">–</span>
    return desired === 'included' ? <span className="ch in">✓ Included</span> : desired === 'excluded' ? <span className="ch ex">✕ Excluded</span> : desired === 'price' ? <span className="ch pr">$ Price</span> : <span className="ch tx">Text</span>
  }
  if (!a || a.kind === 'blank') return <span className="ch na" title="No answer">No answer</span>
  if (a.kind === 'included') return <span className="ch in">✓ Included</span>
  if (a.kind === 'excluded') return <span className="ch ex">✕ Excluded</span>
  if (a.kind === 'price') return <span className="ch pr">$ Price</span>
  if (a.kind === 'amount') return <span className={`ch am ${(a.amount ?? 0) < 0 ? 'neg' : ''}`}>{(a.amount ?? 0) < 0 ? '−' : '+'}{money(Math.abs(a.amount ?? 0))}</span>
  return <span className="ch tx" title={a.answerText ?? ''}>“{a.answerText}”</span>
}

export default function ScopeMatch({ project, version }: { project: ProjectDetail; version: Version }) {
  const vid = version.versionId; const qc = useQueryClient(); const toast = useToast(); const { run } = useAction(); const lk = useLookups()
  const [sp, setSp] = useSearchParams()
  const board = useQuery({ queryKey: vkey('bid-board', vid), queryFn: () => api.get<BidBoardData>(`/versions/${vid}/bid-board`) })
  const trades = board.data?.trades ?? []
  const tradeId = Number(sp.get('trade')) || trades[0]?.tradeId
  const key = vkey('scope', vid, tradeId)
  const { data, error, isLoading } = useQuery({ queryKey: key, queryFn: () => api.get<ScopeData>(`/trades/${tradeId}/scope`), enabled: !!tradeId })
  const [tab, setTab] = useState<Tab>(() => (['alternates', 'unitprices', 'estimate', 'scratch', 'files', 'quals', 'inbox'].includes(sp.get('tab') ?? '') ? sp.get('tab') as Tab : 'items')); const feats = useFeatures().data; const [sel, setSel] = useState<number | null>(null); const [cell, setCell] = useState<{ l: number; b: number } | null>(null)
  const [q, setQ] = useState(''); const [hit, setHit] = useState(0); const [gaps, setGaps] = useState(false)
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set()); const [addBid, setAddBid] = useState(false); const [bidOpen, setBidOpen] = useState<number | null>(null); const [picker, setPicker] = useState(false)
  const [ctx, setCtx] = useState<{ x: number; y: number; items: CtxItem[] } | null>(null); const [ask, setAsk] = useState<{ title: string; label: string; initial?: string; multiline?: boolean; number?: boolean; save(v: string): void } | null>(null)
  const nav = useNavigate()
  const others = useOthersFocus('answer')
  const canEdit = project.access !== 'viewer' && version.status !== 'locked'
  useEffect(() => { setSel(null); setCell(null) }, [tradeId])
  // ?row=<lineId> (e.g. from clicking a teammate in "who's here"): select that row and scroll to it
  const rowParam = Number(sp.get('row')) || null
  useEffect(() => {
    if (!rowParam || !data) return
    setSel(rowParam); setTimeout(() => document.querySelector(`[data-lid="${rowParam}"]`)?.scrollIntoView({ block: 'center' }), 50)
    setSp(s => { s.delete('row'); return s }, { replace: true })
  }, [rowParam, data?.tradeId])

  const go = (id: number) => setSp(s => { s.set('trade', String(id)); return s }, { replace: true })
  const idx = trades.findIndex(t => t.tradeId === tradeId)
  const setData = (f: (d: ScopeData) => ScopeData) => qc.setQueryData<ScopeData>(key, d => d && f(d))
  const syncBoard = (t: Trade) => qc.setQueryData<BidBoardData>(vkey('bid-board', vid), d => d && { ...d, trades: d.trades.map(x => x.tradeId === t.tradeId ? t : x) })

  const answers = useMemo(() => new Map((data?.answers ?? []).map(a => [akey(a.lineId, a.bidId), a])), [data?.answers])
  if (!tradeId && board.isSuccess) return <><PageHead title="Scope match" was="Scope leveling" /><div className="empty">This version has no trades yet. Add one on the Bid board.</div></>
  if (isLoading || board.isLoading) return <Loading what="scope match" />
  if (error || !data) return <Failed error={error} />
  const t = data.trade, allBids = t.bids, bids = allBids.filter(b => !b.hiddenInScope), lines = data.lines

  // ------------------------------------------------------------------ saving a cell (browser recalculates first)
  async function saveCells(items: { lineId: number; bidId: number; value?: string; cellColour?: string | null; clearColour?: boolean; note?: string }[]) {
    if (!canEdit) { toast('This version is read-only for you', 'lock'); return }
    const zoneIds = new Set(data!.lines.filter(l => l.section === 'zone').map(l => l.lineId))
    if (items.some(i => zoneIds.has(i.lineId) && i.value !== undefined && (toNum(i.value) ?? 0) < 0)) toast('Negative value in Zones', 'alert')
    const before = data!
    setData(d => {
      const map = new Map(d.answers.map(a => [akey(a.lineId, a.bidId), a]))
      for (const it of items) {
        const cur = map.get(akey(it.lineId, it.bidId)) ?? { lineId: it.lineId, bidId: it.bidId, kind: 'blank', amount: null, answerText: null, formula: null, cellColour: null, note: null, source: 'manual', rowRev: 0, changedAt: '', changedByName: null } as ScopeAnswer
        const p = it.value !== undefined ? parseAnswer(it.value) : null
        map.set(akey(it.lineId, it.bidId), { ...cur, ...(p ? { kind: p.kind, amount: p.amount, answerText: p.text, formula: p.formula } : {}), cellColour: it.clearColour ? null : (it.cellColour as ScopeAnswer['cellColour']) ?? cur.cellColour, note: it.note ?? cur.note })
      }
      const all = [...map.values()]
      const o = calcTrade({ tradeId: d.trade.tradeId, adjustment: d.trade.adjustment, plugAmount: d.trade.plugAmount, budgetValue: d.trade.budgetValue,
        bids: d.trade.bids.map(b => ({ bidId: b.bidId, isPosted: b.isPosted, isCarried: b.isCarried, state: b.state, baseAmount: b.baseAmount })),
        lines: d.lines.map(l => ({ lineId: l.lineId, section: l.section, isHeading: l.isHeading, skipInAdjustments: l.skipInAdjustments })),
        answers: all.map(a => ({ lineId: a.lineId, bidId: a.bidId, kind: a.kind, amount: a.amount })) })
      const trade = { ...d.trade, carriedValue: o.carriedValue, lowestValue: o.lowestValue, bids: d.trade.bids.map(b => { const x = o.bids.find(y => y.bidId === b.bidId)!; return { ...b, scopeAdjustments: x.scopeAdjustments, totalAmount: x.totalAmount, rank: x.rank } }) }
      return { ...d, answers: all, trade }
    })
    const r = await run(() => api.put<{ answers: ScopeAnswer[]; trade: Trade; totalsHash: string }>('/answers', { items: items.map(i => ({ ...i, rowRev: answers.get(akey(i.lineId, i.bidId))?.rowRev ?? 0 })) }))
    if (!r) { setData(() => before); qc.invalidateQueries({ queryKey: key }); return }
    setData(d => { const m = new Map(d.answers.map(a => [akey(a.lineId, a.bidId), a])); r.answers.forEach(a => m.set(akey(a.lineId, a.bidId), a)); return { ...d, answers: [...m.values()], trade: r.trade } })
    syncBoard(r.trade); qc.invalidateQueries({ queryKey: vkey('price-roll-up', vid) })
  }
  async function pick(b: Bid) {
    if (!canEdit) return
    if (!b.isPosted) return toast(`${b.bidderName} hasn't posted a price yet`, 'alert')
    const r = await run(() => api.put<TradeReply>(`/trades/${t.tradeId}/pick`, { bidId: b.isCarried ? null : b.bidId }), b.isCarried ? 'Pick cleared' : `Picked ${b.bidderName}`)
    if (r) { setData(d => ({ ...d, trade: r.trade })); syncBoard(r.trade); qc.invalidateQueries({ queryKey: vkey('price-roll-up', vid) }) }
  }
  const reload = () => qc.invalidateQueries({ queryKey: key })
  async function lineSave(l: ScopeLine, body: object, ok?: string) { const r = await run(() => api.patch(`/lines/${l.lineId}`, { ...body, rowRev: l.rowRev }), ok); if (r) { reload(); qc.invalidateQueries({ queryKey: vkey('bid-board', vid) }) } }
  /** Insert 1/10/50 rows or a header row. Like the original, rows go in just before the row you clicked (or at the end). */
  async function insertLine(at: ScopeLine | null, heading = false, count = 1, where: 'before' | 'after' = 'before') {
    const target = at && at.section === 'tradeitem' ? at : null
    const r = await run(() => api.post<{ lines: ScopeLine[]; firstLineId: number }>(`/trades/${t.tradeId}/lines`, {
      beforeLineId: target && where === 'before' ? target.lineId : null, afterLineId: target && where === 'after' ? target.lineId : null,
      count, isHeading: heading, lineText: heading ? 'New heading' : '' }), heading ? 'Header row inserted' : `${count} row${count > 1 ? 's' : ''} inserted`)
    if (r) { await reload(); setSel(r.firstLineId) }
  }
  async function lanes(order: number[] | null, hidden: number[] | null) {
    const r = await run(() => api.put<Trade>(`/trades/${t.tradeId}/lanes`, { order, hidden }))
    if (r) { setData(d => ({ ...d, trade: r })); syncBoard(r) }
  }
  const moveLane = (b: Bid, d: number) => { const ids = allBids.map(x => x.bidId); const i = ids.indexOf(b.bidId), j = i + d; if (j < 0 || j >= ids.length) return; [ids[i], ids[j]] = [ids[j], ids[i]]; lanes(ids, null) }
  const editCellNote = (l: ScopeLine, b: Bid) => { const a = answers.get(akey(l.lineId, b.bidId))
    setAsk({ title: `Note · ${b.bidderName}`, label: l.lineText || 'Cell note', initial: a?.note ?? '', multiline: true, save: v => { setAsk(null); saveCells([{ lineId: l.lineId, bidId: b.bidId, note: v }]) } }) }
  const editLineNote = (l: ScopeLine) => setAsk({ title: 'Line note', label: l.lineText || 'Note for the whole team', initial: l.lineNote ?? '', multiline: true, save: v => { setAsk(null); lineSave(l, { lineNote: v }, 'Note saved') } })
  /** The right-click menu, with the same commands as today's scope leveling grid. */
  function openMenu(e: { preventDefault(): void; clientX: number; clientY: number }, l: ScopeLine, b?: Bid) {
    e.preventDefault(); setSel(l.lineId)
    const colours: CtxItem[] = b ? [null, ...(['red', 'yellow', 'green'] as const).map(c => ({ label: `Color: ${c[0].toUpperCase() + c.slice(1)}`, onClick: () => saveCells([{ lineId: l.lineId, bidId: b.bidId, cellColour: c }]) })),
      { label: 'Color: Clear', onClick: () => saveCells([{ lineId: l.lineId, bidId: b.bidId, clearColour: true }]) }] : []
    const note: CtxItem = { label: 'Edit note', icon: 'note', onClick: () => b ? editCellNote(l, b) : editLineNote(l) }
    let items: CtxItem[]
    if (!canEdit) items = [{ label: 'Read only', onClick: () => {}, disabled: true }]
    else if (l.section === 'baserequirement') items = [note, ...colours, null, { label: '(Please edit item in Base requirements)', icon: 'lock', onClick: () => nav('../base-requirements', { relative: 'path' }) }]
    else if (l.section === 'zone') items = [note, ...(b && !(l.zoneId != null && data!.zones.find(z => z.zoneId === l.zoneId)?.isRemainder) ? [{ label: 'Clear amount', icon: 'x', onClick: () => saveCells([{ lineId: l.lineId, bidId: b.bidId, value: '' }]) } as CtxItem] : []), ...colours, null,
      { label: '(Please edit zones in Project profile → Zones)', icon: 'lock', onClick: () => nav('../profile?tab=zones', { relative: 'path' }) }]
    else items = [note,
      { label: 'Insert row', icon: 'plus', onClick: () => insertLine(l) }, { label: 'Insert 10 rows', icon: 'plus', onClick: () => insertLine(l, false, 10) },
      { label: 'Insert 50 rows', icon: 'plus', onClick: () => insertLine(l, false, 50) }, { label: 'Insert header row', icon: 'list', onClick: () => insertLine(l, true) },
      null,
      { label: 'Change to normal row', onClick: () => lineSave(l, { isHeading: false }), disabled: !l.isHeading }, { label: 'Change to header row', onClick: () => lineSave(l, { isHeading: true }), disabled: l.isHeading },
      { label: 'Indent Right', icon: 'indent', onClick: () => lineSave(l, { indent: l.indent + 1 }), disabled: l.indent >= 5 }, { label: 'Indent Left', icon: 'outdent', onClick: () => lineSave(l, { indent: l.indent - 1 }), disabled: !l.indent },
      { label: 'Exclude from Scope Adjustments', onClick: () => lineSave(l, { skipInAdjustments: true }, 'Not counted in scope adjustments'), disabled: l.skipInAdjustments || l.isHeading },
      { label: 'Include in Scope Adjustments', onClick: () => lineSave(l, { skipInAdjustments: false }, 'Counted in scope adjustments'), disabled: !l.skipInAdjustments },
      ...colours, null,
      { label: l.sumId ? 'Delete row (allowance: use the Allowances page)' : 'Delete row', icon: 'x', danger: true, disabled: !!l.sumId, onClick: async () => { if (await run(() => api.del(`/lines/${l.lineId}`), 'Row deleted')) { setSel(null); reload(); qc.invalidateQueries({ queryKey: vkey('bid-board', vid) }) } } }]
    setCtx({ x: e.clientX, y: e.clientY, items })
  }


  // ------------------------------------------------------------------ derived
  const posted = bids.filter(b => b.isPosted && b.totalAmount != null)
  const lowTotal = posted.length ? Math.min(...posted.map(b => b.totalAmount!)) : null
  const countable = lines.filter(l => !l.isHeading && l.section !== 'zone')
  const isGap = (l: ScopeLine) => l.desired === 'included' && bids.some(b => b.isPosted && (() => { const a = answers.get(akey(l.lineId, b.bidId)); return !a || a.kind === 'blank' || a.kind === 'excluded' })())
  const gapCount = countable.filter(isGap).length
  const hits = q ? lines.filter(l => !l.isHeading && l.lineText.toLowerCase().includes(q.toLowerCase())).map(l => l.lineId) : []
  const curHit = hits.length ? hits[hit % hits.length] : null
  const cov = (b: Bid) => { let i = 0, x = 0, m = 0; for (const l of countable) { const a = answers.get(akey(l.lineId, b.bidId)); if (!a || a.kind === 'blank') m++; else if (a.kind === 'excluded' && l.desired === 'included') x++; else i++ } return { i, x, m, n: countable.length || 1 } }
  const selLine = lines.find(l => l.lineId === sel) ?? null
  // zones (was: breakout areas): each bidder's amount per zone; the remainder zone = bid total − the other zones
  const zoneOf = new Map((data.zones ?? []).map(z => [z.zoneId, z]))
  const zoneLines = lines.filter(l => l.section === 'zone')
  const remLine = zoneLines.find(l => l.zoneId != null && zoneOf.get(l.zoneId)?.isRemainder) ?? null
  const zoneSum = (b: Bid) => zoneLines.reduce((s, l) => l === remLine ? s : s + (answers.get(akey(l.lineId, b.bidId))?.kind === 'amount' ? answers.get(akey(l.lineId, b.bidId))!.amount ?? 0 : 0), 0)
  const remainder = (b: Bid) => b.totalAmount == null ? null : Math.round((b.totalAmount - zoneSum(b)) * 100) / 100
  const zoneMeta = (l: ScopeLine) => { const z = l.zoneId != null ? zoneOf.get(l.zoneId) : undefined; if (!z) return null
    return [z.floorArea ? `${z.floorArea.toLocaleString()} ${z.areaUnit}` : null, z.unitCount && z.unitLabel !== 'N/A' ? `${z.unitCount.toLocaleString()} ${z.unitLabel}${z.unitCount === 1 ? '' : 's'}` : null].filter(Boolean).join(' · ') }

  const header = <PageHead eyebrow={`${project.projectName} · v${version.versionNo}`} title="Scope match" was="Scope leveling"
    desc="Level every bid to the same scope, then pick one. Trade partners run side by side as lanes."
    actions={<><span className="mut" style={{ fontSize: 11.5 }}><Icon name="sync" size="s" /> Saved · live</span>
      {canEdit && <Menu label="Apply scope kit" icon="copy" items={(lk.data?.scopeKits ?? []).map(k => ({ label: `${k.kitName} · ${k.lines} lines`, onClick: async () => { const r = await run(() => api.post<{ added: number }>(`/trades/${t.tradeId}/apply-kit`, { kitId: k.kitId })); if (r) { toast(`${r.added} lines added from ${k.kitName}`); reload() } } }))} />}
      <ExportMenu items={[['Scope match sheet (Excel)', `/exports/scope-match.xlsx?tradeId=${t.tradeId}`], ['Trade package (PDF)', `/exports/trade-package.pdf?tradeId=${t.tradeId}`]]} /></>} />

  const tradebar = <div className="tb sm3">
    <button className="btn sm ic" disabled={idx <= 0} onClick={() => go(trades[idx - 1].tradeId)} title="Previous trade"><Icon name="chevl" size="s" /></button>
    <div style={{ position: 'relative' }}><button className="btn tpick" onClick={() => setPicker(p => !p)}><span className="code">{t.csiCode}</span><b>{t.tradeName}</b><Icon name="updown" size="s" /></button>
      {picker && <div className="menu" style={{ position: 'absolute', top: '100%', left: 0, zIndex: 60, maxHeight: 360, overflow: 'auto' }}>{trades.map(x =>
        <button key={x.tradeId} onClick={() => { setPicker(false); go(x.tradeId) }}><span className="code">{x.csiCode}</span>{x.tradeName}<span className="mut" style={{ marginLeft: 'auto' }}>{x.carriedValue == null ? 'unresolved' : money(x.carriedValue)}</span></button>)}</div>}</div>
    <button className="btn sm ic" disabled={idx < 0 || idx >= trades.length - 1} onClick={() => go(trades[idx + 1].tradeId)} title="Next trade"><Icon name="chevr" size="s" /></button>
    <span className="tchips"><span>{posted.length}/{bids.length} posted</span><span className="g">{gapCount} scope gaps</span>
      <span>{bids.find(b => b.isCarried) ? <>Picked · <b>{bids.find(b => b.isCarried)!.bidderName}</b></> : <b style={{ color: '#d1344b' }}>Nothing picked</b>}</span></span><div className="sp" />
    <label className="sbox srch3"><Icon name="search" size="s" /><input placeholder="Search scope items" value={q} onChange={e => { setQ(e.target.value); setHit(0) }}
      onKeyDown={e => { if (e.key === 'Enter' && hits.length) { const n = (hit + 1) % hits.length; setHit(n); setSel(hits[n]); document.querySelector(`[data-lid="${hits[n]}"]`)?.scrollIntoView({ block: 'center' }) } }} />
      {q && <><span className="hc">{hits.length ? (hit % hits.length) + 1 : 0}/{hits.length}</span>
        <button onClick={() => { const n = (hit - 1 + hits.length) % Math.max(1, hits.length); setHit(n); if (hits[n]) { setSel(hits[n]); document.querySelector(`[data-lid="${hits[n]}"]`)?.scrollIntoView({ block: 'center' }) } }} title="Previous match"><Icon name="chevl" size="s" /></button>
        <button onClick={() => { const n = (hit + 1) % Math.max(1, hits.length); setHit(n); if (hits[n]) { setSel(hits[n]); document.querySelector(`[data-lid="${hits[n]}"]`)?.scrollIntoView({ block: 'center' }) } }} title="Next match"><Icon name="chevr" size="s" /></button>
        <button onClick={() => setQ('')} title="Clear search"><Icon name="x" size="s" /></button></>}</label>
    <Toggle label="Gaps only" on={gaps} onChange={setGaps} />
    </div>

  const tabsEl = <Tabs<Tab> value={tab} onChange={setTab} items={[['items', 'Scope items', countable.length], ['scratch', 'Scratch pad'], ['estimate', 'Internal estimate'], ['files', 'Files'], ['quals', 'Qualifications'], ...(feats?.options ? [['alternates', 'Alternates & VE']] as [Tab, string][] : []), ...(feats?.unitrates ? [['unitprices', 'Unit prices']] as [Tab, string][] : []), ['inbox', 'Bid inbox', data.invites.length + data.readings.length || null]]} />
  if (tab !== 'items') return <>{header}<div className="card sl3">{tradebar}{tabsEl}<div className="cb">
    {tab === 'estimate' && <EstimateTab tradeId={t.tradeId} vid={vid} canEdit={canEdit} />}
    {tab === 'scratch' && <Suspense fallback={<Loading what="the scratch pad" />}><ScratchPad tradeId={t.tradeId} vid={vid} canEdit={canEdit} title={`${t.csiCode} ${t.tradeName}`} /></Suspense>}
    {tab === 'files' && <FilesTab tradeId={t.tradeId} vid={vid} canEdit={canEdit} bids={allBids} />}
    {tab === 'quals' && <QualsTab tradeId={t.tradeId} vid={vid} canEdit={canEdit} />}
    {tab === 'alternates' && <TradeAlternatesTab tradeId={t.tradeId} vid={vid} canEdit={canEdit} />}
    {tab === 'unitprices' && <TradeUnitPricesTab tradeId={t.tradeId} vid={vid} canEdit={canEdit} />}
    {tab === 'inbox' && <InboxTab data={data} vid={vid} canEdit={canEdit} onChanged={() => { reload(); qc.invalidateQueries({ queryKey: vkey('bid-board', vid) }) }} />}</div></div></>

  // ------------------------------------------------------------------ the spreadsheet grid (RevoGrid): rows, columns and edits
  type SRow = { kind: 'sec'; sec: ScopeLine['section']; name: string; lock: string | null; n: number } | { kind: 'line'; l: ScopeLine } | { kind: 'zchk' } | { kind: 'tot'; which: 'base' | 'adj' | 'total' }
  const rk = (r: SRow) => r.kind === 'sec' ? 's' + r.sec : r.kind === 'line' ? 'l' + r.l.lineId : r.kind === 'tot' ? 't' + r.which : 'zchk'
  const rows: SRow[] = []
  for (const [sec, name, lock] of SECTIONS) { const ls = lines.filter(l => l.section === sec); rows.push({ kind: 'sec', sec, name, lock, n: ls.filter(l => !l.isHeading).length })
    if (collapsed.has(sec)) continue
    for (const l of ls) { if (gaps && l.section !== 'zone' && !l.isHeading && !isGap(l)) continue; rows.push({ kind: 'line', l }) }
    if (sec === 'zone' && ls.length) rows.push({ kind: 'zchk' }) }
  const totRows: SRow[] = [{ kind: 'tot', which: 'base' }, { kind: 'tot', which: 'adj' }, { kind: 'tot', which: 'total' }]
  const numbered = lines.filter(x => !x.isHeading)
  const rowClsOf = (l: ScopeLine) => [l.isHeading ? 'hd3' : '', l.section === 'zone' ? 'zrow' : '', l === remLine ? 'zrem' : '', sel === l.lineId ? 'on' : '', isGap(l) ? 'gaprow' : '',
    l.skipInAdjustments ? 'exrow' : '', q && l.lineText.toLowerCase().includes(q.toLowerCase()) ? 'hit' : '', curHit === l.lineId ? 'cur' : ''].join(' ')
  const chip = (h: H, a?: { kind: string; amount?: number | null; answerText?: string | null } | null) => {
    if (!a || a.kind === 'blank') return h('span', { class: 'ch na' }, 'No answer')
    if (a.kind === 'included') return h('span', { class: 'ch in' }, '✓ Included'); if (a.kind === 'excluded') return h('span', { class: 'ch ex' }, '✕ Excluded')
    if (a.kind === 'price') return h('span', { class: 'ch pr' }, '$ Price')
    if (a.kind === 'amount') return h('span', { class: `ch am ${(a.amount ?? 0) < 0 ? 'neg' : ''}` }, `${(a.amount ?? 0) < 0 ? '−' : '+'}${money(Math.abs(a.amount ?? 0))}`)
    return h('span', { class: 'ch tx' }, `“${a.answerText ?? ''}”`) }
  const DES: Record<string, string> = { included: '✓ Included', excluded: '✕ Excluded', price: '$ Price', text: 'Text' }
  const laneHead = (b: Bid) => (h: H) => { const c = cov(b), low = b.totalAmount != null && b.totalAmount === lowTotal && posted.length > 1
    return h('div', { class: `cap lane2 ${b.isCarried ? 'car' : ''}`, style: { '--lc': letterColour(b.laneNo) } }, [
      h('div', { class: 'c1' }, [h('span', { class: 'lt' }, lane(b.laneNo)), h('span', { class: `rk ${b.rank === 1 ? 'one' : ''}` }, b.rank ? '#' + b.rank : '–'),
        h('span', { class: `po ${b.isPosted ? '' : 'no'}` }, b.isPosted ? 'Posted' : 'Not posted'), low && !b.isCarried ? h('span', { class: 'flag low' }, 'Lowest') : null,
        canEdit ? h('span', { class: 'lane-act' }, [h('button', { title: 'Move column left', onClick: () => moveLane(b, -1) }, '‹'), h('button', { title: 'Move column right', onClick: () => moveLane(b, 1) }, '›'),
          h('button', { title: 'Hide this column', onClick: () => lanes(null, [...allBids.filter(x => x.hiddenInScope).map(x => x.bidId), b.bidId]) }, '×')]) : null]),
      h('div', { class: 'nm', title: `${b.bidderName} · click for bid details`, onClick: (e: MouseEvent) => { e.stopPropagation(); setBidOpen(b.bidId) } }, b.bidderName + (b.addedByUser ? ' *' : '')),
      h('div', { class: 'c3' }, [h('b', {}, b.totalAmount != null ? money(b.totalAmount) : 'No bid'), b.totalAmount != null && lowTotal && b.totalAmount !== lowTotal ? h('small', {}, ' +' + pct((b.totalAmount - lowTotal) / lowTotal * 100)) : null]),
      h('div', { class: 'cov', title: `${c.i} of ${c.n} scope lines addressed` }, [h('i', { class: 'ci', style: { width: `${c.i / c.n * 100}%` } }), h('i', { class: 'cx', style: { width: `${c.x / c.n * 100}%` } }), h('i', { class: 'cm', style: { width: `${c.m / c.n * 100}%` } })]),
      h('div', { class: 'c4' }, [h('span', {}, `${b.state} · ${Math.round(c.i / c.n * 100)}%`),
        h('button', { class: `carry3 ${b.isCarried ? 'on' : ''}`, role: 'switch', 'aria-checked': String(b.isCarried), disabled: !canEdit, onClick: () => pick(b) }, [h('i', {}), b.isCarried ? 'Picked' : 'Pick'])])]) }
  const gcols: GCol<SRow>[] = [
    { prop: 'no', name: '#', size: 54, pin: 'start', drag: r => canEdit && r.kind === 'line' && r.l.section === 'tradeitem', value: r => r.kind === 'line' && !r.l.isHeading ? numbered.indexOf(r.l) + 1 : '',
      show: (r, h) => r.kind === 'line' && r.l.section === 'zone' ? h('i', { class: 'sw', style: { background: (r.l.zoneId != null && zoneOf.get(r.l.zoneId)?.colour) || '#27624d' } }) : r.kind === 'line' && !r.l.isHeading ? String(numbered.indexOf(r.l) + 1) : '', cls: () => 'code' },
    { prop: 'item', name: 'Scope item', size: ITEM_W, pin: 'start', edit: r => r.kind === 'line' && r.l.section === 'tradeitem' && !r.l.sumId,
      value: r => r.kind === 'line' ? r.l.lineText : r.kind === 'sec' ? r.name : r.kind === 'tot' ? ({ base: 'Base bid', adj: 'Scope adjustments', total: 'Total' })[r.which] : (remLine ? 'Zones total' : 'Not in a zone'),
      tip: r => r.kind === 'line' ? [r.l.lineNote, r.l.section === 'baserequirement' ? 'Edit this text on the Base requirements page' : '', r.l.sumId ? 'Allowance line: change or remove it on the Allowances page' : ''].filter(Boolean).join(' · ') || undefined : undefined,
      cls: r => r.kind === 'line' ? `itm ind${Math.min(6, r.l.indent)}` : undefined,
      show: (r, h) => {
        if (r.kind === 'sec') return h('span', { class: 'gname' }, [h('button', { class: `tgl ${collapsed.has(r.sec) ? 'c' : ''}`, onClick: () => setCollapsed(s => { const n = new Set(s); n.has(r.sec) ? n.delete(r.sec) : n.add(r.sec); return n }) }, '▾'),
          h('b', {}, r.name), h('span', { class: 'cnt' }, ` ${r.n}`), r.lock ? h('span', { class: 'lock', title: r.lock }, '🔒') : null,
          r.sec === 'tradeitem' && canEdit ? h('button', { class: 'gadd', onClick: () => setAsk({ title: 'Add rows', label: 'How many blank rows? (1–50)', initial: '10', number: true, save: v => { setAsk(null); const n = Math.max(1, Math.min(50, Math.round(toNum(v) ?? 1))); const last = [...lines.filter(l => l.section === 'tradeitem')].reverse()[0] ?? null; insertLine(last, false, n, 'after') } }) }, '+ Add rows') : null])
        if (r.kind === 'tot') return h('b', {}, ({ base: 'Base bid', adj: 'Scope adjustments', total: 'Total' })[r.which])
        if (r.kind === 'zchk') return [h('b', {}, remLine ? 'Zones total' : 'Not in a zone'), h('small', { class: 'mut' }, remLine ? ' · matches the bid total' : ' · bid total minus the zones')]
        const l = r.l
        return [l.indent > 0 ? h('span', { class: 'indm' }, '↳') : null, l.section !== 'tradeitem' ? h('span', { class: 'lk' }, '🔒') : null,
          h('span', { class: 'tx' }, l.lineText || (l.section === 'tradeitem' ? h('span', { class: 'mut' }, l.isHeading ? 'Header text' : 'Type the scope item') : '')),
          l.section === 'zone' ? h('small', { class: 'zmeta' }, [zoneMeta(l), l === remLine ? ' · remainder of base bid' : ''].filter(Boolean).join(' ')) : null,
          l.sumId ? h('span', { class: 'tag o', title: 'Allowance line (Allowances page)' }, 'Allowance') : null, l.skipInAdjustments ? h('span', { class: 'tag n' }, 'not in adj.') : null, isGap(l) ? h('span', { class: 'gapb' }, 'gap') : null, l.lineNote ? h('span', { class: 'nt', title: l.lineNote }, '✎') : null] } },
    { prop: 'desired', name: 'Desired', size: 120, pin: 'start', edit: r => r.kind === 'line' && r.l.section === 'tradeitem' && !r.l.isHeading,
      value: r => r.kind === 'line' && r.l.desired ? ({ included: 'Included', excluded: 'Excluded', price: 'Price', text: 'Text' })[r.l.desired] ?? '' : '',
      show: (r, h) => r.kind === 'line' && !r.l.isHeading ? (r.l.section === 'zone' ? h('span', { class: 'mut' }, r.l === remLine ? 'calculated' : '$ amount') : r.l.desired ? h('span', { class: `ch ${({ included: 'in', excluded: 'ex', price: 'pr', text: 'tx' })[r.l.desired]}` }, DES[r.l.desired]) : h('span', { class: 'mut' }, '–')) : '' },
    ...bids.map<GCol<SRow>>(b => ({ prop: 'b' + b.bidId, name: b.bidderName, size: LANE_W, head: laneHead(b), headCls: 'lanecol',
      edit: r => r.kind === 'tot' ? r.which === 'base' : r.kind === 'line' && !r.l.isHeading && r.l !== remLine,
      value: r => { if (r.kind === 'tot') return r.which === 'base' ? b.baseAmount : r.which === 'adj' ? b.scopeAdjustments : b.totalAmount
        if (r.kind === 'zchk') return remLine ? b.totalAmount : remainder(b); if (r.kind !== 'line') return ''
        if (r.l === remLine) return remainder(b); return answerText(answers.get(akey(r.l.lineId, b.bidId))) },
      cls: r => { const c = b.isCarried ? 'carcol' : ''; if (r.kind !== 'line') return c + (r.kind === 'tot' || r.kind === 'zchk' ? ' a-r' : '')
        const a = answers.get(akey(r.l.lineId, b.bidId)); const o = others.get(akey(r.l.lineId, b.bidId))
        return `${c} lc ${a?.cellColour ? 'cc-' + (a.cellColour === 'yellow' ? 'yel' : a.cellColour) : ''} ${o ? 'oth' : ''} ${cell?.l === r.l.lineId && cell?.b === b.bidId ? 'cur' : ''}` },
      tip: r => { if (r.kind !== 'line') return undefined; const a = answers.get(akey(r.l.lineId, b.bidId)); const o = others.get(akey(r.l.lineId, b.bidId))
        return [o ? `${o.personName} is ${o.activity}` : '', a?.note, a?.source && a.source !== 'manual' ? `from ${a.source}` : '', a?.changedByName ? `${a.changedByName} · ${date(a.changedAt?.slice(0, 10))}` : ''].filter(Boolean).join(' · ') || undefined },
      show: (r, h) => {
        if (r.kind === 'sec') return ''
        if (r.kind === 'tot') { const v = r.which === 'base' ? b.baseAmount : r.which === 'adj' ? b.scopeAdjustments : b.totalAmount
          return r.which === 'adj' ? h('span', { class: `mn ${b.scopeAdjustments > 0 ? 'warn' : ''}` }, v ? (v > 0 ? '+' : '') + money(v) : '$0') : v == null ? h('span', { class: 'mut' }, '—') : h('span', { class: `mn ${r.which === 'total' ? 'big' : ''}` }, money(v)) }
        if (r.kind === 'zchk') { const v = remLine ? b.totalAmount ?? null : remainder(b); return v == null ? h('span', { class: 'mut' }, '—') : h('span', { class: `mn ${!remLine && zoneSum(b) !== 0 && Math.abs(v) > 0.005 ? 'warn' : ''}` }, money(v)) }
        const l = r.l; if (l.isHeading) return ''
        const a = answers.get(akey(l.lineId, b.bidId)); const o = others.get(akey(l.lineId, b.bidId))
        if (l.section === 'zone') { const amt = l === remLine ? remainder(b) : a?.kind === 'amount' ? a.amount : null; return amt == null ? h('span', { class: 'mut' }, '—') : h('span', { class: `mn ${amt < 0 ? 'neg' : ''} ${l === remLine ? 'calc' : ''}` }, money(amt)) }
        return [chip(h, a), o ? h('span', { class: 'lockline', style: { '--rc': o.colour }, title: `${o.personName} is ${o.activity}` }, o.initials) : null] } })),
  ]
  /** Typing, pasting a block from Excel or dragging the fill handle all land here. */
  async function gridEdit(ch: GridEdit<SRow>[]) {
    const ans: { lineId: number; bidId: number; value: string }[] = []
    for (const c of ch) {
      const r = c.row
      if (r.kind === 'tot' && r.which === 'base' && c.prop.startsWith('b')) { const b = bids.find(x => 'b' + x.bidId === c.prop)!; const n = pasteNum(c.value)
        if (c.value.trim() && n == null) { toast(`"${c.value}" is not an amount`, 'alert'); continue }
        // a typo like 156978321 for a $300k trade still saves, but say so
        let odd: string | null = null
        if (n != null && n > 0) { const others = bids.filter(x => x.bidId !== b.bidId && x.baseAmount != null && x.baseAmount > 0).map(x => x.baseAmount!).sort((a, c) => a - c)
          const ref = others.length ? others[Math.floor(others.length / 2)] : t.budgetValue ?? null
          if (ref && (n > ref * 5 || n < ref / 5)) odd = `Base bid saved, but check it: ${money(n)} is ${n > ref ? Math.round(n / ref) + '× more than' : Math.round(ref / n) + '× less than'} ${others.length ? 'the other bids' : 'the budget'} (${money(ref)})` }
        const x = await run(() => api.patch<TradeReply>(`/bids/${b.bidId}`, { ...(n == null ? { clearBase: true } : { baseAmount: n }), rowRev: b.rowRev }), odd ? undefined : 'Base bid saved'); if (x) { if (odd) toast(odd, 'alert'); setData(d => ({ ...d, trade: x.trade })); syncBoard(x.trade) } continue }
      if (r.kind !== 'line') continue
      if (c.prop === 'item') { if (c.value.trim() !== r.l.lineText) lineSave(r.l, { lineText: c.value.trim() || ' ' }); continue }
      if (c.prop === 'desired') { const d = ['included', 'excluded', 'price', 'text'].find(k => k === c.value.trim().toLowerCase().replace(/^[✓✕$]\s*/, ''))
        if (c.value.trim() && !d) { toast(`Desired scope "${c.value}" should be Included, Excluded, Price or Text`, 'alert'); continue }
        lineSave(r.l, d ? { desired: d } : { clearDesired: true }); continue }
      const b = bids.find(x => 'b' + x.bidId === c.prop); if (!b) continue
      if (r.l.section === 'zone' && c.value.trim() && pasteNum(c.value) == null && !c.value.trim().startsWith('=')) { toast(`Zones take an amount ("${c.value}" isn't one)`, 'alert'); continue }
      const v = r.l.section === 'zone' && pasteNum(c.value) != null ? String(pasteNum(c.value)) : c.value.trim()
      if (v !== answerText(answers.get(akey(r.l.lineId, b.bidId)))) ans.push({ lineId: r.l.lineId, bidId: b.bidId, value: v })
    }
    if (ans.length) { await saveCells(ans); if (ans.length > 1) toast(`Pasted ${ans.length} cells`) }
  }
  async function moveLine(l: ScopeLine, before: ScopeLine) {
    if (l.lineId === before.lineId || before.section !== 'tradeitem') return
    const r = await run(() => api.patch(`/lines/${l.lineId}`, { moveBeforeLineId: before.lineId, rowRev: l.rowRev }), 'Row moved'); if (r) reload()
  }

  const selAnswer = cell ? answers.get(akey(cell.l, cell.b)) : null
  return <>{header}
    <div className="card sl3">{tradebar}{tabsEl}
      <div className="tb fxbar"><div className="fx"><b>fx</b><span>{cell ? `Line ${lines.findIndex(l => l.lineId === cell.l) + 1} · ${lane(bids.find(b => b.bidId === cell.b)?.laneNo ?? 1)}` : 'Pick a cell'}</span>
        <FxInput key={cell ? akey(cell.l, cell.b) + (selAnswer?.rowRev ?? 0) : 'none'} disabled={!cell || !canEdit} initial={answerText(selAnswer)} onSave={v => cell && saveCells([{ lineId: cell.l, bidId: cell.b, value: v }])} /></div>
        {canEdit && <><button className="btn sm ic" title="Indent left" disabled={!selLine || selLine.section !== 'tradeitem'} onClick={() => selLine && lineSave(selLine, { indent: Math.max(0, selLine.indent - 1) })}><Icon name="outdent" size="s" /></button>
          <button className="btn sm ic" title="Indent right" disabled={!selLine || selLine.section !== 'tradeitem'} onClick={() => selLine && lineSave(selLine, { indent: Math.min(6, selLine.indent + 1) })}><Icon name="indent" size="s" /></button>
          <button className="btn sm" onClick={() => insertLine(selLine)} title="Insert a row before the selected row (right-click a row for more)"><Icon name="plus" size="s" />Insert row</button><button className="btn sm" onClick={() => insertLine(selLine, true)}><Icon name="list" size="s" />Header row</button></>}
        <Menu cls="sm" label={`Columns${allBids.length - bids.length ? ` · ${allBids.length - bids.length} hidden` : ''}`} icon="cols" items={[
          ...allBids.map(b => ({ label: `${b.hiddenInScope ? '☐' : '☑'} ${lane(b.laneNo)} · ${b.bidderName}`, onClick: () => lanes(null, allBids.filter(x => x.bidId === b.bidId ? !x.hiddenInScope : x.hiddenInScope).map(x => x.bidId)) })),
          { label: 'View all columns', icon: 'eye', onClick: () => lanes(null, []) }]} />
        <span className="legend"><Chip a={{ kind: 'included' }} /><Chip a={{ kind: 'excluded' }} /><Chip a={{ kind: 'amount', amount: 25000 }} /><Chip a={{ kind: 'text', answerText: 'note' }} /><Chip a={null} /></span></div>
      <DataGrid<SRow> className="scope" rows={rows} rowKey={rk} cols={gcols} top={totRows} readOnly={!canEdit} onEdit={gridEdit} rowSize={38}
        onMenu={(e, r, p) => { if (r?.kind !== 'line') return; const b = p?.startsWith('b') ? bids.find(x => 'b' + x.bidId === p) : undefined; openMenu(e, r.l, b) }}
        onCellClick={(r, p) => { if (r.kind !== 'line') return; setSel(r.l.lineId); const b = p.startsWith('b') ? bids.find(x => 'b' + x.bidId === p) : undefined
          if (b && !r.l.isHeading) { setCell({ l: r.l.lineId, b: b.bidId }); focus('answer', akey(r.l.lineId, b.bidId), 'editing') } }}
        onRowMove={(r, to) => { if (r.kind === 'line' && to?.kind === 'line') moveLine(r.l, to.l) }} focusKey={sel != null ? 'l' + sel : null}
        rowCls={r => r.kind === 'sec' ? `grp sec-${r.sec}` : r.kind === 'zchk' ? 'zchk' : r.kind === 'tot' ? `tot3 ${r.which}` : rowClsOf(r.l)} height="calc(100vh - 330px)" />
      </div>
    {bidOpen != null && allBids.some(x => x.bidId === bidOpen) && <BidDrawer t={t} b={allBids.find(x => x.bidId === bidOpen)!} canEdit={canEdit} onClose={() => setBidOpen(null)}
      onPick={(_, b) => pick(b)} onReply={r => { if (r) { setData(d => ({ ...d, trade: r.trade })); syncBoard(r.trade); qc.invalidateQueries({ queryKey: vkey('price-roll-up', vid) }) } }} />}
    {addBid && <AddBidModal t={t} onClose={() => setAddBid(false)} onDone={r => { setAddBid(false); syncBoard(r.trade); reload() }} />}
    {ctx && <ContextMenu {...ctx} onClose={() => setCtx(null)} />}
    {ask && <AskText title={ask.title} label={ask.label} initial={ask.initial} multiline={ask.multiline} number={ask.number} onClose={() => setAsk(null)} onSave={ask.save} />}
  </>
}

function FxInput({ initial, disabled, onSave }: { initial: string; disabled: boolean; onSave(v: string): void }) {
  const [v, setV] = useState(initial)
  return <input value={v} disabled={disabled} placeholder="Cell value or formula, e.g. =62700+25000" onChange={e => setV(e.target.value)}
    onKeyDown={e => { if (e.key === 'Enter' && v !== initial) onSave(v) }} onBlur={() => { if (v !== initial) onSave(v) }} />
}

// ------------------------------------------------------------------ Internal estimate (qty × unit cost)
interface EstRow { lineText: string; qty: number | null; uomCode: string | null; unitCost: number | null; lineTotal?: number }
function EstimateTab({ tradeId, vid, canEdit }: { tradeId: number; vid: number; canEdit: boolean }) {
  const lk = useLookups(); const { run, busy } = useAction(); const qc = useQueryClient()
  const key = vkey('estimate', vid, tradeId)
  const { data } = useQuery({ queryKey: key, queryFn: () => api.get<{ rows: EstRow[]; total: number }>(`/trades/${tradeId}/estimate`) })
  const [rows, setRows] = useState<EstRow[] | null>(null)
  useEffect(() => { if (data) setRows(data.rows.map(r => ({ ...r }))) }, [data])
  if (!rows) return <Loading what="the estimate" />
  const total = rows.reduce((s, r) => s + Math.round((r.qty ?? 0) * (r.unitCost ?? 0) * 100) / 100, 0)
  const set = (i: number, k: keyof EstRow, v: string) => setRows(rs => rs!.map((r, j) => j !== i ? r : { ...r, [k]: k === 'lineText' || k === 'uomCode' ? v : toNum(v) }))
  return <div>
    <p className="muted">Your own estimate for this trade (quantity × unit cost), to check the bids against. It is not part of the totals.</p>
    <div className="gw"><table className="dg" style={{ width: '100%' }}><thead><tr><th>Item</th><th className="r" style={{ width: 110 }}>Qty</th><th style={{ width: 90 }}>Unit</th><th className="r" style={{ width: 130 }}>Unit cost</th><th className="r" style={{ width: 140 }}>Total</th><th style={{ width: 40 }} /></tr></thead>
      <tbody>{rows.map((r, i) => <tr key={i}>
        <td><input className="inp cellin" style={{ width: '100%' }} disabled={!canEdit} value={r.lineText} onChange={e => set(i, 'lineText', e.target.value)} /></td>
        <td><input className="inp cellin num" style={{ width: '100%' }} disabled={!canEdit} value={r.qty ?? ''} onChange={e => set(i, 'qty', e.target.value)} /></td>
        <td><select className="inp cellin" disabled={!canEdit} value={r.uomCode ?? ''} onChange={e => set(i, 'uomCode', e.target.value)}><option value="" />{lk.data?.units.map(u => <option key={u.uomCode} value={u.uomCode}>{u.uomCode}</option>)}</select></td>
        <td><input className="inp cellin num" style={{ width: '100%' }} disabled={!canEdit} value={r.unitCost ?? ''} onChange={e => set(i, 'unitCost', e.target.value)} /></td>
        <td className="n">{money((r.qty ?? 0) * (r.unitCost ?? 0), 2)}</td>
        <td>{canEdit && <button className="rm" style={{ opacity: 1 }} onClick={() => setRows(rs => rs!.filter((_, j) => j !== i))}><Icon name="x" size="s" /></button>}</td></tr>)}</tbody>
      <tfoot><tr><td>Total</td><td /><td /><td /><td className="n"><b>{money(total, 2)}</b></td><td /></tr></tfoot></table></div>
    {canEdit && <div className="tb" style={{ border: 0 }}><button className="btn sm" onClick={() => setRows(rs => [...rs!, { lineText: '', qty: null, uomCode: null, unitCost: null }])}><Icon name="plus" size="s" />Add row</button><span className="sp" />
      <button className="btn p" disabled={busy} onClick={async () => { const r = await run(() => api.put(`/trades/${tradeId}/estimate`, rows), 'Estimate saved'); if (r) qc.invalidateQueries({ queryKey: key }) }}><Icon name="check" />Save estimate</button></div>}
  </div>
}

// ------------------------------------------------------------------ Bid inbox: online bids from trade partners + proposal reader
function InboxTab({ data, vid, canEdit, onChanged }: { data: ScopeData; vid: number; canEdit: boolean; onChanged(): void }) {
  const { run, busy } = useAction(); const toast = useToast(); const qc = useQueryClient()
  const [review, setReview] = useState<number | null>(null); const [reading, setReading] = useState<number | null>(null)
  const [bidId, setBidId] = useState<number>(data.trade.bids[0]?.bidId ?? 0)
  const running = data.readings.some(r => r.status === 'queued' || r.status === 'running')
  useQuery({ queryKey: vkey('scope-poll', vid, data.tradeId), queryFn: async () => { await qc.invalidateQueries({ queryKey: vkey('scope', vid, data.tradeId) }); return 1 }, refetchInterval: running ? 2000 : false, enabled: running })
  const bidName = (id: number | null) => data.trade.bids.find(b => b.bidId === id)?.bidderName ?? '–'
  return <div className="grid2">
    <div className="card"><div className="ch"><h3>Online bids</h3><span className="muted" style={{ marginLeft: 'auto' }}>Create a link from a bid on the Bid board</span></div>
      {data.invites.length === 0 ? <div className="empty">No bid links sent for this trade yet.</div> :
        <div className="list">{data.invites.map(i => <div className="li" key={i.inviteId}><div className="t"><b>{i.companyName}</b><small>{i.email} · sent {date(i.sentAt?.slice(0, 10))}{i.openedAt ? ` · opened ${date(i.openedAt.slice(0, 10))}` : ''}</small></div>
          <span className={`tag ${i.status === 'submitted' ? 'g' : i.status === 'opened' ? 'b' : i.status === 'withdrawn' ? 'r' : 'n'}`}>{i.status}</span>
          {i.pendingSubmissionId && canEdit && <button className="btn sm p" onClick={() => setReview(i.pendingSubmissionId!)}>Review</button>}
          {canEdit && i.status !== 'withdrawn' && i.status !== 'submitted' && <Menu cls="sm g" label="" icon="dots" items={[
            { label: 'New link (resend)', icon: 'send', onClick: async () => { const r = await run(() => api.post<{ link: string }>(`/invites/${i.inviteId}/resend`)); if (r) { const url = location.origin + r.link; navigator.clipboard.writeText(url).then(() => toast('New link copied'), () => toast(url)) } } },
            { label: 'Withdraw', icon: 'x', danger: true, onClick: async () => { if (await run(() => api.del(`/invites/${i.inviteId}`), 'Link withdrawn')) onChanged() } }]} />}</div>)}</div>}</div>
    <div className="card"><div className="ch"><h3>Proposal reader</h3><span className="muted" style={{ marginLeft: 'auto' }}>Reads a proposal PDF and suggests answers</span></div>
      {canEdit && <div className="cb" style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
        <select className="inp" value={bidId} onChange={e => setBidId(+e.target.value)}>{data.trade.bids.map(b => <option key={b.bidId} value={b.bidId}>{lane(b.laneNo)} · {b.bidderName}</option>)}</select>
        <label className="btn p"><Icon name="upload" />{busy ? 'Uploading…' : 'Upload proposal PDF'}<input type="file" accept=".pdf" hidden onChange={async e => { const f = e.target.files?.[0]; if (!f || !bidId) return
          const fd = new FormData(); fd.append('file', f); const r = await run(() => api.post<{ readingId: number }>(`/bids/${bidId}/proposals`, fd), 'Proposal uploaded · reading it now'); if (r) onChanged(); e.target.value = '' }} /></label></div>}
      {data.readings.length === 0 ? <div className="empty">No proposals read yet.</div> :
        <div className="list">{data.readings.map(r => <div className="li" key={r.readingId}><div className="t"><b>{bidName(r.bidId)}</b><small>{r.finishedAt ? `read ${date(r.finishedAt.slice(0, 10))}` : 'in the queue'}</small></div>
          <span className={`tag ${r.status === 'done' ? 'g' : r.status === 'failed' ? 'r' : 'y'}`}>{r.status === 'done' ? `${r.pending} to review` : r.status}</span>
          <button className="btn sm" onClick={() => setReading(r.readingId)} disabled={r.status === 'queued' || r.status === 'running'}>Open</button></div>)}</div>}</div>
    {review && <SubmissionModal id={review} onClose={() => setReview(null)} onDone={() => { setReview(null); onChanged() }} />}
    {reading && <ReadingModal id={reading} onClose={() => setReading(null)} onDone={() => { setReading(null); onChanged() }} />}
  </div>
}
function SubmissionModal({ id, onClose, onDone }: { id: number; onClose(): void; onDone(): void }) {
  const { run, busy } = useAction()
  const { data } = useQuery({ queryKey: ['submission', id], queryFn: () => api.get<{ baseAmount: number; answers: string; notes: string | null; submittedAt: string; companyName: string }>(`/submissions/${id}`) })
  const answers = data ? JSON.parse(data.answers) as { lineId: number; kind: string; amount: number | null; text: string | null }[] : []
  const decide = async (d: string) => { if (await run(() => api.post(`/submissions/${id}/decide`, { decision: d }), d === 'accepted' ? 'Bid accepted into the lane' : 'Submission rejected')) onDone() }
  return <Modal title={`Online bid · ${data?.companyName ?? ''}`} onClose={onClose} footer={<><button className="btn" disabled={busy} onClick={() => decide('rejected')}>Reject</button><button className="btn p" disabled={busy} onClick={() => decide('accepted')}><Icon name="check" />Accept into lane</button></>}>
    {!data ? <Loading what="the submission" /> : <><div className="kpis" style={{ gridTemplateColumns: '1fr 1fr' }}><div className="kpi"><span className="k">Base bid</span><span className="v">{money(data.baseAmount)}</span></div><div className="kpi"><span className="k">Answers</span><span className="v">{answers.length}</span><span className="d">submitted {date(data.submittedAt.slice(0, 10))}</span></div></div>
      {data.notes && <p><b>Notes:</b> {data.notes}</p>}<p className="muted">Accepting sets the lane's base bid and fills its answers (marked as from the portal). Existing answers on those lines are replaced.</p></>}
  </Modal>
}
interface Suggestion { suggestionId: number; lineId: number | null; lineText: string | null; kind: string; amount: number | null; answerText: string | null; evidence: string | null; pageNo: number | null; confidence: number | null; decision: string; currentKind: string | null }
function ReadingModal({ id, onClose, onDone }: { id: number; onClose(): void; onDone(): void }) {
  const { run, busy } = useAction()
  const { data } = useQuery({ queryKey: ['reading', id], queryFn: () => api.get<{ reading: { status: string; modelName: string; baseAmountFound: number | null; summary: string | null; errorText: string | null; fileName: string; currentBase: number | null; bidderName: string }; suggestions: Suggestion[] }>(`/readings/${id}`) })
  const [choice, setChoice] = useState<Record<number, string>>({}); const [applyBase, setApplyBase] = useState(true)
  useEffect(() => { if (data) setChoice(Object.fromEntries(data.suggestions.map(s => [s.suggestionId, s.decision === 'pending' ? (s.lineId ? 'accepted' : 'rejected') : s.decision]))) }, [data])
  if (!data) return <Modal title="Proposal reader" onClose={onClose}><Loading what="the reading" /></Modal>
  const r = data.reading
  return <Modal wide title={`Proposal · ${r.bidderName} · ${r.fileName}`} onClose={onClose} footer={<><span className="muted" style={{ fontSize: 11.5 }}>{r.modelName}</span><span style={{ flex: 1 }} />
    <button className="btn" onClick={() => setChoice(Object.fromEntries(data.suggestions.map(s => [s.suggestionId, 'rejected'])))}>Reject all</button>
    <button className="btn" onClick={() => setChoice(Object.fromEntries(data.suggestions.map(s => [s.suggestionId, 'accepted'])))}>Accept all</button>
    <button className="btn p" disabled={busy || r.status !== 'done'} onClick={async () => { const x = await run(() => api.post(`/readings/${id}/decide`, { items: Object.entries(choice).map(([k, d]) => ({ suggestionId: +k, decision: d })), applyBase }), 'Decisions saved'); if (x) onDone() }}><Icon name="check" />Apply</button></>}>
    {r.errorText && <p className="tag r">{r.errorText}</p>}{r.summary && <p>{r.summary}</p>}
    {r.baseAmountFound != null && <label className="check"><input type="checkbox" checked={applyBase} onChange={e => setApplyBase(e.target.checked)} /> Use base bid {money(r.baseAmountFound)} (now {money(r.currentBase)})</label>}
    <div className="gw" style={{ maxHeight: 380, marginTop: 8 }}><table className="dg" style={{ width: '100%' }}><thead><tr><th>Scope line</th><th>Suggested</th><th>Evidence (page)</th><th className="r">Confidence</th><th>Decision</th></tr></thead>
      <tbody>{data.suggestions.map(s => <tr key={s.suggestionId}><td style={{ whiteSpace: 'normal', maxWidth: 260 }}>{s.lineText ?? <span className="tag y">No matching line</span>}</td>
        <td><Chip a={{ kind: s.kind, amount: s.amount, answerText: s.answerText }} />{s.currentKind && s.currentKind !== s.kind && <small className="muted"> (now {s.currentKind})</small>}</td>
        <td style={{ whiteSpace: 'normal', maxWidth: 320, fontSize: 11.5 }}>“{s.evidence}” {s.pageNo ? <span className="muted">p.{s.pageNo}</span> : null}</td>
        <td className="n">{s.confidence != null ? pct(s.confidence * 100, 0) : '–'}</td>
        <td><Seg<string> value={choice[s.suggestionId] ?? 'pending'} onChange={d => setChoice({ ...choice, [s.suggestionId]: d })} items={[['accepted', 'Accept'], ['rejected', 'Reject']]} /></td></tr>)}</tbody></table></div>
  </Modal>
}

// ------------------------------------------------------------------ Files on this trade (drawings, proposals, bid forms)
function FilesTab({ tradeId, vid, canEdit, bids }: { tradeId: number; vid: number; canEdit: boolean; bids: Bid[] }) {
  const { run, busy } = useAction(); const qc = useQueryClient(); const key = vkey('trade-files', vid, tradeId)
  const { data } = useQuery({ queryKey: key, queryFn: () => api.get<{ documentId: number; docKind: string; fileName: string; byteSize: number; bidId: number | null; uploadedAt: string; uploadedBy: string | null }[]>(`/trades/${tradeId}/documents`) })
  const [bidId, setBidId] = useState('')
  async function upload(files: FileList | null) {
    if (!files?.length) return; const fd = new FormData(); [...files].forEach(f => fd.append('file', f)); if (bidId) fd.append('bidId', bidId)
    if (await run(() => api.post(`/trades/${tradeId}/documents`, fd), `${files.length} file(s) uploaded`)) qc.invalidateQueries({ queryKey: key })
  }
  const bidName = (id: number | null) => bids.find(b => b.bidId === id)?.bidderName
  return <div>
    {canEdit && <div className="drop" style={{ marginBottom: 12 }} onDragOver={e => e.preventDefault()} onDrop={e => { e.preventDefault(); upload(e.dataTransfer.files) }}>
      <Icon name="upload" size="l" /><div><b>Drop files here</b> or <label className="lnk" style={{ cursor: 'pointer' }}>browse<input type="file" multiple hidden onChange={e => upload(e.target.files)} /></label> — proposals, drawings, bid forms</div>
      <div style={{ marginTop: 8 }}><span className="muted">For trade partner </span><select className="inp" value={bidId} onChange={e => setBidId(e.target.value)}><option value="">Whole trade</option>{bids.map(b => <option key={b.bidId} value={b.bidId}>{lane(b.laneNo)} · {b.bidderName}</option>)}</select></div>
      {busy && <p className="muted">Uploading…</p>}</div>}
    {!data ? <Loading what="files" /> : data.length === 0 ? <div className="empty">No files on this trade yet.</div> :
      <div className="gw"><table className="dg" style={{ width: '100%' }}><thead><tr><th>File</th><th>Trade partner</th><th>Type</th><th className="r">Size</th><th>Uploaded</th><th /></tr></thead>
        <tbody>{data.map(d => <tr key={d.documentId}><td><Icon name="file" size="s" /> <b>{d.fileName}</b></td><td>{bidName(d.bidId) ?? <span className="mut">whole trade</span>}</td><td><span className="tag n">{d.docKind}</span></td>
          <td className="n">{Math.max(1, Math.round(d.byteSize / 1024)).toLocaleString()} KB</td><td>{date(d.uploadedAt.slice(0, 10))}{d.uploadedBy ? ` · ${d.uploadedBy}` : ''}</td>
          <td style={{ textAlign: 'right' }}><button className="btn sm g ic" title="Download" onClick={() => run(() => api.download(`/documents/${d.documentId}/file`, d.fileName))}><Icon name="down" size="s" /></button>
            {canEdit && <button className="btn sm g ic" title="Delete" onClick={async () => { if (await run(() => api.del(`/documents/${d.documentId}`), 'File deleted')) qc.invalidateQueries({ queryKey: key }) }}><Icon name="x" size="s" /></button>}</td></tr>)}</tbody></table></div>}
  </div>
}

// ------------------------------------------------------------------ Qualifications (was: Clarifications): assumptions, exclusions, qualifications
interface Qual { qualificationId: number; tradeId: number | null; kind: string; qualText: string; showToClient: boolean; changedByName: string | null }
function QualsTab({ tradeId, vid, canEdit }: { tradeId: number; vid: number; canEdit: boolean }) {
  const { run } = useAction(); const qc = useQueryClient(); const key = vkey('quals', vid, tradeId)
  const { data } = useQuery({ queryKey: key, queryFn: () => api.get<{ trade: Qual[]; project: Qual[] }>(`/trades/${tradeId}/qualifications`) })
  const sets = useQuery({ queryKey: ['qualification-sets'], queryFn: () => api.get<{ setId: number; setName: string; lines: number }[]>('/qualification-sets') })
  const put = (d?: { trade: Qual[]; project: Qual[] }) => d && qc.setQueryData(key, d)
  const KINDS: [string, string][] = [['assumption', 'Assumption'], ['exclusion', 'Exclusion'], ['qualification', 'Qualification']]
  if (!data) return <Loading what="qualifications" />
  const list = (rows: Qual[], projectWide: boolean) => <div className="gw"><table className="dg" style={{ width: '100%' }}><thead><tr><th style={{ width: 150 }}>Type</th><th>Text</th><th style={{ width: 110 }}>Client sees it</th><th style={{ width: 90 }} /></tr></thead>
    <tbody>{rows.map((q, i) => <tr key={q.qualificationId}>
      <td>{canEdit ? <select className="inp cellin" value={q.kind} onChange={async e => put(await run(() => api.patch(`/qualifications/${q.qualificationId}?tradeId=${tradeId}`, { kind: e.target.value })))}>{KINDS.map(([k, l]) => <option key={k} value={k}>{l}</option>)}</select> : q.kind}</td>
      <td>{canEdit ? <EditCell value={q.qualText} onSave={async v => v.trim() && put(await run(() => api.patch(`/qualifications/${q.qualificationId}?tradeId=${tradeId}`, { qualText: v })))} /> : q.qualText}</td>
      <td><input type="checkbox" disabled={!canEdit} checked={q.showToClient} onChange={async e => put(await run(() => api.patch(`/qualifications/${q.qualificationId}?tradeId=${tradeId}`, { showToClient: e.target.checked })))} /></td>
      <td style={{ textAlign: 'right' }}>{canEdit && <>
        {i > 0 && <button className="rm" style={{ opacity: 1 }} title="Move up" onClick={async () => put(await run(() => api.patch(`/qualifications/${q.qualificationId}?tradeId=${tradeId}`, { moveAfterId: i > 1 ? rows[i - 2].qualificationId : 0 })))}><Icon name="chevl" size="s" /></button>}
        <button className="rm" style={{ opacity: 1 }} title={projectWide ? 'Make this trade only' : 'Make project-wide'} onClick={async () => put(await run(() => api.patch(`/qualifications/${q.qualificationId}?tradeId=${tradeId}`, { projectWide: !projectWide })))}><Icon name="layers" size="s" /></button>
        <button className="rm" style={{ opacity: 1 }} title="Delete" onClick={async () => put(await run(() => api.del(`/qualifications/${q.qualificationId}?tradeId=${tradeId}`), 'Deleted'))}><Icon name="x" size="s" /></button></>}</td></tr>)}
      {!rows.length && <tr><td colSpan={4} className="mut">None yet.</td></tr>}</tbody></table></div>
  return <div className="stack" style={{ display: 'grid', gap: 14 }}>
    <div className="tb" style={{ border: 0, padding: 0 }}><b>This trade</b><span className="sp" />
      {canEdit && <><Menu cls="sm" label="Add from set" icon="copy" items={(sets.data ?? []).map(x => ({ label: `${x.setName} · ${x.lines}`, onClick: async () => put(await run(() => api.post(`/trades/${tradeId}/qualifications/apply-set`, { setId: x.setId }), 'Qualifications added')) }))} />
        {KINDS.map(([k, l]) => <button key={k} className="btn sm" onClick={async () => put(await run(() => api.post(`/trades/${tradeId}/qualifications`, { kind: k, qualText: `New ${l.toLowerCase()}` })))}><Icon name="plus" size="s" />{l}</button>)}</>}</div>
    {list(data.trade, false)}
    <div className="tb" style={{ border: 0, padding: 0 }}><b>Whole project</b><span className="muted" style={{ marginLeft: 8 }}>shown on every trade</span><span className="sp" />
      {canEdit && <button className="btn sm" onClick={async () => put(await run(() => api.post(`/trades/${tradeId}/qualifications`, { kind: 'qualification', qualText: 'New project qualification', projectWide: true })))}><Icon name="plus" size="s" />Project qualification</button>}</div>
    {list(data.project, true)}
  </div>
}

// ------------------------------------------------------------------ Alternates & VE and Unit prices for this trade (was: trade scope → Alternates / Unit Prices)
function TradeAlternatesTab({ tradeId, vid, canEdit }: { tradeId: number; vid: number; canEdit: boolean }) {
  const { run } = useAction(); const qc = useQueryClient(); const nav = useNavigate(); const key = vkey('trade-alternates', vid, tradeId)
  const { data } = useQuery({ queryKey: key, queryFn: () => api.get<TradeAlternate[]>(`/trades/${tradeId}/alternates`) })
  if (!data) return <Loading what="alternates" />
  async function save(optionId: number, body: { amount?: number; clearAmount?: boolean; note?: string }) {
    const d = await run(() => api.put<TradeAlternate[]>(`/trades/${tradeId}/alternates`, [{ optionId, ...body }]), 'Saved')
    if (d) { qc.setQueryData(key, d); qc.invalidateQueries({ queryKey: vkey('alternates', vid) }) }
  }
  const total = (k: string) => data.filter(a => a.kind === k).reduce((s, a) => s + (a.amount ?? 0), 0)
  return <div>
    <div className="tb" style={{ border: 0, padding: '0 0 8px' }}><p className="muted" style={{ margin: 0 }}>What this trade adds (alternates) or saves (VE, enter a negative amount) for each item. The quoted totals are on the Alternates & VE page.</p><span className="sp" />
      <a className="lnk" onClick={() => nav('../alternates', { relative: 'path' })}>Open Alternates & VE</a></div>
    <div className="gw"><table className="dg" style={{ width: '100%' }}><thead><tr><th style={{ width: 80 }}>Code</th><th style={{ width: 100 }}>Type</th><th>Description</th><th style={{ width: 100 }}>Status</th><th className="r" style={{ width: 150 }}>Amount</th><th style={{ width: 280 }}>Comments</th></tr></thead>
      <tbody>{data.map(a => <tr key={a.optionId}>
        <td><span className="code">{a.code}</span></td><td><span className={`tag ${a.kind === 'saving' ? 'o' : 'k'}`}>{a.kind === 'saving' ? 'VE' : 'Alternate'}</span></td>
        <td style={{ whiteSpace: 'normal' }}>{a.description}</td><td><span className={`tag ${ALT_STATUS[a.status]?.[1] ?? 'n'}`}>{ALT_STATUS[a.status]?.[0] ?? a.status}</span></td>
        <td>{canEdit ? <EditCell number value={a.amount} placeholder="–" style={{ textAlign: 'right' }} onSave={v => { const n = toNum(v); save(a.optionId, n == null ? { clearAmount: true } : { amount: n }) }} /> : <span className="num">{money(a.amount, 2)}</span>}</td>
        <td>{canEdit ? <EditCell value={a.note} placeholder="Add a comment" onSave={v => save(a.optionId, { note: v })} /> : a.note}</td></tr>)}
        {!data.length && <tr><td colSpan={6} className="mut">No alternates or VE ideas on this version yet. <a className="lnk" onClick={() => nav('../alternates', { relative: 'path' })}>Add one</a>.</td></tr>}</tbody>
      {data.length > 0 && <tfoot><tr><td colSpan={4}>Alternates {money(total('option'), 2)} · VE {money(total('saving'), 2)}</td><td className="n"><b>{money(total('option') + total('saving'), 2)}</b></td><td /></tr></tfoot>}</table></div>
  </div>
}
function TradeUnitPricesTab({ tradeId, vid, canEdit }: { tradeId: number; vid: number; canEdit: boolean }) {
  const { run } = useAction(); const qc = useQueryClient(); const nav = useNavigate(); const key = vkey('trade-unit-prices', vid, tradeId)
  const { data } = useQuery({ queryKey: key, queryFn: () => api.get<TradeUnitPrice[]>(`/trades/${tradeId}/unit-prices`) })
  if (!data) return <Loading what="unit prices" />
  async function save(unitRateId: number, body: { price?: number; clearPrice?: boolean; note?: string }) {
    const d = await run(() => api.put<TradeUnitPrice[]>(`/trades/${tradeId}/unit-prices`, [{ unitRateId, ...body }]), 'Saved')
    if (d) { qc.setQueryData(key, d); qc.invalidateQueries({ queryKey: vkey('unit-prices', vid) }) }
  }
  return <div>
    <div className="tb" style={{ border: 0, padding: '0 0 8px' }}><p className="muted" style={{ margin: 0 }}>This trade's price per unit for each unit price. Pick which trade's price to carry on the Unit prices page.</p><span className="sp" />
      <a className="lnk" onClick={() => nav('../unit-prices', { relative: 'path' })}>Open Unit prices</a></div>
    <div className="gw"><table className="dg" style={{ width: '100%' }}><thead><tr><th style={{ width: 50 }}>#</th><th>Description</th><th style={{ width: 120 }}>Unit</th><th className="r" style={{ width: 150 }}>Price</th><th style={{ width: 280 }}>Note</th><th style={{ width: 100 }}>Status</th></tr></thead>
      <tbody>{data.map(u => <tr key={u.unitRateId}>
        <td className="code">{u.index}</td><td style={{ whiteSpace: 'normal' }}>{u.description}</td><td>{u.unit ?? <span className="mut">–</span>}</td>
        <td>{canEdit ? <EditCell number value={u.price} placeholder="–" style={{ textAlign: 'right' }} onSave={v => { const n = toNum(v); save(u.unitRateId, n == null ? { clearPrice: true } : { price: n }) }} /> : <span className="num">{money(u.price, 2)}</span>}</td>
        <td>{canEdit ? <EditCell value={u.note} placeholder="Add a note" onSave={v => save(u.unitRateId, { note: v })} /> : u.note}</td>
        <td><span className={`tag ${u.isSelected ? 'g' : 'b'}`}>{u.isSelected ? 'Selected' : 'Proposed'}</span></td></tr>)}
        {!data.length && <tr><td colSpan={6} className="mut">No unit prices on this version yet. <a className="lnk" onClick={() => nav('../unit-prices', { relative: 'path' })}>Add one</a>.</td></tr>}</tbody></table></div>
  </div>
}
