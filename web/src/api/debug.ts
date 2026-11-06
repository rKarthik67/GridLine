// Console logging of every call to the backend: method, URL, request body / form fields, status, response and time.
// On by default while developing (npm run dev). In a built app turn it on from the browser console:
//   gridlineDebug(true)    // on   (remembered in this browser)
//   gridlineDebug(false)   // off
// Passwords and tokens are masked.
const KEY = 'gridline.debug'
export function debugEnabled(): boolean { return enabled() }
export function setDebug(on: boolean) { try { localStorage.setItem(KEY, on ? '1' : '0') } catch { /* ignore */ } }
function enabled(): boolean {
  try { const v = localStorage.getItem(KEY); if (v != null) return v === '1' } catch { /* ignore */ }
  return import.meta.env.DEV
}
;(window as unknown as { gridlineDebug(on: boolean): string }).gridlineDebug = (on: boolean) => {
  try { localStorage.setItem(KEY, on ? '1' : '0') } catch { /* ignore */ }
  return `Gridline API logging ${on ? 'on' : 'off'}`
}

const SECRET = /pass(word)?|token|secret|key$/i
function mask(v: unknown, depth = 0): unknown {
  if (v == null || depth > 6) return v
  if (Array.isArray(v)) return v.map(x => mask(x, depth + 1))
  if (typeof v === 'object') return Object.fromEntries(Object.entries(v as Record<string, unknown>).map(([k, x]) => [k, SECRET.test(k) ? '•••' : mask(x, depth + 1)]))
  return v
}
function describeBody(body: unknown): unknown {
  if (body === undefined) return '(no body)'
  if (body instanceof FormData) {
    const o: Record<string, unknown> = {}
    body.forEach((v, k) => { o[k] = v instanceof File ? `[file] ${v.name} · ${v.size.toLocaleString()} bytes · ${v.type || 'unknown type'}` : SECRET.test(k) ? '•••' : v })
    return { formData: o }
  }
  if (body instanceof Blob) return `[blob] ${body.size} bytes`
  return mask(body)
}
const colour = (status: number) => status >= 500 ? '#d1344b' : status >= 400 ? '#d97706' : status === 0 ? '#d1344b' : '#16a34a'

export const debugLog = {
  request(id: number, method: string, url: string, body: unknown) {
    if (!enabled()) return
    console.log(`%c→ #${id} ${method} ${url}`, 'color:#27624d;font-weight:600', describeBody(body))
  },
  response(id: number, method: string, url: string, status: number, data: unknown, ms: number) {
    if (!enabled()) return
    const head = `%c← #${id} ${status} ${method} ${url} · ${Math.round(ms)} ms`
    const style = `color:${colour(status)};font-weight:600`
    if (status >= 400) {
      console.groupCollapsed(head, style)
      console.error('Error response', mask(data))
      console.groupEnd()
    } else console.log(head, style, mask(data))
  },
  failed(id: number, method: string, url: string, err: unknown, ms: number) {
    if (!enabled()) return
    console.error(`✕ #${id} ${method} ${url} · ${Math.round(ms)} ms · network error (API not running or not reachable)`, err)
  },
  live(direction: '→' | '←', what: string, data?: unknown) {
    if (!enabled()) return
    console.log(`%c${direction} live ${what}`, 'color:#0ea5e9;font-weight:600', data ?? '')
  },
}
