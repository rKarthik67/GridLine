// @gridline/calc — the pricing rules, shared by the browser (instant results) and checked by the API.
// The C# copy in the API must pass the same test vectors (test/vectors.json).
// All money is in dollars; results are rounded to cents.

export type AnswerKind = 'included' | 'excluded' | 'amount' | 'price' | 'text' | 'blank'
export type LineSection = 'baserequirement' | 'tradeitem' | 'zone'
export type BidState = 'invited' | 'bidding' | 'posted' | 'late' | 'incomplete' | 'notplansspecs' | 'undecided' | 'disqualified' | 'declined'

export interface ScopeLineIn { lineId: number; section: LineSection; isHeading?: boolean; skipInAdjustments?: boolean; archived?: boolean }
export interface AnswerIn { lineId: number; bidId: number; kind: AnswerKind; amount?: number | null }
export interface BidIn { bidId: number; isPosted: boolean; isCarried: boolean; state: BidState; baseAmount: number | null }
export interface TradeIn {
  tradeId: number; adjustment: number; plugAmount?: number | null; budgetValue?: number | null
  bids: BidIn[]; lines?: ScopeLineIn[]; answers?: AnswerIn[]
  zoneShares?: Record<string, number>          // zoneCode -> share (0..1)
}
export interface BidOut { bidId: number; scopeAdjustments: number; totalAmount: number | null; rank: number | null }
export interface TradeOut {
  tradeId: number; bids: BidOut[]; lowestValue: number | null; carriedValue: number | null
  budgetDelta: number | null; usesPlug: boolean; zoneAmounts: Record<string, number>
}

export const round2 = (n: number) => Math.round((n + Number.EPSILON) * 100) / 100

/** Scope adjustments for each bid = sum of dollar answers on base-requirement and trade-item lines not marked "not in adjustments". */
export function scopeAdjustments(lines: ScopeLineIn[] = [], answers: AnswerIn[] = []): Map<number, number> {
  const counted = new Set(lines.filter(l => !l.archived && !l.isHeading && !l.skipInAdjustments && (l.section === 'baserequirement' || l.section === 'tradeitem')).map(l => l.lineId))
  const out = new Map<number, number>()
  for (const a of answers) {
    if (a.kind !== 'amount' || a.amount == null || !counted.has(a.lineId)) continue
    out.set(a.bidId, round2((out.get(a.bidId) ?? 0) + a.amount))
  }
  return out
}

/** Everything the trade summary shows for one trade. */
export function calcTrade(t: TradeIn): TradeOut {
  const adj = scopeAdjustments(t.lines, t.answers)
  const bids: BidOut[] = t.bids.map(b => ({
    bidId: b.bidId,
    scopeAdjustments: adj.get(b.bidId) ?? 0,
    totalAmount: b.baseAmount == null ? null : round2(b.baseAmount + (adj.get(b.bidId) ?? 0)),
    rank: null,
  }))
  const eligible = bids.filter((o, i) => t.bids[i].isPosted && t.bids[i].state !== 'disqualified' && o.totalAmount != null)
  const sorted = [...eligible].sort((a, b) => (a.totalAmount! - b.totalAmount!))
  sorted.forEach((o, i) => { o.rank = i > 0 && sorted[i - 1].totalAmount === o.totalAmount ? sorted[i - 1].rank : i + 1 })
  const lowestValue = sorted.length ? sorted[0].totalAmount : null
  const carriedIdx = t.bids.findIndex(b => b.isCarried)
  const carried = carriedIdx >= 0 ? bids[carriedIdx].totalAmount : null
  const usesPlug = carried == null && t.plugAmount != null && t.plugAmount !== 0
  const carriedValue = carried != null ? round2(carried + (t.adjustment || 0)) : (usesPlug ? round2(t.plugAmount!) : null)
  const basis = carriedValue ?? lowestValue ?? 0
  const zoneAmounts: Record<string, number> = {}
  for (const [z, s] of Object.entries(t.zoneShares ?? {})) zoneAmounts[z] = round2(basis * s)
  return {
    tradeId: t.tradeId, bids, lowestValue, carriedValue, usesPlug, zoneAmounts,
    budgetDelta: carriedValue != null && t.budgetValue != null ? round2(carriedValue - t.budgetValue) : null,
  }
}

/** The value a trade contributes to the trades total: carried (or plug), otherwise lowest, otherwise 0. */
export const tradeContribution = (o: TradeOut) => o.carriedValue ?? o.lowestValue ?? 0

