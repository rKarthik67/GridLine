# Gridline as a hosted product: review and refinement (November 2026)

This is the end-to-end review of Gridline done before packaging it as a SaaS product for preconstruction teams, written from two seats: the estimating lead who would use it every day, and the engineer who would run it for many customers. The first half is what was found; the second half is what was changed and what is still ahead.

## 1. What the estimating workflow gets right

Gridline already models the job the way a precon team actually does it, which is rare:

- **One trade, many bids, one picked number.** The bid board keeps every bid visible next to the picked one, with budget, lowest and the delta to lowest. Plugs and adjustments are first-class, with a client note (what the owner sees) separated from a team note.
- **Scope leveling is cell-level and typed.** Included / Excluded / amount / price / text per line per bidder, with formulas, cell colours and notes, and the base-requirements checklist locked at the top of every trade. Scope adjustments flow straight into the bid total and the rank. This is the part most tools get wrong.
- **The roll-up is a real GC price build-up.** Trades → overheads → direct cost → precon → general conditions (from the crew plan) → reserves (project / design / escalation, per zone if wanted) → subtotal → bond, cover, insurance → fee with carve-outs → indirect items → tax. Zones (breakout areas) carry through every line.
- **Crew plan and precon services are costed properly.** Posting rate vs. quoted %, escalation by calendar year, GSA per diem, task-based precon hours. The quoted totals feed the roll-up, not a typed number.
- **The calculation is honest.** The browser computes instantly, the server recomputes and stores, and a `CalcRun` row records whether they agreed. Versions are copies, with a trade-by-trade compare.
- **Collaboration is built in, not bolted on.** Presence per version and cell, live refresh of other people's changes, optimistic locking on every row.

## 2. What stood in the way of hosting it

| Finding | Why it matters for a product | Done |
|---|---|---|
| **Single tenant.** `CompanyProfile` was a one-row table; users, projects, partners, staff and templates had no owner. Two customers in one database would see each other's projects, directories and, worst of all, each other's historic $/SF benchmarks. | This is the defining requirement of SaaS. | ✅ Migration 009: `Company`, `CompanyId` on every owned table, per-company uniqueness, company-scoped views, starter library cloned on sign-up. `Access.cs` scopes every lookup; benchmarks and directories never cross. 45-check smoke test proves it. |
| **No way in.** Accounts were seeded by SQL; no sign-up, no invitations, no password reset. | A customer must be able to onboard themselves and their team. | ✅ `/signup`, invitations (7-day single-use links), forgot/reset (2-hour links), change password, session list and revoke. |
| **Weak sign-in protection.** Unlimited password attempts, no rate limit, 12-hour sessions only. | Credential stuffing is the first attack a hosted app sees. | ✅ Lockout after 8 failures, rate limiting on auth and public routes, "keep me signed in" (30 days), sessions revoked on reset. |
| **Dev conveniences reachable outside Development.** Seeded demo password applied in any non-Production environment; Swagger on in non-Production; health check leaked the database name. | Staging and demo environments are public. | ✅ Dev password only in Development; Swagger opt-in; health hides the db name in Production. |
| **No security headers, no proxy awareness.** | Framing, MIME sniffing, lost client IPs behind a load balancer. | ✅ CSP, X-Frame-Options, nosniff, Referrer-Policy, Permissions-Policy, HSTS, forwarded headers. |
| **No administration surface.** Users, roles, the 700-row trade partner directory, employees and templates could only be changed in SQL. | Every customer needs a self-service admin. | ✅ Administration area: company & brand, people & access, trade partners, employees & roles, template library, audit trail. |
| **No repeatable deployment.** Migrations were applied by hand with `psql`; secrets lived in a committed private file; no container. | Can't onboard a second customer or a second environment reliably. | ✅ Migration runner (`--migrate`, `Db:AutoMigrate`), Dockerfile, compose, `.env.example`, CI workflow, `docs/DEPLOY.md`. |
| **Email.** Bid links were `mailto:`; no transport for invitations or resets. | | ✅ SMTP sender with log fallback; every flow also shows the link on screen. |
| **Brand.** Orchid/tangerine prototype palette. | The product should look like the Precon-E family. | ✅ Forest green `#27624d`, lime `#d8ef58`, ink `#18221f`, IBM Plex; dark sidebar; new sign-in. |

## 3. Estimating refinements made

