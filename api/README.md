# Gridline API (.NET 10)

Minimal API + SignalR over the `gridline` schema (PostgreSQL 17). Calculation rules live in
`src/Gridline.Api/Calc/Rules.cs` and must pass the same `web/packages/calc/test/vectors.json` as the browser.

## Run
1. Database: `db/migrations/001_init.sql`, `002_seed.sql`, `003_indexes.sql` (in that order) into `gridline2`.
2. Copy `src/Gridline.Api/appsettings.Development.private.example.json` → `appsettings.Development.private.json`, set the postgres password.
   (Optional) fill AzureOpenAI for the AI proposal reader; without it a rules-based reader is used.
3. `powershell -ExecutionPolicy Bypass -File Start-Gridline-API.ps1` → http://localhost:5080/api/health
4. Tests: `dotnet test Gridline.slnx`

Dev sign-in (Development only): any seeded user, e.g. `demo@northshore.example` with the dev password from
`appsettings.Development.json` (Seed:DevPassword), set on first start for users that have no password.

## Layout
| Folder | What |
|---|---|
| Infrastructure | Db (Npgsql/Dapper), Auth (opaque tokens, PBKDF2), Access (project roles, locked versions), Work (transaction + activity log + live broadcast), Errors, Storage |
| Calc | Rules (C# copy of @gridline/calc), Recalc (one trade + roll-up per edit; set-based for whole versions) |
| Live | ProjectHub (/hubs/project: Join, Focus, Leave → presence, changed), PresenceSweeper |
| Endpoints | Auth/lookups, Projects (portfolio, versions, zones, markups, members, activity), Trades (bid board), Scope (scope match), Summary, Team (team, precon, genreq), Import, Portal, Documents (+ proposal reader) |
| Import | XlsxReader (streaming), Template (read + check), ImportService (COPY + set-based create) |
| Export | 10 exports: portfolio, trade summary, trade quotes, scope match, trade package, executive summary (xlsx/pdf), team, precon, genreq |
| Proposal | Background proposal reader (Azure OpenAI or rules) |
