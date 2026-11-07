## Title
Scope match — Alternates & VE and Unit prices tabs

### Summary
Two trade tabs where the trade enters its amount and comment for every alternate / VE idea, and its price and note for every unit price. Allowance lines appear in the Scope items grid with an Allowance tag.

### User story
As an estimator leveling a trade, I want to price alternates and unit prices for it in the same place as its scope.

### Acceptance criteria
- [ ] Tabs appear only when `feature.options` / `feature.unitrates` are on; `?tab=alternates|unitprices` opens them directly (links from the Alternates & VE and Unit prices pages).
- [ ] Alternates & VE tab: Code, Type, Description, Status, Amount (editable, blank clears), Comments (editable); footer totals for alternates and VE.
- [ ] Unit prices tab: #, Description, Unit, Price (editable, blank clears), Note (editable), Status (Selected / Proposed).
- [ ] Allowance lines: Allowance tag, text locked, "Delete row" disabled in the right-click menu.

### Technical spec
**Frontend:** `web/src/pages/project/ScopeMatch.tsx` — `TradeAlternatesTab`, `TradeUnitPricesTab`.

**Backend:** `GET/PUT /api/trades/{id}/alternates` `[{ optionId, amount?, clearAmount?, note? }]`; `GET/PUT /api/trades/{id}/unit-prices` `[{ unitRateId, price?, clearPrice?, note? }]`; `PATCH/DELETE /api/lines/{id}` refuse allowance lines.

**Database:** `PriceOptionTrade`, `UnitRateTrade`, `ScopeLine.SumId`.

### Dependencies
Alternates 01–03, Allowances 02, UnitPrices 01–02.

### Notes / edge cases
- Original: TradeAlternates.js, TradeUnitPrices.js.
