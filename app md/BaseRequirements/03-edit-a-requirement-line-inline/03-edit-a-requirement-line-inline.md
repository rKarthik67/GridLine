## Title
Base requirements — Edit requirement lines in the grid (Excel copy/paste)

### Summary
The checklist is shown in the spreadsheet grid: #, Section, Requirement, Desired answer, Trades. Cells are typed, pasted from Excel or filled down; each changed line is saved once and updates every trade.

### User story
As an estimator, I want to paste a requirement list from Excel, so that setting up the checklist is quick.

### Acceptance criteria
- [ ] Section and Desired are editable on normal rows; Requirement is editable on normal and heading rows (a heading's text also becomes its section name).
- [ ] Desired accepts Included, Excluded, Price or Text (any case); anything else is rejected with a toast. Empty requirement text is rejected (“A requirement needs text”).
- [ ] Toast “Updated on every trade” or “N rows updated on every trade”. Right-click menu (insert, heading, move, delete) is unchanged.

### Technical spec
**Frontend:** `web/src/pages/project/BaseRequirements.tsx` (`cols`, `edit()`) on `DataGrid`. See `_Overview/Architecture.md` → Spreadsheet grid (`DataGrid`, RevoGrid).

**Backend:** `PATCH /api/requirements/lines/{id}?versionId=` per line.

**Database:** `RequirementLine`, and the linked `ScopeLine` rows on every trade.

### Dependencies
- _Overview/Architecture.md → Spreadsheet grid

### Notes / edge cases

