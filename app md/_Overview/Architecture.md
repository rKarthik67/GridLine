# Gridline — Platform Architecture (shared reference, not a ticket)

Every ticket under `app md/<Page>/*.md` is a developer build ticket for one feature of one page. This file is
the shared background they all point back to instead of repeating: the plumbing every ticket's "Technical spec"
section assumes already exists (auth, realtime, the Work/Access/Recalc patterns, the DB schema shape). Written
from `D:\NCC_CODES_Specialized\FullOverRide-codes\gridline` as of 2026-11-04.

## Stack

- **Frontend**: `web/` — React + TypeScript, Vite build, React Router v7, TanStack Query (`@tanstack/react-query`),
  `ag-grid-community`/`ag-grid-react` for spreadsheet-like grids, `@microsoft/signalr` client.
- **Backend**: `api/` — ASP.NET Core minimal API on **.NET 10**, Dapper for SQL (no ORM/EF), QuestPDF for PDF
  generation, ClosedXML/DocumentFormat.OpenXml for Excel import/export, PdfPig for reading uploaded PDFs,
  SignalR for realtime (`/hubs/project`).
- **Database**: PostgreSQL, schema `gridline` (search_path `gridline,public`), raw SQL migrations in `db/migrations/`
  (001_init.sql → 007_fee_carveouts.sql), no migration framework — files are applied in order. `db/gen_seed.py`
  generates `002_seed.sql`.
- Single process serves both: if `web/dist` exists, the API host serves the built SPA as static files and
  falls back to `index.html` for client-side routing; otherwise the frontend runs separately via Vite dev server
  proxying `/api` and `/hubs` to the API.

## Routing map (`web/src/App.tsx`)

| Path | Component | Auth | Notes |
|---|---|---|---|
| `/signin` | `pages/SignIn.tsx` | public | |
| `/bid/:token` | `pages/Portal.tsx` | public (token-gated) | external subcontractor bid portal |
| `/projects` | `pages/Portfolio.tsx` | private | project list |
| `/projects/new` | `pages/Portfolio.tsx` (`startNew` prop) | private | same component, opens the new-project flow (`pages/StartProject.tsx`) |
| `/p/:projectNo/v/:versionNo/:page` (+ optional `/*`) | `pages/ProjectLayout.tsx` | private | loads the project/version, renders one tab from `PROJECT_PAGES` |
| `/p/:projectNo` | `pages/ProjectLayout.tsx` | private | redirects to the primary version |
| `*` | → `/projects` | private | catch-all |

`Private` (in `App.tsx`) is a route guard: reads `useAuth()`, redirects to `/signin?next=...` when signed out.

### Project tabs (inside `ProjectLayout`, from `web/src/app/pages.ts` and `web/src/pages/ProjectLayout.tsx`)

`PROJECT_PAGES`, grouped into sidebar sections `project` / `costs` (labelled "Crew & costs") / `pricing`:

| key | label | component | group | legacy name (`was`) |
|---|---|---|---|---|
| `profile` | Project profile | `project/Profile.tsx` | project | Project setup / Project information |
| `crew-plan` | Crew plan | `project/CrewPlan.tsx` | costs | Project team |
| `precon-services` | Precon services | `project/Costs.tsx` | costs | Preconstruction |
| `overheads` | Project overheads | `project/Costs.tsx` (same component, different `pageKey`) | costs | General requirements |
| `bid-board` | Bid board | `project/BidBoard.tsx` | pricing | Bid leveling / Trade summary |
| `scope-match` | Scope match | `project/ScopeMatch.tsx` | pricing | Scope leveling |
| `base-requirements` | Base requirements | `project/BaseRequirements.tsx` | pricing | General scope |
| `price-roll-up` | Price roll-up | `project/PriceRollUp.tsx` | pricing | Executive summary |

`project/Estimator.tsx` is not a routed page — it exports `EstimatorModal`, a what-if calculator opened from
Price roll-up only (documented inside `PriceRollUp.md`).

Each tab component receives `{ project: ProjectDetail, version: Version, pageKey: string }` as props from
`ProjectLayout`, which itself loads `GET /api/projects/{projectNo}` once and shares it across tabs.

## Frontend conventions

