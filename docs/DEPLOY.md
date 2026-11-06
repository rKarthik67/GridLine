# Hosting Gridline

Gridline ships as one container: the .NET API, the live hub (SignalR) and the built React app, with PostgreSQL 17 beside it. One installation serves many companies; each company's people, projects, trade partners and templates are isolated by the API.

## 1. Quick start with Docker Compose

```bash
cp .env.example .env            # set POSTGRES_PASSWORD and PUBLIC_URL at least
docker compose up -d --build
```

Open `http://localhost:8080`. The first start applies `db/migrations` (001 → latest) and seeds the starter template library. Click **Create your company** to make the first company and its administrator, then invite your team from **Administration → People & access**.

> The seed migration (002) also loads the demo company *Northshore Builders* with sample projects and users (`k.raman@northshore.example`, password set only when `Seed__DevPassword` is configured, which the image does not do in Production). It is harmless on a hosted instance and useful for demos; drop it with `DELETE FROM gridline.Company WHERE Slug = 'northshore'` if you don't want it.

## 2. Configuration

Everything is an environment variable (`Section__Key`). The important ones:

| Variable | Meaning | Default |
|---|---|---|
| `ConnectionStrings__Gridline` | PostgreSQL connection string. `Search Path=gridline,public` is added if missing. | required |
| `Db__AutoMigrate` | Apply new `db/migrations/*.sql` files on start-up (the image sets `true`). You can also run `dotnet Gridline.Api.dll --migrate-only`. | `false` |
| `Email__PublicUrl` | Public address of the app, used in invitation and reset links. | request host |
| `Email__Smtp__Host/Port/User/Password`, `Email__From` | Outgoing mail. Without a host, links are written to the log and shown on screen. | not set |
| `Signup__Enabled` | Allow anyone to create a company. Set `false` for a single-customer installation (invite-only). | `true` |
| `Support__Email` | Shown as *Help & support* in the app and on the sign-in page when sign-up is off. | not set |
| `Legal__TermsUrl`, `Legal__PrivacyUrl` | Linked from the sign-up form when set. | not set |
| `Swagger__Enabled` | Swagger UI at `/swagger` (always on in Development). | `false` |
| `Security__Hsts` | Send `Strict-Transport-Security`. Turn on when TLS terminates in front of the app. | `true` |
| `Security__TrustProxies` | Honour `X-Forwarded-*` from the reverse proxy. | `true` |
| `Storage__Root` | Folder for uploads (documents, proposals, imports). Mount a volume (`/data` in the image). | `./data` |
| `Seed__DevPassword` | Development only: password for seeded demo users. Ignored outside `ASPNETCORE_ENVIRONMENT=Development`. | not set |
| `AzureOpenAI__Endpoint/Deployment/Key` | The AI proposal reader. Blank = rules-based reader only. | not set |

## 3. In front of the app

Put a reverse proxy with TLS in front (Caddy, nginx, Traefik, an Azure Container App, AWS ALB…). It must pass WebSocket upgrades for `/hubs/project` and forward `X-Forwarded-For` / `X-Forwarded-Proto`. Example with Caddy:

```
gridline.example.com {
    reverse_proxy app:8080
}
```

Then set `HSTS=true` and `PUBLIC_URL=https://gridline.example.com` in `.env`.

## 4. What's built in for a hosted product

- **Companies (tenants).** Every user belongs to one company. Every project, version, trade, bid, trade partner, employee and template lookup is scoped by the API (`Infrastructure/Access.cs`): a row from another company is a 404, benchmarks only use the company's own past projects, and directories never cross.
- **Sign-up, invitations, password reset.** Tokens are random, stored hashed, expire, and are single use. Passwords are PBKDF2-SHA256 (100k). Eight wrong passwords lock an account for 15 minutes. Sign-in, sign-up and reset routes are rate-limited (10/min per address); the bid portal is limited to 60/min.
- **Sessions.** Opaque bearer tokens (12 h, or 30 days with "keep me signed in"), revocable per user; a password change or reset signs the other sessions out.
- **Headers.** Content-Security-Policy, `X-Frame-Options: DENY`, `nosniff`, Referrer-Policy, Permissions-Policy, HSTS.
- **Audit.** Every write is an `ActivityEvent`; administrators see the company-wide trail under Administration → Audit trail.
- **Health.** `GET /api/health/live` (process up) and `GET /api/health` (database reachable, schema version).

## 5. Backups

PostgreSQL holds everything except uploaded files. Back up both:

```bash
docker compose exec db pg_dump -U gridline -Fc gridline > gridline-$(date +%F).dump
docker compose cp app:/data ./uploads-$(date +%F)
```

Restore with `pg_restore -d gridline --no-owner --no-privileges file.dump` into an empty database, copy the uploads back to the volume, and start the app (it will not re-run migrations the dump already contains).

## 6. Upgrading

Pull the new code, `docker compose up -d --build`. New migration files are applied on start-up, each in its own transaction, guarded by an advisory lock so several replicas can start at once. `GET /api/health` reports the schema number.

## 7. Scaling notes

- The API is stateless apart from the SignalR presence tracker and the 30-second sign-in cache. Several replicas work; for live updates across replicas add a SignalR backplane (Redis) — not included yet.
- Uploads live on a volume. Switch `Storage` to object storage (S3/Blob) when you run more than one replica; the interface is a single class (`Infrastructure/Storage.cs`).
- Per-company feature flags and plans are stored on `Company` (`Plan`, `Status`, `TrialEndsOn`); billing is not wired.
