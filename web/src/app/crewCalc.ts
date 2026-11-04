// Set hours / budget (was: CalcBudget.js): turn a phase budget into weekly hours and back, with yearly salary escalation.
// A week's cost = hours × posting rate × (1 + escalation %) ^ (year − first year of the schedule).
import type { CrewWeek } from '../api/types'

const yearsOf = (weeks: CrewWeek[], phase: string) => {
  const m = new Map<number, number>()
  for (const w of weeks) if (w.phase === phase) m.set(w.year, (m.get(w.year) ?? 0) + 1)
  return [...m.entries()].map(([year, n]) => ({ year, weeks: n }))
}
const baseYear = (weeks: CrewWeek[]) => weeks.length ? Math.min(...weeks.map(w => w.year)) : new Date().getFullYear()

/** Cost of working `hours` every week of the phase. */
export function phaseCost(weeks: CrewWeek[], phase: 'build' | 'closeout', rate: number, hours: number, escalationPct: number) {
  const b = baseYear(weeks), esc = 1 + escalationPct / 100
  return Math.round(yearsOf(weeks, phase).reduce((s, y) => s + rate * Math.pow(esc, y.year - b) * hours * y.weeks, 0) * 100) / 100
}
/** Weekly hours that spend `budget` over the phase (one figure for every week, like the original). */
export function phaseHours(weeks: CrewWeek[], phase: 'build' | 'closeout', rate: number, budget: number, escalationPct: number) {
  if (!rate) return 0
  const b = baseYear(weeks), esc = 1 + escalationPct / 100
  const weighted = yearsOf(weeks, phase).reduce((s, y) => s + y.weeks * Math.pow(esc, y.year - b), 0)
  if (!weighted) return 0
  return Math.round(budget / (rate * weighted) * 10) / 10
}
export const phaseWeeks = (weeks: CrewWeek[], phase: string) => weeks.filter(w => w.phase === phase).length

/** Per diem preview (same rule as the server): hotel by the month's rate, meals per day; week days skip Sat/Sun. */
export function perDiem(start: string, end: string, allDays: boolean, kind: 'hotel' | 'meals' | 'both', lodging: number[], meals: number) {
  const s = new Date(start + 'T00:00:00'), e = new Date(end + 'T00:00:00')
  let days = 0, cost = 0
  if (isNaN(+s) || isNaN(+e) || e < s) return { days, cost }
  for (const d = new Date(s); d <= e; d.setDate(d.getDate() + 1)) {
    const wd = d.getDay(); if (!allDays && (wd === 0 || wd === 6)) continue
    days++; cost += (kind !== 'meals' ? lodging[d.getMonth()] : 0) + (kind !== 'hotel' ? meals : 0)
  }
  return { days, cost }
}
/** Whole weeks between two dates, like today's "(N Weeks)" label next to each date. */
export const weeksBetween = (from: string | null | undefined, to: string | null | undefined) =>
  from && to ? Math.floor((Date.parse(to) - Date.parse(from)) / (7 * 864e5)) : null
