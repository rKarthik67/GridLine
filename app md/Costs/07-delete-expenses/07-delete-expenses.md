## Title
Precon services & Project overheads — Delete expenses (row or bulk)

### Summary
Tick rows (or a whole category) and use **Delete selected**, or right-click → Delete item.

### User story
As an estimator, I want to delete several expenses at once.

### Acceptance criteria
- [ ] A confirm modal (“Are you sure you want to delete the selected items? This action cannot be undone.”) precedes bulk delete; right-click delete is immediate.
- [ ] “Nothing selected.” if the list is empty.

### Technical spec
**Frontend:** `ExpensesGrid` → `api.post(`/versions/${vid}/costs/delete?area=${area}`, { ids })`.

**Backend:** `POST /api/versions/{id}/costs/delete?area=` (`IdsIn`); `DELETE /api/costs/{id}` also exists.

**Database:** `CostLine` delete.

### Dependencies
- 05-expenses-grid

### Notes / edge cases

