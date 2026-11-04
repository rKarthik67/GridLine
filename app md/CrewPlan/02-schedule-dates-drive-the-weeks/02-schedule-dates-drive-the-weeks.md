## Title
Crew plan — Construction and closeout weeks come from the Project profile dates

### Summary
The crew grid's weeks are generated from the five schedule dates on Project profile → Details → Schedule (like today's `sp_synchWeeks`). The crew page has no schedule editor of its own; its “Schedule dates” button and “Edit dates” link jump to that section.

### User story
As a project manager, I want the crew weeks to follow the project dates, so that changing a date updates every crew member's weeks.

### Acceptance criteria
- [ ] Precon weeks = Mondays from Preconstruction start to Preconstruction end; construction weeks = Mondays from Est construction start to Substantial completion; closeout weeks = Mondays from the week after substantial completion to the Closeout date.
- [ ] A week that keeps the same phase and Monday keeps its hours; weeks that fall away are deleted with their hours.
- [ ] Before saving, the profile asks `PUT /projects/{id}/dates` with `preview: true`; if any weeks would be added or removed, a confirm modal lists added/removed weeks and hours lost per phase.
- [ ] Validation messages: “Preconstruction end must be after its start date.”, “Give both preconstruction dates, or neither.”, “Substantial completion must be after the construction start date.”, “Give both the construction start and substantial completion dates.”, “Set substantial completion before the closeout date.”, “The closeout date must be after substantial completion.”, “The schedule can be at most 10 years.”, “Salary escalation must be between 0 and 25% a year.”
- [ ] After a save the crew plan, price roll-up and bid board refresh; other users see the change live.

### Technical spec
**Frontend:** Dates UI is `ScheduleDates` in `web/src/pages/project/Profile.tsx` (see `Profile/09-schedule-dates`). The crew page only navigates there (`nav('../profile?tab=details#schedule')`).

**Backend:** `PUT /api/projects/{id:long}/dates` body `DatesIn(PreconStartOn, PreconEndOn, BuildStartOn, BuildEndOn, CloseoutOn, SalaryEscalationPct, Preview)` → `{ changes: [{phase, added, removed, hoursLost}] }`. `RebuildWeeks(w, projectId, apply)` diffs wanted (phase, Monday) pairs against `ProjectWeek`, renumbers `WeekNo`/`MonthNo`, inserts new weeks, deletes gone ones. The older `PUT /projects/{id}/schedule` (start date + week counts) still exists and converts counts into dates.

**Database:** `Project.PreconStartOn, PreconEndOn, BuildStartOn, BuildEndOn, CloseoutOn, SalaryEscalationPct`; `ProjectWeek`; `TeamMemberWeek` (cascade on week delete).

### Dependencies
- Profile/09-schedule-dates
- 01-view-crew-plan-and-team-members

### Notes / edge cases
- 008 backfilled the dates from the existing ProjectWeek rows so old projects keep their weeks.
