## Title
Portfolio — Browse, filter and open projects

### Summary
The `/projects` route shows every project the signed-in user can see as a sortable table, with stage tabs and a
free-text search box that narrow it down, plus two ways to jump into a project (open in place or open in a new
tab). This is the landing page of the app and the main way an estimator finds "their" work.

### User story
As an estimator or project manager, I want to see all my projects at a glance, filter by stage or search by name/
number/client/city, and open one with a single click, so that I can get back to pricing without hunting through a
spreadsheet.

### Acceptance criteria
- [ ] On navigating to `/projects`, the page shows "Loading projects…" until the project list request resolves,
      then renders the table, or shows "Couldn't load projects: {message}" if the request fails.
- [ ] A row of stage tabs reads `All, Lead, Pursuit, Bidding, Submitted, Awarded, Lost` (title-cased); each tab
      shows a count badge. The `All` count is the full unfiltered row count; every other tab's count is the
      number of rows whose `stage` matches that tab, computed from the same unfiltered dataset — counts never
      reflect the current search text, only the full fetched list.
  - [ ] `archived` is a valid value of the `Stage` type but is **not** one of the tabs, so a project whose `Stage`
        column happens to be `'archived'` (set via the project profile PATCH, independent of the project being
        soft-deleted) is invisible under every tab except `All`.
- [ ] A search box filters the already-fetched rows client-side (no refetch) by case-insensitive substring match
      against `projectName`, `projectNo`, `city`, and `clientName` concatenated together; it combines with the
      selected stage tab (`AND`, not `OR`).
- [ ] Each row shows: a favourite star, the project name (link) with project number and `city, state` underneath
      it, a `{peopleHere} working now` suffix when `peopleHere > 0`, a coloured stage tag (green = awarded, red =
      lost, blue = everything else), client name, bid-due date, `tradesCarried/trades`, `money(contractTotal ??
      tradesTotal)`, estimator name, and an "open in a new tab" (↗) icon button.
- [ ] Clicking the project name navigates in-app to `/p/{projectNo}/v/{primaryVersionNo ?? 1}/bid-board` unless
      the click carries Ctrl/Cmd (browser handles that as "open in new tab" natively, since the anchor's `href`
      is the real URL and `preventDefault()` is only called for a plain click).
- [ ] Clicking the ↗ button always opens that same URL via `window.open(url, '_blank')`, regardless of modifier
      keys.
- [ ] If no row survives the stage + search filter, the table body is replaced with "No projects match." (shown
      even when the underlying fetch succeeded and returned rows — it only depends on the filtered `rows` array).
- [ ] The page never re-fetches on its own after the initial load except when the user calls `refetch()` by
      toggling a favourite or completing "Start a project" — it does not join any realtime group, so stage
      changes, new trades, bid updates, or presence changes made by other users while this page is open are not
      reflected until the user navigates away and back (remounting the query) or triggers one of those two
      `refetch()` calls.

