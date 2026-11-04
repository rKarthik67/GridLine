// Small shared building blocks (modal, drawer, menus, toggles, inline editors) styled by the prototype CSS.
import { useEffect, useRef, useState, type ReactNode } from 'react'
import { Icon } from './Icon'
import { api, ApiError } from '../api/client'
import { useToast } from '../app/toast'

export function Modal({ title, onClose, children, footer, wide }: { title: ReactNode; onClose(): void; children: ReactNode; footer?: ReactNode; wide?: boolean }) {
  useEffect(() => { const k = (e: KeyboardEvent) => e.key === 'Escape' && onClose(); addEventListener('keydown', k); return () => removeEventListener('keydown', k) }, [onClose])
  return <div className="scrim" onMouseDown={onClose}><div className={`modal ${wide ? 'wide' : ''}`} role="dialog" aria-modal="true" onMouseDown={e => e.stopPropagation()}>
    <div className="mh"><h3>{title}</h3><button className="btn g ic x" onClick={onClose} aria-label="Close"><Icon name="x" /></button></div>
    <div className="mb">{children}</div>{footer && <div className="mf">{footer}</div>}</div></div>
}
export function Drawer({ head, onClose, children }: { head: ReactNode; onClose(): void; children: ReactNode }) {
  useEffect(() => { const k = (e: KeyboardEvent) => e.key === 'Escape' && onClose(); addEventListener('keydown', k); return () => removeEventListener('keydown', k) }, [onClose])
  return <><div className="scrim" onMouseDown={onClose} style={{ background: '#161a2e33' }} /><div className="drawer" role="dialog" aria-modal="true">
    <div className="mh">{head}<button className="btn g ic x" onClick={onClose} aria-label="Close" style={{ marginLeft: 'auto' }}><Icon name="x" /></button></div>
    <div className="mb" style={{ overflow: 'auto', flex: 1 }}>{children}</div></div></>
}
export function Tabs<K extends string>({ items, value, onChange }: { items: [K, string, (number | null)?][]; value: K; onChange(k: K): void }) {
  return <div className="tabs" role="tablist">{items.map(([k, l, n]) => <button key={k} role="tab" aria-selected={value === k} onClick={() => onChange(k)}>{l}{n != null && <span className="n">{n}</span>}</button>)}</div>
}
export function Seg<K extends string>({ items, value, onChange }: { items: [K, string][]; value: K; onChange(k: K): void }) {
  return <div className="seg">{items.map(([k, l]) => <button key={k} aria-pressed={value === k} onClick={() => onChange(k)}>{l}</button>)}</div>
}
export function Toggle({ label, on, onChange }: { label: string; on: boolean; onChange(v: boolean): void }) {
  return <label className="check" style={{ fontSize: 12 }}>{label} <button className="tog" role="switch" aria-checked={on} onClick={() => onChange(!on)} /></label>
}
/** A button that opens a small list of actions. */
export function Menu({ label, icon, items, cls = '' }: { label: ReactNode; icon?: string; items: ({ label: string; icon?: string; onClick(): void; danger?: boolean } | null)[]; cls?: string }) {
  const [open, setOpen] = useState(false); const ref = useRef<HTMLDivElement>(null)
  useEffect(() => { if (!open) return; const c = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) setOpen(false) }; addEventListener('mousedown', c); return () => removeEventListener('mousedown', c) }, [open])
  return <div ref={ref} style={{ position: 'relative', display: 'inline-flex' }}>
    <button className={`btn ${cls}`} onClick={() => setOpen(o => !o)} aria-haspopup="menu" aria-expanded={open}>{icon && <Icon name={icon} />}{label}{label !== '' && <Icon name="chev" size="s" />}</button>
    {open && <div className="menu" role="menu" style={{ position: 'absolute', top: '100%', right: 0, zIndex: 60, minWidth: 220, marginTop: 4 }}>
      {items.filter(Boolean).map((it, i) => <button key={i} role="menuitem" onClick={() => { setOpen(false); it!.onClick() }} style={it!.danger ? { color: 'var(--bad, #d1344b)' } : undefined}>{it!.icon && <Icon name={it!.icon} size="s" />}{it!.label}</button>)}
    </div>}
  </div>
}
/** Export dropdown: each item downloads a file from /api/exports. */
export function ExportMenu({ items }: { items: [string, string][] }) {
  const toast = useToast()
  return <Menu label="Export" icon="down" items={items.map(([label, url]) => ({ label, icon: 'file', onClick: async () => {
    try { toast('Exported ' + await api.download(url, label), 'down') } catch (e) { toast((e as Error).message, 'alert') }
  } }))} />
}
/** Runs a save and shows its error as a toast. Returns a wrapper and a busy flag. */
export function useAction() {
  const toast = useToast(); const [busy, setBusy] = useState(false)
  async function run<T>(f: () => Promise<T>, ok?: string): Promise<T | undefined> {
    setBusy(true)
    try { const r = await f(); if (ok) toast(ok); return r }
    catch (e) { const er = e as ApiError; toast(er.code === 'clash' ? er.message : er.message || 'Could not save', er.code === 'clash' ? 'sync' : 'alert'); return undefined }
    finally { setBusy(false) }
  }
  return { run, busy }
}
/** Text/number cell that saves on Enter or blur (Esc cancels). */
export function EditCell({ value, onSave, number, placeholder, className, style, disabled, title }: {
  value: string | number | null | undefined; onSave(v: string): unknown; number?: boolean; placeholder?: string; className?: string; style?: React.CSSProperties; disabled?: boolean; title?: string
}) {
  const shown = value == null ? '' : String(value)
  const [v, setV] = useState(shown); const [editing, setEditing] = useState(false)
  useEffect(() => { if (!editing) setV(shown) }, [shown, editing])
  const shownValue = !editing && number && v !== '' && Number.isFinite(Number(v)) ? Number(v).toLocaleString('en-US', { maximumFractionDigits: 4 }) : v
  return <input className={`inp cellin ${number ? 'num' : ''} ${className ?? ''}`} style={{ width: '100%', ...style }} value={shownValue} placeholder={placeholder} disabled={disabled} title={title}
    onFocus={() => setEditing(true)} onChange={e => setV(e.target.value)}
    onBlur={() => { setEditing(false); if (v !== shown) onSave(v) }}
    onKeyDown={e => { if (e.key === 'Enter') (e.target as HTMLInputElement).blur(); if (e.key === 'Escape') { setV(shown); setEditing(false); setTimeout(() => (e.target as HTMLInputElement).blur()) } }} />
}
export const toNum = (s: string): number | null => { const t = s.replace(/[$,%\s]/g, ''); if (!t) return null; const n = Number(t.startsWith('(') && t.endsWith(')') ? '-' + t.slice(1, -1) : t); return Number.isFinite(n) ? n : null }
export function Kpi({ k, v, d, cls, dc }: { k: string; v: ReactNode; d?: ReactNode; cls?: string; dc?: string }) {
  return <div className={`kpi ${cls ?? ''}`}><span className="k">{k}</span><span className="v">{v}</span>{d && <span className={`d ${dc ?? ''}`}>{d}</span>}</div>
}
export function Loading({ what }: { what: string }) { return <div className="empty">Loading {what}…</div> }
export function Failed({ error }: { error: unknown }) { return <div className="empty">Couldn't load: {(error as Error)?.message}</div> }
export const LETTERC = ['#6a32d4', '#0ea5e9', '#e11d74', '#16a34a', '#f59e0b', '#7c3aed', '#0891b2', '#db2777', '#65a30d', '#ea580c']
export const letterColour = (laneNo: number) => LETTERC[(laneNo - 1) % LETTERC.length]

/** Right-click menu at the mouse position (like the original grid menus). Items: null = separator. */
export type CtxItem = { label: string; icon?: string; onClick(): void; disabled?: boolean; danger?: boolean } | null
export function ContextMenu({ x, y, items, onClose }: { x: number; y: number; items: CtxItem[]; onClose(): void }) {
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const c = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) onClose() }
    const k = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    addEventListener('mousedown', c); addEventListener('keydown', k)
    return () => { removeEventListener('mousedown', c); removeEventListener('keydown', k) }
  }, [onClose])
  const top = Math.min(y, innerHeight - 24 - items.length * 31), left = Math.min(x, innerWidth - 250)
  return <div ref={ref} className="menu" role="menu" style={{ position: 'fixed', top: Math.max(8, top), left, zIndex: 95 }} onContextMenu={e => e.preventDefault()}>
    {items.map((it, i) => it == null ? <hr key={i} /> :
      <button key={i} role="menuitem" disabled={it.disabled} onClick={() => { onClose(); it.onClick() }}
        style={{ ...(it.danger ? { color: 'var(--bad, #d1344b)' } : {}), ...(it.disabled ? { opacity: .4, cursor: 'default' } : {}) }}>
        {it.icon && <Icon name={it.icon} size="s" />}{it.label}</button>)}
  </div>
}
/** Small text box dialog (notes, counts) — replaces window.prompt. */
export function AskText({ title, label, initial, multiline, number, onClose, onSave }: { title: string; label: string; initial?: string; multiline?: boolean; number?: boolean; onClose(): void; onSave(v: string): void }) {
  const [v, setV] = useState(initial ?? '')
  return <Modal title={title} onClose={onClose} footer={<><button className="btn" onClick={onClose}>Cancel</button><button className="btn p" onClick={() => onSave(v)}><Icon name="check" />Save</button></>}>
    <div className="field"><label>{label}</label>{multiline
      ? <textarea className="inp" rows={4} autoFocus value={v} onChange={e => setV(e.target.value)} />
      : <input className={`inp ${number ? 'num' : ''}`} autoFocus value={v} onChange={e => setV(e.target.value)} onKeyDown={e => e.key === 'Enter' && onSave(v)} />}</div>
  </Modal>
}
