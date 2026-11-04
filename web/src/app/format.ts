export const money = (n: number | null | undefined, d = 0) => n == null ? '–' : (n < 0 ? '-$' : '$') + Math.abs(n).toLocaleString('en-US', { minimumFractionDigits: d, maximumFractionDigits: d })
export const pct = (n: number | null | undefined, d = 1) => n == null ? '–' : `${n.toFixed(d)}%`
export const num = (n: number | null | undefined, d = 0) => n == null ? '–' : n.toLocaleString('en-US', { minimumFractionDigits: d, maximumFractionDigits: d })
export const date = (s: string | null | undefined) => s ? new Date(s + (s.length === 10 ? 'T00:00:00' : '')).toLocaleDateString('en-US', { month: 'short', day: '2-digit', year: 'numeric' }) : '–'
export const cap = (s: string | null | undefined) => s ? s[0].toUpperCase() + s.slice(1) : ''
export const lane = (laneNo: number) => String.fromCharCode(64 + laneNo)
