## Title
Project profile — Manage project members and access levels

### Summary
The Members tab shows which users have an explicit access grant on the project (viewer/editor/owner) and lets a project owner add, change or remove people's access, with the rule that a project always needs at least one owner. It is the UI for the `ProjectMember` table that backs the per-project authorization scope described in Architecture.md.

### User story
As a project owner, I want to see and manage who has access to this project and at what level so that the right people can view or edit it and nobody is accidentally locked in or out.

### Acceptance criteria
- [ ] The list shows every explicit member (avatar initials/colour, name, email) with an access-level select (Viewer/Editor/Owner) next to each.
- [ ] Explanatory copy clarifies the fallback rule when there's no explicit row: admins are owners, estimators and managers can edit, read-only users can view (see Architecture.md Authorization/scoping).
- [ ] The access-level select and the remove button per row are editable only when the current user's `project.access === 'owner'`; non-owners see a read-only list.
- [ ] An "Add a person…" dropdown lists only users not already in the member list; picking one adds a new row defaulted to Editor access (not yet saved).
- [ ] "Save members" persists the full edited member list in one call; removing the last remaining owner row (or saving a list with no owner at all) is rejected with "A project needs at least one owner."
- [ ] On successful save, the `['members', projectId]` query is invalidated so the list reflects the server's authoritative state.
- [ ] While the member list is loading, the tab shows a "Loading members" state.

### Technical spec
**Frontend:** `web/src/pages/project/Profile.tsx`, component `Members({ project })`. Loads `useQuery({ queryKey: ['members', project.projectId], queryFn: () => api.get<{...}[]>(\`/projects/${project.projectId}/members\`) })`; keeps an editable local copy (`rows: { userId, access }[]`) seeded from the query data. `owner = project.access === 'owner'` gates all editing. Uses `useLookups()` to resolve each member's display name/email/initials/colour from the global users list and to populate the "add a person" dropdown with users not already present. Save: `api.put(\`/projects/${project.projectId}/members\`, rows)`, then invalidates the `['members', project.projectId]` query key on success.

**Backend** (`ProjectEndpoints.cs`):
- `GET /api/projects/{id:long}/members` — `Access.Project(c, null, ctx.Me(), id, "viewer")`; returns rows via the shared `Members(c, id)` query.
- `PUT /api/projects/{id:long}/members` (`List<MemberIn> { UserId, Access }`) — `Access.Project(w, id, "owner")` (only an owner may call this at all). Rejects the whole list with 400 "A project needs at least one owner." if no entry has `Access == "owner"`. Deletes members not present in the submitted list (`DELETE FROM ProjectMember WHERE ProjectId = @id AND NOT (UserId = ANY(@ids))`), then per member does `INSERT INTO ProjectMember (ProjectId, UserId, Access) VALUES (...) ON CONFLICT (ProjectId, UserId) DO UPDATE SET Access = EXCLUDED.Access`. Logs `"members.update"`, returns the refreshed member list.

**Database:** Table `ProjectMember` (`001_init.sql`): `ProjectId (FK Project ON DELETE CASCADE), UserId (FK AppUser ON DELETE CASCADE), Access (AccessLevel enum), AddedAt`, `PRIMARY KEY (ProjectId, UserId)`. `Members()` query joins `ProjectMember m JOIN AppUser u ON u.UserId = m.UserId`, ordered `Access DESC, GivenName` (owners first). No trigger touches this table directly; it's read by `Access.Project`'s scope resolution on every request against this project (see Architecture.md Authorization/scoping) and by the portfolio project-list filter (`EXISTS (SELECT 1 FROM ProjectMember m WHERE m.ProjectId = s.ProjectId AND m.UserId = @uid)`).

### Dependencies
- See `_Overview/Architecture.md`: Authorization/scoping (`ProjectMember` is the explicit-grant source `Access.Project` checks before falling back to capability-based defaults).
- Every other editing ticket on this page (and every other project tab) depends indirectly on this table: a user's effective `access` level (`viewer`/`editor`/`owner`) on all of them is resolved from here first.
- `CreateProject` (used by `01-view-and-edit-project-details.md`'s project-creation flow and `03-manage-project-versions.md`'s copy-project flow) seeds the creator as `owner` here, and optionally the chosen estimator/manager as `editor`.

### Notes / edge cases
- The "at least one owner" rule is enforced only by this endpoint's application code (`if (!body.Any(m => m.Access == "owner")) throw ...`), not by a DB constraint — any other code path that writes `ProjectMember` directly bypasses this safety check.
- Saving is all-or-nothing against the submitted list: any member omitted from the array is deleted, not left alone — the frontend must round-trip the entire current list (including unchanged rows) or it will unintentionally remove members.
- Access here is independent of `project.edit`/`project.view` capabilities: a user with no `ProjectMember` row still gets implicit access per the fallback rule (admin → owner, `project.edit` capability → editor, else viewer) — removing someone's explicit row does not necessarily lock them out if they qualify for a fallback level.
- Because `PUT` requires `"owner"` access and the UI only shows editable controls to owners, a non-owner can still see other members (read access, `"viewer"` on the `GET`) but has no way to self-promote through this UI.
