## Title
Alternates & VE — Add, delete and bulk status

### Summary
Add an alternate or VE idea from a modal; select rows to set a status or delete them in one go.

### User story
As an estimator, I want to add ideas quickly and approve or reject several at once.

### Acceptance criteria
- [ ] Header buttons **Add alternate** and **Add VE idea** open the same modal with Type preset (Alternate / Value engineering), Description (required), Quoted % (default 100), Status, Comments.
- [ ] After adding, the new item is expanded.
- [ ] Selecting rows shows a bar: Deselect, Proposed, Approved, Rejected, Delete (with confirmation).
- [ ] Deleting removes the trade amounts too.

### Technical spec
**Frontend:** `web/src/pages/project/Alternates.tsx` (route `/p/:no/v/:n/alternates`, sidebar *Pricing → Alternates & VE*, `data-was="Alternates / Value engineering"`). Query key `vkey('alternates', versionId)`. Components `AddAltModal`, bulk bar.

**Backend:** `POST /api/versions/{id}/alternates` `{ kind: option|alternate|saving|ve, description, quotedPct, status, comments }`; `POST /api/versions/{id}/alternates/bulk` `{ ids, status }` (status `null` = delete).

**Database:** `PriceOption`, `PriceOptionTrade` (ON DELETE CASCADE).

### Dependencies
Ticket 01.

### Notes / edge cases
- Version copy (`Versions.CopyContent`) copies alternates and their trade amounts.
