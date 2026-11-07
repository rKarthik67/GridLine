## Title
Alternates & VE — List, filter and totals

### Summary
Lists every alternate (ALT-n) and value-engineering idea (VE-n) on the version with its total quoted, status and last change, plus KPI tiles for each kind.

### User story
As an estimator, I want one list of alternates and VE ideas with their quoted totals, so I can see what is offered to the client next to the base price.

### Acceptance criteria
- [ ] Shown only when Setting `feature.options` is true (`/features` → `options`); the sidebar hides the page otherwise.
- [ ] KPIs: Alternates (sum of total quoted, count, approved sum), Value engineering (same), Direct cost used for the indirects.
- [ ] Type filter All / Alternates / VE, a search over code, description, comments and trade names, and a status filter Any / Proposed / Approved / Rejected.
- [ ] Columns: select, expand, Code, Type badge, Description (+ comments), Total quoted, Status tag, Updated (date · person).
- [ ] Clicking a row expands the edit panel (ticket 02). Codes are renumbered ALT-1… / VE-1… per kind after every add or delete, as in the original app.
- [ ] Alternates never change the contract total or the Price roll-up.

### Technical spec
**Frontend:** `web/src/pages/project/Alternates.tsx` (route `/p/:no/v/:n/alternates`, sidebar *Pricing → Alternates & VE*, `data-was="Alternates / Value engineering"`). Query key `vkey('alternates', versionId)`.

**Backend:** `GET /api/versions/{id}/alternates` → `{ items, directCost }` (`PricingExtras.Alternates`). Viewer access.

**Database:** `PriceOption` (Kind `option` | `saving`, OptionNo, OptionIndex, Description, Status `proposed|accepted|rejected`, QuotedPct numeric(19,2), Comments, IncludeCodes text[], RowRev) and `PriceOptionTrade` (OptionId, TradeId, CarriedAmount, Note). Migration 013.

### Dependencies
Architecture.md (Work, live updates). Original: BidAlternates.js, sp_calcAlternates.

### Notes / edge cases
- Status `accepted` is shown as **Approved** (the original UI label).
- An item that no trade has priced shows $0.00.