// ---------------------------------------------------------------- executive summary
export interface MarkupIn { code: string; name: string; appliesTo: 'direct' | 'subtotal' | 'contract' | 'tax'; ratePct: number; fixedAmount?: number | null; sortOrder: number }
export interface IndirectIn { name: string; amount: number; outsideFee?: boolean }
export interface SummaryIn {
  tradesTotal: number
  genreqTotal: number            // general requirements (quoted)
  preconTotal: number            // preconstruction (quoted)
  gencondsTotal?: number         // general conditions = the crew plan's quoted total
  markups: MarkupIn[]            // reserves, bond, builders risk, insurance, fee, sales tax (rates in %)
  indirect: IndirectIn[]
  grossArea: number
  zoneShares?: Record<string, number>   // share of the trades total in each zone (0..1)
  zoneAreas?: Record<string, number>
  options?: SummaryOptions
}
/**
 * Fee carve-outs (was: fee exclusions) and bid total options.
 *  feeExcludes: parts left out of the fee base — 'genreq' (project overheads), 'precon' (precon services),
 *               'reserves' (all reserves), 'bond' (P & P bond), 'cover' (works cover)
 *  excludeGenreqFromDirect: project overheads are not part of the direct cost total (so not in the reserve base)
 *  reservePrecon / reserveGenconds: reserve codes whose base also includes precon services / general conditions
 *  feeExcludes may also hold 'genconds' (general conditions)
 */
export interface SummaryOptions { feeExcludes?: string[]; excludeGenreqFromDirect?: boolean; reservePrecon?: string[]; reserveGenconds?: string[] }
export interface SummaryLine { name: string; kind: 'trades' | 'cost' | 'markup' | 'custom' | 'calculated' | 'tax'; code?: string; ratePct?: number; amount: number; perArea: number | null; pctOfContract: number | null; zones: Record<string, number> }

/**
 * Build-up (same order as today's roll-up):
 *   Trades total + General requirements = Direct cost total
 *   + Preconstruction + reserves (rate × direct cost)               = Subtotal
 *   + bond, builders risk, insurance (rate × subtotal)
 *   + fee (rate × (subtotal + bond + cover + insurance + custom items that are inside the fee))
 *   + custom items                                                   = Contract total
 *   + sales tax (rate × contract total)                              = Contract total with sales tax
 */
export function calcSummary(s: SummaryIn): SummaryLine[] {
  const lines: Omit<SummaryLine, 'perArea' | 'pctOfContract' | 'zones'>[] = []
  const amt = (m: MarkupIn, base: number) => round2(m.fixedAmount != null ? m.fixedAmount : base * (m.ratePct / 100))
  const by = (a: MarkupIn['appliesTo']) => s.markups.filter(m => m.appliesTo === a).sort((x, y) => x.sortOrder - y.sortOrder)
  const o = s.options ?? {}, ex = new Set(o.feeExcludes ?? []), rp = new Set(o.reservePrecon ?? []), rg = new Set(o.reserveGenconds ?? []), gc = s.gencondsTotal ?? 0
  lines.push({ name: 'Trades total', kind: 'trades', amount: round2(s.tradesTotal) })
  lines.push({ name: 'Project overheads', kind: 'cost', code: 'genreq', amount: round2(s.genreqTotal) })
  const direct = round2(s.tradesTotal + (o.excludeGenreqFromDirect ? 0 : s.genreqTotal))
  lines.push({ name: 'Direct cost total', kind: 'calculated', amount: direct })
  lines.push({ name: 'Precon services', kind: 'cost', code: 'precon', amount: round2(s.preconTotal) })
  lines.push({ name: 'General conditions', kind: 'cost', code: 'genconds', amount: round2(gc) })
  let subtotal = s.tradesTotal + s.genreqTotal + s.preconTotal + gc, reserves = 0
  for (const m of by('subtotal')) { const a = amt(m, direct + (rp.has(m.code) ? s.preconTotal : 0) + (rg.has(m.code) ? gc : 0)); subtotal += a; if (m.code.startsWith('reserve')) reserves += a; lines.push({ name: m.name, kind: 'markup', code: m.code, ratePct: m.ratePct, amount: a }) }
  subtotal = round2(subtotal)
  lines.push({ name: 'Subtotal', kind: 'calculated', amount: subtotal })
  let contract = subtotal
  const contractMarkups = by('contract'), fee = contractMarkups.filter(m => m.code === 'fee'), others = contractMarkups.filter(m => m.code !== 'fee')
  let carved = (ex.has('genreq') ? s.genreqTotal : 0) + (ex.has('precon') ? s.preconTotal : 0) + (ex.has('genconds') ? gc : 0) + (ex.has('reserves') ? reserves : 0)
  for (const m of others) { const a = amt(m, subtotal); contract += a; if (ex.has(m.code)) carved += a; lines.push({ name: m.name, kind: 'markup', code: m.code, ratePct: m.ratePct, amount: a }) }
  const insideFee = s.indirect.filter(i => !i.outsideFee).reduce((x, i) => x + i.amount, 0)
  for (const m of fee) { const a = amt(m, contract + insideFee - carved); contract += a; lines.push({ name: m.name, kind: 'markup', code: m.code, ratePct: m.ratePct, amount: a }) }
  for (const i of s.indirect) { contract += i.amount; lines.push({ name: i.name, kind: 'custom', amount: round2(i.amount) }) }
  contract = round2(contract)
  lines.push({ name: 'Contract total', kind: 'calculated', amount: contract })
  let withTax = contract
  for (const m of by('tax')) { const a = amt(m, contract); withTax += a; lines.push({ name: m.name, kind: 'tax', code: m.code, ratePct: m.ratePct, amount: a }) }
  lines.push({ name: 'Contract total with sales tax', kind: 'calculated', amount: round2(withTax) })
  const shares = s.zoneShares ?? {}
  return lines.map(l => ({
    ...l,
    perArea: s.grossArea > 0 ? round2(l.amount / s.grossArea) : null,
    pctOfContract: contract ? round2((l.amount / contract) * 100) : null,
    zones: Object.fromEntries(Object.entries(shares).map(([z, sh]) => [z, round2(l.amount * sh)])),
  }))
}

