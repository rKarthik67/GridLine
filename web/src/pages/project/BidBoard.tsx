// Bid board (was: Bid leveling / Trade summary). Every trade with all its bids side by side.
// Click a bid tile to pick it; the picked value, footer totals and roll-up update at once (browser calc),
// then the server confirms and everyone else on the project sees it live.
import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { calcTrade, type BidIn } from '@gridline/calc'
import { api } from '../../api/client'
import type { Bid, BidBoardData, ProjectDetail, RollUpData, Trade, TradeReply, Version, Zone } from '../../api/types'
import { PageHead } from '../../components/Shell'
import { Icon } from '../../components/Icon'
import { Drawer, EditCell, ExportMenu, Failed, Loading, Menu, Modal, Toggle, letterColour, toNum, useAction } from '../../components/ui'
import { date, lane, money, pct } from '../../app/format'
import { useLookups, useOthersFocus, vkey } from '../../app/hooks'
import { focus } from '../../live/hub'
import { useToast } from '../../app/toast'

type Filter = 'all' | 'unres' | 'notlow' | 'single' | 'plug'
const STATE_LABEL: Record<string, string> = { invited: 'Invited', bidding: 'Bidding', posted: 'Posted', late: 'Late', incomplete: 'Incomplete', notplansspecs: 'Not plans & specs', undecided: 'Undecided', disqualified: 'Disqualified', declined: 'Declined' }

/** Browser-side recalculation of one trade (bid totals already include scope adjustments). */
function recalc(t: Trade, pickBidId: number | null | undefined): Trade {
  const bids = t.bids.map(b => ({ ...b, isCarried: pickBidId === undefined ? b.isCarried : b.bidId === pickBidId }))
  const o = calcTrade({ tradeId: t.tradeId, adjustment: t.adjustment, plugAmount: t.plugAmount, budgetValue: t.budgetValue,
    bids: bids.map<BidIn>(b => ({ bidId: b.bidId, isPosted: b.isPosted, isCarried: b.isCarried, state: b.state, baseAmount: b.totalAmount })) })
  return { ...t, bids: bids.map(b => ({ ...b, rank: o.bids.find(x => x.bidId === b.bidId)?.rank ?? null })), carriedValue: o.carriedValue, lowestValue: o.lowestValue, budgetDelta: o.budgetDelta }
}

