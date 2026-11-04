// Spreadsheet-style grid used by Scope match, Base requirements, Precon services and Project overheads.
// Built on RevoGrid (MIT): select a range with the mouse or Shift+arrows, Ctrl+C copies it as Excel-ready cells,
// Ctrl+V pastes a block copied from Excel, the fill handle drags a value down, frozen columns stay put while scrolling.
// The grid never keeps its own copy of the data: every edit, paste or fill comes back to the page as `onEdit`,
// which saves it and hands the grid fresh rows (so live updates from other people land the same way).
import { useEffect, useMemo, useRef } from 'react'
import { RevoGrid } from '@revolist/react-datagrid'
import type { ColumnRegular, VNode } from '@revolist/revogrid'

type Kid = VNode | string | number | null | undefined | false
/** Stencil's hyperscript, as handed to cell templates: h('b', { class: 'x' }, ['text', h('i', {})]) */
export type H = (tag: string, data?: Record<string, unknown> | null, children?: Kid | Kid[]) => VNode
export interface GCol<R> {
  prop: string; name: string; size?: number; pin?: 'start' | 'end'; align?: 'r' | 'c'
  /** can the cell be typed into / pasted into */
  edit?: boolean | ((r: R) => boolean)
  /** the raw value copied to Excel and shown in the editor (defaults to row[prop]) */
  value?: (r: R) => string | number | null | undefined
  /** what the cell shows; a string, or hyperscript for chips and buttons */
  show?: (r: R, h: H) => Kid | Kid[]
  cls?: (r: R) => string | undefined
  tip?: (r: R) => string | undefined
  /** custom header (hyperscript) */
  head?: (h: H) => Kid | Kid[]
  headCls?: string
  /** rows can be dragged by this column's handle (see onRowMove) */
  drag?: (r: R) => boolean
}
export interface GridEdit<R> { row: R; prop: string; value: string }

