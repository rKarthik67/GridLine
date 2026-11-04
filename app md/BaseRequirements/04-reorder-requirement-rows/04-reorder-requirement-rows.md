## Title
Base requirements — Reorder requirement rows (move up / move down)

### Summary
Lets an editor move a requirement or heading row one position up or down in the checklist via the right-click menu, re-sorting it (and every trade's synced copy) without renumbering the rest of the list.

### User story
As an estimator organizing the checklist, I want to move a requirement line up or down one row at a time so that the checklist reads in the order I want, without having to delete and re-insert rows.

### Acceptance criteria
- [ ] Right-click a row → "Move up" is disabled (no-op) when the row is already first in the list; otherwise it moves the row to sit immediately after the row two positions above it (i.e., swaps order with the row directly above).
- [ ] Right-click a row → "Move down" is disabled when the row is already last; otherwise it moves the row to sit immediately after the row directly below it.
- [ ] A successful move re-renders the grid in the new order with no toast message (no `ok` string is passed for move actions).
- [ ] The move affects every trade's synced copy of the line's position (`sortKey`), not just the checklist view.
- [ ] Moving is only available (context menu opens) when the version is editable; otherwise right-click does nothing.

### Technical spec
**Frontend:** `web/src/pages/project/BaseRequirements.tsx`, context menu items "Move up" / "Move down" in the `menu()` handler: `onClick: () => patch(l, { moveAfterId: i > 1 ? lines[i - 2].requirementLineId : 0 })` (disabled when `i === 0`) and `onClick: () => patch(l, { moveAfterId: lines[i + 1].requirementLineId })` (disabled when `i === lines.length - 1`). Both reuse the same `patch()` helper as ticket 03 — `api.patch<ReqData>(\`/requirements/lines/${l.requirementLineId}?versionId=${vid}\`, { moveAfterId })` — with no success message, writing the response into the query cache and invalidating `'scope'`-prefixed queries.
**Backend:** Same endpoint as ticket 03 — `PATCH /api/requirements/lines/{rid}?versionId={versionId}` — handling the `MoveAfterId` branch of `ReqLinePatch`. After `Access.Version` (editor, 423-if-locked) and `OwnSet`/`MapLine`, when `body.MoveAfterId` is present: resolves the anchor's current `SortKey` (`after == 0` means "move to the very top", using `SortKey = 0` as the lower bound; otherwise it maps the anchor id through `MapLine` too, in case it's also a stale template id), finds the next row's `SortKey` above that anchor (`min(SortKey) WHERE SortKey > @a AND RequirementLineId <> @rid`), and computes a new fractional `SortKey` for the moved row as either `a + 1` (no row above) or the midpoint `round((a + b) / 2, 4)`. This computed `key` then feeds the same `UPDATE RequirementLine ... SortKey = coalesce(@key, SortKey)` and the same `UPDATE ScopeLine ... SortKey = r.SortKey` trade-sync statement described in ticket 03, and the same `Calc.Recalc.Version(w, versionId, "requirement.update")` + `w.Log(..., "requirement.update", "requirement", rid, after: body)` + commit.
**Database:** Same tables/columns as ticket 03: `RequirementLine.SortKey numeric(12,4)` and `ScopeLine.SortKey numeric(12,4)` (synced per trade), using index `IxRequirementLineSet (RequirementSetId, SortKey)` for the neighbor lookups and `IxScopeLineTrade (TradeId, Section, SortKey) WHERE ArchivedAt IS NULL` for the trade sync. No new trigger behavior beyond `ScopeLine`'s `TrgTouch` (refreshes `ChangedAt` on the synced-row UPDATE).

### Dependencies
- Shares the entire `PATCH /requirements/lines/{rid}` endpoint, `OwnSet`/`MapLine` copy-on-write/remap logic, and `Calc.Recalc.Version` side effect with ticket 03 (edit) — see that ticket's Notes for copy-on-write cost and recalc scope.
- Depends on ticket 01 for the ordered list the move operates against.
- See `_Overview/Architecture.md`: Database conventions (fractional `SortKey`, "inserting/moving never renumbers" pattern) and Recalculation engine.

### Notes / edge cases
- Because `sortKey` is fractional, repeated moves between the same two neighbors can eventually exhaust the available precision (`numeric(12,4)`); unlike the insert path (ticket 02), the move patch does **not** call `Renumber` as a fallback — if the gap collapses to zero, `Math.Round((a+b)/2, 4)` can produce a duplicate `SortKey`, leaving the two rows' relative order ambiguous (resolved only by the `ORDER BY SortKey, RequirementLineId` tiebreak in the read query) until an insert elsewhere triggers a renumber.
- "Move up" skips one row intentionally — it recomputes against `lines[i-2]`, not `lines[i-1]`, because the row at `i-1` is about to become the one it's moving past; moving the first row (`i === 1`) up uses anchor `0`, placing it before everything.
- The move reuses the exact same update statements as a text/section/heading edit, so a move also triggers a full `Calc.Recalc.Version` even though no displayed value changed — see ticket 03's Notes.
