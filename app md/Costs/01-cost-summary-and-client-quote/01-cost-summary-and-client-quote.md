## Title
Precon services & Project overheads — Cost summary and client quote (Quoted % / Quoted total)

### Summary
Both pages (one component, `Costs.tsx`) open with an **Actual** card and a **Client quote** card. The quote is driven by one **Quoted %** per version and area (was: allowable markup); typing a **Total quoted** back-calculates the %.

### User story
As an estimator, I want to set what we quote the client for precon and overheads as a % or a total, so that the roll-up uses the right number.

### Acceptance criteria
- [ ] Precon Actual card: Labour actual, Expenses actual, Total actual. Overheads Actual card: Total actual, Billable actual.
- [ ] Client quote card: Total quoted (editable $), or Quoted % (editable), Differential (quoted − actual; green when 0, red when negative).
- [ ] Precon quoted = round((labour + billable expenses) × Quoted %). Overheads quoted = Σ billable line rate × Quoted % × qty (non-billable lines quote $0).
- [ ] A typed Total quoted sets % = total ÷ (billable + labour) × 100, capped at 100%; “The quoted total can't be negative.”; “Send a Quoted % or a Quoted total.”
- [ ] Saving recalculates the roll-up; toast “Quoted saved · roll-up recalculated”.

### Technical spec
**Frontend:** `web/src/pages/project/Costs.tsx` (`area = pageKey === 'precon-services' ? 'precon' : 'genreq'`), query `vkey('costs', vid, area)`; `saveQuoted()` → `api.put(`/versions/${vid}/costs/quoted`, { area, quotedPct | quotedTotal })`.

**Backend:** `GET /api/versions/{id}/costs?area=` → `{ lines, quotedPct, totals { labourActual, expensesActual, billableActual, actual, quoted, difference } }` (`LoadCosts`). `PUT /api/versions/{id}/costs/quoted` (`QuotedIn`); every cost change calls `ApplyQuotedPct` (QuotedRate = rate × % for billable lines, else 0) then `Recalc.Summary`.

**Database:** `ProjectVersion.PreconQuotedPct`, `ProjectVersion.GenreqQuotedPct`, `CostLine.QuotedRate`, `QuotedTotal`, `IsBillable`.

### Dependencies
- PriceRollUp/01-view-the-price-roll-up

### Notes / edge cases
- Precon labour comes from the precon team (ticket 02), not from the Crew plan.