export default function BidBoard({ project, version }: { project: ProjectDetail; version: Version }) {
  const vid = version.versionId; const qc = useQueryClient(); const nav = useNavigate(); const toast = useToast(); const { run } = useAction()
  const key = vkey('bid-board', vid)
  const { data, error, isLoading } = useQuery({ queryKey: key, queryFn: () => api.get<BidBoardData>(`/versions/${vid}/bid-board`) })
  const roll = useQuery({ queryKey: vkey('price-roll-up', vid), queryFn: () => api.get<RollUpData>(`/versions/${vid}/price-roll-up`) })
  const [q, setQ] = useState(''); const [filter, setFilter] = useState<Filter>('all')
  const [showBudget, setShowBudget] = useState(true); const [onlyUnres, setOnlyUnres] = useState(false); const [buyout, setBuyout] = useState(false)
  const [adj, setAdj] = useState<Trade | null>(null); const [drawer, setDrawer] = useState<{ t: Trade; b: Bid } | null>(null)
  const [addBidFor, setAddBidFor] = useState<Trade | null>(null); const [addTrade, setAddTrade] = useState(false); const [splitFor, setSplitFor] = useState<Trade | null>(null)
  const [renameT, setRenameT] = useState<Trade | null>(null); const [copyBudget, setCopyBudget] = useState(false); const [importOpen, setImportOpen] = useState(false); const [addIndirect, setAddIndirect] = useState(false)
  const editing = useOthersFocus('trade')
  const canEdit = project.access !== 'viewer' && version.status !== 'locked'

  const setTrade = (t: Trade) => qc.setQueryData<BidBoardData>(key, d => d && { ...d, trades: d.trades.map(x => x.tradeId === t.tradeId ? t : x) })
  const after = (r?: TradeReply) => { if (r) { setTrade(r.trade); qc.invalidateQueries({ queryKey: vkey('price-roll-up', vid) }) } return r }

  async function pick(t: Trade, b: Bid) {
    if (!canEdit) return toast('This version is read-only for you', 'lock')
    if (b.state === 'disqualified' || b.state === 'declined') return toast('Disqualified or declined bids can’t be picked', 'alert')
    if (!b.isPosted) return toast(`${b.bidderName} hasn't posted a price yet`, 'alert')
    const next = b.isCarried ? null : b.bidId
    const before = t; setTrade(recalc(t, next))                    // instant
    const r = after(await run(() => api.put<TradeReply>(`/trades/${t.tradeId}/pick`, { bidId: next })))
    if (!r) setTrade(before)
    else toast(next ? `Picked ${b.bidderName} for ${t.tradeName} · everyone sees it now` : `Cleared the pick on ${t.tradeName}`)
  }
  const patchTrade = async (t: Trade, body: object, ok?: string) => after(await run(() => api.patch<TradeReply>(`/trades/${t.tradeId}`, { ...body, rowRev: t.rowRev }), ok))

  const trades = data?.trades ?? []
  const zones = data?.zones ?? []
  const rows = useMemo(() => trades.filter(t => {
    const posted = t.bids.filter(b => b.isPosted).length, picked = t.bids.find(b => b.isCarried)
    if (q && !`${t.csiCode} ${t.tradeName} ${t.bids.map(b => b.bidderName).join(' ')}`.toLowerCase().includes(q.toLowerCase())) return false
    if ((onlyUnres || filter === 'unres') && t.carriedValue != null) return false
    if (filter === 'notlow' && !(picked && picked.totalAmount !== t.lowestValue)) return false
    if (filter === 'single' && posted >= 2) return false
    if (filter === 'plug' && !(t.plugAmount || t.clientNote || t.teamNote)) return false
    return true
  }), [trades, q, filter, onlyUnres])
  if (isLoading) return <Loading what="the bid board" />
  if (error || !data) return <Failed error={error} />

  const maxB = Math.max(1, ...trades.map(t => t.bids.length)) + (canEdit ? 1 : 0)
  const tt = trades.reduce((s, t) => s + (t.carriedValue ?? t.lowestValue ?? 0), 0), lw = trades.reduce((s, t) => s + (t.lowestValue ?? 0), 0)
  const bd = trades.reduce((s, t) => s + (t.budgetValue ?? 0), 0), picked = trades.filter(t => t.bids.some(b => b.isCarried)).length
  const unresolved = trades.filter(t => t.carriedValue == null).length
  const area = data.version.grossArea
  const metric: string[] = [...(showBudget ? ['Budget', 'Δ budget'] : []), 'Lowest', 'Δ to lowest', ...(buyout ? ['Buyout %', 'Buyout savings'] : [])]
  const W0 = 290, W1 = 176
  const zoneShare = (z: Zone) => tt ? trades.reduce((s, t) => s + (t.carriedValue ?? t.lowestValue ?? 0) * (t.zoneShares[z.zoneCode] ?? 0), 0) / tt : 0
  const lines = roll.data?.lines.filter(l => l.kind !== 'trades') ?? []

  return <>
    <PageHead eyebrow={`${project.projectName} · v${version.versionNo}${version.status === 'locked' ? ' · locked' : ''}`} title="Bid board" was="Bid leveling / Trade summary"
      desc="All bidders side by side for every trade. Click a bid to pick it; click a name for bid details."
      actions={<><span className="mut" style={{ fontSize: 11.5, marginRight: 4 }}><Icon name="sync" size="s" /> Saved · live</span>
        {canEdit && <button className="btn" onClick={() => setAddTrade(true)}><Icon name="plus" />Add trade</button>}
        {canEdit && <button className="btn" onClick={() => setImportOpen(true)}><Icon name="upload" />Import trades</button>}
        <ExportMenu items={[['Bid board (Excel)', `/exports/bid-board.xlsx?versionId=${vid}`], ['Bid quotes (PDF)', `/exports/bid-quotes.pdf?versionId=${vid}`]]} /></>} />
    <div className="card blsheet">
      <div className="tb"><label className="sbox"><Icon name="search" size="s" /><input placeholder="Search trades or trade partners" value={q} onChange={e => setQ(e.target.value)} style={{ width: 220 }} /></label>
        <div className="pills">{([['all', 'All'], ['unres', 'Unresolved'], ['notlow', 'Not lowest'], ['single', 'Single bid'], ['plug', 'With plug / notes']] as [Filter, string][]).map(([k, l]) =>
          <button key={k} aria-pressed={filter === k} onClick={() => setFilter(k)}>{l}</button>)}</div></div>
      <div className="tb sub2"><span className="stats"><b>{trades.length}</b> trades <i className="d car" /><b>{picked}</b> picked <i className="d un" /><b>{unresolved}</b> unresolved</span><span className="sep" />
        <Toggle label="Budget" on={showBudget} onChange={setShowBudget} /><Toggle label="Only unresolved" on={onlyUnres} onChange={setOnlyUnres} /><Toggle label="Anticipated buyouts" on={buyout} onChange={setBuyout} />
        <span className="sp" />{showBudget && <><span className="mut" style={{ fontSize: 12 }}>Budget source</span>
          <Menu cls="sm" label={data.baselineName ?? 'Trade budgets'} items={[{ label: 'Trade budgets (as entered)', onClick: () => run(() => api.patch(`/versions/${vid}`, { baselineName: '' }).then(() => qc.invalidateQueries({ queryKey: key })), 'Budget source: trade budgets') },
            ...data.budgets.map(b => ({ label: `${b.budgetName} · ${b.values} codes`, onClick: () => run(() => api.patch(`/versions/${vid}`, { baselineName: b.budgetName }).then(() => qc.invalidateQueries({ queryKey: key })), `Budget source: ${b.budgetName}`) }))]} />
          {canEdit && <button className="btn sm" onClick={() => setCopyBudget(true)}><Icon name="copy" size="s" />Copy budget…</button>}</>}</div>
      <div className="gw bsw"><table className="dg bs"><thead>
        <tr className="g1"><th className="st" style={{ left: 0, minWidth: W0, maxWidth: W0 }} rowSpan={2}>Trade</th><th className="st edge" style={{ left: W0, minWidth: W1, maxWidth: W1 }} rowSpan={2}>Picked value</th>
          <th colSpan={metric.length} className="grp-h m">Position</th>
          <th colSpan={maxB} className="grp-h b">Bids · side by side <span className="muted">click a bid to pick it · click a name for details</span></th>
          {zones.map(z => <th key={z.zoneId} colSpan={2} className="grp-h z" style={{ ['--zc' as string]: z.colour ?? '#6a32d4' }}>{z.zoneName}<small>{z.floorArea ? z.floorArea.toLocaleString() + ' SF' : 'lump sum'}</small></th>)}
          <th rowSpan={2} className="grp-h p">Plug & notes</th></tr>
        <tr className="g2">{metric.map(m => <th key={m} className="r">{m}</th>)}{Array.from({ length: maxB }, (_, i) => <th key={i} className="bhd">Bid {i + 1}</th>)}
          {zones.map(z => [<th key={z.zoneId + 't'} className="r zt" style={{ ['--zc' as string]: z.colour ?? '#6a32d4' }}>Total</th>, <th key={z.zoneId + 's'} className="r">$/SF</th>])}</tr>
      </thead><tbody>
        {rows.map(t => {
          const posted = t.bids.filter(b => b.isPosted).length; const pk = t.bids.find(b => b.isCarried); const cv = t.carriedValue
          const basis = cv ?? t.lowestValue ?? 0; const other = editing.get(String(t.tradeId))
          const d2l = pk && pk.totalAmount != null && t.lowestValue != null ? pk.totalAmount - t.lowestValue : null
          const psf = area > 0 && cv != null ? cv / area : null; const bm = t.benchmark
          const outOfRange = bm && psf != null && (psf < bm.lowPerSf * 0.9 || psf > bm.highPerSf * 1.1)
          return <tr key={t.tradeId} className={cv == null ? 'unres' : ''} style={other ? { outline: `2px solid ${other.colour}`, outlineOffset: -2 } : undefined}>
            <td className="st" style={{ left: 0, minWidth: W0, maxWidth: W0 }}><div className="trc"><span className="code">{t.csiCode}</span>
              <a className="lnk" href={`../scope-match?trade=${t.tradeId}`} onClick={e => { e.preventDefault(); nav(`../scope-match?trade=${t.tradeId}`, { relative: 'path' }) }} title="Open in scope match">{t.tradeName}</a>
              <span className="bcount">{posted}/{t.bids.length}</span>
              {other && <span className="tag" style={{ background: other.colour, color: '#fff' }} title={`${other.personName} is ${other.activity}`}>{other.initials}</span>}
              {canEdit && <Menu cls="sm g ic rm" label="" icon="dots" items={[{ label: 'Rename / change CSI code', icon: 'note', onClick: () => setRenameT(t) }, { label: t.zonesFromScope ? 'Zone split (from Scope match zone amounts)' : 'Zone split', icon: 'layers', onClick: () => setSplitFor(t) },
                { label: 'Add bidder', icon: 'plus', onClick: () => setAddBidFor(t) }, { label: 'Open in scope match', icon: 'list', onClick: () => nav(`../scope-match?trade=${t.tradeId}`, { relative: 'path' }) },
                { label: 'Delete trade', icon: 'x', danger: true, onClick: () => run(() => api.del(`/trades/${t.tradeId}`).then(() => qc.invalidateQueries({ predicate: x => JSON.stringify(x.queryKey).includes(`"v${vid}"`) })), `Deleted ${t.tradeName}`) }]} />}
            </div></td>
            <td className="st edge" style={{ left: W0, minWidth: W1, maxWidth: W1 }}><div className="pv">
              <button className={`adjb ${t.plugAmount ? 'plug' : ''} ${t.clientNote ? 'cl' : ''} ${t.teamNote ? 'in' : ''}`} onClick={() => canEdit && setAdj(t)} title="Adjust: plug, adjustment, client note, team note">
                <Icon name={t.plugAmount ? 'bolt' : (t.clientNote || t.teamNote) ? 'note' : 'sliders'} size="s" /></button>
              <div className="pvt"><b>{cv == null ? <span className="tag r">Unresolved</span> : money(cv)}</b>
                <small>{pk ? `${pk.bidderName}${t.adjustment ? ` · adj ${t.adjustment > 0 ? '+' : ''}${money(t.adjustment)}` : ''}` : t.plugAmount ? 'Plug value' : 'No bid picked'}</small>
                {bm && psf != null && <small title={`Past ${bm.projects} similar projects: $${bm.lowPerSf}–$${bm.highPerSf}/SF (median $${bm.medianPerSf})`} style={{ color: outOfRange ? 'var(--bad, #d1344b)' : 'var(--muted)' }}>
                  ${psf.toFixed(2)}/SF {outOfRange ? '· outside past range' : `· range $${bm.lowPerSf}–${bm.highPerSf}`}</small>}</div></div></td>
            {showBudget && <><td className="n mut">{money(t.budgetValue)}</td><td className="n" style={{ color: (basis - (t.budgetValue ?? basis)) > 0 ? 'var(--bad,#d1344b)' : 'var(--good)' }}>{t.budgetValue == null ? '–' : `${basis - t.budgetValue > 0 ? '+' : ''}${money(basis - t.budgetValue)}`}</td></>}
            <td className="n">{money(t.lowestValue)}</td><td className="n">{d2l == null ? '–' : d2l === 0 ? <span className="tag g">Lowest</span> : <span style={{ color: 'var(--warn)' }}>+{money(d2l)}</span>}</td>
            {buyout && <><td className="n">{pct(t.buyoutPct)}</td><td className="n" style={{ color: 'var(--good)' }}>{t.budgetValue != null && cv != null ? money(t.budgetValue - cv) : '–'}</td></>}
            {Array.from({ length: maxB }, (_, bi) => <td key={bi} className="bc">{t.bids[bi] ? <Tile t={t} b={t.bids[bi]} posted={posted} onPick={() => pick(t, t.bids[bi])} onOpen={() => setDrawer({ t, b: t.bids[bi] })} />
              : bi === t.bids.length && canEdit ? <div className="btile add" onClick={() => setAddBidFor(t)}><Icon name="plus" size="s" />Invite</div> : null}</td>)}
            {zones.map(z => { const s = t.zoneShares[z.zoneCode] ?? 0; const v = basis * s
              return [<td key={z.zoneId + 't'} className="n zt" style={{ ['--zc' as string]: z.colour ?? '#6a32d4' }}>{s ? money(Math.round(v)) : <span className="mut">–</span>}</td>,
                <td key={z.zoneId + 's'} className="n mut">{s && z.floorArea ? (v / z.floorArea).toFixed(2) : '–'}</td>] })}
            <td className="plc">{t.plugAmount || t.clientNote || t.teamNote ? <div className="plg">{t.plugAmount ? <span className="pa1"><Icon name="bolt" size="s" />{money(t.plugAmount)}</span> : null}
              {t.clientNote && <span className="pa2" title="Shown to the client">{t.clientNote}</span>}{t.teamNote && <span className="pa3" title="Team only"><Icon name="note" size="s" />{t.teamNote}</span>}</div> : <span className="mut">–</span>}</td>
          </tr>
        })}
        <tr className="sec"><td className="st" style={{ left: 0 }}><Icon name="layers" size="s" />Indirect costs & roll-up</td><td className="st edge" style={{ left: W0 }} />
          <td colSpan={metric.length + maxB + zones.length * 2 + 1}>{canEdit && <button className="btn sm g" onClick={() => setAddIndirect(true)}><Icon name="plus" size="s" />Add indirect item</button>}</td></tr>
        {lines.map((l, i) => { const isCalc = l.kind === 'calculated'; const b = l.budgetAmount
          return <tr key={i} className={`${isCalc ? 'calc' : 'ind'} ${l.name.startsWith('Contract total') ? 'ct' : ''}`}>
            <td className="st" style={{ left: 0 }}><div className="trc">{l.kind === 'custom' && <span className="tag b">Custom</span>}<span>{l.name}</span>{l.ratePct != null && <span className="mut" style={{ marginLeft: 'auto', fontSize: 11 }}>{pct(l.ratePct, 3)}</span>}</div></td>
            <td className="st edge n" style={{ left: W0 }}><b>{money(l.amount)}</b></td>
            {showBudget && <><td className="n mut">{money(b)}</td><td className="n">{b != null ? money(l.amount - b) : ''}</td></>}<td /><td />{buyout && <><td /><td /></>}
            <td colSpan={maxB} className="bc mut" style={{ fontSize: 11 }}>{isCalc ? 'Calculated' : l.kind === 'cost' ? (l.code === 'genreq' ? 'From Project overheads' : 'From Precon services') : ''}</td>
            {zones.map(z => { const v = l.zones[z.zoneCode] ?? l.amount * zoneShare(z)
              return [<td key={z.zoneId + 't'} className="n zt" style={{ ['--zc' as string]: z.colour ?? '#6a32d4' }}>{money(Math.round(v))}</td>, <td key={z.zoneId + 's'} className="n mut">{z.floorArea ? (v / z.floorArea).toFixed(2) : '–'}</td>] })}<td /></tr>
        })}
      </tbody></table></div>
      <div className="mbar">{([['Budget', money(bd)], ['Δ budget', (tt - bd > 0 ? '+' : '') + money(tt - bd), tt - bd > 0 ? 'bad' : 'good'], ['Trades total', money(tt), 'acc'], ['Δ to lowest', '+' + money(tt - lw), 'warn'], ['Lowest', money(lw)],
        ['Contract total', money(roll.data?.lines.find(l => l.name === 'Contract total')?.amount)], ['$/SF', area ? '$' + (tt / area).toFixed(2) : '–']] as [string, string, string?][]).map(([k, v, c]) =>
        <div key={k} className={`mi ${c ?? ''}`}><span>{k}</span><b>{v}</b></div>)}</div>
    </div>

    {adj && <AdjustModal t={adj} onClose={() => setAdj(null)} onSave={async body => { if (await patchTrade(adj, body, 'Trade adjustment saved')) setAdj(null) }} />}
    {drawer && <BidDrawer t={trades.find(x => x.tradeId === drawer.t.tradeId) ?? drawer.t} b={(trades.find(x => x.tradeId === drawer.t.tradeId)?.bids.find(x => x.bidId === drawer.b.bidId)) ?? drawer.b}
      canEdit={canEdit} onClose={() => setDrawer(null)} onPick={pick} onReply={r => after(r)} />}
    {addBidFor && <AddBidModal t={addBidFor} onClose={() => setAddBidFor(null)} onDone={r => { after(r); setAddBidFor(null) }} />}
    {addTrade && <AddTradeModal versionId={vid} onClose={() => setAddTrade(false)} onDone={() => { setAddTrade(false); qc.invalidateQueries({ queryKey: key }); qc.invalidateQueries({ queryKey: vkey('price-roll-up', vid) }) }} />}
    {renameT && <RenameModal t={renameT} onClose={() => setRenameT(null)} onSave={async b => { if (await patchTrade(renameT, b, 'Trade updated')) setRenameT(null) }} />}
    {splitFor && <SplitModal t={splitFor} zones={zones} onClose={() => setSplitFor(null)} onDone={r => { after(r); setSplitFor(null) }} />}
    {copyBudget && <CopyBudgetModal project={project} version={version} onClose={() => setCopyBudget(false)} onDone={() => { setCopyBudget(false); qc.invalidateQueries({ queryKey: key }) }} />}
    {importOpen && <ImportTradesModal versionId={vid} onClose={() => setImportOpen(false)} onDone={() => { setImportOpen(false); qc.invalidateQueries({ predicate: x => JSON.stringify(x.queryKey).includes(`"v${vid}"`) }) }} />}
    {addIndirect && <IndirectModal versionId={vid} onClose={() => setAddIndirect(false)} onDone={() => { setAddIndirect(false); qc.invalidateQueries({ queryKey: vkey('price-roll-up', vid) }) }} />}
  </>

  function Tile({ t, b, posted, onPick, onOpen }: { t: Trade; b: Bid; posted: number; onPick(): void; onOpen(): void }) {
    const isPosted = b.isPosted && b.totalAmount != null, low = isPosted && b.totalAmount === t.lowestValue && posted > 1
    const over = isPosted && t.lowestValue ? ((b.totalAmount! - t.lowestValue) / t.lowestValue) * 100 : 0
    const st = b.state, stTag = st !== 'posted' && st !== 'invited' ? <span className={`bst s-${st}`}>{st === 'disqualified' ? 'DQ' : STATE_LABEL[st]}</span> : null
    return <div className={`btile ${b.isCarried ? 'pk' : ''} ${low ? 'lw' : ''} ${!isPosted ? 'np' : ''} ${st === 'disqualified' ? 'dq' : ''}`} style={{ ['--lc' as string]: letterColour(b.laneNo) }}
      onClick={e => { if ((e.target as HTMLElement).closest('.nm')) return; if (isPosted) onPick() }} title={isPosted ? (b.isCarried ? 'Picked · click to clear' : 'Click to pick') : 'Not posted'}
      onMouseEnter={() => focus('trade', String(t.tradeId), 'viewing')}>
      <div className="r1"><span className="lt">{lane(b.laneNo)}</span><span className="nm" onClick={onOpen}>{b.bidderName}{b.addedByUser && <sup title="Added by a user, not from the directory">*</sup>}</span>
        {isPosted && posted > 1 && b.rank && <span className="rk" title="Rank by price">#{b.rank}</span>}</div>
      <div className="r2">{isPosted ? <span className="am">{money(b.totalAmount)}</span> : <span className="bst s-invited">{STATE_LABEL[st] ?? 'Not posted'}</span>}
        {b.isCarried ? <span className="pkb"><Icon name="check" size="s" />Picked</span> : low ? <span className="lwb">Lowest</span> : stTag ?? (isPosted && over ? <span className="ov">+{pct(over)}</span> : null)}</div>
      {isPosted && posted > 1 && <i className="heat" style={{ width: `${Math.max(4, Math.min(100, 100 - over * 6))}%` }} />}</div>
  }
}

function AdjustModal({ t, onClose, onSave }: { t: Trade; onClose(): void; onSave(b: object): void }) {
  const [plug, setPlug] = useState(t.plugAmount?.toString() ?? ''); const [a, setA] = useState(t.adjustment ? String(t.adjustment) : '')
  const [cl, setCl] = useState(t.clientNote ?? ''); const [tn, setTn] = useState(t.teamNote ?? '')
  return <Modal title={`${t.csiCode} · ${t.tradeName}`} onClose={onClose} footer={<><button className="btn" onClick={onClose}>Cancel</button><button className="btn p" onClick={() => {
    const p = toNum(plug), ad = toNum(a)
    onSave({ ...(p == null ? { clearPlug: true } : { plugAmount: p }), adjustment: ad ?? 0, clientNote: cl, teamNote: tn })
  }}><Icon name="check" />Save</button></>}>
    <div className="adjm"><h4><Icon name="bolt" size="s" />Manual cost adjustment</h4>
      <div className="fgrid"><div className="field"><label>Plug amount</label><input className="inp num" value={plug} onChange={e => setPlug(e.target.value)} placeholder="0" /><small>Used as the trade value when no bid is picked.</small></div>
        <div className="field"><label>Adjustment to picked bid</label><input className="inp num" value={a} onChange={e => setA(e.target.value)} placeholder="0" /><small>Added to the picked bid's total.</small></div></div>
      <h4><Icon name="company" size="s" />Client note</h4><div className="field"><input className="inp" value={cl} onChange={e => setCl(e.target.value)} placeholder="Consolidated joint firestopping for better pricing" /></div>
      <h4><Icon name="note" size="s" />Team note</h4><div className="field"><textarea className="inp" rows={3} value={tn} onChange={e => setTn(e.target.value)} placeholder="Notes for your team…" /><small>Only your team sees this.</small></div></div>
  </Modal>
}

export function BidDrawer({ t, b, canEdit, onClose, onPick, onReply }: { t: Trade; b: Bid; canEdit: boolean; onClose(): void; onPick(t: Trade, b: Bid): void; onReply(r?: TradeReply): void }) {
  const { run } = useAction(); const toast = useToast(); const nav = useNavigate()
  const [link, setLink] = useState<string | null>(null); const [email, setEmail] = useState(b.email ?? '')
  const posted = t.bids.filter(x => x.isPosted && x.totalAmount != null).length
  const patch = async (body: object, ok: string) => onReply(await run(() => api.patch<TradeReply>(`/bids/${b.bidId}`, { ...body, rowRev: b.rowRev }), ok))
  return <Drawer onClose={onClose} head={<><span className="lt" style={{ background: letterColour(b.laneNo), width: 28, height: 28, borderRadius: 8, color: '#fff', display: 'grid', placeItems: 'center', fontWeight: 800 }}>{lane(b.laneNo)}</span>
    <div><h3>{b.bidderName}</h3><div className="muted" style={{ fontSize: 12 }}>{t.csiCode} · {t.tradeName}{b.rank && posted > 1 ? ` · Rank ${b.rank} of ${posted}` : ''}</div></div></>}>
    <div className="kpis" style={{ gridTemplateColumns: 'repeat(3,1fr)' }}>
      <div className="kpi"><span className="k">Total</span><span className="v">{b.totalAmount != null ? money(b.totalAmount) : 'Not posted'}</span><span className="d">base {money(b.baseAmount)} · scope {money(b.scopeAdjustments)}</span></div>
      <div className="kpi"><span className="k">vs lowest</span><span className="v">{b.totalAmount != null && t.lowestValue ? (b.totalAmount === t.lowestValue ? 'Lowest' : '+' + pct((b.totalAmount - t.lowestValue) / t.lowestValue * 100)) : '–'}</span></div>
      <div className="kpi"><span className="k">Status</span><span className="v" style={{ fontSize: 16 }}>{b.isCarried ? 'Picked' : STATE_LABEL[b.state]}</span><span className="d">{b.baseSource ? `price from ${b.baseSource}` : ''}</span></div></div>
    <div className="fgrid" style={{ marginTop: 12 }}>
      <div className="field"><label>Base bid</label><EditCell number value={b.baseAmount} disabled={!canEdit} placeholder="Enter the base bid" onSave={v => { const n = toNum(v); patch(n == null ? { clearBase: true } : { baseAmount: n }, 'Base bid saved') }} /></div>
      <div className="field"><label>Status</label><select className="inp" disabled={!canEdit} value={b.state} onChange={e => patch({ state: e.target.value }, 'Status updated')}>
        {Object.entries(STATE_LABEL).map(([k, l]) => <option key={k} value={k}>{l}</option>)}</select></div>
      <div className="field"><label>Trade partner name</label><EditCell value={b.bidderName} disabled={!canEdit} onSave={v => patch({ bidderName: v }, 'Name saved')} /></div>
      <div className="field"><label>Contact</label><input className="inp" readOnly value={[b.contact, b.email].filter(Boolean).join(' · ') || '–'} /></div></div>
    {canEdit && <div className="card pad" style={{ marginTop: 14 }}><b>Online bid link</b><p className="muted" style={{ margin: '4px 0 8px' }}>The trade partner opens it without signing in, answers the scope and enters a base bid. You review it in Scope match → Bid inbox.</p>
      <div style={{ display: 'flex', gap: 8 }}><input className="inp" style={{ flex: 1 }} placeholder="email@tradepartner.com" value={email} onChange={e => setEmail(e.target.value)} />
        <button className="btn p" onClick={async () => { const r = await run(() => api.post<{ link: string }>(`/trades/${t.tradeId}/invites`, { bidId: b.bidId, email }), 'Bid link created'); if (r) setLink(location.origin + r.link) }}><Icon name="send" />Create link</button></div>
      {link && <div style={{ display: 'flex', gap: 8, marginTop: 8 }}><input className="inp" readOnly value={link} style={{ flex: 1 }} onFocus={e => e.target.select()} />
        <button className="btn" onClick={() => navigator.clipboard.writeText(link).then(() => toast('Link copied'), () => toast('Select the link and copy it', 'alert'))}><Icon name="copy" />Copy</button>
        <a className="btn" href={`mailto:${email}?subject=${encodeURIComponent('Bid invitation · ' + t.tradeName)}&body=${encodeURIComponent('Please submit your bid here: ' + link)}`}><Icon name="send" />Email</a></div>}</div>}
    <div className="acts" style={{ marginTop: 14, display: 'flex', gap: 8, flexWrap: 'wrap' }}>
      {canEdit && b.isPosted && <button className="btn p" onClick={() => onPick(t, b)}><Icon name="check" />{b.isCarried ? 'Clear pick' : 'Pick this bid'}</button>}
      <button className="btn" onClick={() => { onClose(); nav(`../scope-match?trade=${t.tradeId}`, { relative: 'path' }) }}><Icon name="list" />Open in scope match</button>
      {canEdit && <button className="btn" style={{ color: 'var(--bad,#d1344b)' }} onClick={async () => { const r = await run(() => api.del<TradeReply>(`/bids/${b.bidId}`), 'Bid removed'); if (r) { onReply(r); onClose() } }}><Icon name="x" />Remove bid</button>}</div>
  </Drawer>
}

export function AddBidModal({ t, onClose, onDone }: { t: Trade; onClose(): void; onDone(r: TradeReply): void }) {
  const { run, busy } = useAction(); const [name, setName] = useState(''); const [email, setEmail] = useState(''); const [base, setBase] = useState(''); const [partnerId, setPartnerId] = useState<number | null>(null)
  const found = useQuery({ queryKey: ['partners', name], queryFn: () => api.get<{ partnerId: number; companyName: string; email: string | null; contact: string | null }[]>(`/partners?search=${encodeURIComponent(name)}`), enabled: name.length >= 2 && partnerId == null })
  return <Modal title={`Add a bidder · ${t.tradeName}`} onClose={onClose} footer={<><button className="btn" onClick={onClose}>Cancel</button>
    <button className="btn p" disabled={busy || !name.trim()} onClick={async () => { const r = await run(() => api.post<TradeReply>(`/trades/${t.tradeId}/bids`, { bidderName: name, partnerId, email: email || null, baseAmount: toNum(base) }), `${name} added`); if (r) onDone(r) }}><Icon name="plus" />Add bidder</button></>}>
    <div className="fgrid">
      <div className="field" style={{ gridColumn: '1 / -1' }}><label>Trade partner</label><input className="inp" value={name} onChange={e => { setName(e.target.value); setPartnerId(null) }} placeholder="Start typing a company name" autoFocus />
        {found.data && found.data.length > 0 && partnerId == null && <div className="list" style={{ border: '1px solid var(--line)', borderRadius: 8, marginTop: 4, maxHeight: 180, overflow: 'auto' }}>
          {found.data.map(p => <button key={p.partnerId} className="li" style={{ all: 'unset', display: 'block', padding: '6px 10px', cursor: 'pointer' }} onClick={() => { setName(p.companyName); setPartnerId(p.partnerId); setEmail(p.email ?? '') }}>
            <b>{p.companyName}</b> <span className="muted">{p.contact ?? ''} {p.email ?? ''}</span></button>)}</div>}
        <small>{partnerId ? 'From the trade partner directory' : name ? 'New companies are added to the directory (marked *)' : ''}</small></div>
      <div className="field"><label>Email</label><input className="inp" value={email} onChange={e => setEmail(e.target.value)} placeholder="estimating@company.com" /></div>
      <div className="field"><label>Base bid (optional)</label><input className="inp num" value={base} onChange={e => setBase(e.target.value)} placeholder="$0" /></div></div>
  </Modal>
}

function AddTradeModal({ versionId, onClose, onDone }: { versionId: number; onClose(): void; onDone(): void }) {
  const lk = useLookups(); const { run, busy } = useAction()
  const [code, setCode] = useState(''); const [name, setName] = useState(''); const [budget, setBudget] = useState(''); const [kit, setKit] = useState('')
  const opts = (lk.data?.csiCodes ?? []).filter(c => !code || `${c.csiCode} ${c.codeTitle}`.toLowerCase().includes(code.toLowerCase())).slice(0, 8)
  return <Modal title="Add a trade" onClose={onClose} footer={<><button className="btn" onClick={onClose}>Cancel</button>
    <button className="btn p" disabled={busy || !code.trim()} onClick={async () => { if (await run(() => api.post(`/versions/${versionId}/trades`, { csiCode: code, tradeName: name || null, budgetValue: toNum(budget), kitId: kit ? +kit : null }), 'Trade added')) onDone() }}><Icon name="plus" />Add trade</button></>}>
    <div className="fgrid">
      <div className="field"><label>CSI code</label><input className="inp" value={code} onChange={e => setCode(e.target.value)} placeholder="08 44 00" autoFocus />
        {code && opts.length > 0 && <div className="list" style={{ border: '1px solid var(--line)', borderRadius: 8, marginTop: 4 }}>{opts.map(c =>
          <button key={c.codeId} style={{ all: 'unset', display: 'block', padding: '5px 10px', cursor: 'pointer' }} onClick={() => { setCode(c.csiCode); if (!name) setName(c.codeTitle) }}><span className="code">{c.csiCode}</span> {c.codeTitle}</button>)}</div>}</div>
      <div className="field"><label>Trade name</label><input className="inp" value={name} onChange={e => setName(e.target.value)} placeholder="From the CSI code if empty" /></div>
      <div className="field"><label>Budget</label><input className="inp num" value={budget} onChange={e => setBudget(e.target.value)} placeholder="$0" /></div>
      <div className="field"><label>Scope kit</label><select className="inp" value={kit} onChange={e => setKit(e.target.value)}><option value="">None</option>{lk.data?.scopeKits.map(k => <option key={k.kitId} value={k.kitId}>{k.kitName} · {k.lines} lines</option>)}</select></div></div>
    <p className="muted" style={{ marginTop: 10 }}>The new trade gets the version's base requirements and one line per zone in Scope match.</p>
  </Modal>
}

function RenameModal({ t, onClose, onSave }: { t: Trade; onClose(): void; onSave(b: object): void }) {
  const [code, setCode] = useState(t.csiCode); const [name, setName] = useState(t.tradeName); const [due, setDue] = useState(t.bidDueOn ?? '')
  return <Modal title="Trade details" onClose={onClose} footer={<><button className="btn" onClick={onClose}>Cancel</button><button className="btn p" onClick={() => onSave({ csiCode: code !== t.csiCode ? code : undefined, tradeName: name, bidDueOn: due || undefined })}><Icon name="check" />Save</button></>}>
    <div className="fgrid"><div className="field"><label>CSI code</label><input className="inp" value={code} onChange={e => setCode(e.target.value)} /></div>
      <div className="field"><label>Trade name</label><input className="inp" value={name} onChange={e => setName(e.target.value)} /></div>
      <div className="field"><label>Bid due</label><input className="inp" type="date" value={due} onChange={e => setDue(e.target.value)} /></div></div>
    <p className="muted">Last changed {date(t.changedAt?.slice(0, 10))}{t.changedByName ? ` by ${t.changedByName}` : ''}.</p>
  </Modal>
}

function SplitModal({ t, zones, onClose, onDone }: { t: Trade; zones: Zone[]; onClose(): void; onDone(r: TradeReply): void }) {
  const { run, busy } = useAction()
  const [v, setV] = useState<Record<string, string>>(() => Object.fromEntries(zones.map(z => [z.zoneCode, t.zoneShares[z.zoneCode] != null ? String(+(t.zoneShares[z.zoneCode] * 100).toFixed(2)) : ''])))
  const total = Object.values(v).reduce((s, x) => s + (toNum(x) ?? 0), 0)
  if (!zones.length) return <Modal title="Zone split" onClose={onClose}><p>This version has no zones. Add zones in Project profile → Zones.</p></Modal>
  if (t.zonesFromScope) return <Modal title={`Zone split · ${t.tradeName}`} onClose={onClose}><p>This trade's split comes from the zone amounts entered in Scope match for the {t.bids.some(b => b.isCarried) ? 'picked' : 'lowest'} bid{zones.some(z => z.isRemainder) ? ' (the remainder zone gets what is left of the bid total)' : ''}:</p>
    <div className="fgrid">{zones.map(z => <div className="field" key={z.zoneId}><label><i className="sw" style={{ background: z.colour ?? '#6a32d4' }} /> {z.zoneName}</label><div className="inp ro num">{((t.zoneShares[z.zoneCode] ?? 0) * 100).toFixed(2)}%</div></div>)}</div>
    <p className="muted">To split it by hand, clear the zone amounts in Scope match{zones.some(z => z.isRemainder) ? ' and remove the remainder zone in Project profile → Zones' : ''}.</p></Modal>
  return <Modal title={`Zone split · ${t.tradeName}`} onClose={onClose} footer={<><span className={total === 100 ? 'tag g' : 'tag r'}>{total.toFixed(2)}%</span><span style={{ flex: 1 }} />
    <button className="btn" onClick={() => setV(Object.fromEntries(zones.map(z => [z.zoneCode, z.defaultShare != null ? String(+(z.defaultShare * 100).toFixed(2)) : ''])))}>Use default split</button>
    <button className="btn p" disabled={busy || Math.abs(total - 100) > 0.05} onClick={async () => { const r = await run(() => api.put<TradeReply>(`/trades/${t.tradeId}/zones`, { shares: Object.fromEntries(Object.entries(v).filter(([, x]) => toNum(x)).map(([k, x]) => [k, toNum(x)! / 100])) }), 'Zone split saved'); if (r) onDone(r) }}><Icon name="check" />Save</button></>}>
    <div className="fgrid">{zones.map(z => <div className="field" key={z.zoneId}><label><i className="sw" style={{ background: z.colour ?? '#6a32d4' }} /> {z.zoneName} ({z.zoneCode})</label>
      <input className="inp num" value={v[z.zoneCode]} onChange={e => setV({ ...v, [z.zoneCode]: e.target.value })} placeholder="0" /></div>)}</div>
  </Modal>
}

function CopyBudgetModal({ project, version, onClose, onDone }: { project: ProjectDetail; version: Version; onClose(): void; onDone(): void }) {
  const { run, busy } = useAction(); const others = project.versions.filter(v => v.versionId !== version.versionId); const [from, setFrom] = useState(others[0]?.versionId ?? 0)
  return <Modal title="Copy trade budgets" onClose={onClose} footer={<><button className="btn" onClick={onClose}>Cancel</button><button className="btn p" disabled={busy || !from} onClick={async () => { const r = await run(() => api.post<{ trades: number }>(`/versions/${version.versionId}/budgets/copy`, { fromVersionId: from })); if (r) { onDone() } }}><Icon name="copy" />Copy</button></>}>
    {others.length ? <div className="field"><label>Copy budgets from</label><select className="inp" value={from} onChange={e => setFrom(+e.target.value)}>{others.map(v => <option key={v.versionId} value={v.versionId}>v{v.versionNo} · {v.versionLabel}</option>)}</select>
      <small>Trades are matched by CSI code. Trades without a match keep their budget.</small></div> : <p>This project has only one version.</p>}
  </Modal>
}

interface Check { token: string; ok: boolean; fileName: string; counts: Record<string, number>; errors: number; issues: { sheet: string; row: number | null; severity: string; message: string }[]; trades: { code: string; title: string; budget: number | null; bidders: number; match: string }[] }
function ImportTradesModal({ versionId, onClose, onDone }: { versionId: number; onClose(): void; onDone(): void }) {
  const { run, busy } = useAction(); const [check, setCheck] = useState<Check | null>(null)
  return <Modal wide title="Import trades and bidders from Excel" onClose={onClose} footer={<><button className="btn" onClick={onClose}>Cancel</button>{check && <button className="btn g" onClick={() => setCheck(null)}>Upload another</button>}
    <button className="btn p" disabled={busy || !check?.ok} onClick={async () => { const r = await run(() => api.post<{ tradesAdded: number; tradesUpdated: number; bidsAdded: number }>(`/imports/${check!.token}/create`)); if (r) { onDone() } }}><Icon name="check" />Import</button></>}>
    {!check ? <div className="drop"><Icon name="upload" size="l" /><div style={{ margin: '8px 0 4px' }}>Uses the <b>Trades</b> and <b>Bidders</b> sheets of the Gridline template. New CSI codes are added; existing trades get their budget and any new bidders.</div>
      <label className="btn p" style={{ marginTop: 10 }}><Icon name="folder" />Choose file<input type="file" accept=".xlsx" hidden onChange={async e => { const f = e.target.files?.[0]; if (!f) return; const fd = new FormData(); fd.append('file', f); const r = await run(() => api.post<Check>(`/versions/${versionId}/imports/check`, fd)); if (r) setCheck(r) }} /></label>
      <a className="btn" style={{ marginLeft: 8 }} href="/api/template.xlsx">Download template</a>{busy && <p className="muted">Checking…</p>}</div>
    : <><p><b>{check.fileName}</b> · {check.counts.Trades} trades · {check.counts.Bidders} bidders · {check.errors ? <span className="tag r">{check.errors} errors</span> : <span className="tag g">ready</span>}</p>
      {check.issues.length > 0 && <div className="gw" style={{ maxHeight: 160 }}><table className="dg" style={{ width: '100%' }}><tbody>{check.issues.map((i, k) => <tr key={k}><td>{i.sheet}</td><td className="n">{i.row ?? '–'}</td><td><span className={`tag ${i.severity === 'error' ? 'r' : 'y'}`}>{i.severity}</span></td><td style={{ whiteSpace: 'normal' }}>{i.message}</td></tr>)}</tbody></table></div>}
      <div className="gw" style={{ maxHeight: 240, marginTop: 8 }}><table className="dg" style={{ width: '100%' }}><thead><tr><th>CSI</th><th>Trade</th><th className="r">Budget</th><th className="r">Bidders</th><th>Change</th></tr></thead>
        <tbody>{check.trades.map(t => <tr key={t.code}><td className="code">{t.code}</td><td>{t.title}</td><td className="n">{money(t.budget)}</td><td className="n">{t.bidders}</td><td><span className={`tag ${t.match === 'New' ? 'o' : 'b'}`}>{t.match}</span></td></tr>)}</tbody></table></div></>}
  </Modal>
}

export function IndirectModal({ versionId, onClose, onDone }: { versionId: number; onClose(): void; onDone(): void }) {
  const { run, busy } = useAction(); const [name, setName] = useState(''); const [amt, setAmt] = useState(''); const [outside, setOutside] = useState(false)
  return <Modal title="Add indirect item" onClose={onClose} footer={<><button className="btn" onClick={onClose}>Cancel</button><button className="btn p" disabled={busy || !name.trim()} onClick={async () => { if (await run(() => api.post(`/versions/${versionId}/indirect`, { itemName: name, amount: toNum(amt) ?? 0, outsideFee: outside }), 'Indirect item added')) onDone() }}><Icon name="plus" />Add</button></>}>
    <div className="fgrid"><div className="field"><label>Name</label><input className="inp" value={name} onChange={e => setName(e.target.value)} placeholder="Owner IT & AV coordination" autoFocus /></div>
      <div className="field"><label>Amount</label><input className="inp num" value={amt} onChange={e => setAmt(e.target.value)} placeholder="$0" /></div></div>
    <label className="check" style={{ marginTop: 10 }}><input type="checkbox" checked={outside} onChange={e => setOutside(e.target.checked)} /> Outside the fee (fee is not charged on this item)</label>
  </Modal>
}