- **Portfolio is a dashboard first.** Live pipeline value, bids due in 14 days (with past-due called out), value out for decision, awarded value and hit rate, and the number of unresolved trades across bidding projects. A "bids due soon" strip jumps to the project. $/SF column on every project.
- **Bid board flags thin coverage and wide spread.** Fewer than 3 posted bids is marked *thin* (the usual minimum for a competitive number); a spread of 15 % or more between the lowest and highest posted bid is marked, because it almost always means the bidders read the scope differently. Both are filters, and the stats bar shows how many trades have 3+ bids.
- **Price roll-up by CSI division.** One switch groups the trades by MasterFormat division with subtotals, % of contract, $/SF, budget delta and zone splits, which is the view an owner-facing summary starts from.
- **Bid portal is branded** with the inviting company's name and the trade's CSI code.
- **Data clean-up** of double-encoded text in the demo backup (migration 010).

## 4. Roadmap (in the order I would build it)

1. **Alternates, allowances and unit rates** (DB tables exist since migration 005, switched off). These are on every GC bid form; they belong as tabs in Scope match with per-bidder cells and lines on the roll-up.
2. **Client summary export** (owner-facing PDF/Excel of the roll-up by division with qualifications), using the by-division grouping above.
3. **Risk register and estimate review** (probability × impact with cost exposure; a submit/approve step on a version before it is marked *submitted*), as in Precon-E.
4. **SSO** (Entra ID / Okta via OpenID Connect) alongside passwords; `AppUser.SsoSubject` is already there.
5. **Template editing** in the library (today: rename, enable, view lines; lines are edited on a project).
6. **Billing and plans** (`Company.Plan/TrialEndsOn` exist; wire Stripe and enforce seat counts).
7. **Multi-replica hosting**: SignalR backplane (Redis) and object storage for uploads.
8. **Email delivery provider** (SES/SendGrid) instead of raw SMTP, with templates.
9. **Observability**: OpenTelemetry traces and metrics; the request log already carries trace ids.

## 5. How to review this build

1. `cd web && npm run dev`, `cd api && .\Start-Gridline-API.ps1` (or `docker compose up -d --build`).
2. Open http://localhost:5173. Sign in as `k.raman@northshore.example` (demo company) and look at Portfolio, Bid board (filters *Under 3 bids* and *Wide spread*), Price roll-up (*By CSI division*), and Administration.
3. Sign out and click **Create your company**. Notice the empty portfolio, the empty directory, the copied template library (Administration → Template library), and that `/p/EST-24031` is not found.
4. Invite yourself at a second address from People & access; open the link in a private window; accept; check the role.
5. Run `node api/tests/smoke.mjs` for the automated version of steps 3–4.

## 6. Second pass: UI/UX, product owner and CIO review (same day)

Screenshots of every page at desktop, 1024 px and phone width were reviewed with three hats on. What changed:

**UI/UX**
- Typeface switched from IBM Plex to **Inter** (text) and **JetBrains Mono** (numbers, codes): Inter is the standard for dense SaaS dashboards and has proper tabular figures; Plex read as a typewriter in money columns.
- Mobile menu: a hamburger opens the sidebar as a drawer with a scrim; it closes on navigation. The crumb, page header and KPI grid compact at phone width.
- Branded, centred states for an invalid bid link and while the portal loads; avatars are round everywhere; the employees page is stacked so posting and bill rates are no longer clipped; precon/overheads warning copy says what is wrong and why it matters.
- Template names cleaned of double-encoded dashes (migration 011).

**Product owner**
- **Jump to anything (Ctrl+K):** a palette that finds projects, the current project's pages, My desk and Administration by typing a few letters; also opened from the search box in the top bar and from the project switcher.
- **Mine** filter on the portfolio (projects you estimate, are a member of, or starred), remembered per browser.
- **Getting started** on an empty portfolio: invite your team, start a project, make the library yours.
- **Trial status** chip in the top bar for trial companies, turning amber in the last five days.
- **Help & support** in the profile menu (mailto the configured support address) and the product version.

**CIO**
- `GET /api/config` (public): is sign-up open, support email, terms and privacy links, version. The sign-in page hides *Create your company* on invite-only installations and the sign-up form links the legal pages.
- **Security & your data** section on the Company page: isolation, password policy, sessions, audit, transport, with SSO and 2FA marked as roadmap.
- **Export all company data (JSON)**: people, partners, employees, every project with versions, trades, bids and roll-up, and the template library; recorded in the audit trail.

Still open after this pass (ordered): SSO, 2FA, inactivity timeout, alternates/allowances/unit rates, client summary export, template line editing in the library.
