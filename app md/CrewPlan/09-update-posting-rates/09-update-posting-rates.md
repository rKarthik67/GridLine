## Title
Crew plan — Update posting rates

### Summary
**Update posting rates** (was: Update Posting Rates) sets every member back to the current rate of their staff record, or their role's default rate for TBA rows.

### User story
As an estimator, I want to refresh everyone's rate after rates change, so that the crew uses current rates.

### Acceptance criteria
- [ ] Shown on the Team members tab for editors; toast “Posting rates updated”; the grid and roll-up refresh.

### Technical spec
**Frontend:** `CrewPlan.tsx` → `api.post<CrewData>(`/projects/${pid}/crew/reset-rates`)`.

**Backend:** `POST /api/projects/{id:long}/crew/reset-rates` updates `TeamMember.HourlyCost` from `Staff.HourlyCost` / `StaffRole.DefaultRate`; logs `team.rates` with the number updated.

**Database:** `TeamMember`, `Staff`, `StaffRole`.

### Dependencies
- 01-view-crew-plan-and-team-members

### Notes / edge cases

