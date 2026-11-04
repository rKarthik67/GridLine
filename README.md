# Gridline

**Gridline** is a preconstruction estimating and bid-leveling app. It covers the whole path from a new project to a priced proposal: invite trade partners, level their bids scope line by scope line, plan the crew and overheads, and roll everything up into a contract total.

It is a rebuild of the NCC app on the Gridline DB, with the same calculations and plain-language names for every page. Several people can work on one project at the same time and see each other's changes live.

Repository: <https://github.com/rKarthik67/GridLine>

## Stack

| Layer | Technology |
|---|---|
| Database | PostgreSQL 17, one schema called `gridline`, plain SQL migrations |
| API | .NET 10 minimal API, Npgsql + Dapper, SignalR (live updates and presence), ClosedXML (Excel), QuestPDF (PDF) |
| Web | React 19, TypeScript, Vite, TanStack Query, React Router |
| Grids | [RevoGrid](https://github.com/revolist/revogrid) (MIT): range select, copy and paste to and from Excel, fill handle, frozen columns |
| Scratch pad | [FortuneSheet](https://github.com/ruilisi/fortune-sheet) (MIT) for an Excel-like sheet, and [ExcelJS](https://github.com/exceljs/exceljs) (MIT) for .xlsx export |
| Calculations | `@gridline/calc` in TypeScript (instant results in the browser) and `Rules.cs` in C# (the stored results). Both must pass the same `vectors.json` test cases. |

## What's in the app

| Page | Was called | What it does |
|---|---|---|
| Portfolio | Projects | Search, filter and open projects, each in its own browser tab |
| Start a project | New project | Start a blank project, or import one from the Excel template (project, zones, trades, bidders, crew, base requirements) |
| Project profile | Project setup | Details, schedule dates (precon, construction, closeout), salary escalation, zones, rates and indirect items with on/off switches, fee carve-outs, versions |
| Crew plan | Project team | Team members by employee group, 21 selectable columns, Set hours/budget, per diem (FY2026 rates), resource loader by week or month, right-click menu |
| Precon services | Preconstruction | Precon team hours per task (standard and ad-hoc tasks), expenses, and quoted % or quoted total |
| Project overheads | General requirements | Expenses by category with CSI code, billable flag and client quote |
| Bid board | Trade summary | Every trade with all its bids side by side; pick a bid, budgets, filters |
| Scope match | Scope leveling | Trade partners as lanes. Answer each scope line with Included, Excluded, an amount or text; paste from Excel. Tabs: Scratch pad, Internal estimate, Files, Qualifications, Bid inbox |
| Base requirements | General scope | The checklist every trade answers; changes apply to all trades |
| Price roll-up | Pricing review summary | Trades, overheads, precon, General conditions, reserves, markups, fee, indirect items and tax, plus the Estimator sandbox |
| Bid portal | — | A link trade partners open without signing in to submit a bid |

Across the app:

- **Live editing:** live updates and "who's here" presence, scoped to the project version. If two people save the same row, the second save is stopped with a clear message.
- **Exports:** Excel and PDF.
- **API documentation:** Swagger.
- **Error handling:** every error comes back as a readable message, and each page has its own error fallback.

## Repository layout

```
api/                     .NET 10 API (src/Gridline.Api) and xUnit tests (tests/)
web/                     React app (src/) and the shared calculation package (packages/calc)
db/migrations/           001–008, run in order
db backup/               pg_dump of the gridline2 database
db/seed/                 Harborview-Tower-Import.xlsx (246 trades, 10–14 bidders each) and the script that makes it
docs/06-build-plan.md    Pages, functions, endpoints and test plan
app md/                  One write-up per page
```

## Getting started (Windows)

You need:

- [.NET SDK 10](https://dotnet.microsoft.com/download). `api/global.json` selects it.
- [Node.js 20+](https://nodejs.org/)
- [PostgreSQL 17](https://www.postgresql.org/download/) with `psql`. If PowerShell can't find `psql`, use its full path, for example `& "C:\Program Files\PostgreSQL\17\bin\psql.exe"`.

### 1. Database

```powershell
psql -h localhost -U postgres -c "CREATE DATABASE gridline2"
Get-ChildItem db\migrations\*.sql | Sort-Object Name | ForEach-Object {
  psql -h localhost -U postgres -d gridline2 -v ON_ERROR_STOP=1 -f $_.FullName
}
psql -h localhost -U postgres -d gridline2 -c "select max(ChangeNo) from gridline.SchemaChange"   # should print 8
```

`002_seed.sql` loads demo data: 8 projects, including 5 past projects used as benchmarks. Migrations from 003 onward can safely be run more than once.

### 2. API (http://localhost:5080)

```powershell
cd api
copy src\Gridline.Api\appsettings.Development.private.example.json src\Gridline.Api\appsettings.Development.private.json
# open the copy and put in your PostgreSQL password
.\Start-Gridline-API.ps1
```

- Health check: <http://localhost:5080/api/health>. It shows the database name and the schema version.
- Swagger: <http://localhost:5080/swagger>
- Tests: `dotnet test` in `api/`

### 3. Web app (http://localhost:5173)

```powershell
cd web
npm install
npm run dev      # calls the API on :5080 through the Vite proxy
npm test         # calculation tests
npm run build    # type-check and production build
```

**Sign in** with any seeded user, for example `k.raman@northshore.example`, using the local development password set in `appsettings.Development.json` (`Seed:DevPassword`). These are local test accounts only.

### Optional: a large test project

Go to **Start a project → Import from Excel template** and upload `db/seed/Harborview-Tower-Import.xlsx`. It creates Harborview Mixed-Use Tower (EST-26100) with:

- 246 trades and 2,938 bids
- 5 zones, a crew and base requirements

All of its company names and contacts are made up. To make a different size, edit and rerun `db/seed/make_big.py` (Python 3 + openpyxl).

## Working rules

- Gridline database names are PascalCase with no underscores, and work in any letter case.
- Every save checks `RowRev`, so one person can't silently overwrite another person's change.
- The browser (`@gridline/calc`) and the server (`Rules.cs`) must give the same numbers. Update `web/packages/calc/test/vectors.json` when a rule changes.
- `appsettings.Development.private.json` (database password) and `db backup/` (database dump) are committed. Keep this repository **private**.

## Status

| Area | Status |
|---|---|
| Database, migrations 001–008 | ✅ |
| API: 23 tests, plus API smoke checks | ✅ |
| Web app: all pages above | ✅ |
| Calculation tests (18) | ✅ |
| Still to do | Works cover from the rate tables and county levels; Unit rates, Provisional sums, Options and Savings ideas (API and UI); Entra / Okta sign-in |
