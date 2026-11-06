# Gridline

**Gridline** is a preconstruction estimating and bid-leveling workspace for general contractors and construction managers, built to be hosted as a product. It covers the whole path from a new project to a priced proposal: invite trade partners, level their bids scope line by scope line, plan the crew and overheads, and roll everything up into a contract total. Several people work on one project at the same time and see each other's changes live.

Every customer is a **company**: its people, projects, trade partner directory, employees and template library are separate from every other company's. A company signs up at `/signup`, invites its team, and starts from Gridline's starter library of scope kits, base-requirement checklists, cost kits and qualification sets.

Repository: <https://github.com/rKarthik67/GridLine> (private)

## Stack

| Layer | Technology |
|---|---|
| Database | PostgreSQL 17, one schema called `gridline`, plain SQL migrations applied by the API on start-up |
| API | .NET 10 minimal API, Npgsql + Dapper, SignalR (live updates and presence), ClosedXML (Excel), QuestPDF (PDF) |
| Web | React 19, TypeScript, Vite, TanStack Query, React Router. Theme: forest green and lime on IBM Plex |
| Grids | [RevoGrid](https://github.com/revolist/revogrid) (MIT): range select, copy and paste to and from Excel, fill handle, frozen columns |
| Scratch pad | [FortuneSheet](https://github.com/ruilisi/fortune-sheet) (MIT) and [ExcelJS](https://github.com/exceljs/exceljs) (MIT) |
| Calculations | `@gridline/calc` in TypeScript (instant results in the browser) and `Rules.cs` in C# (the stored results). Both must pass the same `vectors.json` test cases. |
| Hosting | One Docker image (API + web), `docker-compose.yml` with PostgreSQL, GitHub Actions CI. See [docs/DEPLOY.md](docs/DEPLOY.md). |

## What's in the app

| Area | What it does |
|---|---|
| Portfolio | Pipeline value, bids due in 14 days, out for decision, awarded and hit rate, unresolved trades; search and filter projects, open each in its own tab |
| Start a project | Blank, or import the Excel template (project, zones, trades, bidders, crew, base requirements) |
| Project profile | Details, schedule dates, salary escalation, zones, rates and indirect items with on/off switches, fee carve-outs, versions, members, documents, activity |
| Crew plan | Team by employee group, 21 selectable columns, set hours/budget, per diem (GSA rates), resource loader by week or month |
| Precon services | Precon team hours per task, expenses, quoted % or quoted total |
| Project overheads | Expenses by category with CSI code, billable flag and client quote |
| Bid board | Every trade with all its bids side by side; pick a bid; budgets, buyouts, zones; **coverage** (fewer than 3 bids) and **spread** (high vs. low) flags and filters |
| Scope match | Trade partners as lanes; Included / Excluded / amount / text per line; paste from Excel; scratch pad, internal estimate, files, qualifications, bid inbox with the AI proposal reader and the online bid portal |
| Base requirements | The checklist every trade answers; changes apply to all trades |
| Price roll-up | Trades (also **by CSI division**), overheads, precon, general conditions, reserves, markups, fee, indirect items and tax, plus the Estimator sandbox |
| Bid portal | A link trade partners open without signing in to submit a bid |
| Administration | Company settings and brand, people & access (invitations, roles, deactivation), trade partner directory, employees & roles, template library, audit trail |
| Account | Password change, where you're signed in, sign out everywhere else |

Across the app: live editing and presence per project version, clash detection on every row (`RowRev`), Excel and PDF exports, Swagger, readable errors, per-page error fallbacks.

## Repository layout

```
api/                     .NET 10 API (src/Gridline.Api), xUnit tests and the end-to-end smoke test (tests/)
web/                     React app (src/) and the shared calculation package (packages/calc)
db/migrations/           001–010, applied in order (the API does this on start-up with Db:AutoMigrate)
db backup/               pg_dump of the demo database and the backup/restore scripts
db/seed/                 Harborview-Tower-Import.xlsx (246 trades, 10–14 bidders each)
docs/                    DEPLOY.md (hosting), 07-saas-review.md (product review and roadmap), 06-build-plan.md
app md/                  One write-up per page
Dockerfile, docker-compose.yml, .env.example, .github/workflows/ci.yml
```

## Getting started (Windows, local development)

Follow **[SETUP.md](SETUP.md)** for installs and restoring the demo database. In short:

```powershell
cd api;  .\Start-Gridline-API.ps1        # http://localhost:5080  (applies new migrations with: dotnet run -- --migrate)
cd web;  npm install; npm run dev         # http://localhost:5173
```

Sign in with a seeded user such as `k.raman@northshore.example` and the local development password in `appsettings.Development.json` (`Seed:DevPassword`), or click **Create your company** to start a fresh one.

### Tests

```powershell
cd api;  dotnet test                      # calculation vectors and import template (23)
cd web;  npm test                         # calculation vectors (18)
node api/tests/smoke.mjs                  # end to end against a running API: sign-up, tenant isolation, invitations, resets, lockout, admin (45 checks)
```

## Hosting it

```bash
cp .env.example .env     # passwords, public URL, SMTP
docker compose up -d --build
```

Everything about running it as a product (configuration, reverse proxy and TLS, backups, upgrades, what the security layer does) is in **[docs/DEPLOY.md](docs/DEPLOY.md)**.

## Working rules

- Gridline database names are PascalCase with no underscores, and work in any letter case.
- Every company-owned table carries `CompanyId`; every lookup goes through `Infrastructure/Access.cs` so a row from another company is a 404. New endpoints must do the same.
- Every save checks `RowRev`, so one person can't silently overwrite another person's change.
- The browser (`@gridline/calc`) and the server (`Rules.cs`) must give the same numbers. Update `web/packages/calc/test/vectors.json` when a rule changes.
- Secrets never go in the repository: use `appsettings.Development.private.json` locally (git-ignored) and environment variables when hosted.

## Status

| Area | Status |
|---|---|
| Database, migrations 001–010 | ✅ |
| Companies, sign-up, invitations, password reset, lockout, rate limiting, security headers | ✅ |
| Company administration (people, partners, employees, templates, audit) | ✅ |
| API tests (23), calculation tests (18), end-to-end smoke (45 checks) | ✅ |
| Docker image, compose, CI | ✅ |
| Roadmap | See [docs/07-saas-review.md](docs/07-saas-review.md): alternates & allowances UI, unit rates, risk register, client summary, SSO (Entra/Okta), billing, SignalR backplane for multi-replica hosting |