export function DataGrid<R>({ rows, rowKey, cols, onEdit, onMenu, onCellClick, onRowMove, focusKey, rowCls, top, bottom, height = 'calc(100vh - 300px)', readOnly, rowSize = 34, className }: {
  rows: R[]; rowKey(r: R): string | number; cols: GCol<R>[]
  onEdit?(changes: GridEdit<R>[]): void; onMenu?(e: MouseEvent, row: R | null, prop: string | null): void; onCellClick?(row: R, prop: string): void
  rowCls?(r: R): string | undefined; top?: R[]; bottom?: R[]; height?: string; readOnly?: boolean; rowSize?: number; className?: string
  /** a row was dragged onto another row's place */
  onRowMove?(row: R, before: R): void
  /** scroll this row into view when it changes */
  focusKey?: string | number | null
}) {
  const wrap = useRef<HTMLDivElement>(null); const grid = useRef<HTMLRevoGridElement>(null)
  const byKey = useRef(new Map<string, R>())
  const toSource = (list: R[] | undefined, kind: string) => (list ?? []).map((r, i) => {
    const k = `${kind}:${rowKey(r)}:${i}`; byKey.current.set(k, r)
    const o: Record<string, unknown> = { __k: k, __cls: rowCls?.(r) ?? '' }
    for (const c of cols) { const v = c.value ? c.value(r) : (r as Record<string, unknown>)[c.prop]; o[c.prop] = v ?? '' }
    return o
  })
  const source = useMemo(() => { byKey.current = new Map(); return toSource(rows, 'r') }, [rows, cols]) // eslint-disable-line react-hooks/exhaustive-deps
  const pinTop = useMemo(() => toSource(top, 't'), [top, cols]) // eslint-disable-line react-hooks/exhaustive-deps
  const pinBottom = useMemo(() => toSource(bottom, 'b'), [bottom, cols]) // eslint-disable-line react-hooks/exhaustive-deps
  const rowOf = (m: Record<string, unknown> | undefined) => m ? byKey.current.get(m.__k as string) : undefined
  const canEdit = (c: GCol<R> | undefined, r: R | undefined) => !!c && !!r && !readOnly && (typeof c.edit === 'function' ? c.edit(r) : !!c.edit)

  const columns = useMemo<ColumnRegular[]>(() => cols.map(c => ({
    prop: c.prop, name: c.name, size: c.size ?? 120, pin: c.pin === 'start' ? 'colPinStart' : c.pin === 'end' ? 'colPinEnd' : undefined,
    readonly: ({ model }: { model: Record<string, unknown> }) => !canEdit(c, rowOf(model)),
    rowDrag: c.drag && !readOnly ? (({ model }: { model: Record<string, unknown> }) => { const r = rowOf(model); return !!r && c.drag!(r) }) as never : undefined,
    columnProperties: () => ({ class: `gh ${c.align ? 'a-' + c.align : ''} ${c.headCls ?? ''}` }),
    columnTemplate: c.head ? ((h: H) => c.head!(h)) as never : undefined,
    cellProperties: ({ model }: { model: Record<string, unknown> }) => { const r = rowOf(model); if (!r) return {}
      return { class: `gc ${c.align ? 'a-' + c.align : ''} ${canEdit(c, r) ? 'ed' : 'ro'} ${c.cls?.(r) ?? ''}`, 'data-k': model.__k as string, 'data-p': c.prop, title: c.tip?.(r) } },
    cellTemplate: c.show ? ((h: H, p: { model: Record<string, unknown> }) => { const r = rowOf(p.model); if (!r) return ''; const v = c.show!(r, h); return v == null || v === false ? '' : typeof v === 'object' ? v : String(v) }) as never : undefined,
  }) as ColumnRegular), [cols, readOnly]) // eslint-disable-line react-hooks/exhaustive-deps

  const colOf = (p: string) => cols.find(c => c.prop === p)
  function single(e: CustomEvent<{ model: Record<string, unknown>; prop: string | number; val?: unknown }>) {
    e.preventDefault(); const r = rowOf(e.detail.model), c = colOf(String(e.detail.prop))
    if (r && canEdit(c, r)) onEdit?.([{ row: r, prop: c!.prop, value: e.detail.val == null ? '' : String(e.detail.val) }])
  }
  function range(e: CustomEvent<{ data: Record<number, Record<string, unknown>>; models: Record<number, Record<string, unknown> | undefined> }>) {
    e.preventDefault(); const out: GridEdit<R>[] = []
    for (const [i, changed] of Object.entries(e.detail.data)) {
      const r = rowOf(e.detail.models[+i]); if (!r) continue
      for (const [p, v] of Object.entries(changed)) { const c = colOf(p); if (c && !p.startsWith('__') && canEdit(c, r)) out.push({ row: r, prop: p, value: v == null ? '' : String(v) }) }
    }
    if (out.length) onEdit?.(out)
  }
  useEffect(() => { if (focusKey == null) return; const i = rows.findIndex(r => rowKey(r) === focusKey); if (i >= 0) grid.current?.scrollToRow(Math.max(0, i - 3)) }, [focusKey]) // eslint-disable-line react-hooks/exhaustive-deps
  function moved(e: CustomEvent<{ from: number; to: number }>) {
    e.preventDefault(); const r = rows[e.detail.from], t = rows[e.detail.to > e.detail.from ? e.detail.to + 1 : e.detail.to]
    if (r && onRowMove) onRowMove(r, t ?? rows[e.detail.to])
  }
  const target = (e: Event) => { const el = (e.target as HTMLElement).closest('[data-k]') as HTMLElement | null
    return el ? { r: byKey.current.get(el.dataset.k!) ?? null, p: el.dataset.p ?? null } : { r: null, p: null } }

  return <div ref={wrap} className={`xgrid ${className ?? ''}`} style={{ height }}
    onContextMenu={e => { if (!onMenu) return; const t = target(e.nativeEvent); if (!t.r) return; e.preventDefault(); onMenu(e.nativeEvent, t.r, t.p) }}
    onClick={e => { if (!onCellClick) return; const t = target(e.nativeEvent); if (t.r && t.p) onCellClick(t.r, t.p) }}>
    <RevoGrid ref={grid} columns={columns} source={source} pinnedTopSource={pinTop} pinnedBottomSource={pinBottom} rowClass="__cls" theme="compact" rowSize={rowSize}
      range resize canFocus useClipboard readonly={readOnly} hideAttribution
      onBeforeedit={single as never} onBeforerangeedit={range as never} onRoworderchanged={moved as never} />
  </div>
}

/** Numbers typed or pasted from Excel: "$1,234.50", "(200)", "12%" → number (null when it isn't one). */
export function pasteNum(v: string): number | null {
  let s = v.trim().replace(/[$,\s]/g, ''); if (!s) return null
  const neg = /^\(.*\)$/.test(s); if (neg) s = s.slice(1, -1)
  if (s.endsWith('%')) s = s.slice(0, -1)
  const n = Number(s); return isNaN(n) ? null : neg ? -n : n
}
