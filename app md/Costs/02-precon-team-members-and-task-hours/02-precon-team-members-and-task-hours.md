## Title
Precon services — Team members and hours per task

### Summary
The **Team members** tab (was: Preconstruction team) lists the precon team by employee group with Act posting rate, Quoted posting rate, Total hours and one column per selected precon task. Hours are typed or pasted from Excel.

### User story
As a precon manager, I want to enter each person's hours per task, so that precon labour is priced.

### Acceptance criteria
- [ ] Columns: # with a select checkbox, Team member (TBA tag for unnamed), Employee role, Act posting rate (editable), Quoted posting rate (role default), Total hours, then the tasks (ad-hoc tasks marked “(ad-hoc)”).
- [ ] Groups are collapsible with “+ Add” per group; a pinned Σ Total row shows total hours, labour actual and hours per task.
- [ ] Range select, Ctrl+C (Excel-ready), Ctrl+V of a block from Excel, and fill handle all save; non-numbers are rejected with a toast.
- [ ] Labour = hours × act posting rate. Errors: “Hours on a task must be between 0 and 10,000.”, “That task is not on this version. Use Change tasks to add it.”, “The posting rate can't be negative.”
- [ ] Selecting rows shows a bar with **Delete selected** (confirm modal).

### Technical spec
**Frontend:** `PreconTeamGrid` in `Costs.tsx` on `DataGrid`. Edits are grouped per member: rate → `api.patch(`/precon-team/${id}`, { postingRate, rowRev })`; hours → `api.put(`/precon-team/${id}/tasks`, [{ activityId, hours }])`; bulk delete → `api.post(`/versions/${vid}/precon-team/delete`, { ids })`. See `_Overview/Architecture.md` → Spreadsheet grid (`DataGrid`, RevoGrid).

**Backend:** `GET /api/versions/{id}/precon-team` → `{ members, tasks, totals {hours, labour} }`; `PATCH /api/precon-team/{id}`; `PUT /api/precon-team/{id}/tasks` (`TaskCell[]`); `DELETE /api/precon-team/{id}`; `POST /api/versions/{id}/precon-team/delete` (`IdsIn`). All return `{ team, totalsHash }` (`PreconEndpoints.cs`).

**Database:** `PreconMember (VersionId, TeamGroup, StaffId, StaffRoleId, RoleLabel, PostingRate, QuotedRate, SortKey, RowRev)`, `PreconMemberTask (PreconMemberId, ActivityId, Hours)`, `VersionTask`. Added by 008, which moved old crew precon-week hours onto the “Other” task.

### Dependencies
- 03-add-or-change-precon-team-member
- 04-change-precon-tasks
- _Overview/Architecture.md → Spreadsheet grid

### Notes / edge cases
- Version copies carry the precon team and its task hours.
