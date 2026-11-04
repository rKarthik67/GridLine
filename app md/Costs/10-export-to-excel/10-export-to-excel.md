## Title
Precon services & Project overheads — Export to Excel

### Summary
Export → “Precon services (Excel)” or “Project overheads (Excel)”.

### User story
As an estimator, I want the cost lists in Excel.

### Acceptance criteria
- [ ] Precon workbook has a Team sheet (hours per task) and an Expenses sheet with the actual and quoted columns; overheads has the expenses sheet.

### Technical spec
**Frontend:** `ExportMenu` → `/exports/precon-services.xlsx?versionId=` or `/exports/project-overheads.xlsx?versionId=`.

**Backend:** `ExportEndpoints.cs` → `CostsXlsx` (ClosedXML).

**Database:** Read only.

### Dependencies
- 05-expenses-grid

### Notes / edge cases

