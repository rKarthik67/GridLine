## Title
Crew plan — Resource loader (hours by week or month)

### Summary
The **Resource loader** tab shows every member's hours per **week** or per **month** (Organize by, remembered per browser) with a Construction / Closeout phase header row, heat-shaded cells and totals.

### User story
As a project manager, I want to type hours per week or per month, so that I can load the crew over time.

### Acceptance criteria
- [ ] Week columns show “W<n>” and the Monday date; month columns show “Mon YY” and the number of weeks; closeout columns are marked in orange.
- [ ] Typing in a week cell saves 0–80 hours for that week; typing in a month cell spreads the hours evenly over that month's weeks.
- [ ] Errors: “Hours per week must be between 0 and 80.”, “Some weeks are not on this project's schedule.”, “That month is not on the construction or closeout schedule.”, “A month here can take 0 to <80×weeks> hours.”
- [ ] Right-click on a row offers the same menu as Team members except Set per diem.

### Technical spec
**Frontend:** `CrewPlan.tsx` (`tab === 'loader'`, `period` stored in `localStorage` key `gridline.crew.period`). Week edit → `api.put(`/crew/${id}/hours`, { cells: [{ weekId, hours }] })`; month edit → `api.put(`/crew/${id}/month-hours`, { monthNo, hours })`. Phase classes are `ph-build` / `ph-closeout` (not `build`, which is a global layout class).

**Backend:** `PUT /api/crew/{id:long}/hours` (`HoursIn`), `PUT /api/crew/{id:long}/month-hours` (`MonthIn`) → `CrewData`; both log `team.hours`.

**Database:** `TeamMemberWeek`, `ProjectWeek.MonthNo`.

### Dependencies
- 02-schedule-dates-drive-the-weeks

### Notes / edge cases

