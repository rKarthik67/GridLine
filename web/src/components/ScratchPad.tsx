// Scratch pad (was: ScratchPad.js on Trade scope): a spreadsheet per trade for notes and quick sums.
// Formulas, fonts, colours, borders, merge, number formats, freeze panes, copy/paste with Excel, Excel export,
// a pop-out window that stays in step, and auto-save. Built on FortuneSheet (MIT) and ExcelJS (MIT).
import { useEffect, useMemo, useRef, useState } from 'react'
import { Workbook } from '@fortune-sheet/react'
import '@fortune-sheet/react/dist/index.css'
import type { Sheet } from '@fortune-sheet/core'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { Icon } from './Icon'
import { Loading } from './ui'
import { vkey } from '../app/hooks'

const ROWS = 200, COLS = 80   // same size as the original's sheet
type Stored = { format?: 'fortune'; sheets?: Sheet[]; cells?: Record<string, string> }

/** Earlier Gridline builds saved { cells: { A1: '…' } }; turn that into a sheet. */
function toSheets(s: Stored | undefined): Sheet[] {
  if (s?.format === 'fortune' && s.sheets?.length) return s.sheets.map(x => ({ ...x, row: Math.max(x.row ?? ROWS, ROWS), column: Math.max(x.column ?? COLS, COLS) }))
  const celldata: Sheet['celldata'] = []
  for (const [k, v] of Object.entries(s?.cells ?? {})) {
    const m = /^([A-Z]+)(\d+)$/.exec(k); if (!m || v === '') continue
    const c = [...m[1]].reduce((n, ch) => n * 26 + ch.charCodeAt(0) - 64, 0) - 1, r = +m[2] - 1
    celldata.push({ r, c, v: v.startsWith('=') ? { f: v } : { v, m: v } })
  }
  return [{ name: 'Sheet1', id: 's1', order: 0, status: 1, row: ROWS, column: COLS, celldata }]
}
/** Keep only what's needed to rebuild the sheet (the live sheet also carries a full 2-D copy of every cell). */
function toStored(sheets: Sheet[]): Stored {
  return { format: 'fortune', sheets: sheets.map(s => {
    const celldata: NonNullable<Sheet['celldata']> = []
    s.data?.forEach((row, r) => row?.forEach((v, c) => { if (v && (v.v != null || v.f || v.bg || v.mc || v.bl || v.ps)) celldata.push({ r, c, v }) }))
    return { name: s.name, id: s.id, order: s.order, status: s.status, color: s.color, row: s.row, column: s.column, config: s.config, frozen: s.frozen,
      celldata: s.data ? celldata : s.celldata, luckysheet_conditionformat_save: s.luckysheet_conditionformat_save, dataVerification: s.dataVerification, hyperlink: s.hyperlink } as Sheet })
  }
}

