import { describe, expect, it } from 'vitest'
import vectors from './vectors.json'
import { calcSummary, calcTrade, parseAnswer, totalsHash, type SummaryIn, type TradeIn } from '../src/index'

describe('trade rules (shared with the API)', () => {
  for (const [i, v] of vectors.trades.entries()) {
    it(`trade vector ${i + 1}`, () => {
      const o = calcTrade(v.in as TradeIn)
      expect(o.bids).toEqual(v.out.bids)
      expect(o.lowestValue).toBe(v.out.lowestValue)
      expect(o.carriedValue).toBe(v.out.carriedValue)
      expect(o.budgetDelta).toBe(v.out.budgetDelta)
      expect(o.usesPlug).toBe(v.out.usesPlug)
      expect(o.zoneAmounts).toEqual(v.out.zoneAmounts)
    })
  }
})
describe('executive summary build-up', () => {
  for (const [i, v] of vectors.summaries.entries()) {
    it(`summary vector ${i + 1}`, () => {
      const lines = calcSummary(v.in as SummaryIn)
      expect(lines.map(l => [l.name, l.amount])).toEqual(v.out)
      const ct = lines.find(l => l.name === 'Contract total')!
      expect(ct.pctOfContract).toBe(100)
      expect(ct.zones.A + ct.zones.B).toBeCloseTo(ct.amount, 1)
      expect(lines[0].perArea).toBe(100)
    })
  }
})
describe('typing into a scope cell', () => {
  for (const [input, kind, amount] of vectors.answers as [string, string, number | null][]) {
    it(`"${input}" → ${kind}`, () => {
      const a = parseAnswer(input)
      expect(a.kind).toBe(kind)
      if (amount != null) expect(a.amount).toBe(amount)
    })
  }
})
it('totals fingerprint is stable and sensitive', () => {
  expect(totalsHash([1, 2, null])).toBe(totalsHash([1, 2, null]))
  expect(totalsHash([1, 2, null])).not.toBe(totalsHash([1, 2.01, null]))
})
