## Title
Crew plan — Change employee, note, % quoted, relocation, reorder

### Summary
Everything about one member that isn't hours goes through `PATCH /crew/{id}`: Change employee (StaffPicker pre-filled), Note (right-click → Note), inline % quoted and Relocation, inline weeks, and drag-to-reorder / move to another group.

### User story
As an estimator, I want to swap a TBA for a named person, adjust their % quoted or relocation and add notes, so that the crew is accurate.

### Acceptance criteria
- [ ] Right-click menu (editors): Note · Set hours/budget · Set per diem (Team members tab only) · Change employee · Add team member · Delete team member.
- [ ] Change employee sends `staffId` (or `clearStaff: true` for TBA), `staffRoleId`, `teamGroup`, `hourlyCost` and the role name as `roleLabel`; hours stay.
- [ ] Every PATCH carries `rowRev`; if someone else saved first the user gets “<name> changed this a moment ago…” (409 clash).
- [ ] Errors: “% quoted must be between 0 and 1000.”, “Rates and costs can't be negative.”, “The role can't be empty.”

### Technical spec
**Frontend:** `patch()` in `CrewPlan.tsx`; `AskText` modal for notes; `StaffPicker` for change employee; `dropOn()` for drag reorder.

**Backend:** `PATCH /api/crew/{id:long}` body `MemberIn(StaffId, StaffRoleId, RoleLabel, TeamGroup, HourlyCost, QuotedPct, RelocationCost, Note, MoveAfterId, RowRev, ClearStaff)` → `CrewData`; logs `team.update`.

**Database:** `TeamMember` (fractional `SortKey` for moves).

### Dependencies
- 03-add-team-member

### Notes / edge cases
- Moving a row to another group sets its `TeamGroup` in the same PATCH.
