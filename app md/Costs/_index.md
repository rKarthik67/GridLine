# Precon services & Project overheads — ticket index

One component, `web/src/pages/project/Costs.tsx`, serves **Precon services** (`precon-services`, area `precon`, was: Preconstruction) and **Project overheads** (`overheads`, area `genreq`, was: General requirements). Backend: `Endpoints/TeamEndpoints.cs` (costs) and `Endpoints/PreconEndpoints.cs` (precon team and tasks).

- `01-cost-summary-and-client-quote/` — Actual and Client quote cards; Quoted % or Quoted total → `PUT /versions/{id}/costs/quoted`.
- `02-precon-team-members-and-task-hours/` — Precon team grid, hours per task, paste from Excel → `/precon-team/*`.
- `03-add-or-change-precon-team-member/` — StaffPicker add / change employee / delete.
- `04-change-precon-tasks/` — Library ⇄ project tasks plus ad-hoc tasks → `PUT /versions/{id}/precon-tasks`.
- `05-expenses-grid/` — Expenses by category, actual and client quote sides, typing/paste with validation → `PATCH /costs/{id}`.
- `06-add-expense/` — `POST /versions/{id}/costs`.
- `07-delete-expenses/` — Row or bulk delete → `POST /versions/{id}/costs/delete`.
- `08-missing-information-warning/` — Missing CSI code / description bar and list.
- `09-copy-expense-template/` — `POST /versions/{id}/costs/apply-kit`.
- `10-export-to-excel/` — `/exports/precon-services.xlsx`, `/exports/project-overheads.xlsx`.
