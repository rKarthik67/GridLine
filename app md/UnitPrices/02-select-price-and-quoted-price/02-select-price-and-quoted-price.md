## Title
Unit prices — Pick a trade price and the quoted price

### Summary
The expanded row edits the unit price and shows every trade's price with a Select button, then Selected price, Discounted price, each indirect with its share of the contract and an on/off switch, and the Quoted price.

### User story
As an estimator, I want to carry one trade's rate and quote it with the same indirects as the contract.

### Acceptance criteria
- [ ] Selected price = the selected trade's price. Discounted price = price × Quoted % / 100.
- [ ] Each indirect = discounted price × (that roll-up line ÷ contract total) for Precon, General conditions, the reserves, P & P bond, Works cover, Fee, L & P insurance; unticked = 0.
- [ ] Quoted price = discounted price + included indirects.
- [ ] Select is offered only for trades with a price; the × on the Selected tag clears the selection.

### Technical spec
**Frontend:** `web/src/pages/project/UnitPrices.tsx` (route `unit-prices`, sidebar *Pricing → Unit prices*). Query key `vkey('unit-prices', versionId)`. Component `UnitPanel`.

**Backend:** `PATCH /api/unit-prices/{id}` `{ description?, unit?, quotedPct?, comments?, includeCode?, included?, selectTradeId?, clearSelection?, rowRev }`.

**Database:** `UnitRateTrade.IsSelected`, `UnitRate.IncludeCodes`, `RollupLine`.

### Dependencies
Original: sp_calcUnitPrices, UnitPriceRowDetails.js.

### Notes / edge cases
- Unit prices never change the contract total.
