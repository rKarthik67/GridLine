## Title
Crew plan — Add team member

### Summary
“Add team member” (header button, group add-row, or right-click) opens the shared **StaffPicker** modal (was: Add Team Member): search by name, choose Employee group, Employee role (with head-count per role), Team member or **TBA**, and Avg. posting rate, plus construction / closeout hours per week and % quoted.

### User story
As an estimator, I want to add a named person or a TBA role to a crew group, so that their cost is included.

### Acceptance criteria
- [ ] Search lists up to 8 staff matches (name · role · group · rate); picking one fills group, role and rate.
- [ ] Choosing a role clears the person and sets the rate to the role's default rate; the person list is filtered by group and role; “TBA (not named yet)” is allowed.
- [ ] Save is disabled until a role or a person is chosen. Construction and closeout hours per week are capped at 80 and filled into every week of that phase; % quoted defaults to 100.
- [ ] Errors: “Team must be operations, siteops or corporate.”, “% quoted must be between 0 and 1000.”, “The posting rate can't be negative.”, “Unknown staff member.”, “Unknown employee role.”, “Pick a role for the team member.”

### Technical spec
**Frontend:** `AddMember` in `CrewPlan.tsx` wraps `web/src/components/StaffPicker.tsx` (`Picked {teamGroup, staffRoleId, staffId, rate}`; staff and roles from `useLookups()` → `GET /lookups`, staff include `teamGroup`). Calls `api.post<CrewData>(`/projects/${pid}/crew`, { teamGroup, staffId, staffRoleId, hourlyCost, buildHours, closeoutHours, quotedPct })`.

**Backend:** `POST /api/projects/{id:long}/crew` body `MemberIn` → full `CrewData`. Logs `team.create` and broadcasts.

**Database:** `TeamMember` insert (TeamGroup, StaffId, StaffRoleId, RoleLabel, HourlyCost, QuotedPct, SortKey) + `TeamMemberWeek` rows for the phase weeks.

### Dependencies
- _Overview/Architecture.md — lookups
- 04-change-employee-and-edit-member

### Notes / edge cases
- The same StaffPicker is used by Precon services → Add/Change team member.
