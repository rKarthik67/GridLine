## Title
Crew plan — Export to Excel

### Summary
Export → **Crew plan (Excel)** downloads a workbook with a **Team members** sheet and a **Resource loader** sheet.

### User story
As a project manager, I want the crew plan in Excel, so that I can share it.

### Acceptance criteria
- [ ] The file contains every member with rates, hours, per diem, relocation, actual and quoted, and hours per week.

### Technical spec
**Frontend:** `ExportMenu` item `/exports/crew-plan.xlsx?versionId=${vid}`.

**Backend:** `GET /api/exports/crew-plan.xlsx?versionId=` (`Export/ExportEndpoints.cs`, `TeamXlsx`, ClosedXML).

**Database:** Read only.

### Dependencies
- 01-view-crew-plan-and-team-members

### Notes / edge cases

