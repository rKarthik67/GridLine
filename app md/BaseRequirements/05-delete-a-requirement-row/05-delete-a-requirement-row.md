## Title
Base requirements — Delete a requirement row

### Summary
Lets an editor permanently remove a requirement or heading row from the checklist, from every trade's copy at once, via the right-click menu.

### User story
As an estimator cleaning up the checklist, I want to delete a requirement line that no longer applies so that it stops appearing on every trade in Scope match.

### Acceptance criteria
- [ ] Right-click any row (heading or normal) → "Delete row (from every trade)" is always enabled (not conditioned on row state) when the version is editable, and is styled as a dangerous/destructive action.
- [ ] Deleting shows a success toast "Row deleted" and the grid immediately reflects the row's removal.
- [ ] The row disappears from every trade's Scope match grid as well as from the base requirements checklist — the answer history tied to it is archived, not left dangling as a visible row.
- [ ] Deleting is unavailable (menu doesn't open) when the version isn't editable (viewer access or locked version).

### Technical spec
**Frontend:** `web/src/pages/project/BaseRequirements.tsx`, context menu item "Delete row (from every trade)": `onClick: () => run(() => api.del<ReqData>(\`/requirements/lines/${l.requirementLineId}?versionId=${vid}\`), 'Row deleted').then(put)`. Unlike the other actions it calls `run()` directly rather than going through the `patch`/`add` wrappers, then feeds the resolved `ReqData` into `put()` to update the query cache and invalidate `'scope'`-prefixed queries.
**Backend:** `DELETE /api/requirements/lines/{rid}?versionId={versionId}` in `Endpoints/ScopeExtrasEndpoints.cs`. Calls `Access.Version(w, versionId)` (editor rank, 423 if locked), `OwnSet(w, versionId)` (copy-on-write — see ticket 02's Notes), then `MapLine(w, set, rid)` to resolve a possibly-stale template-row id onto the version-owned copy (404 "Requirement line" if no match). First archives every trade's synced copy: `UPDATE ScopeLine l SET ArchivedAt = now(), RequirementLineId = NULL FROM Trade t WHERE l.RequirementLineId = @rid AND t.TradeId = l.TradeId AND t.VersionId = @versionId` — this detaches the `ScopeLine` row from the requirement before deleting the parent, and soft-deletes it (archives) rather than removing it outright. Then hard-deletes the requirement itself: `DELETE FROM RequirementLine WHERE RequirementLineId = @rid`. Calls `Calc.Recalc.Version(w, versionId, "requirement.delete")`, logs `w.Log(projectId, versionId, "requirement.delete", "requirement", rid)`, commits, and returns the refreshed `Requirements(...)` payload.
**Database:** Writes `ScopeLine.ArchivedAt = now()` and `ScopeLine.RequirementLineId = NULL` (scoped to rows whose `TradeId` belongs to a `Trade` with `VersionId = @versionId`), then issues a hard `DELETE FROM RequirementLine WHERE RequirementLineId = @rid`. `RequirementLine` has no `ON DELETE` cascade *from* `ScopeLine` (the FK is nullable and already cleared by the prior UPDATE, so the delete does not fail/cascade into `ScopeLine`). `ScopeLine` carries `TrgTouch` (fires on the archiving UPDATE, refreshing `ChangedAt`). The index `IxScopeLineTrade (TradeId, Section, SortKey) WHERE ArchivedAt IS NULL` means archived rows drop out of that partial index automatically once `ArchivedAt` is set.

### Dependencies
- Shares `OwnSet`/`MapLine` copy-on-write/remap logic with tickets 02–04, and triggers the same `Calc.Recalc.Version` side effect as ticket 03.
- Downstream: Scope match's grid loses the row once its `scope`-keyed queries refetch (client-invalidated, not server-pushed to other tabs beyond the standard `changed` broadcast).
- See `_Overview/Architecture.md`: Authorization/scoping, Unit of work (`Work.Log`/`Work.Commit` broadcast), Recalculation engine.

### Notes / edge cases
- Deleting a line does not remove the historical fact that trades once had an answer against it — the `ScopeLine` row itself is archived (kept, `ArchivedAt` set, `RequirementLineId` nulled) rather than deleted, so any `ScopeAnswer` rows keyed to that `ScopeLine.LineId` are preserved too (its FK is `LineId`, unaffected by the archive). Only the shared `RequirementLine` definition is actually destroyed.
- Because the delete also forces copy-on-write via `OwnSet`, deleting a line on a version that still shares the company template checklist will first silently clone the *entire* checklist for that version (and remap every trade's `ScopeLine.RequirementLineId` to the clone) before deleting the one targeted line — see ticket 02's Notes for the full mechanics.
- There is no confirmation dialog before delete in the UI — the context menu item fires immediately on click (styled red/`danger` as the only warning).
