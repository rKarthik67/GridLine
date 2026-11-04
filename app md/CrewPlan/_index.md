# Crew plan — ticket index

Page: `web/src/pages/project/CrewPlan.tsx` (was: Project team). Backend: `Endpoints/TeamEndpoints.cs`, `Calc/Crew.cs`. The crew belongs to the project; its quoted total is **General conditions** on the Price roll-up.

- `01-view-crew-plan-and-team-members/` — Summary cards, Team members tab grouped by employee group, inline edits, drag reorder, from `GET /projects/{id}/crew`.
- `02-schedule-dates-drive-the-weeks/` — Construction/closeout weeks come from the Project profile dates via `PUT /projects/{id}/dates` (preview + confirm).
- `03-add-team-member/` — StaffPicker modal (group, role, person or TBA, rate, hours/week, % quoted) → `POST /projects/{id}/crew`.
- `04-change-employee-and-edit-member/` — Change employee, note, % quoted, relocation, reorder → `PATCH /crew/{id}`.
- `05-set-hours-budget/` — Budget ⇄ hours/week ⇄ weekly budget with salary escalation → `PUT /crew/{id}/phase-hours`.
- `06-resource-loader/` — Hours per week or per month → `PUT /crew/{id}/hours`, `PUT /crew/{id}/month-hours`.
- `07-set-per-diem/` — GSA destination, travel dates, days and expense type → `GET /per-diem/places`, `PUT /crew/{id}/per-diem`.
- `08-choose-columns/` — Pick from 21 columns, per project or all projects → `PUT /columns/crew`.
- `09-update-posting-rates/` — Reset everyone to current rates → `POST /projects/{id}/crew/reset-rates`.
- `10-delete-team-member/` — `DELETE /crew/{id}`.
- `11-export-crew-plan-to-excel/` — `GET /exports/crew-plan.xlsx`.
