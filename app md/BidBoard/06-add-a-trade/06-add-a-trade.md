## Title
Bid board — Add a trade

### Summary
The "Add trade" button opens a modal to create a new trade row on the version: a CSI code (autocompleted against the code book), an optional name override, a starting budget, and an optional scope kit to pre-populate its scope lines. The new trade is created ready to bid, with the version's base requirements, one scope line per zone, and a default zone split already applied.

### User story
As an estimator, I want to add a new trade by CSI code so that I can start collecting bids for a scope that isn't already on the board.

### Acceptance criteria
- [ ] The button is visible only to editors (`canEdit`); it is hidden for viewers and on locked versions.
- [ ] Typing in the CSI code field shows up to 8 matching codes from the lookups cache (`csiCode`/`codeTitle` containing the typed text); selecting one fills the code and, if the Trade name field is still empty, the code's title.
- [ ] The Add button is disabled until a CSI code is entered (name is optional — it falls back to the matched code's title, or the request fails if no title can be resolved either way).
- [ ] An optional starting Budget and an optional Scope kit (from the active `ScopeKit` list) can be chosen; the modal notes "The new trade gets the version's base requirements and one line per zone in Scope match."
- [ ] On success the modal closes, the bid-board and price-roll-up queries are invalidated, and the new trade appears in the grid (sorted after existing trades by `SortOrder`, then `CsiCode`, then `ScopeNo`).
- [ ] A CSI code is normalised before saving: "084400", "08-44-00", and "08 44 00" are all accepted and stored as "08 44 00"; anything that isn't exactly 6 digits is kept exactly as typed.
- [ ] Adding a second trade with the same CSI code on the same version is allowed — it gets the next `ScopeNo` after the existing one(s) for that code, rather than being rejected as a duplicate.

### Technical spec
**Frontend:** `web/src/pages/project/BidBoard.tsx`, component `AddTradeModal` (`setAddTrade(true)`). CSI/kit options come from `useLookups()` (`GET /lookups`, cached 10 min) — `lk.data.csiCodes` and `lk.data.scopeKits`. Submit calls `api.post('/versions/${versionId}/trades', { csiCode, tradeName, budgetValue, kitId })`; on success it invalidates `vkey('bid-board', vid)` and `vkey('price-roll-up', vid)`.
**Backend:** `POST /versions/{id:long}/trades` in `Endpoints/TradeEndpoints.cs`. `Access.Version(w, id)` (editor scope). `ResolveCode(w, body.CsiCode, body.TradeName, body.CodeId)` normalises the CSI code and resolves it against `WorkCode` (preferring the current `CodeEdition`), or uses the given `CodeId` directly; throws 400 if no code/title can be determined. Computes the next `ScopeNo` for that CSI code on the version. Inserts the `Trade` row, then: `ScopeSetup.AddStandardLines(w, tid)` (copies the version's `RequirementSet` lines plus one zone-section line per `Zone` into `ScopeLine`), `ScopeSetup.DefaultZoneSplit(w, tid)` (seeds `TradeZone` from each `Zone.DefaultShare` when none exist), and — if a kit was chosen — `ScopeEndpoints.ApplyKit(w, tid, kitId)` (appends the kit's `ScopeKitLine`s as `tradeitem` scope lines, skipping any whose text already exists on the trade). Then `Recalc.TradeAndSummary(w, tid, "trade.create")`, logs `trade.create`, and returns `201 /api/trades/{tid}` with a `TradeReply`.
**Database:** Inserts into `Trade` (`VersionId`, `CodeId`, `CsiCode`, `TradeName`, `ScopeNo`, `KitId`, `EstimatorId`, `BidDueOn`, `BudgetValue`, `SortOrder` = `max(SortOrder)+10` for the version, `ChangedBy`). Unique index `UxTradeCode ON Trade (VersionId, CsiCode, ScopeNo) WHERE ArchivedAt IS NULL` is what makes repeated codes safe (different `ScopeNo`) rather than erroring. `Trade.ProjectId` is set automatically by trigger `TrgTradeProject BEFORE INSERT OR UPDATE OF VersionId ON Trade` (`SetProjectId()`). `ScopeSetup.AddStandardLines` inserts into `ScopeLine` (`Section = 'baserequirement'` from `RequirementLine`/`ProjectVersion.RequirementSetId`, and `Section = 'zone'` one per `Zone`). `ScopeSetup.DefaultZoneSplit` inserts into `TradeZone` (`TradeId`, `ZoneId`, `Share`) from `Zone.DefaultShare`. `ScopeEndpoints.ApplyKit` inserts into `ScopeLine` (`Section = 'tradeitem'`) from `ScopeKitLine`.

### Dependencies
Depends on the version already having `Zone` rows (Project profile → Zones) and a `RequirementSet` (base requirements) for the standard-lines seeding to produce anything; depends on `ScopeKit`/`ScopeKitLine` templates existing for the kit option to do anything. The created trade immediately appears in "Browse, search, and filter the bid board" and becomes editable via every other Bid board ticket, and its scope lines are what Scope match (separate page) displays. See Architecture.md: Recalculation engine, DB schema shape.

### Notes / edge cases
- Because `ScopeNo` (not `CsiCode` alone) disambiguates trades, the same CSI code can legitimately appear more than once on a version (e.g. splitting one trade into two bid packages); don't assume CSI code is unique per version when building other features against this table.
- `CodeId` resolution prefers the **current** `CodeEdition` (`ORDER BY e.IsCurrent DESC LIMIT 1`) when a code matches multiple historical editions — an older edition's code with the same digits still resolves, just to the current edition's title/metadata.
- The scope kit is applied via a text-equality de-dupe (`lower(l.LineText) = lower(k.LineText)`), not an identity/foreign-key link — editing a kit line's wording later does not retroactively change trades that already had it applied, and re-applying the same kit will skip lines whose exact wording is already present.
- `Recalc.TradeAndSummary` runs even though a brand-new trade has no bids yet; this is what sets its initial (null) `LowestValue`/`CarriedValue` and folds its `BudgetValue` into the roll-up immediately, before any bid exists.
