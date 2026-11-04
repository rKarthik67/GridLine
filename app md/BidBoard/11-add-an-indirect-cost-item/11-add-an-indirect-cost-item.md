## Title
Bid board — Add an indirect cost item

### Summary
Below the trade rows, the "Indirect costs & roll-up" section shows every calculated and custom roll-up line. "Add indirect item" lets the estimator add a new custom cost line (name, amount, and whether it's outside the fee base) that then appears both here and on the Price roll-up page.

### User story
As an estimator, I want to add a custom indirect cost line (like owner IT/AV coordination) so that it's included in the contract roll-up alongside the calculated markups and trade totals.

### Acceptance criteria
- [ ] "Add indirect item" is shown only to editors, in the "Indirect costs & roll-up" section header row.
- [ ] The modal requires a Name (Add is disabled until non-blank) and an Amount (defaults to 0 if left blank), plus an "Outside the fee" checkbox captioned "fee is not charged on this item."
- [ ] On success, the modal closes, a toast "Indirect item added" appears, and the price-roll-up query is invalidated so the new line appears in the Indirect costs & roll-up section and the Price roll-up page.
- [ ] New items default to the end of the sort order (after the current maximum) when no explicit order is given.
- [ ] A blank name is rejected server-side ("Give the line a name.") even if the disabled-button guard were bypassed.

### Technical spec
**Frontend:** `web/src/pages/project/BidBoard.tsx`, exported component `IndirectModal` (`setAddIndirect(true)`), shared with other pages that also add indirect items (it's exported, not page-local). Submit calls `api.post('/versions/${versionId}/indirect', { itemName, amount, outsideFee })`; on success invalidates `vkey('price-roll-up', vid)`.
**Backend:** `POST /versions/{id:long}/indirect` in `Endpoints/SummaryEndpoints.cs`. `Access.Version(w, id)` (editor scope). Validates `ItemName` is non-blank. Inserts the `IndirectItem` row with `Amount` defaulting to 0, `SortOrder` defaulting to `max(SortOrder)+1` for the version, `Description`/`IsOff` left as sent (both unused by this modal, used elsewhere). Calls `Recalc.Summary(w, id, "indirect.create")` (rebuilds `RollupLine`/`RollupZone`/`ProjectVersion.TotalsHash`), logs `indirect.create`, returns the full roll-up (`SummaryEndpoints.Build(w, id)`), which is what both this page's indirect section and the Price roll-up page render from.
**Database:** `IndirectItem` (migration 001, extended in 007): `IndirectId`, `VersionId` (FK `ProjectVersion ON DELETE CASCADE`), `ItemName text NOT NULL`, `Amount numeric(16,2) NOT NULL DEFAULT 0`, `OutsideFee boolean NOT NULL DEFAULT false`, `SortOrder smallint NOT NULL DEFAULT 1`; migration `007_fee_carveouts.sql` adds `Description text` and `IsOff boolean NOT NULL DEFAULT false` (the on/off switch shown elsewhere, not in this modal). `Recalc.Summary` reads `IndirectItem` (treating `IsOff` rows as amount 0) as part of `SummaryInput`, then rewrites `RollupLine`/`RollupZone` from scratch (`DELETE FROM RollupLine WHERE VersionId = @versionId` followed by one insert per line) and records a `CalcRun`.

### Dependencies
The resulting line is rendered by "Browse, search, and filter the bid board" (Indirect costs & roll-up section) and by the Price roll-up page, which also exposes editing/removing/toggling-off indirect items (`PATCH`/`DELETE /indirect/{id}`, not used from this page — documented under PriceRollUp). See Architecture.md: Recalculation engine.

### Notes / edge cases
- This modal only ever creates plain, "on" indirect items with no description — the `Description` and `IsOff` columns exist and are used by the Price roll-up page's editing UI, but are invisible/unreachable from this Bid board ticket's modal.
- `OutsideFee` changes which fee-base calculation the item participates in (`Rules.Summary`), not just its display — getting this checkbox wrong silently changes the contract total's fee math, not just a label.
- `SortOrder` defaults from the *version's* current max indirect sort order at insert time, so two indirect items added in quick succession by different tabs could race to compute the same "next" sort order before either commits; the schema does not enforce uniqueness on `SortOrder`, so this is a display-order collision, not a data-integrity error.
