## Title
Precon services & Project overheads — Add expense

### Summary
**Add expense** (header, category “+ Add”, right-click) opens a form: Category (pick or type new), Description, CSI code (searchable list), Qty, Unit, Rate, Billable.

### User story
As an estimator, I want to add an expense to a category.

### Acceptance criteria
- [ ] Add is disabled until there is a description; Qty defaults to 1, Unit to LS, Billable on.

### Technical spec
**Frontend:** `AddExpense` in `Costs.tsx` → `api.post(`/versions/${vid}/costs`, { area, categoryName, itemName, csiCode, qty, uomCode, rate, isBillable })`.

**Backend:** `POST /api/versions/{id}/costs` (`CostIn`) → `{ costs }`; logs `cost.create`.

**Database:** `CostLine` insert.

### Dependencies
- 05-expenses-grid

### Notes / edge cases