- **API client** (`web/src/api/client.ts`): thin `fetch` wrapper. Every request carries header `X-Tab-Id` (a
  per-browser-tab UUID in `sessionStorage`, key `gridline.tab`) and `Authorization: Bearer <token>` from
  `localStorage` (key `gridline.token`). Base path is always `/api`. `api.download()` is used for file exports
  (reads the filename from `Content-Disposition`). Non-2xx responses throw `ApiError {status, message, code,
  detail}`; a 401 clears the stored token.
- **Auth state** (`web/src/app/auth.tsx`): `useAuth()` — not read in this pass in depth per-page, but every
  private page depends on it; the current user and `me.capabilities` drive what UI is enabled (see `Auth.cs`
  below for how capabilities are computed server-side).
- **Realtime** (`web/src/live/hub.ts` + backend `Live/ProjectHub.cs`): one SignalR connection per browser tab.
  `join(projectId, versionId, page)` is called by `ProjectLayout` on mount/navigation; it calls the hub's
  `Join` method, which adds the connection to SignalR group `p{projectId}`, upserts a `Presence` row, and
  upserts `RecentProject`. `focus(kind, key, activity)` reports fine-grained cell/row focus (`viewing` /
  `editing` / `typing` / `idle`) for the "who's editing this cell" indicators. Two server→client events:
  - `presence` — full list of who's on the project (`PresenceRow[]`), rebroadcast on any join/focus/leave/disconnect.
  - `changed` — one `ChangeEvent` per committed write (see Work/ActivityEvent below), sent to every tab **except**
    the tab that made the change (`GroupExcept`, matched by `X-Tab-Id`). The frontend's `onChange` handler
    invalidates the relevant TanStack Query cache keys and shows a toast ("{person} {verb}").
  - `components/Presence.tsx` renders the live avatar stack from the `presence` event.
- **Formatting/verbs**: `web/src/app/format.ts` (number/currency/date formatting, `cap()`), `web/src/app/verbs.ts`
  (`describe(verb)` — turns an `ActivityEvent.Verb` into human text for toasts).
- **Shell/navigation**: `web/src/components/Shell.tsx` renders the sidebar (`PROJECT_PAGES`/`PAGE_GROUPS`), top
  bar (project/version picker, breadcrumbs), and hosts `Presence`.

## Backend conventions

- **Program.cs** wires one `MapGroup("/api")`, tagged by feature area for Swagger, each mapped from its own
  `Endpoints/*Endpoints.cs` static class:
  - `AuthEndpoints` — "Sign-in and lookups"
  - `ProjectEndpoints` — "Portfolio, versions and project profile"
  - `TradeEndpoints` — "Bid board"
  - `ScopeEndpoints` — "Scope match"
  - `ScopeExtrasEndpoints` — "Scope match: files, qualifications, base requirements"
  - `SummaryEndpoints` — "Price roll-up"
  - `TeamEndpoints` — "Crew plan, precon services, project overheads"
  - `ImportEndpoints` — "Excel import"
  - `Export.ExportEndpoints` — "Exports"
  - `PortalEndpoints` — "Bid portal"
  - `DocumentEndpoints` — "Documents and bid inbox (proposal reader)"
  - `GET /api/health` is anonymous (checks DB connectivity + max `SchemaChange.ChangeNo`).
  - `/hubs/project` is the SignalR hub endpoint.
  - Fallback: unknown `/api/*` → 404 problem+json; everything else (when `web/dist` exists) → `index.html`.
- **Auth** (`Infrastructure/Auth.cs`): custom scheme `"Gridline"` (`TokenAuthHandler`), opaque bearer tokens —
  not JWT. Token is a random 32-byte value, only its SHA-256 hash is stored (`LoginToken.TokenHash`); looked up
  per request (30s in-memory cache keyed `tok:<token>`), must be unrevoked and unexpired. SignalR passes the
  same token as `?access_token=` on the websocket URL. `CurrentUser` carries `Roles` and `Capabilities` (from
  `UserRole` → `AccessRole`/`RoleGrant`); `Capabilities` of `"*"` means admin/can-do-anything. Passwords are
  PBKDF2-SHA256 (100k iterations) via `Passwords.Hash/Verify`.
- **Authorization/scoping** (`Infrastructure/Access.cs`): per-request access level resolved from `ProjectMember`
  (explicit grant) or falls back to `owner` (admin), `editor` (has `project.edit` capability), else `viewer`.
  `Access.Version/Trade/Bid/Line/Cost/Indirect/Project/TeamMember(...)` all resolve a `Scope` (ProjectId,
  VersionId, TradeId, Status, Access) from the entity id and `Access.Check` throws `403` if the rank is too low,
  or `423 Locked` if the target version's `Status == "locked"` and the caller needs more than `viewer`.
