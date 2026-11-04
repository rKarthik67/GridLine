## Title
Crew plan — View the crew plan (cards + Team members tab)

### Summary
The Crew plan page (was: Project team) loads the project crew from `GET /projects/{id}/crew` and shows four summary cards (Construction, Closeout, Total, Schedule) plus a **Team members** tab: people grouped by employee group (Operations, Site operations, Corporate resources) with the columns the user picked. The crew is per **project** (shared by every version); its quoted total is the **General conditions** line on the Price roll-up.

### User story
As an estimator, I want to see every crew member, their rates, weeks, hours, per diem and actual vs quoted cost, so that I can price general conditions.

### Acceptance criteria
- [ ] Cards show hours, actual and quoted for Construction, Closeout and Total; a red “actual is more than quoted” badge appears when cost > quoted.
- [ ] The Schedule card shows construction start – substantial completion with week count, closeout date with week count, and salary escalation %/year; **Edit dates** opens `../profile?tab=details#schedule`.
- [ ] When the project has no construction/closeout weeks an empty state asks for the dates in Project profile (“Set the dates”).
- [ ] Rows are grouped by `TeamGroup` in the order operations → siteops → corporate, each group collapsible, with a count and an “Add team member” row (editors only).
- [ ] Team member shows the staff name, or a yellow **TBA** tag + role label; a note icon shows the member note; a coloured initials tag shows a teammate currently focused on that row (presence).
- [ ] Employee role column and Team member column are sticky on horizontal scroll; the footer row totals every column flagged `total`.
- [ ] Search filters by name/role. Viewers see values only; editors can type Contr weeks, Closeout weeks, Relocation and % quoted inline; Per diem and Constr/Closeout actual cells are buttons that open the Per diem and Set hours/budget modals.
- [ ] Rows can be dragged to reorder and dropped into another group (PATCH with `moveAfterId` and `teamGroup`).

### Technical spec
**Frontend:** `web/src/pages/project/CrewPlan.tsx` — `CrewPlan` component, query key `vkey('crew', vid, `p${pid}`)`; `COLS` (21 column definitions) and `DEFAULT_COLS`; `memberCell()` renders inline edits via `EditCell`; `put()` writes the returned `CrewData` into the cache and invalidates `price-roll-up`. Live updates: `ProjectLayout` invalidates every `crew` query on any project change event.

**Backend:** `GET /api/projects/{id:long}/crew` (`TeamEndpoints`) → `{ schedule {preconStartOn, preconEndOn, buildStartOn, buildEndOn, closeoutOn, preconWeeks, buildWeeks, closeoutWeeks}, salaryEscalationPct, weeks[] (non-precon, with year + escalation multiplier), members[], totals {construction, closeout, total}, columns }`. Member figures are computed in `Calc/Crew.cs` (`Crew.Load`): cost per week = hours × posting rate × (1 + esc)^(year − first year); actual = construction + closeout + per diem + relocation; quoted = actual × QuotedPct. Precon weeks are excluded (precon labour lives in Precon services).

**Database:** `TeamMember` (TeamGroup, QuotedPct, RelocationCost, Note, PerDiem* columns, SortKey, RowRev), `TeamMemberWeek` (hours), `ProjectWeek` (WeekNo, StartsOn, Phase, MonthNo), `Project.SalaryEscalationPct`, `ColumnChoice` (saved column list). Added by `008_crew_precon.sql`.

### Dependencies
- 02-schedule-dates-drive-the-weeks
- 08-choose-columns
- _Overview/Architecture.md — Work unit + broadcast, recalculation

### Notes / edge cases
- Dates come back as plain `yyyy-MM-dd` strings (SQL `to_char`) so date inputs and the per diem preview work.
- Editing weeks inline keeps the member's current hours per week (or 40 if none) and sets that many weeks via `phase-hours`.
