## Title
Portfolio — Favourite a project

### Summary
Each portfolio row has a star toggle that marks a project as a personal favourite. Favourited projects sort to
the top of the list for that user only, making frequently-used projects faster to find.

### User story
As a user with many projects assigned to me, I want to star the ones I'm actively working, so that they stay at
the top of my portfolio list instead of getting buried by bid-due-date order.

### Acceptance criteria
- [ ] The star button is always visible (not just on hover) in the first column of every row; it renders in
      `var(--accent)` when `p.isFavourite` is true and `var(--faint)` otherwise.
- [ ] Clicking the star calls `PUT /projects/{projectId}/favourite` with `{ isFavourite: !p.isFavourite }`, then
      refetches the whole project list — there is no optimistic UI update, so the star's colour and the row's
      sort position only change after the round trip completes and the list re-renders.
- [ ] Favouriting does not require edit access and is unaffected by whether the project's primary (or any)
      version is `locked` — the favourite check only resolves project-level access, it never inspects
      `ProjectVersion.Status`.
- [ ] Favouriting is per-user: toggling it changes only the clicking user's own ordering/star colour on next
      fetch, never anyone else's.
- [ ] Because `Access.Project`'s fallback rule gives **every** authenticated user at least `'viewer'` standing on
      every non-archived project (an explicit `ProjectMember` row can only raise that to `'editor'`/`'owner'`, it
      is never required to reach `'viewer'` — see Architecture.md Authorization/scoping), and this endpoint only
      requires `'viewer'`, there is no capability or membership combination that makes this call 403 for an
      existing, non-archived project. The only error case is `404` for a project that doesn't exist or is
      archived (`Access.Project`'s query filters `WHERE ... AND p.ArchivedAt IS NULL`).
- [ ] There is no error handling around the favourite click in the UI: if the request rejects (e.g. a 404 or a
      network failure), it throws an unhandled promise rejection — no toast, no visual feedback, and the star
      stays in its previous state.

### Technical spec
**Frontend:** `web/src/pages/Portfolio.tsx`, `async function fav(p: ProjectRow) { await
api.put(\`/projects/${p.projectId}/favourite\`, { isFavourite: !p.isFavourite }); refetch() }`, wired to the star
button's `onClick`. Uses the `PUT` verb of the thin `api` client (`web/src/api/client.ts`); no request/response DTO
beyond the inline `{ isFavourite }` body.

**Backend:** `PUT /api/projects/{id:long}/favourite` (`ProjectEndpoints.Map`), body `FavIn(bool IsFavourite)`.
Calls `Access.Project(c, null, me, id, "viewer")` (`Infrastructure/Access.cs`). `Access.Project`'s `LevelSql`
resolves access as `coalesce((SELECT m.Access FROM ProjectMember m WHERE ...), CASE WHEN @admin THEN 'owner' WHEN
@edit THEN 'editor' ELSE 'viewer' END)` — note the `ELSE 'viewer'`, which is why every caller clears the
`need = "viewer"` bar regardless of capabilities or membership (see Architecture.md Authorization/scoping: "falls
back to ... else `viewer`"). **This endpoint opens a plain `Db` connection and runs one `ExecuteAsync` directly —
it does not go through `Work.Begin`/`Work.Commit`.** As a result there is no `ActivityEvent` row logged and no
SignalR `changed` broadcast for a favourite toggle (favouriting is treated as a private preference, not a
collaborative project change worth notifying other open tabs about — contrast with every other mutating endpoint
in this file). Returns `204 No Content`.

**Database:** Upserts table `RecentProject` (`UserId, ProjectId, OpenedAt timestamptz default now(), IsFavourite
boolean default false`, primary key `(UserId, ProjectId)`): `INSERT INTO RecentProject (UserId, ProjectId,
IsFavourite) VALUES (@u, @p, @f) ON CONFLICT (UserId, ProjectId) DO UPDATE SET IsFavourite = EXCLUDED.IsFavourite`.
This statement does **not** touch `OpenedAt` — that column is only ever set/bumped by `Live/ProjectHub.cs`'s
`Join` handler (`ON CONFLICT (UserId, ProjectId) DO UPDATE SET OpenedAt = now()`), so starring a project does not
change its "recently opened" tie-break ordering. `RecentProject` has no `TrgTouch` trigger (that trigger is only
attached to `AppUser, Project, ProjectVersion, TradePartner, Trade, Bid, ScopeLine, ScopeAnswer, TeamMember,
CostLine`, per the `DO $$ ... FOREACH t IN ARRAY [...]` block in `001_init.sql`), so there's no
`ChangedAt`/`RowRev` concept here at all. Index `IxRecentProjectUser (UserId, OpenedAt DESC)` exists for other
queries but isn't needed by this single-row upsert.

### Dependencies
- `../01-browse-filter-and-open-projects/01-browse-filter-and-open-projects.md` — `isFavourite` drives both the
  star's colour and the list's primary sort key there.
- Architecture.md: Authorization/scoping (`Access.Project`'s viewer-by-default fallback), Unit of work (notes this
  endpoint is the exception that bypasses `Work`/`Work.Log`/the realtime broadcast).

### Notes / edge cases
- Because this is the one portfolio mutation that skips `Work`, a developer extending it (e.g. to log activity)
  needs to switch it to `Work.Begin(ctx)` like every other mutating endpoint, not just add a `w.Log` call to the
  existing bare `Db` connection.
- Archived projects are excluded from `ProjectSummary` entirely (see `01`), so there is no portfolio row to click
  a star on for one; `Access.Project` also explicitly filters `p.ArchivedAt IS NULL`, so a stale favourite request
  (e.g. a cached page) against an archived project's id correctly 404s instead of silently writing a row.
- Don't assume a 403 is reachable here just because other project-scoped endpoints (e.g. `PATCH /projects/{id}`,
  which requires `"editor"`) do 403 for low-access users — this endpoint's `need = "viewer"` combined with
  `Access.Project`'s default-to-viewer fallback makes that path unreachable specifically for favouriting.
