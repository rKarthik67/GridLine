## Title
Crew plan — Delete team member

### Summary
Right-click → **Delete team member** removes the member and all their hours.

### User story
As an estimator, I want to remove someone from the crew, so that they are no longer costed.

### Acceptance criteria
- [ ] Toast “<name> removed”; totals and the General conditions line update; other users see it live.

### Technical spec
**Frontend:** `del()` in `CrewPlan.tsx` → `api.del<CrewData>(`/crew/${id}`)`.

**Backend:** `DELETE /api/crew/{id:long}` → `CrewData`; logs `team.delete`.

**Database:** `TeamMember` delete (cascades `TeamMemberWeek`).

### Dependencies
- 01-view-crew-plan-and-team-members

### Notes / edge cases
- There is no undo.
