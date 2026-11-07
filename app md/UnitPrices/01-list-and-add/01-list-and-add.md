## Title
Unit prices — List and add

### Summary
Lists unit prices with unit, selected price, quoted price and status; adds new ones from a modal.

### User story
As an estimator, I want the per-unit rates the client may call on, priced by the trades.

### Acceptance criteria
- [ ] Shown only when `feature.unitrates` is on.
- [ ] Search and status filter Any / Proposed / Selected (Selected = a trade price is picked).
- [ ] Columns: select, expand, Description (+ comments), Unit, Price, Quoted price, Status, Updated. Bulk Delete with confirmation.
- [ ] Add modal: Description (required), Unit (Cubic Yard, Day, Each, Gallon, Hour, Linear Foot, Lump Sum, Month, Pound, Square Foot, Square Yard, Ton, Week — the original list), Quoted % (default 100), Comments.

### Technical spec
**Frontend:** `web/src/pages/project/UnitPrices.tsx` (route `unit-prices`, sidebar *Pricing → Unit prices*). Query key `vkey('unit-prices', versionId)`.

**Backend:** `GET/POST /api/versions/{id}/unit-prices`, `POST /api/versions/{id}/unit-prices/delete` `{ ids }`.

**Database:** `UnitRate` (Description, Unit, QuotedPct, Comments, IncludeCodes, UnitIndex, RowRev) and `UnitRateTrade` (Price numeric(19,4), Note, IsSelected; one selected per unit price by a unique partial index). Migration 013.

### Dependencies
Original: BidUnitPrices.js, AddUnitPriceModal.js.

### Notes / edge cases
- The original stores no unit-price status; Gridline derives Proposed/Selected from the selection.
