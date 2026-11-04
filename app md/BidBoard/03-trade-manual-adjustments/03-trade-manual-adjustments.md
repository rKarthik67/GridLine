## Title
Bid board — Trade manual adjustments (plug, adjustment, notes)

### Summary
The small button on each trade's "Picked value" tile opens a modal where the estimator can set a plug amount (a manual value used when no bid is picked), an adjustment added to the picked bid's total, a client-visible note, and a team-only note. This covers trades that have no clean apples-to-apples bid, or need a flagged exception.

### User story
As an estimator, I want to set a plug amount, an adjustment to the picked bid, or a note on a trade so that I can represent costs that don't come directly from a posted bid, and record why.

### Acceptance criteria
- [ ] The adjust button (bolt/note/sliders icon depending on current state) only opens the modal when `canEdit` is true; it's still visible but inert otherwise.
- [ ] The modal pre-fills Plug amount, Adjustment, Client note, and Team note from the trade's current values.
- [ ] Clearing the Plug amount field (leaving it blank) sends `clearPlug: true` rather than `plugAmount: 0`, which nulls the column instead of zeroing it; entering a number sends `plugAmount`.
- [ ] Adjustment is always sent as a number (blank → `0`); it is described in the UI as "Added to the picked bid's total."
- [ ] Client note and Team note are sent verbatim (empty string clears the note — see backend: `nullif(@ClientNote, '')`); the client note is captioned "Shown to the client" and the team note "Only your team sees this" everywhere they're displayed (drawer and row).
- [ ] On save, the row updates in place (picked value, "Plug value"/adjustment caption, plug/note badges on the trade-name cell and the Plug & notes column) and a toast "Trade adjustment saved" appears.
- [ ] A stale save (another tab changed the trade since this tab loaded it) is rejected with a 409 "clash" naming who changed it first — see Architecture.md for the optimistic-concurrency pattern; this ticket's save includes the trade's `rowRev`.
- [ ] Trades with a non-null `plugAmount` show a bolt icon and "Plug value" caption when no bid is picked; the plug amount only becomes the trade's `carriedValue` once no bid is carried (a carried bid's total + adjustment still takes priority — see `calcTrade`/`Rules.Trade`).

### Technical spec
**Frontend:** `web/src/pages/project/BidBoard.tsx`, component `AdjustModal` (opened via `setAdj(t)` from the "Picked value" cell's button, state `adj`). On save it builds `{ ...(plug == null ? { clearPlug: true } : { plugAmount: plug }), adjustment: adj ?? 0, clientNote, teamNote }` and calls the page's `patchTrade(t, body, 'Trade adjustment saved')`, which calls `api.patch<TradeReply>('/trades/${t.tradeId}', { ...body, rowRev: t.rowRev })`. On success the modal closes and `after(r)` merges the trade into the `bid-board` cache and invalidates the `price-roll-up` query.
**Backend:** `PATCH /trades/{id:long}` in `Endpoints/TradeEndpoints.cs`. `Access.Trade(w, id)` (editor scope / 423 if locked — see Architecture.md: Authorization/scoping). `TradeEndpoints.CheckRev(w, "Trade", "TradeId", id, body.RowRev)` throws a 409 clash if the row's current `RowRev` doesn't match what the client last saw (`SELECT ... FOR UPDATE` — also takes a row lock for the rest of the request). Body is `TradePatch` (`PlugAmount`, `ClearPlug`, `Adjustment`, `ClientNote`, `TeamNote`, plus the fields used by other tickets: `TradeName`, `CsiCode`, `CodeId`, `BudgetValue`, `ClearBudget`, `EstimatorId`, `BidDueOn`, `SortOrder`, `RowRev`, `ClientHash`). After the update, calls `Recalc.TradeAndSummary(w, id, verb, body.ClientHash)` where `verb` is derived from which fields were sent (`trade.plug` / `trade.note` / `trade.adjust` / `trade.update`, in that priority order) and logs it against subject `trade`/`id` with the before/after snapshot. Returns a `TradeReply`.
**Database:** `UPDATE Trade SET ... Adjustment = coalesce(@Adjustment, Adjustment), PlugAmount = CASE WHEN @ClearPlug THEN NULL ELSE coalesce(@PlugAmount, PlugAmount) END, ClientNote = CASE WHEN @SetClient THEN nullif(@ClientNote, '') ELSE ClientNote END, TeamNote = CASE WHEN @SetTeam THEN nullif(@TeamNote, '') ELSE TeamNote END, ... ChangedBy = @u WHERE TradeId = @id`. Columns live on `Trade` (`db/migrations/001_init.sql`): `Adjustment numeric(16,2) NOT NULL DEFAULT 0`, `PlugAmount numeric(16,2)`, `ClientNote text`, `TeamNote text`. The `TrgTouch` trigger (`CREATE TRIGGER TrgTouch BEFORE UPDATE ON gridline.Trade ...`) sets `ChangedAt := now()` and `RowRev := OLD.RowRev + 1` on this update, which is what later `rowRev` checks compare against. `Recalc.Trade` re-derives `LowestValue`/`CarriedValue`/`BudgetDelta`/`BuyoutPct` from the new `Adjustment`/`PlugAmount`.

### Dependencies
Interacts with "Pick or clear a trade's bid" (adjustment applies on top of whichever bid is picked; plug only takes effect with none picked). Feeds the Price roll-up page totals via `Recalc.Summary`. See Architecture.md: Work unit-of-work (`rowRev`/`ClientHash` concurrency), Recalculation engine.

### Notes / edge cases
- `clearPlug` and `plugAmount` are mutually exclusive in the payload (the frontend only ever sends one), but the server trusts whichever was sent — a client that sent both would have `ClearPlug` win (`CASE WHEN @ClearPlug THEN NULL ELSE ...`).
- Sending `clientNote`/`teamNote` as an empty string **clears** the note (`nullif(..., '')`); omitting the key entirely leaves the existing note untouched — the frontend always sends both keys from the modal's local state, so saving with blank fields clears both notes even if the user didn't intend to touch them.
- The verb logged to `ActivityEvent` (and hence the "someone just changed this" toast text on other tabs, see `web/src/app/verbs.ts`) depends on *which* fields were present in the patch, prioritized plug > note > adjust > generic update — a save that sets both a plug and a note logs as `trade.plug`, not `trade.note`.
- `RowRev`/`CheckRev` locks the trade row (`FOR UPDATE`) for the remainder of the transaction, so a concurrent adjust-and-save from two tabs on the same trade serializes rather than silently overwriting; the loser sees a 409 naming the person who won.