### Technical spec
**Frontend:** `web/src/pages/Portfolio.tsx`, component `Portfolio({ startNew })`. Data: `useQuery({ queryKey:
['projects'], queryFn: () => api.get<ProjectRow[]>('/projects') })` — always calls `GET /projects` with no query
string. Local state: `stage` (`Stage | 'all'`), `q` (search text). `rows = useMemo(...)` applies the stage/search
filter over `data`. `open(p, newTab)` builds `/p/{encodeURIComponent(p.projectNo)}/v/{p.primaryVersionNo ?? 1}/bid-
board` and either `nav()`s (react-router) or `window.open(url, '_blank')`. `ProjectRow` (from `web/src/api/
types.ts`) carries `projectId, projectNo, projectName, stage, clientName, projectType, city, stateCode, bidDueOn,
primaryVersionNo, grossArea, trades, tradesCarried, tradesTotal, contractTotal, estimator, isFavourite,
peopleHere`. No SignalR `join`/`onChange` wiring exists on this page (contrast with `ProjectLayout`, which does
join a project's realtime group — see Architecture.md Realtime).

**Backend:** `GET /api/projects` (`ProjectEndpoints.Map`, file `Endpoints/ProjectEndpoints.cs`). Accepts optional
`string? search, string? stage` query parameters that this page never sends. No `Require(...)` call — visibility
is enforced entirely in the SQL `WHERE` clause (see Database below) using `ctx.Me()`'s `IsAdmin` and
`Can("project.edit")`/`Can("project.view")` capabilities plus an `EXISTS` check against `ProjectMember`. Returns a
plain `IEnumerable` (anonymous/dynamic row shape), not a typed DTO.

**Database:** Reads view `ProjectSummary` (`ProjectId, ProjectNo, ProjectName, Stage, ClientName, ProjectType,
City, StateCode, BidDueOn, PrimaryVersionId/No/Label, GrossArea, Versions, Trades, TradesCarried, TradesTotal,
ContractTotal`), redefined in `db/migrations/004_names.sql` (supersedes the `001_init.sql` definition) and built
from `Project` (filtered `WHERE ArchivedAt IS NULL`) left-joined to its `IsPrimary` `ProjectVersion`, with
`Trades`/`TradesCarried` counted from `Trade` (`ArchivedAt IS NULL`, carried = has a `Bid` with `IsCarried AND
Bid.ArchivedAt IS NULL` — the `Bid.ArchivedAt` half of that check was added in `004_names.sql` and is easy to miss
if you're only skimming `001_init.sql`), `TradesTotal` summed from `coalesce(Trade.CarriedValue, Trade.LowestValue)`,
and `ContractTotal` pulled from `RollupLine` where `LineName = 'Contract total'` (lower-case "total" — `004_names.sql`
renamed this from the original `001_init.sql` value of `'Contract Total'`; `Calc/Rules.cs`'s `Add("Contract
total", ...)` is what actually writes that row, confirming the lower-case spelling is current). Joined to
`RecentProject` (`LEFT JOIN ... ON r.ProjectId = s.ProjectId AND r.UserId = @uid`) for `isFavourite`/`openedAt`,
and to a correlated subquery against view `WhoIsHere` (`count(DISTINCT UserId)`, project-wide — not scoped to the
version/tab the viewer is on) for `peopleHere`. `estimator` is resolved via a correlated subquery joining
`Project.EstimatorId` to `AppUser`. Sort: `isFavourite DESC, openedAt DESC NULLS LAST, bidDueOn NULLS LAST,
projectNo`. No writes happen on this endpoint.

### Dependencies
- Architecture.md: Authorization/scoping (capability-based visibility, no per-request `Access.Check` 403/423 here
  since this is a list, not a single-entity read), Realtime (this page deliberately doesn't join), Database
  conventions (view-based denormalization).
- `../02-favourite-a-project/02-favourite-a-project.md` — the star this ticket renders is toggled there.
- `../03-export-portfolio-to-excel/03-export-portfolio-to-excel.md` and
  `../04-start-a-blank-project/04-start-a-blank-project.md`/
  `../05-import-a-project-from-excel-template/05-import-a-project-from-excel-template.md` — the other actions on
  this same page/toolbar.
- Downstream: `ProjectLayout`/Bid board page — every row and the "Start a project" flow both land there.

### Notes / edge cases
- Because `@view` (the `project.view` capability) is OR'd unconditionally into the visibility predicate, any user
  with that capability sees **every** non-archived project company-wide, not just ones they're a member of —
  `ProjectMember` only matters for users who hold neither `project.edit` nor `project.view` nor admin.
- `peopleHere` reflects presence across the whole project (any version, any tab/page), not just whoever is on the
  bid-board tab this row's link would open.
- The search box and stage tabs never issue a new request; everything after the initial `GET /projects` is
  client-side array filtering over the one cached result set.
- `ProjectSummary` has been redefined once (migration `004_names.sql`); always check for the latest `CREATE OR
  REPLACE VIEW` when quoting its column logic instead of trusting the first `CREATE VIEW` in `001_init.sql`.
