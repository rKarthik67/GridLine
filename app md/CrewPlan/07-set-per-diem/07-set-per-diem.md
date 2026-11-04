## Title
Crew plan — Set per diem

### Summary
Right-click → **Set per diem** (or click the Per diem cell) opens a modal to pick a GSA destination (city/county, zip, state), travel start/end dates, week days or all days, and Hotel / Meals / Both, with a live preview of days and cost.

### User story
As an estimator, I want per diem priced from real rates for the travel dates, so that travelling staff are costed correctly.

### Acceptance criteria
- [ ] Destination list comes from `GET /per-diem/places?q=&state=&zip=`; state defaults to the project's state; first match is pre-selected.
- [ ] Travel dates default to Est construction start → Closeout date; a warning shows when the dates are outside construction start → closeout, or end is before start.
- [ ] Preview: hotel uses each night's month rate, meals per day; “Week days” skips Saturdays and Sundays. Cards show destination, hotel rate for the start month, meals rate, days and total.
- [ ] “Clear per diem” removes it. Errors: “Per diem days must be week days or all days.”, “Expense type must be hotel, meals or both.”, “Pick a destination and the travel dates.”, “The travel end date is before the start date.”, “Travel can be at most 10 years.”, “Unknown per diem destination.”

### Technical spec
**Frontend:** `PerDiemModal` in `CrewPlan.tsx`; preview via `perDiem()` in `crewCalc.ts`. Save → `api.put(`/crew/${id}/per-diem`, { placeId, start, end, days, kind })`; clear → `{ clear: true }`.

**Backend:** `PUT /api/crew/{id:long}/per-diem` (`PerDiemIn`) computes days and cost with `Crew.PerDiem` (same rule as the browser) and stores them; `GET /api/per-diem/places` searches `PerDiemPlace` (and `PerDiemZip` when a zip is given).

**Database:** `PerDiemPlace (City, County, StateCode, LocationDefined, Lodging int[12], Meals, FiscalYear)` — 912 FY2026 places loaded from the Gridline DB `PerDiemRates`; `PerDiemZip`; `TeamMember.PerDiemPlaceId, PerDiemStart, PerDiemEnd, PerDiemDays, PerDiemKind, PerDiemDaysCount, PerDiemCost`.

### Dependencies
- 01-view-crew-plan-and-team-members

### Notes / edge cases
- The zip list is empty until the GSA zip file is loaded into `PerDiemZip`.
