## Title
Project profile — View project activity timeline

### Summary
The Activity tab shows a read-only, chronological audit feed of every logged change on the project — who did what and when, across every version — giving the team visibility into recent edits without opening each tab individually.

### User story
As a project team member, I want to see a running timeline of what's changed on this project and by whom so that I can catch up on recent activity without digging through every tab.

### Acceptance criteria
- [ ] The tab shows up to the 60 most recent activity events for the project, newest first, each with the actor's avatar (initials/colour, or "SY"/grey when the event has no associated user — e.g. a system action), a human-readable description of the action (via `describe(verb)`), the timestamp, and — when the event is tied to a version — that version's number.
- [ ] The list auto-refreshes on a 15-second poll without the user needing to reload the tab.
- [ ] An empty project history shows "Nothing yet." instead of an empty list.
- [ ] While the first page is loading, the tab shows a "Loading activity" state.
- [ ] Every mutating action documented elsewhere on this page (project edits, zone saves, version changes, rate/indirect edits, fee carve-out changes, member changes, document uploads/deletes) appears in this feed, since they all log through the same mechanism.

### Technical spec
**Frontend:** `web/src/pages/project/Profile.tsx`, component `Activity({ project })`. `useQuery({ queryKey: ['activity', project.projectId], queryFn: () => api.get<{...}[]>(\`/projects/${project.projectId}/activity\`), refetchInterval: 15000 })`. Each row renders `describe(e.verb)` (from `web/src/app/verbs.ts`, the same verb-to-text mapping used for the realtime "{person} {verb}" toast described in Architecture.md) plus `new Date(e.happenedAt).toLocaleString()` and, if `e.versionId` is set, looks up that version's number from `project.versions`.

**Backend:** `GET /api/projects/{id:long}/activity` (`ProjectEndpoints.cs`) — `before: long?` query parameter supported for pagination (older-than-this-event-id), though not currently used by this tab's frontend (it always fetches the latest page). `Access.Project(c, null, ctx.Me(), id, "viewer")`. Query: `SELECT e.EventId, e.HappenedAt, e.Verb, e.SubjectType, e.SubjectKey, e.VersionId, u.GivenName || ' ' || u.FamilyName AS "personName", u.Initials, u.Colour FROM ActivityEvent e LEFT JOIN AppUser u ON u.UserId = e.UserId WHERE e.ProjectId = @id AND (@before IS NULL OR e.EventId < @before) ORDER BY e.EventId DESC LIMIT 60`. Read-only; no Calc/Recalc/Import/Export/Proposal involvement. Every other endpoint on this page (and every other mutating endpoint in the system) is this feed's data source via `Work.Log(...)` (see Architecture.md Unit of work), called once per logged change right before `w.Commit()`.

**Database:** Table `ActivityEvent` (`001_init.sql`): `EventId, HappenedAt, UserId (FK AppUser, nullable — null for system-originated events), ProjectId (FK Project ON DELETE SET NULL), VersionId, Verb, SubjectType, SubjectKey, BeforeState (jsonb), AfterState (jsonb)`. Index `IxActivityProject ON ActivityEvent (ProjectId, EventId DESC)` backs this query's `WHERE ProjectId = @id ORDER BY EventId DESC` access pattern directly. `BeforeState`/`AfterState` are captured by `Work.Log` but are not selected/shown by this endpoint's query — the tab only ever displays `Verb` and the subject reference, not the diffed payload.

### Dependencies
- See `_Overview/Architecture.md`: Unit of work (`Work.Log`/`Work.Commit` — the sole writer of `ActivityEvent`), Realtime (the `changed` SignalR event is a live, per-change echo of the same log entries this tab polls for; the `describe(verb)` text here matches the toast text shown on other open tabs).
- Every other ticket on this page (01–07) is a producer of rows this ticket reads: project/version updates (`project.update`, `project.archive`, `version.create`, `version.update`, `version.primary`, `version.{status}`), zones (`zones.update`), rates/indirects (`markups.update`, `indirect.create/update/delete`), fee carve-outs (`feecarveouts.update`), members (`members.update`), documents (`document.upload`, `document.delete`).

### Notes / edge cases
- `ProjectId` is `ON DELETE SET NULL`, not cascade — archiving a project does not delete its activity rows (archiving doesn't delete the `Project` row at all, it only sets `ArchivedAt`), but if a `Project` row were ever hard-deleted, its historical `ActivityEvent` rows would survive with `ProjectId = NULL` and silently stop showing up in this feed (which filters `WHERE ProjectId = @id`).
- The feed is capped at 60 rows per request with cursor-style `before` pagination available on the endpoint, but the frontend never calls it with a `before` value — a developer wiring up "load more" should use this parameter rather than inventing a new one.
- Because this is a 15-second poll (not a SignalR push), a change logged by this exact tab's own save (e.g. editing Details) can take up to 15 seconds to appear here, unlike the near-instant `changed` broadcast other open tabs receive per Architecture.md's Realtime section — don't assume this list updates instantly after a local save.
- `personName`/`initials`/`colour` are `null` for events with no `UserId` (background/system actions); the frontend falls back to a grey avatar with literal text "SY" in that case — verify any new system-originated verb still reads sensibly through `describe(verb)` with no actor name.
