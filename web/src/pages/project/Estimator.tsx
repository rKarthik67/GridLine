// Estimator (was: the "Estimator" / Calculation test sandbox on the Pricing review summary).
// Change any input and see how the contract total responds — nothing here is saved.
// It runs the same roll-up rules as the server (@gridline/calc), starting from this version's real numbers.
import { useMemo, useState } from 'react'
import { calcSummary, type SummaryIn } from '@gridline/calc'
import type { RollUpData } from '../../api/types'
import { Modal, toNum } from '../../components/ui'
import { Icon } from '../../components/Icon'
import { money } from '../../app/format'

interface Inputs {
  trades: string; genreq: string; precon: string; genconds: string; buyoutPct: string
  rates: Record<string, string>; fixed: Record<string, string | null>
  indirect: { name: string; amount: string; inFee: boolean }[]
  apply: boolean; feeExcludes: string[]; excludeGenreqFromDirect: boolean; reservePrecon: string[]; reserveGenconds: string[]
  shared: boolean; buyoutBackPct: string; reserveSpendPct: string; reserveBackPct: string
}
const FEE_PARTS: [string, string][] = [['genreq', 'Project overheads'], ['precon', 'Precon services'], ['genconds', 'General conditions'], ['reserves', 'Reserves'], ['bond', 'P & P bond'], ['cover', 'Works cover']]
const RESERVES: [string, string][] = [['reserveproject', 'Project reserve'], ['reservedesign', 'Design reserve'], ['reserveescalation', 'Escalation reserve']]
const n = (s: string | null | undefined) => toNum(s ?? '') ?? 0

function seedFrom(d: RollUpData): Inputs {
  const i = d.input, co = d.carveOuts
  return {
    trades: String(i.tradesTotal), genreq: String(i.genreqTotal), precon: String(i.preconTotal), genconds: String(i.gencondsTotal ?? 0), buyoutPct: '0',
    rates: Object.fromEntries(i.markups.map(m => [m.code, String(m.ratePct)])),
    fixed: Object.fromEntries(i.markups.map(m => [m.code, m.fixedAmount != null ? String(m.fixedAmount) : null])),
    indirect: i.indirect.map(x => ({ name: x.name, amount: String(x.amount), inFee: !x.outsideFee })),
    apply: co.applyFeeExcludes, feeExcludes: [...co.feeExcludes], excludeGenreqFromDirect: co.excludeGenreqFromDirect, reservePrecon: [...co.reservePrecon], reserveGenconds: [...(co.reserveGenconds ?? [])],
    shared: false, buyoutBackPct: '0', reserveSpendPct: '100', reserveBackPct: '0',
  }
}
function toSummaryIn(d: RollUpData, x: Inputs): SummaryIn {
  return {
    tradesTotal: n(x.trades), genreqTotal: n(x.genreq), preconTotal: n(x.precon), gencondsTotal: n(x.genconds), grossArea: d.input.grossArea, zoneShares: d.input.zoneShares ?? undefined,
    markups: d.input.markups.map(m => ({ ...m, ratePct: n(x.rates[m.code]), fixedAmount: x.fixed[m.code] != null && x.fixed[m.code] !== '' ? n(x.fixed[m.code]) : null })),
    indirect: x.indirect.map(i => ({ name: i.name, amount: n(i.amount), outsideFee: !i.inFee })),
    options: { feeExcludes: x.apply ? x.feeExcludes : [], excludeGenreqFromDirect: x.excludeGenreqFromDirect, reservePrecon: x.reservePrecon, reserveGenconds: x.reserveGenconds },
  }
}