- **Unit of work** (`Infrastructure/Work.cs`): `Work` wraps one Postgres connection + transaction per request.
  Every mutating endpoint calls `Work.Begin(ctx)`, does its reads/writes through `w.Exec/Query/One/Scalar`, calls
  `w.Log(projectId, versionId, verb, subjectType, subjectKey, before, after)` to append an `ActivityEvent` row,
  then `w.Commit()`. Commit both commits the DB transaction **and** broadcasts a `changed` SignalR event (one per
  logged change) to the project's group, excluding the originating tab. This is the mechanism behind the
  "someone else just changed this" toast + cache invalidation on every other open tab.
- **Recalculation engine** (`Calc/Recalc.cs`, `Calc/Rules.cs`): hybrid calculation — the browser computes bid
  totals/roll-ups client-side for instant feedback, then the server recomputes authoritatively and stores the
  result (`Trade.LowestValue/CarriedValue/BudgetDelta/BuyoutPct`, zone splits, `RollupLine`/`RollupZone`), logging
  a `CalcRun` row to detect client/server mismatches. `Recalc.Trade(w, tradeId)` is called after any bid/scope
  change that affects a trade; `Recalc.Version(w, versionId, reason)` recalculates a whole version (also run at
  app startup for any version missing a roll-up or with a trade flagged `NeedsRecalc`).
- **Background services**: `PresenceSweeper` (every 15s: refresh heartbeats for connected tabs, delete stale
  `Presence`/`EditLock` rows) and `ProposalReader` (`Proposal/ProposalReader.cs`) — a queue-driven background
  worker that OCRs/reads uploaded subcontractor proposal PDFs (PdfPig), optionally calls Azure OpenAI if
  configured (else falls back to a rules-based base-amount/keyword reader), and writes `ProposalSuggestion` rows
  that the Scope match UI can accept.
- **Import/Export**: `Import/` (XlsxReader, Template, ImportService) backs `ImportEndpoints` for bulk Excel
  uploads (bid board/scope templates) logging `ImportBatch`/`ImportIssue`. `Export/ExportEndpoints.cs` generates
  PDFs (QuestPDF) and Excel workbooks (ClosedXML) for bid board, scope match and price roll-up, logging
  `ExportRun`.
- **Db.cs**: opens `NpgsqlDataSource` with `SearchPath = gridline,public`, 300s command timeout (for big
  imports), custom Dapper type handlers for `JsonElement`→jsonb and `DateOnly`.

## Database conventions (`db/migrations/001_init.sql` plus later files)

- Schema `gridline`; ~70 tables covering: identity/access (`AppUser`, `AccessRole`, `RoleGrant`, `UserRole`,
  `LoginToken`, `UserPreference`), company reference data (`CompanyProfile`, `Division`, `Branch`, `StaffRole`,
  `Staff`, `Setting`, `UsState`, `County`, `CodeEdition`, `CodeGroup`, `WorkCode`, `UnitOfMeasure`, `MarkupType`,
  `BondBand`, `TravelRate`), subcontractor directory (`TradePartner`, `PartnerBranch`, `PartnerPerson`,
  `PartnerCode`), project/version core (`Project`, `ProjectVersion`, `ProjectMember`, `RecentProject`, `Zone`,
  `VersionMarkup`, `Document`), templates (`ScopeKit`, `ScopeKitLine`, `RequirementSet`, `RequirementLine`,
  `CostKit`, `CostKitItem`), pricing (`Trade`, `TradeZone`, `Bid`, `BudgetColumn`, `BudgetValue`, `IndirectItem`,
  `ScopeLine`, `ScopeAnswer`, `EstimateLine`, `ScratchSheet`, `RollupLine`, `RollupZone`, `CalcRun`), crew/cost
  (`ProjectWeek`, `TeamMember`, `TeamMemberWeek`, `TeamMemberActivity`, `CostLine`, `Activity`), bidding/portal
  (`BidInvite`, `BidSubmission`, `ProposalReading`, `ProposalSuggestion`), collaboration/audit (`Presence`,
  `EditLock`, `ActivityEvent`), imports/exports (`ImportBatch`, `ImportIssue`, `ExportRun`), and `SchemaChange`
  (schema version marker used by `/api/health`). Later migrations add scope-match parity: `Qualification`,
  `QualificationSet`, `QualificationSetLine`, `PriceOption`, `PriceOptionTrade`, `PriceOptionBid`,
  `ProvisionalSum`, `ProvisionalSumBid`, `UnitRate`, `UnitRateBid` (005), zones (006), fee carve-outs (007).
