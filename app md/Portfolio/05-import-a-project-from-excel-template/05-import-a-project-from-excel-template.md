## Title
Portfolio — Start a project: import from Excel template (entry point)

### Summary
The same "Start a project" button and `/projects/new` route that open the blank-start flow also open the
`StartProject` modal's default "Import from Excel" mode, where a user uploads a filled template workbook and
Gridline builds the whole project from it. This ticket covers only how the Portfolio page opens that modal (in its
import-first default state) and wires the finished result back into the list — the upload/check/preview and the
create call themselves are `StartProject.tsx`'s concern and are documented fully in
`app md/StartProject/01-import-excel-upload-and-validate.md` and
`app md/StartProject/02-import-excel-create-project.md`.

### User story
As an estimator who already has a trades/bidders workbook for a project, I want "Start a project" to default
straight into the import flow, so that uploading a filled template is the path of least resistance from my
portfolio.

### Acceptance criteria
- [ ] The "Start a project" button (Portfolio page action bar) and the route `/projects/new` both render the
      `StartProject` modal on top of the portfolio list (`Portfolio` renders `<StartProject>` when its `startNew`
      prop is true) — identical entry point to `04-start-a-blank-project.md`.
- [ ] The modal opens with "Import from Excel" selected by default (`StartProject`'s own `mode` state starts as
      `'import'`) — a user who does nothing but open `/projects/new` and upload a file never has to touch the
      segmented control. Everything about the drop zone, "Download template" link, upload/check request, issues
      preview, and the "Create project" submit in import mode is `StartProject.tsx`'s concern — see Dependencies.
- [ ] On a successful import-mode create, the same `Portfolio`-supplied `onCreated={(no, v) => { refetch();
      nav(\`/p/${encodeURIComponent(no)}/v/${v}/bid-board\`) }}` callback fires as in the blank-mode case: the
      portfolio list refetches and the browser navigates to the new project's Bid board.
- [ ] Closing the modal from the import screen (✕, Cancel, or the scrim) calls the same `onClose={() =>
      nav('/projects')}` as blank mode, discarding the in-progress upload state and returning to `/projects` —
      note that this does **not** cancel or delete the already-uploaded `ImportBatch`/stored workbook on the
      server (see the StartProject tickets' Notes for what that implies about re-uploads).
- [ ] The Portfolio page applies no capability gating before opening the modal — a user without `import.run` can
      reach `/projects/new`, select "Import from Excel", and attempt an upload; they only find out they can't
      proceed when the underlying `POST /api/imports/check` call is rejected (see the StartProject ticket for that
      behavior).

### Technical spec
**Frontend:** `web/src/pages/Portfolio.tsx`. Entry points and toggle are identical to
`04-start-a-blank-project.md`: the action-bar "Start a project" button navigates to `/projects/new`; the route
(`web/src/App.tsx`) renders `<Portfolio startNew />`; `Portfolio` conditionally mounts `<StartProject
onClose={() => nav('/projects')} onCreated={(no, v) => { refetch(); nav(\`/p/${encodeURIComponent(no)}/v/${
v}/bid-board\`) }} />`. The only mode-specific fact that lives in `Portfolio.tsx` at all is that it doesn't set an
initial mode — `StartProject`'s own `useState<'blank' | 'import'>('import')` is what makes import the default. The
drop zone, `upload()`/`FormData` handling, `Check` preview rendering, and the `api.post('/imports/{token}/create')`
call all live in `web/src/pages/StartProject.tsx`, not here.

**Backend:** No endpoint is owned by the Portfolio page for this feature. `POST /api/imports/check` and
`POST /api/imports/{token}/create` (`ImportEndpoints.Map`, `import.run` capability, `ImportBatch`/`ImportService`
pipeline) are invoked from inside `StartProject.tsx` and are fully documented in
`app md/StartProject/01-import-excel-upload-and-validate.md` (check) and
`app md/StartProject/02-import-excel-create-project.md` (create).

**Database:** No tables are written by the Portfolio page itself. `ImportBatch`/`ImportIssue` (check step) and the
full `Project`/`ProjectVersion`/`VersionMarkup`/`ProjectMember`/`Zone`/`Trade`/`Bid`/`TradePartner`/`TradeZone`/
`ScopeLine`/`CostLine` write set (create step) happen inside the shared `ImportService.CreateProject` /
`ProjectEndpoints.CreateProject` pipeline invoked by `StartProject.tsx` — see the two StartProject tickets above
for exact columns, constraints, and triggers.

### Dependencies
- `app md/StartProject/01-import-excel-upload-and-validate.md` — the full technical trace for uploading and
  validating the workbook (the `token`/`Check` preview this entry point eventually leads to).
- `app md/StartProject/02-import-excel-create-project.md` — the full technical trace for turning a checked import
  into a real project (what actually runs when "Create project" is clicked in import mode).
- `../01-browse-filter-and-open-projects/01-browse-filter-and-open-projects.md` — destination list that
  `refetch()`s after the import-created project appears.
- `../04-start-a-blank-project/04-start-a-blank-project.md` — shares this exact entry point (`/projects/new` →
  `<StartProject>`) and the same `onClose`/`onCreated` wiring; differs only in which mode the user ends up using
  inside the modal.

### Notes / edge cases
- "Import from Excel" being the *default* mode is a `StartProject.tsx` implementation detail (its `mode` state's
  initial value), not something `Portfolio.tsx` configures when it opens the modal — don't go looking for a
  `defaultMode="import"` prop on `<StartProject>`, there isn't one.
- Because `Portfolio` unconditionally supplies the same `onClose`/`onCreated` pair regardless of mode, any change
  to that navigation behavior (e.g. where the user lands post-create) affects both this ticket and `04` at once.
