// Price roll-up (was: Executive summary). How the contract total builds up, by trade and by zone.
// Rates and custom indirect items are edited here: the browser recalculates instantly, the server confirms.
import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { calcSummary } from '@gridline/calc'
import { api } from '../../api/client'
import type { BidBoardData, ProjectDetail, RollUpData, SummaryLineDto, Version } from '../../api/types'
import { PageHead } from '../../components/Shell'
import { Icon } from '../../components/Icon'
import { EditCell, ExportMenu, Failed, Loading, Seg, toNum, useAction } from '../../components/ui'
import { money, pct } from '../../app/format'
import { vkey } from '../../app/hooks'
import { IndirectModal } from './BidBoard'
import { EstimatorModal } from './Estimator'

export default function PriceRollUp({ project, version }: { project: ProjectDetail; version: Version }) {
  const vid = version.versionId; const qc = useQueryClient(); const nav = useNavigate(); const { run } = useAction()
  const key = vkey('price-roll-up', vid)
  const { data, error, isLoading } = useQuery({ queryKey: key, queryFn: () => api.get<RollUpData>(`/versions/${vid}/price-roll-up`) })
  const board = useQuery({ queryKey: vkey('bid-board', vid), queryFn: () => api.get<BidBoardData>(`/versions/${vid}/bid-board`) })
  const [cols, setCols] = useState<'total' | 'sf'>('total'); const [heat, setHeat] = useState(true); const [showTrades, setShowTrades] = useState(true)
  const [draft, setDraft] = useState<Record<string, number>>({})      // rate being typed → live numbers before the save returns
  const [addInd, setAddInd] = useState(false); const [estOpen, setEstOpen] = useState(false)
  const canEdit = project.access !== 'viewer' && version.status !== 'locked'

  const lines: SummaryLineDto[] = useMemo(() => {
    if (!data) return []
    if (!Object.keys(draft).length) return data.lines
    const input = { ...data.input, zoneShares: data.input.zoneShares ?? undefined, markups: data.input.markups.map(m => draft[m.code] != null ? { ...m, ratePct: draft[m.code], fixedAmount: null } : m) }
    return calcSummary(input).map(l => ({ ...l, budgetAmount: data.lines.find(x => x.name === l.name)?.budgetAmount ?? null, editable: l.kind === 'markup' || l.kind === 'tax' || l.kind === 'custom' }))
  }, [data, draft])
  if (isLoading) return <Loading what="the price roll-up" />
  if (error || !data) return <Failed error={error} />

  const zones = data.zones, area = data.grossArea
  const get = (n: string) => lines.find(l => l.name === n)?.amount ?? 0
  const CT = get('Contract total'), tt = get('Trades total'), gr = get('Project overheads')
  const indirects = lines.filter(l => l.kind === 'markup' && !['fee', 'liability', 'bond', 'cover'].includes(l.code ?? '')).reduce((s, l) => s + l.amount, 0) + get('Precon services') + lines.filter(l => l.kind === 'custom').reduce((s, l) => s + l.amount, 0)
  const insFee = lines.filter(l => ['fee', 'liability', 'bond', 'cover'].includes(l.code ?? '')).reduce((s, l) => s + l.amount, 0)
  const zmax = Object.fromEntries(zones.map(z => [z.zoneCode, Math.max(1, ...(board.data?.trades ?? []).map(t => (t.carriedValue ?? t.lowestValue ?? 0) * (t.zoneShares[z.zoneCode] ?? 0)))]))

  async function saveRate(code: string, v: string) {
    const r = toNum(v); if (r == null) return
    setDraft(d => ({ ...d, [code]: r }))
    const ok = await run(() => api.put(`/versions/${vid}/markups`, [{ code, ratePct: r, clearFixed: true }]), 'Rate saved · roll-up recalculated')
    await qc.invalidateQueries({ queryKey: key }); setDraft(d => { const n = { ...d }; delete n[code]; return n })
    if (ok) qc.invalidateQueries({ queryKey: vkey('bid-board', vid) })
  }
  const indirectOf = (name: string) => data.indirect.find(i => i.itemName === name)
  async function saveIndirect(name: string, body: object) {
    const it = indirectOf(name); if (!it) return
    if (await run(() => api.patch(`/indirect/${it.indirectId}`, body), 'Indirect item saved')) qc.invalidateQueries({ queryKey: key })
  }

  const zoneCells = (_amt: number, z: (typeof zones)[number], isTrade: boolean, zv?: number) => {
    const v = zv ?? 0
    const style = heat && isTrade && v ? { background: `rgba(106,50,212,${(Math.sqrt(v / zmax[z.zoneCode]) * 0.45 + 0.04).toFixed(2)})`, color: v / zmax[z.zoneCode] > 0.55 ? '#fff' : undefined } : undefined
    return [<td key={z.zoneId + 't'} className="n zt" style={{ ['--zc' as string]: z.colour ?? '#6a32d4', ...style }}>{v ? money(v) : <span className="mut">–</span>}</td>,
      cols === 'sf' ? <td key={z.zoneId + 's'} className="n mut">{v && z.floorArea ? '$' + (v / z.floorArea).toFixed(2) : '–'}</td> : null]
  }

  return <>
    <PageHead eyebrow={`${project.projectName} · v${version.versionNo}`} title="Price roll-up" was="Executive summary" desc="How the contract total builds up, by trade and by zone. Rates and custom items can be changed here."
      actions={<><button className="btn" onClick={() => setEstOpen(true)} title="Try different numbers without saving anything" data-was="Estimator (calculation test)"><Icon name="calc" />Estimator</button>
        <button className="btn" onClick={() => nav('../profile?tab=fees', { relative: 'path' })} data-was="Fee exclusions"><Icon name="sliders" />Fee carve-outs{data.carveOuts.applyFeeExcludes && data.carveOuts.feeExcludes.length ? ` · ${data.carveOuts.feeExcludes.length}` : ''}</button>
        {canEdit && <button className="btn" onClick={() => setAddInd(true)}><Icon name="plus" />Add indirect item</button>}
        <ExportMenu items={[['Price roll-up (Excel)', `/exports/price-roll-up.xlsx?versionId=${vid}`], ['Price roll-up (PDF)', `/exports/price-roll-up.pdf?versionId=${vid}`]]} /></>} />
    <div className="card" style={{ marginBottom: 14 }}><div className="build">
      {([['Trades', tt], ['Project overheads', gr], ['Indirects', indirects], ['Insurance & fee', insFee]] as [string, number][]).map(([k, v], i) =>
        <div key={k} className={`s ${i ? 'add' : ''}`}><span className="k">{k}</span><span className="v">{money(v)}</span><span className="muted num" style={{ fontSize: 11 }}>{CT ? pct(v / CT * 100) : '–'}</span></div>)}
      <div className="s fin"><span className="k">Contract total</span><span className="v">{money(CT)}</span><span className="num" style={{ fontSize: 11 }}>{area ? `$${(CT / area).toFixed(2)}/SF` : ''}</span></div></div></div>
    <div className="card"><div className="tb"><span className="muted" style={{ fontWeight: 800, fontSize: 11, letterSpacing: '.08em', textTransform: 'uppercase' }}>Zones</span>
      <Seg<'total' | 'sf'> value={cols} onChange={setCols} items={[['total', 'Total'], ['sf', '+ $/SF']]} /><Seg value={heat ? 'h' : 'p'} onChange={v => setHeat(v === 'h')} items={[['h', 'Heat map'], ['p', 'Plain']]} />
      <Seg value={showTrades ? 'y' : 'n'} onChange={v => setShowTrades(v === 'y')} items={[['y', 'With trades'], ['n', 'Build-up only']]} />
      <div className="sp" /><span className="muted" style={{ fontSize: 12 }}>Gross area {area.toLocaleString()} SF · {Object.keys(draft).length ? 'recalculating…' : 'confirmed by the server'}</span></div>
      <div className="gw"><table className="dg" style={{ minWidth: '100%' }}>
        <thead><tr><th style={{ minWidth: 280 }}>Line</th><th className="r" style={{ width: 96 }}>Rate</th><th className="r">Amount</th><th className="r">% contract</th><th className="r">$/SF</th><th className="r">Budget</th><th className="r">Δ budget</th>
          {zones.map(z => [<th key={z.zoneId} className="r zt" style={{ ['--zc' as string]: z.colour ?? '#6a32d4' }}>{z.zoneName}<div className="muted" style={{ fontSize: 10.5 }}>{z.floorArea ? z.floorArea.toLocaleString() + ' SF' : 'site'}</div></th>, cols === 'sf' ? <th key={z.zoneId + 's'} className="r">$/SF</th> : null])}</tr></thead>
        <tbody>
          {showTrades && (board.data?.trades ?? []).map(t => { const v = t.carriedValue ?? t.lowestValue ?? 0; const pk = t.bids.find(b => b.isCarried)
            return <tr key={t.tradeId}><td><span className="code">{t.csiCode}</span> <a className="lnk" onClick={() => nav(`../scope-match?trade=${t.tradeId}`, { relative: 'path' })}>{t.tradeName}</a>
              <span className="muted" style={{ marginLeft: 6, fontSize: 11.5 }}>{pk ? pk.bidderName : t.plugAmount ? 'plug' : <span className="tag y">Nothing picked</span>}</span></td>
              <td /><td className="n">{money(v)}</td><td className="n mut">{CT ? pct(v / CT * 100, 2) : '–'}</td><td className="n mut">{area ? (v / area).toFixed(2) : '–'}</td>
              <td className="n mut">{money(t.budgetValue)}</td><td className="n" style={{ color: t.budgetValue != null && v - t.budgetValue > 0 ? 'var(--bad,#d1344b)' : 'var(--good)' }}>{t.budgetValue != null ? money(v - t.budgetValue) : ''}</td>
              {zones.map(z => zoneCells(v, z, true, v * (t.zoneShares[z.zoneCode] ?? 0)))}</tr> })}
          {lines.map((l, i) => { const calc = l.kind === 'calculated' || l.kind === 'trades'; const ind = l.kind === 'custom' ? indirectOf(l.name) : undefined
            return <tr key={i} className={calc ? 'sub' : ''} style={calc ? { fontWeight: 700, background: 'var(--primary-50, #f3effd)' } : undefined}>
              <td>{l.kind === 'custom' && <span className="tag b" style={{ marginRight: 6 }}>Custom</span>}
                {ind && canEdit ? <span style={{ display: 'inline-flex', gap: 6, alignItems: 'center', width: '75%' }}><EditCell value={l.name} onSave={v => v.trim() && saveIndirect(l.name, { itemName: v })} />
                  <label className="check" style={{ fontSize: 11, whiteSpace: 'nowrap' }} title="Fee is not charged on this item"><input type="checkbox" checked={ind.outsideFee} onChange={e => saveIndirect(l.name, { outsideFee: e.target.checked })} />outside fee</label>
                  <button className="rm" style={{ opacity: 1 }} title="Delete" onClick={() => run(() => api.del(`/indirect/${ind.indirectId}`).then(() => qc.invalidateQueries({ queryKey: key })), 'Indirect item deleted')}><Icon name="x" size="s" /></button></span>
                  : calc ? <b style={{ fontSize: 11, letterSpacing: '.08em', textTransform: 'uppercase' }}>{l.name}</b> : l.name}
                {l.kind === 'cost' && <a className="lnk" style={{ marginLeft: 8, fontSize: 11.5 }} onClick={() => nav(`../${l.code === 'genreq' ? 'overheads' : l.code === 'genconds' ? 'crew-plan' : 'precon-services'}`, { relative: 'path' })}>open</a>}</td>
              <td className="n">{l.isOff ? <span className="tag n" title="Switched off on Project profile → Rates & indirects (counts as $0)">off</span> : (l.kind === 'markup' || l.kind === 'tax') && l.code ? (data.zoneReserves?.includes(l.code) ? <span title="Weighted from each zone's reserve % (Project profile → Zones)">{pct(l.ratePct, 3)} <small className="mut">by zone</small></span> : canEdit ? <EditCell number value={draft[l.code] ?? l.ratePct ?? 0} onSave={v => saveRate(l.code!, v)} style={{ textAlign: 'right', width: 80 }} /> : pct(l.ratePct, 3)) : ''}</td>
              <td className="n">{ind && canEdit ? <EditCell number value={l.amount} onSave={v => { const n = toNum(v); if (n != null) saveIndirect(l.name, { amount: n }) }} style={{ textAlign: 'right' }} /> : <b className="num">{money(l.amount)}</b>}</td>
              <td className="n mut">{l.pctOfContract != null ? pct(l.pctOfContract, 2) : ''}</td><td className="n mut">{l.perArea != null ? l.perArea.toFixed(2) : ''}</td>
              <td className="n mut">{money(l.budgetAmount ?? null)}</td><td className="n">{l.budgetAmount != null ? money(l.amount - l.budgetAmount) : ''}</td>
              {zones.map(z => zoneCells(l.amount, z, false, l.zones[z.zoneCode]))}</tr> })}
        </tbody></table></div></div>
    {estOpen && <EstimatorModal data={data} onClose={() => setEstOpen(false)} />}
    {addInd && <IndirectModal versionId={vid} onClose={() => setAddInd(false)} onDone={() => { setAddInd(false); qc.invalidateQueries({ queryKey: key }) }} />}
  </>
}
