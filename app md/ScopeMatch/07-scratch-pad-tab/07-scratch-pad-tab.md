## Title
Scope match — Scratch pad (Excel-like sheet per trade)

### Summary
The **Scratch pad** tab (was: ScratchPad.js on Trade scope) is a full spreadsheet per trade built on FortuneSheet (MIT): formulas, fonts, colours, borders, merge, number formats, freeze panes, several sheets, copy/paste with Excel, export to .xlsx and a pop-out window. It is not part of any total.

### User story
As an estimator, I want quick sums and notes next to a trade, so that I don't need a separate Excel file.

### Acceptance criteria
- [ ] The sheet is 200 rows × 80 columns (like the original); formulas such as `=SUM(A1:A9)` work.
- [ ] Changes save automatically 1.2 s after the last edit; the bar shows Saved / Saving… / Unsaved changes / Not saved · retry. Opening the sheet doesn't save anything by itself.
- [ ] **Export to Excel** writes an .xlsx with values, formulas, number formats, bold/italic/underline/strike, font, colours, fill, alignment, wrap, merges, column widths, row heights and frozen panes (ExcelJS, loaded on click).
- [ ] **Pop out** opens `/scratch/{versionId}/{tradeId}` in its own window; the two windows stay in step through a `BroadcastChannel`, and a save by another person refreshes the sheet when there are no unsaved local edits.
- [ ] Sheets saved by earlier builds as `{ cells: { A1: … } }` are converted on load.

### Technical spec
**Frontend:** `web/src/components/ScratchPad.tsx` (lazy-loaded from `ScopeMatch.tsx`), `web/src/pages/ScratchWindow.tsx` (route in `App.tsx`). Stored format: `{ format: 'fortune', sheets: [{ name, id, order, row, column, celldata, config (merge, columnlen, rowlen…), frozen, … }] }`.

**Backend:** `GET /api/trades/{id}/scratch` returns the stored JSON (or `{}`); `PUT /api/trades/{id}/scratch` stores any JSON up to 1 MB (“The scratch pad is too large (1 MB max).”); logs `scratch.update`.

**Database:** `ScratchSheet (TradeId PK, Sheet jsonb, ChangedAt)`.

### Dependencies
- ScopeMatch/01-scope-item-answer-grid

### Notes / edge cases
- The original used HyperFormula (GPL); Gridline uses FortuneSheet's own MIT formula engine instead.
