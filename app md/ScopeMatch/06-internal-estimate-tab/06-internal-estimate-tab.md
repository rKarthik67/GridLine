## Title
Scope match — Internal estimate tab (quantity × unit cost)

### Summary
A simple, separate quantity-takeoff style worksheet per trade — line item, quantity, unit of measure, unit cost — that the estimator keeps for their own sanity check against the bids. It is explicitly not part of the trade's scope-adjustment or roll-up totals.

### User story
As an estimator, I want to keep my own rough quantity × unit-cost estimate for a trade alongside the bids, so that I can sanity-check what subcontractors are pricing without that estimate affecting the carried total.

### Acceptance criteria
- [ ] The "Internal estimate" tab shows a note: "Your own estimate for this trade (quantity × unit cost), to check the bids against. It is not part of the totals."
- [ ] Each row has a free-text item description, a numeric quantity, a unit-of-measure dropdown (from `lk.data.units`), a numeric unit cost, and a read-only computed line total (`qty × unitCost`, displayed to 2 decimals); a per-row delete button when editable.
- [ ] "Add row" appends a blank row (`lineText: '', qty: null, uomCode: null, unitCost: null`) to the bottom of the in-memory list; rows are not persisted until "Save estimate" is pressed.
- [ ] The footer total sums `qty × unitCost` (rounded to cents) across all rows and updates live as the user types, before saving.
- [ ] "Save estimate" replaces the entire row set for the trade in one call; rows that are entirely blank (no text, no qty, no unit cost) are silently dropped on save; a success toast "Estimate saved" is shown and the tab's query is invalidated/refetched (server-side `LineTotal`, a generated column, becomes the source of truth after save).
- [ ] All inputs, "Add row", per-row delete, and "Save estimate" are disabled/hidden when `!canEdit`.
- [ ] Loading shows "Loading the estimate…" (`Loading what="the estimate"` component) until the first fetch completes.

### Technical spec
**Frontend:** `web/src/pages/project/ScopeMatch.tsx` — `EstimateTab({ tradeId, vid, canEdit })`, local `EstRow` interface `{ lineText, qty, uomCode, unitCost, lineTotal? }`. Data: `useQuery(['estimate', vid, tradeId], () => api.get<{ rows: EstRow[]; total: number }>('/trades/{tradeId}/estimate'))`. Save: `api.put('/trades/{tradeId}/estimate', rows)` where `rows` is the full in-memory array (client sends every row, including ones the server will drop). Units come from `useLookups()` (`lk.data.units`, each `{ uomCode }`).
**Backend:** `GET /trades/{id:long}/estimate` (`ScopeEndpoints`) — `Access.Trade` (viewer+ for GET); returns `{ rows, total }` via the shared `Estimate()` helper (`SELECT ... FROM EstimateLine WHERE TradeId = @id ORDER BY SortKey` plus `SELECT sum(LineTotal) ...`). `PUT /trades/{id:long}/estimate` (`ScopeEndpoints`) — `Access.Trade` (editor+); **deletes all existing `EstimateLine` rows for the trade** then re-inserts the non-blank ones in posted order with a fresh `SortKey` (1, 2, 3…); logs `estimate.update` with the row count; no recalculation is triggered (this data doesn't feed `Recalc`); returns the freshly reloaded `{ rows, total }`.
**Database:** `EstimateLine` (`EstimateLineId` PK, `TradeId` FK→`Trade` cascade, `SortKey numeric(12,4)`, `LineText`, `Qty numeric(14,4)`, `UomCode` FK→`UnitOfMeasure`, `UnitCost numeric(14,4)`, `LineTotal numeric(16,2) GENERATED ALWAYS AS (round(coalesce(Qty,0) * coalesce(UnitCost,0), 2)) STORED` — computed by Postgres, not written directly). No triggers fire on this table (it's not in the `TrgTouch` loop and has no `RowRev`/`ChangedAt` columns, so there is no optimistic-concurrency protection or audit timestamp on individual rows — only the `estimate.update` `ActivityEvent` records that *a* save happened).

### Dependencies
- Self-contained; does not affect `Trade.CarriedValue`/`LowestValue`, scope adjustments, or the Price roll-up.
- Shares `useLookups()`'s unit-of-measure list with other pages (Crew plan, Precon services/Overheads) that also use `UnitOfMeasure`.
- See `_Overview/Architecture.md`: Authorization/scoping (editor-level write), Unit of work (log-only here — no recalculation).

### Notes / edge cases
- Save is a **full delete-and-reinsert**, not a diff/upsert — two tabs saving the estimate concurrently will silently clobber each other with no conflict detection (there's no `RowRev` on `EstimateLine` and no `rowRev` field in the request), unlike almost every other mutable grid on this page.
- Because rows are only persisted on explicit "Save estimate", navigating away or switching trades/tabs without saving discards in-progress edits with no warning dialog.
- `LineTotal` is a Postgres generated/stored column — do not attempt to `INSERT`/`UPDATE` it directly; it is computed from `Qty`/`UnitCost` automatically and any mismatch between the client's locally computed total and the server's after save indicates a rounding difference, not a bug in the client math alone.
- `UomCode` is upper-cased and trimmed server-side (`UomCode?.Trim().ToUpperInvariant()`) before insert — client-side values are not normalized the same way until round-tripped.
