# Gridline: first-release build plan

**Rule for every item below:** it is only "done" when it works end to end. That means it saves to PostgreSQL, the other people on the project see the change, and the automated test for it passes.

## Stack

| Layer | Choice |
|---|---|
| Database | PostgreSQL 17. One schema, `gridline`, with no underscores in names. Migrations live in `db/migrations`. |
| API | .NET 10 minimal API, Npgsql + Dapper, SignalR (live updates and presence), ClosedXML (Excel), QuestPDF (PDF), a background job queue, and xUnit + Testcontainers for tests. |
| Web | React 19 + TypeScript + Vite, AG Grid Community, TanStack Query and Router, plus our own design tokens (Orchid & Tangerine) and Playwright end-to-end tests. |
| Calculation | `@gridline/calc` in TypeScript, used by the browser for instant results. The API has the same rules in C#. Both must pass one shared JSON test set. |

## Pages and what must work

### 1. Sign-in and app shell
- Sign in with a local account (dev and test); Entra / Okta SSO comes later. Sign out.
- A sidebar and top bar, project switcher, search (Ctrl K), and who's here (avatars).
- **Several projects in several tabs.** Every page address carries the project and version, for example `/p/EST-24031/v/2/trade-summary`. Each tab loads its own project, has its own live connection and shows up as its own presence row. Nothing project-specific is kept in shared browser storage.

### 2. Portfolio (projects list)
- **List, search and filter:** search, filter by stage, type and estimator, and sort.
- **Shortcuts:** favourites, recent projects and paging.
- **Open a project:** in the same tab or a new one.
- **Export the list:** to Excel.

### 3. New project
- **Start blank:** a form that creates the project, Version 1 and its zones.
- **Import from Excel (Gridline-Project-Import-Template.xlsx):** upload or drop the workbook, check every row (errors block the import), preview and match, then create.
  - The project, version, zones, trades, bids, team and base requirements are written in one transaction.
  - It handles large files: tested up to 284k rows.
- **Copy an existing project:** creates a new project from a chosen version.

### 4. Project information (setup)
- **Details:** edit project details (name, number, client, address, stage, dates, type, contract, division, branch, estimator, manager).
- **Zones:** add, edit, reorder and delete zones (breakout areas), with SF, units and default split.
- **Versions:**
  - List versions, create a new version (copying everything from a chosen one), rename, set the primary version, and lock a version.
  - Compare two versions trade by trade.
- **Members:** add or remove people and set their access (viewer / editor / owner).
- **Markup rates for this version:**
  - Reserves, bond, builders risk, insurance and fee.
  - Sales tax, using the state default unless changed.
- **Documents:** upload and download.

### 5. Trade summary (bid board)
- **The grid:** every trade with all its bids side by side.
  - Carry a bid or clear it (only one carried bid per trade is enforced by the database).
  - Bid details panel with rank and status.
  - Change a bid's status, invite a bidder, and add or edit a bid (base amount, status).
- **Per trade:**
  - Plug amount, adjustment, client note and team note.
  - Add, rename and delete a trade, and pick its CSI code.
  - Zone split.
- **Columns:**
  - Budget and Δ budget, using the budget source chosen from the custom budget columns. Copy a budget between versions.
  - Lowest and Δ to lowest, buyout % and anticipated buyout.
  - Zone totals and $/SF.
- **Benchmarks:** a $/SF range per trade from past projects of the same type. Bids outside the range are flagged.
- **Toolbar:** filters (unresolved, not lowest, single bid, with plug / notes) and switches (budget, only unresolved, anticipated buyouts).
- **Footer totals:** live totals that match the executive summary.
- **Import trades into this version from Excel**, using the same template and the Trades and Bidders sheets.
- **Export:** trade summary (Excel) and trade quotes (PDF).

