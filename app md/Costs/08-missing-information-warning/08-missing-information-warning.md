## Title
Precon services & Project overheads — Missing CSI code / description warning

### Summary
A yellow bar warns “CSI code is not added for N item(s). Description is not added for N item(s). Please add them.” with **View**, which lists the lines and what is missing.

### User story
As an estimator, I want to know which expenses are incomplete before submitting.

### Acceptance criteria
- [ ] The bar appears only when something is missing; counts update as lines are fixed.

### Technical spec
**Frontend:** `Costs.tsx` (`missingCsi`, `missingDesc`, `missing` modal).

**Backend:** Uses the `GET /versions/{id}/costs` data.

**Database:** `CostLine.CsiCode`, `ItemName`.

### Dependencies
- 05-expenses-grid

### Notes / edge cases

