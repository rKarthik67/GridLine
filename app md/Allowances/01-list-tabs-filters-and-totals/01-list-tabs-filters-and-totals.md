## Title
Allowances — List, tabs, filters and totals

### Summary
Summary tiles, tabs All / Owner prescribed / Miscellaneous, search, status and trade filters, and the allowance list grouped as in the original (owner prescribed first, then miscellaneous by trade).

### User story
As an estimator, I want to see every allowance and what the picked bids carry for it.

### Acceptance criteria
- [ ] Shown only when `feature.provisionalsums` is on.
- [ ] Tiles: Owner prescribed allowances, Miscellaneous allowances, Total project allowances (Σ included contributions).
- [ ] Search "allowance or trade"; status All / Excluded / Included (an allowance shows when any of its trades has that status); Trades multi-select.
- [ ] Columns: select, expand, Allowance (+ "n of m trades included"), Total, Updated.
- [ ] Bulk bar: Deselect, Include, Exclude (all trades of the selected allowances), Delete (confirmation).

### Technical spec
**Frontend:** `web/src/pages/project/Allowances.tsx` (route `allowances`, sidebar *Pricing → Allowances*). Query key `vkey('allowances', versionId)`.

**Backend:** `GET /api/versions/{id}/allowances` → `{ items, summary }`; `POST /api/versions/{id}/allowances/status` `{ items: [{sumId, tradeId}], included }`; `POST /api/versions/{id}/allowances/delete` `{ ids }`.

**Database:** `ProvisionalSum` (Kind `ownerprescribed|miscellaneous`, TradeId for miscellaneous, SumIndex, RowRev), `ProvisionalSumTrade` (SumId, TradeId, Included default false), `ScopeLine.SumId`. Migration 013.

### Dependencies
Original: BidAllowances.js, sp_getAllowanceItemsGrid.

### Notes / edge cases
- Trades start as Excluded, as in the original.
- Deleting archives the allowance lines in every trade and recalculates those trades.
