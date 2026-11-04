## Title
Crew plan — Set hours / budget

### Summary
Right-click → **Set hours/budget** (or click a Constr/Closeout actual cell) opens “Set team member budget” (was: CalcBudget.js). For Construction and Closeout the user types a **Budget**, **Hours / week** or **Weekly budget**, and the other two are worked out using the posting rate and yearly salary escalation.

### User story
As an estimator, I want to enter a budget and get the weekly hours (or the reverse), so that I can staff to a number.

### Acceptance criteria
- [ ] Each phase shows its week count; changing one field recalculates the others on blur.
- [ ] Hours per week are capped at 40, like the original; the budget is then recalculated from 40 h.
- [ ] Cost = hours × rate × (1 + esc%)^(year − first schedule year), summed over the phase's weeks per calendar year.
- [ ] Save sends both phases; the server sets those hours in every week of each phase.

### Technical spec
**Frontend:** `BudgetModal` in `CrewPlan.tsx`; math in `web/src/app/crewCalc.ts` (`phaseCost`, `phaseHours`, `phaseWeeks`). Save → `api.put<CrewData>(`/crew/${id}/phase-hours`, [{phase:'build', hours}, {phase:'closeout', hours}])`.

**Backend:** `PUT /api/crew/{id:long}/phase-hours` body `PhaseHoursIn[] (Phase, Hours, Weeks?)` — with `Weeks` it fills only the first N weeks of the phase and clears the rest. Error “Nothing to save.”

**Database:** `TeamMemberWeek`.

### Dependencies
- 01-view-crew-plan-and-team-members
- 06-resource-loader

### Notes / edge cases
- The browser math matches the server's `Crew.Multiplier`.
