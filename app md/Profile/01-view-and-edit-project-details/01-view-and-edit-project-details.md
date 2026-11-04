## Title
Project profile — View and edit project details

### Summary
The Details tab of the Project profile page shows the project's core identity (name, number, client, stage, type, address, estimator/manager, bid due date) in an editable form, and lets an editor save changes or archive the whole project. It is the landing tab of the `profile` page and the only place project-level metadata is edited.

### User story
As a project estimator or manager, I want to view and edit a project's core details (name, client, stage, address, assigned estimator/manager, bid due date) so that the project record stays accurate as it moves through the pipeline.

### Acceptance criteria
- [ ] On load, the form is pre-filled from the current `ProjectDetail`: project name, project number (read-only), client name, stage, project type, contract type, business unit (division), office (branch), street/city/state/county/ZIP, bid due date, CRM reference, estimator, project manager.
- [ ] `Project number` is always rendered disabled, even for editors — it cannot be changed from this form.
- [ ] All other fields are disabled when `project.access === 'viewer'`.
- [ ] The `Save changes` button is disabled unless the form differs from the loaded values (`dirty`) or a save is in flight (`busy`).
- [ ] An `Undo` button appears next to Save only while the form is dirty, and resets the form back to the loaded values.
- [ ] Saving sends the whole form plus the project's current `rowRev`; if another user changed the project since this form loaded (`RowRev` mismatch), the save fails with a 409 naming who last changed it, instead of silently overwriting their change.
- [ ] Saving with an empty project name is rejected with "Project name can't be empty."
- [ ] On successful save, the `['project', projectNo]` query is invalidated so the page (and every tab using `project`) refetches the fresh row, and a "Project saved" toast/confirmation appears.
- [ ] If the project loads a new `rowRev` (e.g. another tab saved first), the form resets to the new server values, discarding any local edits (`useEffect` keyed on `project.rowRev`).
- [ ] "Archive project" is shown only when `project.access === 'owner'`, requires a confirm dialog ("Archive {projectName}? It disappears from the portfolio."), and on confirmation navigates back to `/projects` after the archive call succeeds.
- [ ] Archiving does not physically delete the project row — it soft-deletes it (sets an archived timestamp) so it drops out of the portfolio list but its data remains.

### Technical spec
**Frontend:** `web/src/pages/project/Profile.tsx`, component `Details({ project })`. Builds local form state `f` from `project` fields via `init()`, reset on `project.rowRev` change. `canEdit = project.access !== 'viewer'`. `dirty` is computed by deep JSON-comparing `f` to a fresh `init()`. Save handler:
- `api.patch(\`/projects/${project.projectId}\`, body)` where `body` is the form fields (numeric IDs coerced with `+`, `bidDueOn` turned into `null` when blank, `projectNo` explicitly stripped, and `rowRev: project.rowRev` attached) — on success calls `qc.invalidateQueries({ queryKey: ['project', project.projectNo] })`.
Archive handler (owner only): `api.del(\`/projects/${project.projectId}\`)`, then `nav('/projects')`.
Uses `useLookups()` for the `stages`, `divisions`, `branches`, `states`, `users` select options, and `useAction()` for the busy/run wrapper.

**Backend:**
- `PATCH /api/projects/{id:long}` (`ProjectEndpoints.Map`, `ProjectIn` body incl. optional `RowRev`). `Access.Project(w, id, "editor")` (default need is `"editor"`) — throws 403 if rank too low, 423 if the project/version context is locked per Architecture.md Authorization/scoping. Reads current `RowRev` + last changer's name; if `body.RowRev` is supplied and differs, throws `ApiException.Clash(who)` (409). Rejects blank `ProjectName` with 400. Runs an `UPDATE Project SET ... = coalesce(@X, X) ... WHERE ProjectId = @id` (every column is `coalesce`d against the existing value, so omitted/null fields are left untouched) and sets `ChangedBy`. Logs `w.Log(id, null, "project.update", "project", id, after: body)` then `w.Commit()` (see Architecture.md Work/Unit-of-work — commit also broadcasts a `changed` SignalR event to the project's group). Returns the refreshed `Detail(...)` projection.
- `DELETE /api/projects/{id:long}`: `Access.Project(w, id, "owner")` — only an owner can archive. Runs `UPDATE Project SET ArchivedAt = now(), ChangedBy = @u WHERE ProjectId = @id`, logs `"project.archive"`, commits, returns 204.
- No Calc/Recalc, Import/Export, or Proposal involvement — this is a plain metadata edit.

**Database:** Table `Project` (`db/migrations/001_init.sql`), columns written: `ProjectName, Stage (ProjectStage enum), ClientName, ProjectType, ContractType, DivisionId (FK Division), BranchId (FK Branch), Street, City, StateCode (FK UsState), CountyName, PostalCode, BidDueOn, CrmRef, EstimatorId (FK AppUser), ManagerId (FK AppUser), ChangedBy`, plus `ArchivedAt` on delete. `ProjectNo` has a `UNIQUE` constraint and is never written by this endpoint. `RowRev int NOT NULL DEFAULT 1` is bumped automatically by trigger `TrgTouch` (function `TouchRow()`, attached to `Project` in the generic trigger loop in `001_init.sql`) on every `UPDATE`, which also sets `ChangedAt`; this is what the optimistic-concurrency check compares against. Archiving does not delete the row — `ArchivedAt` is a soft-delete marker; `GET /api/projects/{no}` and the portfolio list both filter `WHERE ArchivedAt IS NULL`.

### Dependencies
- See `_Overview/Architecture.md`: Authorization/scoping (Access.Project rank check), Unit of work (Work/Commit/Log + SignalR `changed` broadcast), Database conventions (generic `TrgTouch` trigger).
- `ProjectLayout`'s single `GET /api/projects/{projectNo}` load (shared across all profile tabs) depends on this same `Project` row and the `access` value this ticket computes.
- Feeds the Portfolio page's project list (archived projects drop out via `ArchivedAt IS NULL`).

### Notes / edge cases
- The `RowRev` optimistic-concurrency check only exists on this PATCH, not on the Versions tab's `PATCH /versions/{id}` (see `03-manage-project-versions.md`) — don't assume the same clash protection applies there.
- `StateCode` is upper-cased server-side before the `UsState` FK lookup; county is free text (no FK to `County` is enforced on write here despite `County` existing as a reference table).
- Because every column in the `UPDATE` is `coalesce`d, sending `null` for a field never clears it — there's no way to blank out e.g. `ClientName` from this form once set, only to replace it with a different non-empty value (the frontend doesn't exercise this since inputs default to `''`, but a developer changing this endpoint needs to know `null` is a no-op, not a clear).
- Archiving is a hard business-rule action gated on `owner`, not just `editor` — don't relax this without checking `Access.Project(w, id, "owner")`.
