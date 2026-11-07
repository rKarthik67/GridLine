# Setting up Gridline on a new laptop (Windows)

This guide takes a fresh Windows laptop to a running Gridline: the PostgreSQL database, the .NET API on **http://localhost:5080** and the React web app on **http://localhost:5173**. Allow about 30–45 minutes, mostly for installs.

---

## 1. Install the tools

| Tool | Version | Download | Check it worked (new PowerShell window) |
|---|---|---|---|
| Git | any recent | <https://git-scm.com/download/win> | `git --version` |
| .NET SDK | **10.x** (the repo's `api/global.json` asks for 10.0.100 or later) | <https://dotnet.microsoft.com/download/dotnet/10.0> | `dotnet --list-sdks` shows a `10.` line |
| Node.js | **22 LTS** (minimum 20.19) | <https://nodejs.org/> | `node -v` |
| PostgreSQL | **17** (or newer) | <https://www.postgresql.org/download/windows/> | see below |

**PostgreSQL install notes**
- Keep the default port **5432**. Tick **Command Line Tools** (and pgAdmin if you want a GUI).
- Choose a password for the `postgres` user and remember it. You'll need it in steps 3 and 4.
- Put the PostgreSQL tools on PATH so `psql` works in any window (change `17` to your version):

```powershell
[Environment]::SetEnvironmentVariable('Path', $env:Path + ';C:\Program Files\PostgreSQL\17\bin', 'User')
```
Close and reopen PowerShell, then check: `psql --version`.

> .NET 7 or 8 can stay installed alongside .NET 10. `global.json` picks the right one.

---

## 2. Get the code

```powershell
cd D:\                       # or wherever you keep projects
git clone https://github.com/rKarthik67/GridLine.git
cd GridLine
```
The repository is private, so Git asks you to sign in to GitHub the first time (a browser window opens).

All commands below are run from this `GridLine` folder unless a step says otherwise.

---

## 3. Create the database

Pick **one** of the two options.

### Option A (recommended): restore the team backup, with all the data

`db backup\` has the latest backup (`gridline2-<date-time>.dump`). Restore it with the included script; it asks for the postgres password once:

```powershell
powershell -ExecutionPolicy Bypass -File ".\db backup\Restore-Gridline.ps1" -DumpFile ".\db backup\gridline2-20261104-2006.dump"
```
Use the newest `.dump` in the folder if there is a later one. The script creates the `gridline2` database, restores everything, and prints the schema version (**10**) and the number of projects.

Then put back the uploaded files (documents and proposals) that go with the backup:
```powershell
New-Item -ItemType Directory -Force api\src\Gridline.Api\data | Out-Null
Expand-Archive ".\db backup\gridline-files-20261104-2006.zip" -DestinationPath api\src\Gridline.Api\data -Force
```

### Option B: start empty with demo data

```powershell
psql -h localhost -U postgres -c "CREATE DATABASE gridline2"
Get-ChildItem db\migrations\*.sql | Sort-Object Name | ForEach-Object {
  Write-Host "Running $($_.Name)"
  psql -h localhost -U postgres -d gridline2 -v ON_ERROR_STOP=1 -f $_.FullName
}
psql -h localhost -U postgres -d gridline2 -c "select max(ChangeNo) from gridline.SchemaChange"
```
The last command should print **13**. Migration `002_seed.sql` adds 8 demo projects and the demo users.

> `psql` asks for the password on each command. To avoid that for this window only: `$env:PGPASSWORD = Read-Host 'postgres password'`. Close the window afterwards.

---

## 4. Configure the API

The API reads the database password from a private settings file:

```powershell
copy api\src\Gridline.Api\appsettings.Development.private.example.json api\src\Gridline.Api\appsettings.Development.private.json
notepad api\src\Gridline.Api\appsettings.Development.private.json
```
In Notepad:
- In `ConnectionStrings:Gridline`, replace the password with **your** postgres password from step 1. Keep everything else (`Database=gridline2`, `Search Path=gridline,public`).
- `AzureOpenAI` is optional. It powers the proposal reader in Scope match → Bid inbox. Leave the placeholders if you don't have a key; everything else works without it.

Save and close.

---

## 5. Start the API (window 1)

```powershell
cd api
.\Start-Gridline-API.ps1
```
The first run restores NuGet packages (a few minutes). When it's ready you'll see `Now listening on: http://localhost:5080`.

Check it in a browser:
- <http://localhost:5080/api/health>: should show `"ok":true,"db":"gridline2","schema":10`
- <http://localhost:5080/swagger>: the API documentation

Leave this window open.

> If PowerShell refuses to run the script ("running scripts is disabled"), run it once with: `powershell -ExecutionPolicy Bypass -File .\Start-Gridline-API.ps1`

---

## 6. Start the web app (window 2)

Open a **second** PowerShell window:
```powershell
cd D:\GridLine\web           # your clone path
npm install                  # first time only, a few minutes
npm run dev
```
Open <http://localhost:5173>.

---

## 7. Sign in

Use any seeded user, for example **k.raman@northshore.example**, with the local development password set in `api\src\Gridline.Api\appsettings.Development.json` (`Seed:DevPassword`). The API applies it to the seeded users when it starts in Development.

These are local test accounts only.

---

## 8. Check that everything works

- [ ] Portfolio lists the projects (Riverside Medical Pavilion, Harborview Mixed-Use Tower if you restored the backup, …).
- [ ] Open a project. Bid board, Scope match, Crew plan, Precon services, Project overheads, Price roll-up, Alternates & VE, Allowances and Unit prices all load.
- [ ] Type a value in Scope match, refresh the page, and the value is still there (it saved to PostgreSQL).
- [ ] Open the same project in a second browser (or an incognito window) signed in as another user. A change in one appears in the other within a couple of seconds.
- [ ] Optional: tests pass. Run `dotnet test` in `api\` and `npm test` in `web\`.

---

## Daily use

1. Make sure PostgreSQL is running. It starts with Windows by default; if not: Services → `postgresql-x64-17` → Start.
2. Window 1: `cd api` then `.\Start-Gridline-API.ps1`
3. Window 2: `cd web` then `npm run dev`
4. Open <http://localhost:5173>

**Single window, no Vite:** run `npm run build` once in `web\`. The API then serves the built app itself at <http://localhost:5080>. Rebuild after pulling new web code.

---

## Since the hosted-product refinement (November 2026)

- **Companies.** The database now has a `Company` table and every user, project, trade partner, employee and template belongs to one. The restored backup becomes company *Northshore Builders*; a second company *Gridline starter library* holds the templates new companies are cloned from.
- **Migrations run themselves.** New files in `db\migrations` are applied when the API starts with `Db:AutoMigrate=true`, or on demand: `dotnet run --project src\Gridline.Api\Gridline.Api.csproj -- --migrate` (from `api\`). `/api/health` shows the schema number (**13** now).
- **Sign-up and invitations.** On the sign-in page, *Create your company* makes a new company with you as administrator. Administrators invite people under *Administration → People & access*; without an SMTP server configured the invitation link is shown on screen and written to the API log.
- **Forgot password.** `/forgot` emails a reset link (or shows it, in Development, when no SMTP is configured).
- **Lockout.** Eight wrong passwords lock an account for 15 minutes; sign-in routes allow 10 attempts a minute per address.
- **End-to-end check.** `node api	ests\smoke.mjs` against a running API creates a throw-away company and verifies isolation, invitations, resets and the admin API (45 checks).
- **Hosting.** `docker compose up -d --build` with a `.env` (see `.env.example` and `docs/DEPLOY.md`).

---

## Getting updates later

```powershell
git pull
# new database migrations? run only the new ones, in order, e.g.:
psql -h localhost -U postgres -d gridline2 -v ON_ERROR_STOP=1 -f db\migrations\009_<name>.sql
cd web; npm install          # if package.json changed
```
Restart the API afterwards. `/api/health` shows the schema version the database is on. Migrations from 003 onward are safe to run twice, or just start the API with `--migrate`.

---

## Backups

Take a backup (asks for the postgres password; files go into `db backup\`):
```powershell
powershell -ExecutionPolicy Bypass -File ".\db backup\Backup-Gridline.ps1"
```
See `db backup\README.md` for restoring on another machine.

---

## Troubleshooting

| Problem | Fix |
|---|---|
| `psql` / `pg_dump` "is not recognized" | PostgreSQL's `bin` folder isn't on PATH. Redo the PATH step in section 1 and open a new window, or call it by full path: `& "C:\Program Files\PostgreSQL\17\bin\psql.exe" …` |
| `password authentication failed for user "postgres"` | The password in `appsettings.Development.private.json` (or what you typed) doesn't match the one set when installing PostgreSQL. |
| API says "Create …private.json first" | Do step 4. |
| `A compatible .NET SDK was not found` | Install the .NET **10** SDK (not only the runtime), then open a new window. |
| `/api/health` shows a schema lower than 13 | Run the missing migrations from `db\migrations` in order (Option B loop, skipping `CREATE DATABASE`). |
| Restore script: "Database 'gridline2' already exists" | Restore under another name (`-Database gridline2_copy`) and change `Database=` in the private settings, or drop the old database first. |
| Port 5080 or 5173 already in use | Close the other app, or find it with `netstat -ano | findstr :5080` and end that process in Task Manager. |
| Web app loads but shows "Can't reach the database right now" | PostgreSQL isn't running. Start the `postgresql-x64-17` service. |
| `npm install` fails behind a company proxy | `npm config set proxy http://<proxy>:<port>` and `npm config set https-proxy http://<proxy>:<port>`. |
| Pages look unstyled or old after `git pull` | Stop `npm run dev`, run `npm install`, start it again, and hard-refresh the browser (Ctrl+F5). |

---

## What's where

| Path | What it is |
|---|---|
| `api\` | .NET 10 API (`src\Gridline.Api`) and tests (`tests\`) |
| `web\` | React 19 web app and the shared calculation package (`packages\calc`) |
| `db\migrations\` | Database scripts 001–013, run in order |
| `db\seed\` | `Harborview-Tower-Import.xlsx` large test project (import from **Start a project → Import from Excel template**) |
| `db backup\` | Database backups and the backup / restore scripts |
| `app md\` | One write-up per page and feature |
| `docs\` | Build plan |
| `README.md` | Project overview |
