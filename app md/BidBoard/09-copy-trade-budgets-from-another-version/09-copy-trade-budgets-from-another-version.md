## Title
Bid board — Copy trade budgets from another version

### Summary
"Copy budget…" lets the estimator pull every trade's budget value from a sibling version of the same project into the current version in one action, matching trades by CSI code, instead of re-entering budgets by hand after creating a new version.

### User story
As an estimator starting a new version, I want to copy trade budgets from an earlier version of the same project so that I don't have to retype every trade's budget.

### Acceptance criteria
- [ ] "Copy budget…" is shown next to the Budget-source menu only when the Budget toggle is on and the caller can edit.
- [ ] The modal lists every other version of the same project to copy from (`v{versionNo} · {versionLabel}`), defaulting to the first one; if there's only one version in the project, it shows "This project has only one version." instead of a picker.
- [ ] The modal notes "Trades are matched by CSI code. Trades without a match keep their budget."
- [ ] On success, the modal closes and the bid-board query is invalidated so updated budgets appear.
- [ ] Copying from a version in a *different* project is rejected (400): "Budgets can only be copied between versions of the same project."
- [ ] Only trades whose `(CsiCode, ScopeNo)` matches between source and target get their `BudgetValue` overwritten; trades with no match retain their existing budget unchanged.

### Technical spec
**Frontend:** `web/src/pages/project/BidBoard.tsx`, component `CopyBudgetModal` (`setCopyBudget(true)`), receives `project`/`version` as props to list `project.versions.filter(v => v.versionId !== version.versionId)`. Submit calls `api.post<{ trades: number }>('/versions/${version.versionId}/budgets/copy', { fromVersionId: from })`; on success calls `onDone()` which invalidates `vkey('bid-board', vid)`.
**Backend:** `POST /versions/{id:long}/budgets/copy` in `Endpoints/TradeEndpoints.cs`. `Access.Version(w, id)` (editor scope on the target) and `Access.Version(w, body.FromVersionId, "viewer")` (viewer scope is enough on the source — a read). Checks both versions' `ProjectId` match, then runs a single set-based `UPDATE ... FROM` join, recalculates the whole target version (`Recalc.Version(w, id, "budget.copy")`), logs `budget.copy` against subject `version`/`id` with `{ from, trades }`, returns `{ trades: n }` (count of trades updated).
**Database:** `UPDATE Trade t SET BudgetValue = f.BudgetValue FROM Trade f WHERE f.VersionId = @from AND f.ArchivedAt IS NULL AND t.VersionId = @id AND t.ArchivedAt IS NULL AND t.CsiCode = f.CsiCode AND t.ScopeNo = f.ScopeNo`. Both sides rely on the `UxTradeCode (VersionId, CsiCode, ScopeNo) WHERE ArchivedAt IS NULL` index for the match. `Recalc.Version` (full-version recalculation — see Architecture.md: Recalculation engine) re-derives every affected trade's `BudgetDelta`/`BuyoutPct` and rewrites the roll-up (`RollupLine`/`RollupZone`, `ProjectVersion.TotalsHash`).

### Dependencies
Depends on trades existing in both versions with matching CSI codes (often true when a version was created via "copy from" elsewhere, or via the Excel import path). Interacts with the "Budget source" selector in "Browse, search, and filter the bid board" — copying budgets this way is independent of (and overwritten by) choosing a `BudgetColumn` as the baseline, since both write to the same `Trade.BudgetValue` column. See Architecture.md: Recalculation engine.

### Notes / edge cases
- This is a one-time copy, not a link: once copied, the target version's budgets are independent and further edits to the source version's budgets do not propagate.
- Matching is strictly by `(CsiCode, ScopeNo)` — a trade that was renamed/recoded between versions, or has a different `ScopeNo` because it was split differently, silently keeps its old budget rather than erroring or warning the user which trades didn't match.
- The response only reports a count of trades updated (`{ trades: n }`); the UI does not currently surface which specific trades were or weren't matched — a developer building a more detailed confirmation would need to add that server-side.
- Because `Access.Version(..., "viewer")` is sufficient on the *source* version, a user with only viewer access to another version of the same project (but editor access to the target) can still pull budget numbers from it — this is intentionally looser than the edit-level check required on the target.