export function ScratchPad({ tradeId, vid, canEdit, title, popout = true }: { tradeId: number; vid: number; canEdit: boolean; title?: string; popout?: boolean }) {
  const key = vkey('scratch', vid, tradeId); const qc = useQueryClient()
  const { data, isLoading, dataUpdatedAt } = useQuery({ queryKey: key, queryFn: () => api.get<Stored>(`/trades/${tradeId}/scratch`) })
  const [state, setState] = useState<'saved' | 'saving' | 'unsaved' | 'error'>('saved')
  const latest = useRef<Sheet[] | null>(null); const timer = useRef<number>(0); const mine = useRef(0); const bc = useRef<BroadcastChannel | null>(null)
  // a fresh copy of the sheet only when someone else (another person, or the pop-out window) saved it
  const [gen, setGen] = useState(0); const sheets = useMemo(() => toSheets(data), [gen, data === undefined]) // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { if (data && Date.now() - mine.current > 4000 && state === 'saved') setGen(g => g + 1) }, [dataUpdatedAt]) // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    try { bc.current = new BroadcastChannel(`gridline-scratch-${tradeId}`) } catch { bc.current = null }
    if (bc.current) bc.current.onmessage = () => qc.invalidateQueries({ queryKey: key })
    return () => { flush(); bc.current?.close() }
  }, [tradeId]) // eslint-disable-line react-hooks/exhaustive-deps

  async function flush() {
    window.clearTimeout(timer.current); const s = latest.current; if (!s) return
    latest.current = null; setState('saving'); mine.current = Date.now()
    try { await api.put(`/trades/${tradeId}/scratch`, toStored(s)); mine.current = Date.now(); setState(latest.current ? 'unsaved' : 'saved'); bc.current?.postMessage('saved') }
    catch { setState('error'); latest.current = latest.current ?? s }
  }
  // the sheet reports itself once as it opens; only save when the content really differs from what was last saved
  const base = useRef<string | null>(null); useEffect(() => { base.current = null }, [gen])
  const changed = (s: Sheet[]) => { if (!canEdit) return; const str = JSON.stringify(toStored(s))
    if (base.current == null || base.current === str) { base.current = str; return }
    base.current = str; latest.current = s; setState('unsaved'); window.clearTimeout(timer.current); timer.current = window.setTimeout(flush, 1200) }
  useEffect(() => { const f = () => { if (latest.current) flush() }; window.addEventListener('beforeunload', f); return () => window.removeEventListener('beforeunload', f) })

  async function exportXlsx() {
    const ExcelJS = (await import('exceljs')).default
    const wb = new ExcelJS.Workbook(); const src = latest.current ?? sheets
    for (const s of src) {
      const ws = wb.addWorksheet(s.name || 'Sheet1')
      const cells = s.data ? s.data.flatMap((row, r) => (row ?? []).map((v, c) => ({ r, c, v }))).filter(x => x.v) : s.celldata ?? []
      for (const { r, c, v } of cells) {
        if (!v) continue; const cell = ws.getCell(r + 1, c + 1)
        if (v.f) cell.value = { formula: String(v.f).replace(/^=/, ''), result: v.v as never }
        else if (v.v != null) cell.value = typeof v.v === 'number' ? v.v : (v.ct?.t === 'n' && !isNaN(+v.v) ? +v.v : String(v.m ?? v.v))
        const fmt = v.ct?.fa; if (fmt && fmt !== 'General' && fmt !== '@') cell.numFmt = fmt
        cell.font = { bold: v.bl === 1, italic: v.it === 1, underline: v.un ? true : undefined, strike: v.cl === 1, name: typeof v.ff === 'string' ? v.ff : undefined, size: v.fs ? +v.fs : undefined,
          color: v.fc ? { argb: 'FF' + String(v.fc).replace('#', '').toUpperCase().padEnd(6, '0').slice(0, 6) } : undefined }
        if (v.bg) cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF' + String(v.bg).replace('#', '').toUpperCase().padEnd(6, '0').slice(0, 6) } }
        const h = v.ht === 0 ? 'center' : v.ht === 2 ? 'right' : v.ht === 1 ? 'left' : undefined, vt = v.vt === 0 ? 'middle' : v.vt === 1 ? 'top' : v.vt === 2 ? 'bottom' : undefined
        if (h || vt || v.tb === '2') cell.alignment = { horizontal: h, vertical: vt, wrapText: v.tb === '2' }
      }
      for (const m of Object.values(s.config?.merge ?? {})) ws.mergeCells(m.r + 1, m.c + 1, m.r + m.rs, m.c + m.cs)
      for (const [c, w] of Object.entries(s.config?.columnlen ?? {})) ws.getColumn(+c + 1).width = Math.round(+w / 7)
      for (const [r, h] of Object.entries(s.config?.rowlen ?? {})) ws.getRow(+r + 1).height = Math.round(+h * 0.75)
      const fz = s.frozen
      if (fz) { const rr = (fz.range?.row_focus ?? 0) + 1, cc = (fz.range?.column_focus ?? 0) + 1
        const y = fz.type === 'row' || fz.type === 'both' ? 1 : fz.type === 'rangeRow' || fz.type === 'rangeBoth' ? rr : 0
        const x = fz.type === 'column' || fz.type === 'both' ? 1 : fz.type === 'rangeColumn' || fz.type === 'rangeBoth' ? cc : 0
        if (x || y) ws.views = [{ state: 'frozen', xSplit: x, ySplit: y }] }
    }
    const buf = await wb.xlsx.writeBuffer(); const url = URL.createObjectURL(new Blob([buf], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }))
    const a = document.createElement('a'); a.href = url; a.download = `Scratch pad${title ? ' - ' + title : ''}.xlsx`.replace(/[\\/:*?"<>|]/g, '-'); a.click(); setTimeout(() => URL.revokeObjectURL(url), 2000)
  }
  function openPopup() {
    flush()
    const w = Math.min(1400, screen.availWidth - 80), h = Math.min(900, screen.availHeight - 80)
    window.open(`/scratch/${vid}/${tradeId}?title=${encodeURIComponent(title ?? '')}`, `gridline-scratch-${tradeId}`, `width=${w},height=${h},left=40,top=40,resizable=yes`)
  }

  if (isLoading || !data) return <Loading what="the scratch pad" />
  return <div className="scratch">
    <div className="scratch-bar">
      <span className="mut">Your notes and quick sums for this trade. Formulas work like Excel (=SUM(A1:A9)); copy and paste to and from Excel. Not part of the totals.</span>
      <span className="sp" />
      <span className={`save-st ${state}`}>{state === 'saved' ? <><Icon name="check" size="s" />Saved</> : state === 'saving' ? 'Saving…' : state === 'unsaved' ? 'Unsaved changes' : <><Icon name="alert" size="s" />Not saved · <button className="lnk" onClick={flush}>retry</button></>}</span>
      <button className="btn sm" onClick={exportXlsx} data-was="Export to Excel"><Icon name="down" size="s" />Export to Excel</button>
      {popout && <button className="btn sm" onClick={openPopup} title="Open the scratch pad in its own window"><Icon name="expand" size="s" />Pop out</button>}
    </div>
    <div className="scratch-sheet"><Workbook key={gen} data={sheets} onChange={changed} allowEdit={canEdit} showSheetTabs row={ROWS} column={COLS} lang="en" /></div>
  </div>
}