/** Fingerprint of the totals so the browser and server can compare cheaply. */
export function totalsHash(values: (number | null)[]): string {
  let h = 2166136261
  for (const v of values) { const s = v == null ? '~' : v.toFixed(2); for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619) >>> 0 } h ^= 124 }
  return h.toString(16).padStart(8, '0')
}

/** Parse what a user typed into a scope cell: Included / Excluded / amount / =formula / text. */
export function parseAnswer(input: string): { kind: AnswerKind; amount: number | null; text: string | null; formula: string | null } {
  const s = (input ?? '').trim()
  if (!s) return { kind: 'blank', amount: null, text: null, formula: null }
  if (/^(included|incl|inc|✓|y|yes)$/i.test(s)) return { kind: 'included', amount: null, text: null, formula: null }
  if (/^(excluded|excl|exc|✕|x|n|no)$/i.test(s)) return { kind: 'excluded', amount: null, text: null, formula: null }
  if (/^price$/i.test(s)) return { kind: 'price', amount: null, text: null, formula: null }
  if (s.startsWith('=')) {
    const expr = s.slice(1).replace(/[$,\s]/g, '')
    if (/^[0-9+\-*/().]+$/.test(expr)) {
      const v = evalArithmetic(expr)
      if (v != null) return { kind: 'amount', amount: round2(v), text: null, formula: s }
    }
    return { kind: 'text', amount: null, text: s, formula: null }
  }
  const n = s.replace(/[$,\s]/g, '')
  if (/^[+-]?\d+(\.\d+)?$/.test(n)) return { kind: 'amount', amount: round2(Number(n)), text: null, formula: null }
  if (/^\(\d+(\.\d+)?\)$/.test(n)) return { kind: 'amount', amount: -round2(Number(n.slice(1, -1))), text: null, formula: null }
  return { kind: 'text', amount: null, text: s, formula: null }
}

/** Small safe arithmetic evaluator (+ - * / and brackets), no eval(). */
export function evalArithmetic(expr: string): number | null {
  let i = 0
  const peek = () => expr[i], next = () => expr[i++]
  const num = (): number | null => {
    if (peek() === '(') { next(); const v = add(); if (next() !== ')') return null; return v }
    if (peek() === '-') { next(); const v = num(); return v == null ? null : -v }
    const m = /^\d+(\.\d+)?/.exec(expr.slice(i)); if (!m) return null; i += m[0].length; return Number(m[0])
  }
  const mul = (): number | null => {
    let v = num(); if (v == null) return null
    while (peek() === '*' || peek() === '/') { const op = next(); const r = num(); if (r == null) return null; if (op === '/' && r === 0) return null; v = op === '*' ? v * r : v / r }
    return v
  }
  const add = (): number | null => {
    let v = mul(); if (v == null) return null
    while (peek() === '+' || peek() === '-') { const op = next(); const r = mul(); if (r == null) return null; v = op === '+' ? v + r : v - r }
    return v
  }
  const v = add()
  return v != null && i === expr.length && Number.isFinite(v) ? v : null
}
