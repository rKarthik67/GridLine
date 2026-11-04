## Title
Precon services & Project overheads — Expenses grid

### Summary
The **Expenses** tab (precon) and the overheads page list cost lines by category, with the actual side on the left and the **client quote** side on the right, in the spreadsheet grid.

### User story
As an estimator, I want to edit expenses like a spreadsheet and paste them from Excel, so that entering a list is fast.

### Acceptance criteria
- [ ] Columns: # (select), Item, CSI code, Billable, Qty, Unit, Rate, Subtotal | Quoted qty, Quoted rate, Quoted subtotal, Delta (green/red).
- [ ] Category rows are collapsible, show the category subtotal/quote/delta, a select-all checkbox and “+ Add”; a pinned Σ Total row closes the grid.
- [ ] Typing, Ctrl+V from Excel and fill handle save every changed line once. Values are checked first: CSI code must be in the code list, Billable takes Yes/No/True/False/1/0/x, Unit must be a known unit, Qty/Rate must be numbers (“$1,234”, “(200)” accepted). Problems are shown in a toast and skipped.
- [ ] Missing description or CSI code cells are highlighted. Right-click: Add item to <category>, Delete item.
- [ ] Errors from the server: “Give the expense a description.”, “Quantity and rate can't be negative.”

### Technical spec
**Frontend:** `ExpensesGrid` in `Costs.tsx` on `DataGrid`; `edit()` validates then `api.patch(`/costs/${id}`, { ...fields, rowRev })` per line.

**Backend:** `PATCH /api/costs/{id}` (`CostIn` incl. `CsiCode`, `IsBillable`, `UomCode`) → `{ costs, totalsHash }` (`CostsDone` re-applies the Quoted % and recalculates).

**Database:** `CostLine (VersionId, Area, CategoryName, ItemName, CsiCode, Qty, UomCode, Rate, IsBillable, QuotedRate, ActualTotal, QuotedTotal, SortKey, RowRev)`.

### Dependencies
- 01-cost-summary-and-client-quote
- 06-add-expense
- 07-delete-expenses
- _Overview/Architecture.md → Spreadsheet grid

### Notes / edge cases

