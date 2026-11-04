## Title
Precon services — Add, change or delete a precon team member

### Summary
“Add team member” (header, group row, right-click) and right-click → Change employee open the shared StaffPicker; right-click → Delete team member removes one person.

### User story
As a precon manager, I want to add people or TBA roles to the precon team and swap them later.

### Acceptance criteria
- [ ] Add defaults the group to Corporate resources. Errors: “Team must be operations, siteops or corporate.”, “Pick an employee role.”, “Unknown staff member.”, “Unknown employee role.”
- [ ] Change employee keeps the task hours and sends `rowRev` (409 clash if someone saved first).

### Technical spec
**Frontend:** `AddPreconMember` and the `change` modal in `Costs.tsx` → `api.post(`/versions/${vid}/precon-team`, { teamGroup, staffId, staffRoleId, postingRate })`, `api.patch(`/precon-team/${id}`, {...})`, `api.del(`/precon-team/${id}`)`.

**Backend:** `POST /api/versions/{id}/precon-team` (`MemberIn`), `PATCH /api/precon-team/{id}`, `DELETE /api/precon-team/{id}`.

**Database:** `PreconMember`, `PreconMemberTask`.

### Dependencies
- 02-precon-team-members-and-task-hours
- CrewPlan/03-add-team-member

### Notes / edge cases

