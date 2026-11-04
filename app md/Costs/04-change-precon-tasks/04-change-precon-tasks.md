## Title
Precon services — Change tasks (library and ad-hoc)

### Summary
**Change tasks** (was: Change Precon Dept Tasks) opens a two-list transfer: the task **Library** on the left and this version's **Project tasks** on the right, plus “Add ad-hoc task for this project”.

### User story
As a precon manager, I want to choose which tasks get hour columns and add my own, so that the grid fits the job.

### Acceptance criteria
- [ ] Library holds the 16 standard tasks (defaults marked) and the project's own ad-hoc tasks; move selected items with › and ‹ (select-all per list).
- [ ] Ad-hoc names are added at the top of Project tasks marked “new”; duplicates are ignored; names up to 120 characters.
- [ ] Save with nothing chosen fails: “You must either select a task from the library or add an ad-hoc one.”
- [ ] Removing a task hides its column; hours already entered come back if the task is added again.

### Technical spec
**Frontend:** `TasksModal` in `Costs.tsx`; query `['precon-tasks', vid]`; save → `api.put(`/versions/${vid}/precon-tasks`, { activityIds, adhoc })`.

**Backend:** `GET /api/versions/{id}/precon-tasks` → `[{ activityId, taskName, isDefault, isAdhoc, selected }]`; `PUT /api/versions/{id}/precon-tasks` (`TasksPut`) creates ad-hoc `Activity` rows for the project and rewrites `VersionTask`.

**Database:** `Activity (IsDefault, ProjectId for ad-hoc, IsActive)`, `VersionTask (VersionId, ActivityId)`. New versions/projects get the default tasks (`PreconSetup.DefaultTasks`).

### Dependencies
- 02-precon-team-members-and-task-hours

### Notes / edge cases