### 6. Scope match
- **Choosing a trade:** a trade picker with previous / next.
- **Bidder lanes:** coverage, total, rank and a Carry switch.
- **Sections:**
  - Totals band.
  - Base requirements (text locked).
  - Trade items (editable).
  - Zones (read-only, shows the trade's split).
- **Editing answers:** each answer is Included, Excluded, an amount, Price or text.
  - **Typing an answer:** formulas such as `=62700+25000` work.
  - **Cell extras:** cell colour, a note on the cell, and a note on the line.
- **Editing lines:** insert a row or heading, indent, edit the text, delete, and "not in adjustments".
- **Finding lines:** search with previous / next, and "gaps only".
- **Templates:** apply a scope kit (template) to a trade.
- **Other tabs:** internal estimate tab (qty × unit cost) and scratch pad.
- **AI proposal reader:**
  - Upload a sub's proposal PDF on a lane. The AI suggests answers and a base amount, with the quoted words and page number as evidence.
  - Exclusions without a matching line are listed separately.
  - You accept or reject each suggestion, or all at once. Accepted suggestions are saved with source `proposal`.
- **Bid portal:**
  - Send a link to a sub (by email or copy link). The sub opens a no-login page showing the scope lines and enters a base bid and answers.
  - You review the submission and accept it into the lane. The invite status shows draft → sent → opened → submitted.
- **Export:** scope match sheet (Excel) and trade package (PDF).

### 7. Executive summary (price roll-up)
- **The build-up:** trades total, general requirements, direct cost total, preconstruction, reserves, subtotal, bond, builders risk, insurance, fee, custom indirect items, contract total, sales tax, and contract total with tax.
- **For every line:** rate, amount, % of contract, $/SF, amount by zone, and budget vs. delta.
- **Editing here:** rates and custom indirect items can be changed on this page. Each change is recalculated and saved.
- **Views:** heat map or plain view, and Total or Total + $/SF.
- **Export:** executive summary (Excel and PDF).

### 8. Project team
- **Team members:** add, remove and reorder members (staff member or TBA), with role, hourly cost, bill rate, travel (per diem) and relocation.
- **The resource loader:**
  - Hours per week, by month or by week. Fill a range, copy a row, and clear.
  - Phases: precon, build and closeout.
- **Totals:** hours, cost and revenue by phase and per member. Precon-phase team cost feeds the Preconstruction page.
- **Export:** project team (Excel).

### 9. Preconstruction
- **Cost lines:** team (from the project team, precon phase) plus expense lines grouped by category. Each line has qty, unit, rate, quoted rate and a billable flag.
- **Totals:** actual, quoted and difference.
- **Templates:** apply a cost kit. Add, edit and delete lines.
- **Feeds the executive summary:** the total goes to its Preconstruction line.
- **Export:** to Excel.

### 10. General requirements
- **Cost lines:** the same as Preconstruction, for site and project overheads.
- **Feeds the executive summary:** the total goes to its General requirements line.
- **Export:** to Excel.

### 11. Working together (on every page)
- **Live avatars:** shown per project and page, with a "who's here" popover (Go to).
- **What others are doing:** the cell someone is typing in is outlined in their colour, with a "typing…" badge.
- **Live changes:** changes by others appear without a reload, through SignalR events from `ActivityEvent`.
- **Clashes:** two people saving the same row is detected with `RowRev`. The second person sees "changed by Alice – reload / overwrite".
- **Activity panel:** a per-project activity feed (who did what, when).

### Later (hidden in this release)
Alternates, value engineering, unit prices, allowances, field office support, risk register, estimate reviews, client summary and variance.

## API (all under `/api`; every write records an ActivityEvent and is broadcast)

| Area | Endpoints |
|---|---|
| Auth | `POST /auth/login` · `POST /auth/refresh` · `POST /auth/logout` · `GET /me` |
| Lookups | `GET /lookups` (states, divisions, branches, staff roles, staff, units, CSI codes, markup types, kits) |
| Portfolio | `GET /projects?search&stage&type&page` · `POST /projects` · `POST /projects/{id}/copy` · `PATCH /projects/{id}` · `DELETE /projects/{id}` (archive) · `PUT /projects/{id}/favourite` |
| Versions | `GET /projects/{no}/versions` · `POST /projects/{id}/versions` (copy from) · `PATCH /versions/{id}` · `POST /versions/{id}/primary` · `GET /versions/compare?a&b` |
| Setup | `GET/PUT /versions/{id}/zones` · `GET/PUT /versions/{id}/markups` · `GET/PUT /projects/{id}/members` · `POST/GET/DELETE /projects/{id}/documents` |
| Trade summary | `GET /versions/{id}/trade-summary` · `POST /versions/{id}/trades` · `PATCH /trades/{id}` · `DELETE /trades/{id}` · `PUT /trades/{id}/carry` · `POST /trades/{id}/bids` · `PATCH /bids/{id}` · `PUT /trades/{id}/zones` · `GET/POST /projects/{id}/budgets` · `POST /versions/{id}/budgets/copy` · `GET /benchmarks?csi&type` |
| Scope match | `GET /trades/{id}/scope` · `PUT /answers` · `POST /trades/{id}/lines` · `PATCH /lines/{id}` · `DELETE /lines/{id}` · `POST /trades/{id}/apply-kit` · `GET/PUT /trades/{id}/estimate` · `GET/PUT /trades/{id}/scratch` |
| Proposal reader | `POST /bids/{id}/proposals` (upload → queued job) · `GET /readings/{id}` · `POST /readings/{id}/decide` |
| Bid portal | `POST /trades/{id}/invites` · `GET /trades/{id}/invites` · `POST /invites/{id}/resend` · `DELETE /invites/{id}` · public: `GET /portal/{token}` · `POST /portal/{token}` · review: `POST /submissions/{id}/decide` |
| Executive summary | `GET /versions/{id}/summary` · `POST /versions/{id}/indirect` · `PATCH/DELETE /indirect/{id}` |
| Team | `GET /projects/{id}/team` · `POST/PATCH/DELETE /team/{id}` · `PUT /team/{id}/hours` (batch of week cells) |
| Costs | `GET /versions/{id}/costs?area=precon\|genreq` · `POST/PATCH/DELETE /costs/{id}` · `POST /versions/{id}/costs/apply-kit` |
| Import | `POST /imports/check` · `POST /imports/{token}/create` · `GET /imports/{id}` · `GET /template.xlsx` |
| Export | `GET /exports/{kind}?versionId` → file (xlsx/pdf), recorded in `ExportRun` |
| Live | SignalR hub `/hubs/project`: `Join(projectId, versionId, page)`, `Focus(kind, key, activity)`, `Leave`; server events `presence`, `changed`, `locked` |

## How a change flows

1. **Browser:** applies the change, recalculates with `@gridline/calc` and shows the new number at once.
2. **API:** in one transaction it saves the input, recalculates only the affected trade and the roll-up, and stores the results. It then logs a `CalcRun` (match yes/no) and an `ActivityEvent`.
3. **Other tabs and users:** SignalR sends `changed` to everyone on the project, and their screens refresh that part.

## Tests (each must pass before the feature counts as done)
- **API:** an xUnit test per endpoint against a real PostgreSQL container. The calculation test set is shared with the browser.
- **Browser:** Playwright scripts that run each page's functions. They also cover two users at once, and two different projects in two tabs.
- **Import:** the sample workbook, a broken workbook, and the 27k-row and 284k-row workbooks.
- **Export:** every export opens. Excel files are read back and checked against the totals, and PDFs are checked for page count and text.
