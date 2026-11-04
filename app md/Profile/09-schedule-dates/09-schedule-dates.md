## Title
Project profile — Schedule dates and salary escalation

### Summary
The top of **Details** has a **Schedule** section (was: the dates on Project setup): Preconstruction start, Preconstruction end, Est construction start, Substantial completion, Closeout date — each with a “(N weeks)” label — and Salary escalation %/year. These dates generate the Crew plan weeks.

### User story
As a project manager, I want to set the project dates in one place, so that the crew plan and its costs follow them.

### Acceptance criteria
- [ ] Fields keep `data-was` attributes with the old labels. Week labels: precon start→end, construction start→substantial completion, completion→closeout.
- [ ] Client checks: end after start, both precon dates or neither, both construction dates or neither, closeout after completion, completion before closeout, escalation 0–25%. Bad fields get a red border and a message.
- [ ] Save first previews (`preview: true`); if weeks would change, “Change the schedule?” lists added/removed weeks and hours lost per phase; confirming saves.
- [ ] After saving: toast “Schedule dates saved · crew plan weeks updated”, and project, crew, price roll-up and bid board queries refresh.

### Technical spec
**Frontend:** `ScheduleDates` in `web/src/pages/project/Profile.tsx`; `weeksBetween()` from `web/src/app/crewCalc.ts`; anchor `#schedule`.

**Backend:** `PUT /api/projects/{id:long}/dates` (see `CrewPlan/02-schedule-dates-drive-the-weeks`). Project detail (`GET /projects/{no}`) returns the five dates as `yyyy-MM-dd` and `salaryEscalationPct`.

**Database:** `Project` date columns and `SalaryEscalationPct` (008).

### Dependencies
- CrewPlan/02-schedule-dates-drive-the-weeks

### Notes / edge cases

