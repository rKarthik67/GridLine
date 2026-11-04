## Title
Bid board — Pick or clear a trade's winning bid

### Summary
Clicking a posted bid tile carries that bid as the trade's picked value; clicking the already-picked tile clears the pick. The browser updates the row instantly (rank, picked value, footer totals) using the shared calc engine, then the server confirms and recalculates the whole version's roll-up, and every other open tab sees the pick live.

### User story
As an estimator leveling bids, I want to click a bidder's tile to carry their price (or click it again to clear it) so that the trade's value, the roll-up, and everyone else's view update immediately without a separate "save" step.

### Acceptance criteria
- [ ] Clicking a bid tile only reacts when the bid `isPosted` (has a total amount and isn't in an unposted state); unposted tiles are inert (`title="Not posted"`, no pointer handler fires the pick).
- [ ] If `project.access === 'viewer'` or `version.status === 'locked'`, clicking shows the toast "This version is read-only for you" and makes no request.
- [ ] Clicking a bid whose `state` is `disqualified` or `declined` shows the toast "Disqualified or declined bids can't be picked" and makes no request.
- [ ] Clicking the currently-picked tile clears the pick (sends `bidId: null`); clicking a different posted tile sets that bid as picked (sends `bidId` of the clicked bid) and automatically un-picks whatever was previously picked on that trade.
- [ ] The click first applies an optimistic update: the row's bids get `isCarried` recomputed, and `calcTrade(...)` (shared `@gridline/calc` package) recomputes `rank`, `carriedValue`, `lowestValue`, `budgetDelta` for instant feedback — this happens before the network call resolves.
- [ ] On success, a toast reads "Picked {bidderName} for {tradeName} · everyone sees it now" (pick) or "Cleared the pick on {tradeName}" (clear), and the price roll-up query is invalidated so its totals refresh.
- [ ] On failure (including a 403/423 from a stale `canEdit` check, or any other API error), the row is rolled back to its pre-click state (`setTrade(before)`).
- [ ] A bid that is not on the trade, or whose state is `disqualified`/`declined`, cannot be picked server-side either — the server independently re-validates and returns a 400 even if the client-side guard were bypassed.
- [ ] Requires capability `trade.pick`; a user without it gets a 403 regardless of their access level.

### Technical spec
**Frontend:** `web/src/pages/project/BidBoard.tsx`, function `pick(t, b)` and helper `recalc(t, pickBidId)` (calls `calcTrade` from `@gridline/calc` with `{ tradeId, adjustment, plugAmount, budgetValue, bids }`). Calls `api.put<TradeReply>('/trades/${t.tradeId}/pick', { bidId: next })` via the shared `useAction().run` wrapper (which surfaces `ApiError` as a toast). `after(r)` merges the returned `TradeReply.trade` into the `bid-board` query cache (`qc.setQueryData`) and invalidates `vkey('price-roll-up', vid)`. The bid tile component is the inner `Tile` function; hovering a tile also calls `focus('trade', String(t.tradeId), 'viewing')` over SignalR (see Architecture.md: Realtime).
**Backend:** `PUT /trades/{id:long}/pick` in `Endpoints/TradeEndpoints.cs`. `w.User.Require("trade.pick")`, then `Access.Trade(w, id)` (editor-level scope, throws 423 if the version is locked and the caller isn't owner/editor — see Architecture.md: Authorization/scoping). Body is `CarryIn(long? BidId, string? ClientHash)`. If `BidId` is set, it must resolve to a non-archived `Bid` on that `Trade` (`ApiException.Bad` otherwise) and must not be `disqualified`/`declined`. Clears any currently-carried bid on the trade, sets the new one carried (if any), bumps `Trade.ChangedBy`, then calls `Recalc.TradeAndSummary(w, id, "trade.pick", body.ClientHash)` and logs `bid.pick` or `bid.unpick` (verb depends on whether `BidId` was null) with subject type `trade`. Returns `{ trade, totalsHash, matched }` (a `TradeReply`).
**Database:** `UPDATE Bid SET IsCarried = false ... WHERE TradeId = @id AND IsCarried` then (conditionally) `UPDATE Bid SET IsCarried = true ... WHERE BidId = @b2`; `UPDATE Trade SET ChangedBy = @u WHERE TradeId = @id`. The unique partial index `UxOneCarriedBid ON Bid (TradeId) WHERE IsCarried AND ArchivedAt IS NULL` enforces at most one carried bid per trade. `Recalc.Trade` (`Calc/Recalc.cs`) then recomputes and stores `Trade.LowestValue`, `CarriedValue`, `BudgetDelta`, `BuyoutPct` and (if the picked/lowest bid has zone amounts from Scope match) rewrites `TradeZone`/`Trade.ZonesFromScope`; `Recalc.Summary` rewrites `RollupLine`/`RollupZone` and `ProjectVersion.TotalsHash`, and inserts a `CalcRun` row (comparing the client's `ClientHash` to the server's). `TrgTouch` on `Bid`/`Trade` bumps `RowRev`/`ChangedAt` on every row the two `UPDATE`s touch.

### Dependencies
Depends on bids already existing on the trade (see "Add a bidder to a trade") and on bid status/base-amount edits (see "Bid details drawer") to make a bid eligible (`isPosted`). Feeds the Price roll-up page's totals and the zone-split display ("Zone split a trade") when `ZonesFromScope` is true. See Architecture.md: Work unit-of-work + SignalR broadcast, Recalculation engine.

### Notes / edge cases
- The optimistic client recalculation and the server's authoritative recalculation can disagree (e.g. if another tab changed scope adjustments between page-load and this click); the server's `TradeReply` always wins and replaces the optimistic row, and `CalcRun.Matched` records whether the two agreed, for debugging drift rather than for blocking the user.
- Picking a bid can silently change the trade's zone split: if the newly-picked bid's `ScopeAnswer` zone amounts exist (or the version has a remainder zone), `Recalc.Trade` overwrites `TradeZone` and flips `Trade.ZonesFromScope` to true, which also changes what the "Zone split" modal shows and whether it's editable.
- Clearing a pick does not restore the previous bid's own `IsCarried`/rank state from before any intervening edits — it is recomputed fresh from current bid data.
- Because `UxOneCarriedBid` is a DB-level unique partial index, any code path that tries to carry two bids on one trade concurrently (e.g. a race between two tabs) will fail at the database rather than silently leaving two carried bids.
