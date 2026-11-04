## Title
Bid board — Edit trade details and delete a trade

### Summary
The row kebab menu's "Rename / change CSI code" opens a small modal to edit a trade's CSI code, name, and bid-due date; "Delete trade" removes the trade from the board entirely (soft delete). Both are housekeeping actions on an existing trade, separate from its bids or value.

### User story
As an estimator, I want to correct a trade's CSI code, name, or due date, or remove a trade that shouldn't be on this version, so that the bid board accurately reflects the project's scope breakdown.

### Acceptance criteria
- [ ] Both actions are only available from the row kebab menu shown to editors (`canEdit`); hidden/unavailable for viewers and locked versions.
- [ ] The rename modal shows the CSI code, trade name, and bid-due date (date input), pre-filled from the trade, plus a read-only "Last changed {date} by {name}" footer line (blank name omitted when unknown).
- [ ] Saving only sends `csiCode` when it actually changed (`code !== t.csiCode ? code : undefined`), always sends the (possibly unchanged) `tradeName`, and sends `bidDueOn` only when non-empty.
- [ ] An empty trade name is rejected with "Trade name can't be empty."; the request otherwise succeeds and the row updates with the new code/name/due date.
- [ ] Changing the CSI code re-resolves it against the code book (same normalisation as "Add a trade": 6 digits → "NN NN NN" spacing) and updates the matched title's `CodeId`, even though the title text itself is driven by the Trade name field, not the code book, when a name is supplied.
- [ ] "Delete trade" has no confirmation dialog in the current build — clicking it immediately archives the trade; it's styled `danger` in the menu.
- [ ] After deleting, the trade row disappears from the grid and every cached query for this version (bid-board, price-roll-up, scope-match, etc.) is invalidated by key-prefix match (`v{versionId}`).
- [ ] A stale rename save (trade changed by someone else first) returns a 409 naming who changed it (same `rowRev`/`CheckRev` pattern as trade adjustments).

### Technical spec
**Frontend:** `web/src/pages/project/BidBoard.tsx`, component `RenameModal` (`setRenameT(t)`) — save goes through the shared `patchTrade(t, body, 'Trade updated')` → `api.patch<TradeReply>('/trades/${t.tradeId}', { ...body, rowRev: t.rowRev })`. Delete is inline in the row's `Menu` items: `api.del('/trades/${t.tradeId}')`, then `qc.invalidateQueries({ predicate: x => JSON.stringify(x.queryKey).includes('"v${vid}"') })` (broad invalidation across every query keyed to this version via `vkey`), with toast `Deleted {tradeName}`.
**Backend:** `PATCH /trades/{id:long}` — same endpoint as "Trade manual adjustments" (`Endpoints/TradeEndpoints.cs`): `Access.Trade(w, id)`, `CheckRev`, re-resolves the CSI code/title/`CodeId` via `ResolveCode` when `CsiCode` is supplied, validates the trade name isn't blank, updates `TradeName`/`CsiCode`/`CodeId`/`BidDueOn` (and the adjustment/plug/note/budget fields from the other ticket, all via `coalesce`), recalculates, logs `trade.update` (since none of rename's fields match the plug/note/adjust verb precedence), returns a `TradeReply`.
`DELETE /trades/{id:long}`: `Access.Trade(w, id)` (editor scope), sets `ArchivedAt = now()` and `ChangedBy`, calls `Recalc.Summary(w, s.VersionId, "trade.delete")` (recalculates the version roll-up — the trade itself needs no more per-trade recalc since it's gone), logs `trade.delete`, returns `{ totalsHash }` only (no trade payload, since it no longer exists in the active set).
**Database:** `UPDATE Trade SET TradeName = ..., CsiCode = ..., CodeId = ..., BidDueOn = coalesce(@BidDueOn, BidDueOn), ... ChangedBy = @u WHERE TradeId = @id` for rename; `UPDATE Trade SET ArchivedAt = now(), ChangedBy = @u WHERE TradeId = @id` for delete — a soft delete, not a `DELETE FROM Trade`. All `LoadTrades`/roll-up/export queries filter `WHERE ... ArchivedAt IS NULL`, so an archived trade silently disappears everywhere without touching its child `Bid`/`ScopeLine`/`TradeZone` rows. `TrgTouch BEFORE UPDATE ON gridline.Trade` bumps `RowRev`/`ChangedAt` on both operations.

### Dependencies
Delete does not cascade-archive the trade's `Bid`/`ScopeLine`/`TradeZone`/`BidInvite` rows (no DB `ON DELETE` fires for a soft delete) — any ticket relying on "a trade with no bids" vs. "an archived trade's orphaned bids" should account for this. Interacts with "Trade manual adjustments" (same `PATCH` endpoint, same `rowRev` concurrency rules). See Architecture.md: Authorization/scoping, Work unit-of-work.

### Notes / edge cases
- `DELETE /trades/{id}` is a soft delete, so the `UxTradeCode (VersionId, CsiCode, ScopeNo) WHERE ArchivedAt IS NULL` partial unique index allows re-creating a trade with the exact same CSI code/scope number after deleting the old one — the archived row with that code still exists in the table, just excluded by the partial index's predicate.
- An archived trade's `Bid`, `ScopeLine`, `TradeZone`, and `BidInvite` rows are **not** archived or deleted alongside it; they become orphaned-but-present rows pointing at an archived `Trade` (the FKs are `ON DELETE CASCADE`, which only fires on a hard delete, never used here). Any report or admin tool that joins those child tables to `Trade` without the `ArchivedAt IS NULL` filter will surface "ghost" data from deleted trades.
- Renaming a trade's CSI code does not move or rename its existing `ScopeLine`/`ScopeKitLine`-derived rows, benchmark lookups elsewhere key off the *current* `CsiCode`, so a code change immediately changes which cross-project benchmark (see "Browse, search, and filter the bid board") applies to it.
- There is no confirmation step before delete in the UI as written; a misclick immediately soft-deletes the trade (recoverable only via direct database access, since there's no "undo archive" endpoint).