- **Generic triggers**: `TouchRow()` (sets an updated-at column) is attached via a loop (`CREATE TRIGGER TrgTouch
  ... EXECUTE FUNCTION gridline.TouchRow()`) to every table that has the relevant column. `SetProjectId()`
  denormalizes `ProjectId` onto child rows on insert/update (`TrgTradeProject` on `Trade`, `TrgBidProject` on
  `Bid`) so queries don't need deep joins just to scope by project.
- **View**: `WhoIsHere` (referenced by `ProjectHub.Rows`) — presence joined with user display info.
- **Versioning model**: everything pricing-related hangs off `ProjectVersion` (replaces the legacy "BidQuote"),
  `ProjectVersion.IsPrimary` marks the version shown by default, `ProjectVersion.Status` of `locked` blocks
  edits (enforced in `Access.Check`, not at the DB layer).
- 002_seed.sql is generated by `db/gen_seed.py` and loads reference/demo data.

## How the tickets use this doc

Each ticket's "Technical spec" traces one feature: the page's React component(s) and what they render/let the
user do → which `api.get/post/put/patch/del` calls it makes → the exact backend endpoint(s) in `Endpoints/*.cs`
that serve them (route, auth/capability checks, `Access` scope check) → the SQL/Dapper statements those
endpoints run → the underlying tables/columns/triggers/indexes in `db/migrations/*.sql` → any SignalR events
emitted (`Work.Log` verbs) and how other open tabs react to them. Tickets should link back here (`See
_Overview/Architecture.md`) for auth/access/realtime/recalc behavior rather than re-explaining it.


## Spreadsheet grid (`DataGrid`)

`web/src/components/DataGrid.tsx` wraps **RevoGrid** (`@revolist/react-datagrid` 4.31.4, MIT) and is used by Scope match, Base requirements, Precon services (team and expenses) and Project overheads.

- **Columns** (`GCol<R>`) declare `prop`, `name`, `size`, `pin` (start/end), `align`, `edit` (bool or per-row), `value` (the raw value copied to Excel and shown in the editor), `show` (hyperscript for chips/buttons), `cls`, `tip`, `head` (custom header) and `drag` (row drag handle).
- **The grid owns no data.** Single edits (`beforeedit`), range edits from paste or fill handle (`beforerangeedit`) and row drags (`roworderchanged`) are cancelled and reported as `onEdit(GridEdit[])` / `onRowMove`. The page validates, saves through the API and puts the reply in the TanStack Query cache, so the grid is redrawn from server truth, and live changes from other users arrive the same way.
- **Excel interop:** range selection, Ctrl+C (tab-separated), Ctrl+V of a block from Excel, fill handle. `pasteNum()` reads “$1,234.50”, “(200)”, “12%”.
- **Other props:** `top`/`bottom` pinned rows (totals), `rowCls`, `onMenu` (right-click with row + column), `onCellClick`, `focusKey` (scroll a row into view), `readOnly`.
- **Layout:** pages whose last card holds a grid (or the Bid board sheet) fill the window: the card stretches to the bottom and the grid takes the remaining height (`app.css`, “grid pages fill the window”).

## Error handling

- **API:** `Infrastructure/Errors.cs` (`UseApiErrors`) turns every exception into problem JSON `{ title, detail, status, code, errors, traceId }`. `ApiException` carries user-facing 400/403/404/409/423 messages; PostgreSQL 23505/23503/23514… become readable 409/400; 40001/40P01 → 409 “try again”; 57014 and unreachable/restarting database → 503 with a plain message; 5xx are logged with method, path and trace id, and the message includes “Reference: <traceId>”. A request the browser abandoned is not logged as an error. `Work` rolls back any transaction that wasn't committed.
- **Web:** `useAction().run()` shows the API message as a toast (clash messages with the sync icon). `components/ErrorBoundary.tsx` wraps the whole app and each project page (keyed by page) and shows “Something went wrong on <page>” with **Try again** and **Reload page** instead of a blank screen.
