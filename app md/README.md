# Gridline — Developer build tickets

This folder documents the existing Gridline application (`D:\NCC_CODES_Specialized\FullOverRide-codes\gridline`)
as **developer build tickets**: one markdown file per distinct feature of each page/tab, written so a developer
could rebuild that exact feature — frontend, backend endpoint, and database — from the ticket alone. They
describe verified current behavior (read from the real code), not a proposal.

Start here: **[`_Overview/Architecture.md`](_Overview/Architecture.md)** — shared platform conventions every
ticket assumes and cross-references instead of repeating: auth, authorization/scoping, the `Work` unit-of-work +
realtime-broadcast pattern, the recalculation engine, background jobs, and the database schema shape.

## Layout

Each page/tab gets its own folder. Inside it, **every feature gets its own sub-folder** containing exactly one
ticket file of the same name:

```
app md/<Page>/
  _index.md                                    ← one-line summary of every feature folder below
  01-kebab-case-feature-name/
    01-kebab-case-feature-name.md               ← the ticket
  02-another-feature/
    02-another-feature.md
  ...
```

## Pages / tabs (78 tickets across 11 areas)

| Folder | Route / tab | Tickets |
|---|---|---|
| [`SignIn`](SignIn/_index.md) | `/signin` | 4 |
| [`Portal`](Portal/_index.md) | `/bid/:token` (external, token-gated subcontractor bid portal) | 2 |
| [`Portfolio`](Portfolio/_index.md) | `/projects` | 5 |
| [`StartProject`](StartProject/_index.md) | `/projects/new` (new-project flow opened from Portfolio) | 3 |
| [`Profile`](Profile/_index.md) | project tab `profile` — Project profile | 9 |
| [`CrewPlan`](CrewPlan/_index.md) | project tab `crew-plan` — Crew plan | 11 |
| [`Costs`](Costs/_index.md) | project tabs `precon-services` + `overheads` (one component, two tabs) | 10 |
| [`BidBoard`](BidBoard/_index.md) | project tab `bid-board` — Bid board | 12 |
| [`ScopeMatch`](ScopeMatch/_index.md) | project tab `scope-match` — Scope match | 11 |
| [`BaseRequirements`](BaseRequirements/_index.md) | project tab `base-requirements` — Base requirements | 6 |
| [`PriceRollUp`](PriceRollUp/_index.md) | project tab `price-roll-up` — Price roll-up (incl. the Estimator what-if modal) | 5 |

## Ticket format

Every ticket follows the same template: **Title**, **Summary**, **User story**, **Acceptance criteria**
(testable bullets describing exact current behavior), **Technical spec** (Frontend file/component + the exact
`api.*` calls it makes · Backend endpoint route/method/auth checks · Database tables/columns/triggers/indexes),
**Dependencies** (other tickets and the relevant Architecture.md sections), **Notes / edge cases**.

## How this was produced

**Pass 1** — an 11-agent workflow (one agent per page/tab), each independently reading the real frontend
component(s), the matching backend `Endpoints/*.cs` file(s), and `db/migrations/*.sql`, then splitting that
page's functionality into tickets at natural feature grain.

**Pass 2 (audit + restructure)** — a second 11-agent workflow re-read every existing ticket against an
authoritative, hand-extracted list of every HTTP route in the relevant backend endpoint file(s), re-read the
frontend component(s) again line-by-line for any UI action without a ticket, and converted every flat ticket
file into its own feature folder. Outcome:
- **No missing pages or endpoint groups** — every route in every endpoint file used by the web app is accounted
  for by a ticket or explicitly marked out-of-scope-for-this-page with a pointer to where it *is* documented.
- **Two backend routes confirmed genuinely unused by the frontend** (`GET`/`POST /projects/{id}/budgets` —
  budget-column creation, and `GET /benchmarks`) and one confirmed unused on this page but used elsewhere
  (`PUT /trades/{id}/lanes`, used only from Scope match) — documented as notes in `BidBoard/01-...` rather than
  invented tickets, since inventing a ticket for dead code would misdocument the app.
- **Portfolio ↔ StartProject duplication resolved** — `Portfolio/04-start-a-blank-project` and
  `Portfolio/05-import-a-project-from-excel-template` now document only the Portfolio-side entry point and
  cross-reference the `StartProject/` tickets for the full technical trace, instead of repeating it.
- **A handful of factual corrections** from re-reading the code a second time (e.g. an unreachable 403 path
  corrected to the real 404 behavior in `Portfolio/02`, an exact DB view literal case-sensitivity fix in
  `Portfolio/01`/`03`, a `DISTINCT`-aggregation detail in `SignIn/04`).

No ticket content was hand-written or guessed — every file name, route, column, and trigger cited was verified
against the source in this repository as of 2026-11-04.

**Pass 3 (2026-11-04)** — brought the tickets up to date with the current code: Crew plan and Precon services / Project overheads were rewritten
(employee groups, columns, Set hours/budget, per diem, resource loader, schedule dates; precon team with tasks, quoted % / total, expenses grid);
added `Profile/09-schedule-dates`; rewrote `ScopeMatch/01` (spreadsheet grid with Excel copy/paste) and `ScopeMatch/07` (FortuneSheet scratch pad)
and `BaseRequirements/03` (grid editing); added *Spreadsheet grid* and *Error handling* to `_Overview/Architecture.md`. The previous Crew plan and
Costs tickets were moved to `_to_delete/app-md-old/`.
