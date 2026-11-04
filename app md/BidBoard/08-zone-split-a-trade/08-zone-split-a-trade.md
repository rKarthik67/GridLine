## Title
Bid board — Zone split a trade's value across zones

### Summary
When a version has zones (breakout areas), each trade's carried/lowest value can be split across them for zone-level totals and $/SF. The split is either entered by hand in a modal, defaulted from each zone's default share, or — once any bidder enters per-zone amounts in Scope match — derived automatically and locked from manual editing.

### User story
As an estimator working on a multi-zone project, I want to split a trade's value across the project's zones (or see why it's locked to the Scope match amounts) so that each zone's roll-up and $/SF numbers are accurate.

### Acceptance criteria
- [ ] "Zone split" (row kebab menu) is unavailable in the menu bar styling as "Zone split" normally, but is relabelled "Zone split (from Scope match zone amounts)" when `t.zonesFromScope` is true.
- [ ] If the version has no zones, the modal just explains "This version has no zones. Add zones in Project profile → Zones." with no inputs.
- [ ] If `t.zonesFromScope` is true, the modal is **read-only**: it shows each zone's current share as a percentage and explains the split comes from the zone amounts entered in Scope match for whichever bid is driving it (picked bid if one exists, otherwise lowest), noting the remainder zone absorbs what's left of the bid total when the version has one; it tells the user to clear the Scope match zone amounts (and remove the remainder zone, if any) to split by hand instead.
- [ ] Otherwise the modal is editable: one percentage input per zone, a running total badge (green at exactly 100%, red otherwise), a "Use default split" button that fills every zone from `Zone.defaultShare`, and Save disabled unless the total is within 0.05% of 100%.
- [ ] Saving sends only the zones with a non-zero value, as fractions (percentage ÷ 100).
- [ ] Saving when the trade's split is actually `ZonesFromScope` server-side is rejected (400) even if the client's stale copy didn't know that — "This trade's zone split comes from the zone amounts in Scope match. Change them there (or clear them) to split it by hand."
- [ ] A negative percentage, or shares that don't sum to 100% (± 0.05%), is rejected (400) with the actual computed percentage quoted back.
- [ ] A zone code that isn't part of this version is rejected (400, "Zone '{code}' is not in this version.").
- [ ] On success the modal closes and the trade row's zone columns (Total, $/SF per zone) update immediately.

### Technical spec
**Frontend:** `web/src/pages/project/BidBoard.tsx`, component `SplitModal` (`setSplitFor(t)`). Local state is a `Record<zoneCode, string>` of percentage strings seeded from `t.zoneShares[z.zoneCode] * 100`. Save calls `api.put<TradeReply>('/trades/${t.tradeId}/zones', { shares })` where `shares` is built from the non-empty/non-zero entries converted back to fractions. The reply flows through `onDone(r) → after(r)`, merging the trade and invalidating `price-roll-up`.
**Backend:** `PUT /trades/{id:long}/zones` in `Endpoints/TradeEndpoints.cs`. `Access.Trade(w, id)` (editor scope). Rejects the call outright if `Trade.ZonesFromScope` is currently true. Validates no negative share and (when any shares are given) the sum is within 0.0005 of 1. Replaces the trade's `TradeZone` rows: deletes all, then inserts one row per non-zero share, resolving `ZoneId` by `(VersionId, ZoneCode)` and failing the whole request (400) if a code doesn't resolve. Calls `Recalc.TradeAndSummary(w, id, "trade.zones")`, logs `trade.zones` with the submitted shares as the after-state, returns a `TradeReply`.
**Database:** `TradeZone` (migration 001): `PRIMARY KEY (TradeId, ZoneId)`, `Share numeric(7,4) NOT NULL`, `ON DELETE CASCADE` from both `Trade` and `Zone`. Index `IxTradeZoneZone ON TradeZone (ZoneId)` (migration 003). The endpoint does `DELETE FROM TradeZone WHERE TradeId = @id` then per-zone `INSERT INTO TradeZone (TradeId, ZoneId, Share) SELECT @id, ZoneId, @share FROM Zone WHERE VersionId = @v AND ZoneCode = @code` (the `INSERT ... SELECT` returning 0 rows is how an unknown zone code is detected). `Trade.ZonesFromScope boolean` is the flag this endpoint checks and that `Recalc.DeriveZoneSplit` (see "Pick or clear a trade's bid") flips automatically whenever scope-entered zone amounts exist for the trade's active bid.

### Dependencies
Mutually exclusive with the automatic zone-derivation that `Recalc.Trade`/`Recalc.DeriveZoneSplit` perform whenever a bid has Scope match zone amounts (documented under "Pick or clear a trade's bid"); this ticket's manual split only has effect while that auto-derivation is inactive for the trade. Zone definitions themselves (`Zone`, `Zone.DefaultShare`, `Zone.IsRemainder`) are managed on Project profile → Zones (a different page). Feeds the per-zone Total/$-per-SF columns in "Browse, search, and filter the bid board" and the zone breakdown on the Price roll-up page. See Architecture.md: Recalculation engine.

### Notes / edge cases
- The 100%-total validation only applies when `body.Shares.Count > 0` — submitting an empty shares map (clearing every zone) is valid and simply removes all `TradeZone` rows for the trade, leaving it with a 0% split everywhere until re-split.
- Because the derive-from-scope logic can flip `ZonesFromScope` to true again on the *very next* recalculation triggered by any other edit (a pick, a scope answer change elsewhere), a manual split saved here can be silently superseded without the user taking any zone-related action themselves — if a trade "loses" its manual split, check whether a bid now has Scope match zone amounts.
- The read-only, scope-derived view lists each zone's share as read from `t.zoneShares`, exactly as last computed by `Recalc.Trade`/`DeriveZoneSplit` — it is not recalculated live by this modal, so it can briefly lag a concurrent change from another tab until the `bid-board` query refreshes.
