## Title
Scope match — Scope item answer grid (spreadsheet grid, Excel copy/paste)

### Summary
Scope items are shown in the spreadsheet grid (RevoGrid): sticky #, Scope item and Desired columns, one lane per bidder with a rich lane header, pinned Base bid / Scope adjustments / Total rows, and sections for Base requirements, Trade items and Zones. Answers are typed, pasted from Excel or filled down.

### User story
As an estimator, I want to level bids like a spreadsheet and paste a column of answers from Excel, so that leveling is fast.

### Acceptance criteria
- [ ] A cell takes Included, Excluded, Price, an amount, `=formula` or text; it shows as a chip (✓ Included, ✕ Excluded, $ Price, ±amount, “text”, No answer).
- [ ] Drag across cells or Shift-select, Ctrl+C copies as tab-separated values for Excel; Ctrl+V pastes a block starting at the focused cell; the fill handle drags values down. Every change goes through `saveCells` (browser recalculates first, then `PUT /answers`).
- [ ] Base bid is editable in the pinned top row (amounts like “$1,250,000” or “(500)” are accepted). Zones take amounts only; the remainder zone is calculated and read-only.
- [ ] Lane header: lane letter, price rank, Posted/Not posted, Lowest flag, bidder name, total and % over lowest, coverage bar, state and %, **Pick** switch, and ‹ › × to move or hide the lane.
- [ ] Item text and Desired are editable on trade items; Desired accepts Included, Excluded, Price or Text.
- [ ] Row drag (handle on #) moves trade items; search highlights and scrolls to hits; Gaps only filters; cell colours, notes and teammate presence show on cells; the fx bar edits the selected cell.
- [ ] Right-click menus are unchanged (insert rows, header row, indent, include/exclude from adjustments, colours, notes, delete).

### Technical spec
**Frontend:** `web/src/pages/project/ScopeMatch.tsx` — rows `SRow` (sec | line | zchk | tot), columns `gcols`, `gridEdit()` and `moveLine()`, on `web/src/components/DataGrid.tsx`. See `_Overview/Architecture.md` → Spreadsheet grid (`DataGrid`, RevoGrid).

**Backend:** Unchanged: `GET /api/trades/{id}/scope`, `PUT /api/answers`, `PATCH /api/lines/{id}`, `PATCH /api/bids/{id}`, `PUT /api/trades/{id}/lanes`, `PUT /api/trades/{id}/pick`.

**Database:** `ScopeLine`, `ScopeAnswer`, `Bid`.

### Dependencies
- ScopeMatch/02-scope-item-row-management
- _Overview/Architecture.md → Spreadsheet grid

### Notes / edge cases
- The grid never mutates its own data; it reports edits and is redrawn from the query cache, so live changes from teammates land the same way.
