## Title
Allowances — Scope lines, budget apply and include/exclude

### Summary
An owner prescribed allowance adds a trade-item line to every trade (and to trades added later); a miscellaneous one to its trade. Bidders price the line on Scope match and it counts in their bid. The panel can push one budget amount to every bidder of a trade.

### User story
As an estimator, I want an allowance to flow into the bids like any other scope line, and to fill in a budget amount for all bidders at once.

### Acceptance criteria
- [ ] Panel left: Description (Save also renames the line in every trade), "Budget amount → all trade partners of a trade" (owner prescribed only): amount + trade + Apply, Delete.
- [ ] Apply writes an amount answer (Source `allowance`) on the allowance line for every bidder of that trade and recalculates the trade and roll-up.
- [ ] Panel right "Trade entered values": trade (link), picked bidder and "x/y priced", Total = the picked bid's amount on the line, Status toggle Included/Excluded, Include all / Exclude all, Total of included trades.
- [ ] On Scope match the line shows an **Allowance** tag; its text can't be edited and the row can't be deleted there (the API returns 400 with a pointer to this page). Answers stay editable.

### Technical spec
**Frontend:** `web/src/pages/project/Allowances.tsx` (route `allowances`, sidebar *Pricing → Allowances*). Query key `vkey('allowances', versionId)`. Components `AllowancePanel`, `AddAllowanceModal`, `TradePicker`.

**Backend:** `POST /api/versions/{id}/allowances` `{ kind, description, tradeId? }`; `PATCH /api/allowances/{id}` `{ description, rowRev }`; `POST /api/allowances/{id}/apply` `{ tradeId, amount }` (400 for miscellaneous). New trades get the lines via `ScopeSetup.AddStandardLines` → `PricingExtras.AddAllowanceLines`.

**Database:** `ScopeLine` (Section `tradeitem`, SumId), `ScopeAnswer` (Source check now allows `allowance`), `ProvisionalSumTrade`.

### Dependencies
ScopeMatch tickets; Architecture.md recalculation.

### Notes / edge cases
- Small difference from the original: trades created after the allowance also get its line.
