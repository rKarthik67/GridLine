// Thin fetch wrapper for the Gridline API. The access token is shared by every tab (it is per user,
// not per project); everything project-specific comes from the page address, so tabs stay independent.
import { debugLog } from './debug'

export class ApiError extends Error {
  status: number; code?: string; detail?: unknown
  constructor(status: number, message: string, code?: string, detail?: unknown) { super(message); this.status = status; this.code = code; this.detail = detail }
}
const TOKEN_KEY = 'gridline.token'
export const tokenStore = {
  get(): string | null { try { return localStorage.getItem(TOKEN_KEY) } catch { return null } },
  set(t: string | null) { try { t ? localStorage.setItem(TOKEN_KEY, t) : localStorage.removeItem(TOKEN_KEY) } catch { /* private mode */ } },
}
export const tabId = (() => { try { let t = sessionStorage.getItem('gridline.tab'); if (!t) { t = crypto.randomUUID(); sessionStorage.setItem('gridline.tab', t) } return t } catch { return crypto.randomUUID() } })()

let seq = 0
async function request<T>(method: string, url: string, body?: unknown, init?: RequestInit): Promise<T> {
  const headers: Record<string, string> = { 'X-Tab-Id': tabId }
  const token = tokenStore.get(); if (token) headers.Authorization = `Bearer ${token}`
  let payload: BodyInit | undefined
  if (body instanceof FormData || body instanceof Blob || body instanceof ArrayBuffer) payload = body as BodyInit
  else if (body !== undefined) { headers['Content-Type'] = 'application/json'; payload = JSON.stringify(body) }
  const id = ++seq, started = performance.now()
  debugLog.request(id, method, `/api${url}`, body)
  let res: Response
  try { res = await fetch(`/api${url}`, { method, headers, body: payload, ...init }) }
  catch (e) { debugLog.failed(id, method, `/api${url}`, e, performance.now() - started); throw new ApiError(0, 'Can’t reach the Gridline API. Is it running?', 'offline') }
  const type = res.headers.get('content-type') ?? ''
  const data = res.status === 204 ? undefined : type.includes('json') ? await res.json().catch(() => null) : await res.text()
  debugLog.response(id, method, `/api${url}`, res.status, data, performance.now() - started)
  if (!res.ok) {
    if (res.status === 401) tokenStore.set(null)
    const p = (data ?? {}) as { title?: string; detail?: string; code?: string; errors?: unknown }
    throw new ApiError(res.status, p.detail || p.title || `Request failed (${res.status})`, p.code, p.errors)
  }
  return data as T
}
export const api = {
  get: <T>(url: string) => request<T>('GET', url),
  post: <T>(url: string, body?: unknown) => request<T>('POST', url, body),
  put: <T>(url: string, body?: unknown) => request<T>('PUT', url, body),
  patch: <T>(url: string, body?: unknown) => request<T>('PATCH', url, body),
  del: <T>(url: string) => request<T>('DELETE', url),
  /** Downloads a file (exports). Uses fetch so the auth header is sent, then saves it. */
  async download(url: string, fallbackName: string) {
    const headers: Record<string, string> = { 'X-Tab-Id': tabId }
    const token = tokenStore.get(); if (token) headers.Authorization = `Bearer ${token}`
    const id = ++seq, started = performance.now()
    debugLog.request(id, 'GET', `/api${url}`, undefined)
    const res = await fetch(`/api${url}`, { headers })
    if (!res.ok) {
      const text = await res.text().catch(() => '')
      debugLog.response(id, 'GET', `/api${url}`, res.status, text, performance.now() - started)
      throw new ApiError(res.status, `Export failed (${res.status})`)
    }
    const name = /filename\*?=(?:UTF-8'')?"?([^";]+)/i.exec(res.headers.get('content-disposition') ?? '')?.[1] ?? fallbackName
    const blob = await res.blob()
    debugLog.response(id, 'GET', `/api${url}`, res.status, `[file] ${decodeURIComponent(name)} · ${blob.size.toLocaleString()} bytes`, performance.now() - started)
    const a = document.createElement('a')
    a.href = URL.createObjectURL(blob); a.download = decodeURIComponent(name); document.body.appendChild(a); a.click(); a.remove()
    setTimeout(() => URL.revokeObjectURL(a.href), 4000)
    return name
  },
}
