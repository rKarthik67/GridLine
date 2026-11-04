## Title
Precon services & Project overheads — Copy expense template

### Summary
**Copy expense template** lists the cost kits for this area (“<kit> · N items”) and copies their items into the version.

### User story
As an estimator, I want to start from a standard list of expenses.

### Acceptance criteria
- [ ] Toast “Items copied from <kit>”; unknown kit → “Unknown cost template.”

### Technical spec
**Frontend:** `Costs.tsx` `Menu` → `api.post(`/versions/${vid}/costs/apply-kit`, { costKitId })`.

**Backend:** `POST /api/versions/{id}/costs/apply-kit` (`CostKitIn`) inserts from `CostKitItem`; logs `cost.kit`. New projects get the default kits (`ApplyDefaultCostKits`).

**Database:** `CostKit (Area)`, `CostKitItem`, `CostLine`.

### Dependencies
- 05-expenses-grid

### Notes / edge cases

