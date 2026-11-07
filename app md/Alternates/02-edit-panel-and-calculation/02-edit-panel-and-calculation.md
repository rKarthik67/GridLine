## Title
Alternates & VE — Edit panel and the quoted total

### Summary
The expanded row edits description, quoted %, status and comments, and shows the trade amounts, trade total, subtotal quoted, each indirect with an on/off switch, and the total quoted.

### User story
As an estimator, I want to set how much of the trade price is quoted and which indirects ride on it, so the client number is right.

### Acceptance criteria
- [ ] Left: Description, Quoted % (0–1000), Status (Proposed / Approved / Rejected), Comments; Save is enabled only when something changed; Cancel resets; Delete asks for confirmation.
- [ ] Right: one row per trade that has an amount (CSI code, trade link → Scope match `?tab=alternates`, picked bidder, amount, comment).
- [ ] Trade total = Σ trade amounts. Subtotal quoted = Trade total × Quoted % / 100 (rounded to cents).
- [ ] Each indirect = roll-up line amount × (Subtotal quoted ÷ direct cost), for Project overheads, Precon services, General conditions (always $0 in the original), the three reserves (only when present on the roll-up), P & P bond, Works cover, Fee, L & P insurance.
- [ ] Unticking an indirect stores it out of `IncludeCodes` and drops its amount; Total quoted = Subtotal quoted + included indirects.
- [ ] Every save sends `rowRev`; a stale save returns 409 with the clash message.

### Technical spec
**Frontend:** `web/src/pages/project/Alternates.tsx` (route `/p/:no/v/:n/alternates`, sidebar *Pricing → Alternates & VE*, `data-was="Alternates / Value engineering"`). Query key `vkey('alternates', versionId)`. Component `AltPanel`.

**Backend:** `PATCH /api/alternates/{id}` `{ description?, quotedPct?, status?, comments?, includeCode?, included?, rowRev }` → refreshed list. Needs `scope.edit`, unlocked version.

**Database:** `PriceOption.QuotedPct`, `IncludeCodes`, `RowRev`; `RollupLine` for the indirect amounts.

### Dependencies
Ticket 01. Original: AlternatePanel.js, AlternateRowDetails.js.

### Notes / edge cases
- Direct cost = trades + project overheads + precon + general conditions from the roll-up; when it is 0 every indirect is 0.
