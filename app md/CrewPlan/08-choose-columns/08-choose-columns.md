## Title
Crew plan — Choose columns

### Summary
**Columns** (was: Add Columns) opens a checklist of the 21 Team members columns with Select all / Clear / Defaults and “Use for all my projects”.

### User story
As a user, I want to pick which crew columns I see, so that the grid shows what I work with.

### Acceptance criteria
- [ ] Team member and Employee role are always shown; the chosen order follows the master list.
- [ ] Saving with “Use for all my projects” stores the choice for every project; otherwise only for this project. Only the current user sees it.
- [ ] With no columns chosen the tab shows “No columns chosen” and an Add columns button.

### Technical spec
**Frontend:** `ColumnsModal` in `CrewPlan.tsx` → `api.put(`/columns/crew`, { columns, projectId, allProjects })`, then refetches the crew query.

**Backend:** `PUT /api/columns/{grid}` (`ColumnsIn`); only `grid = crew` is accepted (“Unknown grid.”); at most 40 columns (“Too many columns.”). `GET /projects/{id}/crew` returns the user's project choice, else their all-projects choice, else null (defaults).

**Database:** `ColumnChoice (UserId, Grid, ProjectId nullable, Columns text[])`.

### Dependencies
- 01-view-crew-plan-and-team-members

### Notes / edge cases