export function EstimatorModal({ data, onClose }: { data: RollUpData; onClose(): void }) {
  const [x, setX] = useState<Inputs>(() => seedFrom(data))
  const set = <K extends keyof Inputs>(k: K, v: Inputs[K]) => setX(o => ({ ...o, [k]: v }))
  const est = useMemo(() => calcSummary(toSummaryIn(data, x)), [data, x])
  const cur = data.lines
  const curOf = (name: string) => cur.find(l => l.name === name)?.amount ?? 0
  const estOf = (name: string) => est.find(l => l.name === name)?.amount ?? 0
  const reserves = est.filter(l => l.code?.startsWith('reserve')).reduce((s, l) => s + l.amount, 0)
  // shared savings (was: Do you have Shared Savings?): what goes back to the client and what we keep
  const buyout = n(x.trades) * n(x.buyoutPct) / 100
  const buyoutBack = x.shared ? buyout * n(x.buyoutBackPct) / 100 : 0
  const spent = x.shared ? reserves * n(x.reserveSpendPct) / 100 : reserves
  const unspent = reserves - spent, reserveBack = x.shared ? unspent * n(x.reserveBackPct) / 100 : 0
  const fee = est.find(l => l.code === 'fee')?.amount ?? 0
  const kept = buyout - buyoutBack + unspent - reserveBack
  const ct = estOf('Contract total'), ctTax = estOf('Contract total with sales tax')

  function inputFor(l: (typeof est)[number]) {
    if (l.name === 'Trades total') return <span className="estin"><input className="inp num" value={x.trades} onChange={e => set('trades', e.target.value)} /></span>
    if (l.code === 'genreq') return <span className="estin"><input className="inp num" value={x.genreq} onChange={e => set('genreq', e.target.value)} /></span>
    if (l.code === 'precon') return <span className="estin"><input className="inp num" value={x.precon} onChange={e => set('precon', e.target.value)} /></span>
    if (l.code === 'genconds') return <span className="estin"><input className="inp num" value={x.genconds} onChange={e => set('genconds', e.target.value)} /></span>
    if ((l.kind === 'markup' || l.kind === 'tax') && l.code) {
      const code = l.code, fixed = x.fixed[code]
      return fixed != null
        ? <span className="estin" title="This item has a fixed amount on the Rates tab"><input className="inp num" value={fixed} onChange={e => set('fixed', { ...x.fixed, [code]: e.target.value })} /><button className="btn sm g" onClick={() => set('fixed', { ...x.fixed, [code]: null })} title="Use the rate instead">use %</button></span>
        : <span className="estin pct"><input className="inp num" value={x.rates[code] ?? ''} onChange={e => set('rates', { ...x.rates, [code]: e.target.value })} /><i>%</i></span>
    }
    if (l.kind === 'custom') { const k = x.indirect.findIndex(i => i.name === l.name); if (k < 0) return null
      const it = x.indirect[k], upd = (p: Partial<Inputs['indirect'][number]>) => set('indirect', x.indirect.map((y, j) => j === k ? { ...y, ...p } : y))
      return <span className="estin"><input className="inp num" value={it.amount} onChange={e => upd({ amount: e.target.value })} />
        <label className="check" title="Fee is charged on this item"><input type="checkbox" checked={it.inFee} onChange={e => upd({ inFee: e.target.checked })} />in fee</label></span> }
    return null
  }
  function exportCsv() {
    const q = (s: string | number) => `"${String(s).replace(/"/g, '""')}"`
    const rows = [['Item', 'Input', 'Current', 'Estimate', 'Delta'], ...est.map(l => {
      const inp = l.name === 'Trades total' ? x.trades : l.code === 'genreq' ? x.genreq : l.code === 'precon' ? x.precon : l.code === 'genconds' ? x.genconds : l.code && x.fixed[l.code] != null ? x.fixed[l.code]! : l.code && x.rates[l.code] != null ? x.rates[l.code] + '%' : l.kind === 'custom' ? x.indirect.find(i => i.name === l.name)?.amount ?? '' : ''
      return [l.name, inp, curOf(l.name), l.amount, Math.round((l.amount - curOf(l.name)) * 100) / 100] })]
    if (x.shared) rows.push([], ['Shared savings'], ['Buyout savings', x.buyoutPct + '%', '', buyout, ''], ['Returned to client', '', '', buyoutBack + reserveBack, ''], ['Kept (buyout + unspent reserves)', '', '', kept, ''])
    const blob = new Blob(['﻿' + rows.map(r => r.map(q).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' })
    const a = document.createElement('a'); a.href = URL.createObjectURL(blob); a.download = 'estimator.csv'; a.click(); setTimeout(() => URL.revokeObjectURL(a.href), 2000)
  }
  const tog = (list: string[], k: string, on: boolean) => on ? [...new Set([...list, k])] : list.filter(v => v !== k)
  const delta = (a: number, b: number) => { const d = Math.round(b - a); return !d ? <span className="mut">–</span> : <span className={d > 0 ? 'up' : 'down'}>{d > 0 ? '+' : '−'}{money(Math.abs(d))}</span> }
  return <Modal wide title={<span data-was="Calculation test">Estimator</span>} onClose={onClose}
    footer={<><span className="mut" style={{ fontSize: 12 }}>Sandbox · nothing here is saved</span><span className="sp" style={{ flex: 1 }} />
      <button className="btn" onClick={() => setX(seedFrom(data))}><Icon name="sync" />Reset to project</button>
      <button className="btn" onClick={exportCsv}><Icon name="down" />Export to Excel (CSV)</button><button className="btn p" onClick={onClose}>Done</button></>}>
    <p className="muted" style={{ marginTop: 0 }}>Adjust the inputs and the estimate recalculates with the same rules as the Price roll-up. <b>Current</b> is this version as saved.</p>
    <div className="estgrid">
      <div className="gw"><table className="dg est"><thead><tr><th>Item</th><th style={{ width: 210 }}>Input</th><th className="r">Current</th><th className="r">Estimate</th><th className="r">Delta</th></tr></thead>
        <tbody>{est.map((l, i) => { const calc = l.kind === 'calculated' || l.kind === 'trades'
          return <tr key={i} className={calc ? 'sub' : ''}><td>{calc ? <b>{l.name}</b> : l.name}</td><td>{inputFor(l)}</td>
            <td className="n mut">{money(curOf(l.name))}</td><td className="n"><b>{money(l.amount)}</b></td><td className="n">{delta(curOf(l.name), l.amount)}</td></tr> })}</tbody></table></div>
      <aside className="estside">
        <section><h4>Fee carve-outs</h4>
          <label className="check"><input type="checkbox" checked={x.apply} onChange={e => set('apply', e.target.checked)} /> Apply carve-outs</label>
          {FEE_PARTS.map(([k, l]) => <label key={k} className={`check ${x.apply ? '' : 'mut'}`}><input type="checkbox" disabled={!x.apply} checked={x.feeExcludes.includes(k)} onChange={e => set('feeExcludes', tog(x.feeExcludes, k, e.target.checked))} /> Leave out {l}</label>)}</section>
        <section><h4>Reserve base</h4>
          {RESERVES.map(([k, l]) => <label key={k} className="check"><input type="checkbox" checked={x.reservePrecon.includes(k)} onChange={e => set('reservePrecon', tog(x.reservePrecon, k, e.target.checked))} /> {l}: + precon services</label>)}
          {RESERVES.map(([k, l]) => <label key={k + 'g'} className="check"><input type="checkbox" checked={x.reserveGenconds.includes(k)} onChange={e => set('reserveGenconds', tog(x.reserveGenconds, k, e.target.checked))} /> {l}: + general conditions</label>)}
          <label className="check"><input type="checkbox" checked={x.excludeGenreqFromDirect} onChange={e => set('excludeGenreqFromDirect', e.target.checked)} /> Leave project overheads out of direct cost</label></section>
        <section><h4 data-was="Shared Savings">Shared savings</h4>
          <div className="field"><label>Anticipated buyout (% of trades)</label><span className="estin pct"><input className="inp num" value={x.buyoutPct} onChange={e => set('buyoutPct', e.target.value)} /><i>%</i></span></div>
          <label className="check"><input type="checkbox" checked={x.shared} onChange={e => set('shared', e.target.checked)} /> This contract shares savings with the client</label>
          {x.shared && <>
            <div className="field"><label>Buyout savings back to client</label><span className="estin pct"><input className="inp num" value={x.buyoutBackPct} onChange={e => set('buyoutBackPct', e.target.value)} /><i>%</i></span></div>
            <div className="field"><label>Expected spend of reserves</label><span className="estin pct"><input className="inp num" value={x.reserveSpendPct} onChange={e => set('reserveSpendPct', e.target.value)} /><i>%</i></span></div>
            <div className="field"><label>Unspent reserves back to client</label><span className="estin pct"><input className="inp num" value={x.reserveBackPct} onChange={e => set('reserveBackPct', e.target.value)} /><i>%</i></span></div></>}
          <dl className="estdl">
            <dt>Buyout savings</dt><dd>{money(buyout)}</dd>
            {x.shared && <><dt>Reserves spent / unspent</dt><dd>{money(spent)} / {money(unspent)}</dd><dt>Back to client</dt><dd>{money(buyoutBack + reserveBack)}</dd></>}
            <dt>Fee</dt><dd>{money(fee)}</dd>
            <dt>Expected margin (fee + kept savings)</dt><dd><b>{money(fee + kept)}</b></dd>
            <dt>Contract total{ctTax !== ct ? ' (with tax)' : ''}</dt><dd><b>{money(ctTax !== ct ? ctTax : ct)}</b>{delta(curOf(ctTax !== ct ? 'Contract total with sales tax' : 'Contract total'), ctTax !== ct ? ctTax : ct)}</dd>
          </dl></section>
      </aside>
    </div>
  </Modal>
}
