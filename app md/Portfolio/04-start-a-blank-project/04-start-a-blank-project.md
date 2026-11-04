## Title
Portfolio — Start a project: blank (entry point)

### Summary
The "Start a project" button and the `/projects/new` route are the Portfolio page's two entry points into the
`StartProject` modal, which in its "Start blank" mode creates a brand-new project and its first version from a
short form. This ticket covers only how the Portfolio page opens and closes that modal and wires its result back
into the list — the blank-form fields, validation, and the `POST /api/projects` call itself are a different page's
component and are documented fully in `app md/StartProject/03-start-blank-project.md`.

### User story
As an estimator, I want a single "Start a project" action on my portfolio that drops me straight into a blank
project in a few seconds, so that I can start pricing without leaving the page I'm already on.

### Acceptance criteria
- [ ] The "Start a project" button in the Portfolio page's action bar (top-right, next to "Export") navigates to
      `/projects/new` (`nav('/projects/new')`) rather than opening the modal directly from local state.
- [ ] The route `/projects/new` is mapped (in `web/src/App.tsx`) to `<Private><Portfolio startNew /></Private>` —
      the same `Portfolio` component as `/projects`, with its `startNew` prop set to `true`.
- [ ] `Portfolio` renders `<StartProject>` only when `startNew` is true: `{startNew && <StartProject
      onClose={...} onCreated={...} />}` — on plain `/projects` (no `startNew`), the modal never mounts.
- [ ] The modal opens with "Import from Excel" selected by default (`StartProject`'s own internal `mode` state);
      the user must click the "Start blank" segment inside the modal to reach the blank form. The segmented
      control, form fields, client/server validation, and the create call are entirely `StartProject.tsx`'s
      concern — see Dependencies.
- [ ] On a successful blank-mode create, `Portfolio`'s `onCreated={(no, v) => { refetch(); nav(\`/p/${
      encodeURIComponent(no)}/v/${v}/bid-board\`) }}` fires: the project list query is refetched (so the new
      project appears in the portfolio immediately if the user navigates back) and the browser navigates straight
      to the new project's Bid board — the modal itself closes as a side effect of `Portfolio` no longer rendering
      `/projects/new`.
- [ ] Closing the modal (✕, Cancel, or clicking the scrim, all inside `StartProject`) calls `onClose={() =>
      nav('/projects')}`, which discards whatever was typed and returns to the plain portfolio list without
      creating anything.
- [ ] The Portfolio page itself applies no capability gating before opening the modal — a viewer-only user (no
      `project.edit`) can reach `/projects/new` and see the blank form; they only find out they can't proceed when
      the underlying create call 403s inside `StartProject` (see the StartProject ticket for that behavior).

### Technical spec
**Frontend:** `web/src/pages/Portfolio.tsx`. Entry points: the action-bar button `<button className="btn p"
onClick={() => nav('/projects/new')}><Icon name="plus" />Start a project</button>`, and the route itself
(`web/src/App.tsx`: `<Route path="/projects/new" element={<Private><Portfolio startNew /></Private>} />` alongside
`<Route path="/projects" element={<Private><Portfolio /></Private>} />`). Toggle: the `startNew?: boolean` prop on
`Portfolio({ startNew })` — there is no internal Portfolio state for this, it's driven purely by which route
matched. Wiring into the modal: `{startNew && <StartProject onClose={() => nav('/projects')} onCreated={(no, v) =>
{ refetch(); nav(\`/p/${encodeURIComponent(no)}/v/${v}/bid-board\`) }} />}`. Everything inside the modal
(`web/src/pages/StartProject.tsx`'s `mode`/`form`/`busy`/`err` state, the `fgrid` of six inputs, and the `create()`
branch that calls `api.post<{ projectNo: string; versionNo: number }>('/projects', form)` for blank mode) belongs
to `StartProject.tsx`, not `Portfolio.tsx` — not re-documented here.

**Backend:** No endpoint is owned by the Portfolio page for this feature — `Portfolio.tsx` never calls the API
directly to create a project. The actual `POST /api/projects` (`ProjectEndpoints.Map`, `ctx.Me().Require
("project.edit")`, shared `CreateProject`/`ApplyDefaultCostKits`/`Recalc.Summary` pipeline) is invoked from inside
`StartProject.tsx` and is fully documented in `app md/StartProject/03-start-blank-project.md`.

**Database:** No tables are written by the Portfolio page itself. The full set of writes (`Project`,
`ProjectVersion`, `VersionMarkup`, `ProjectMember`, `RecentProject`, `CostLine`) happens inside the shared
`CreateProject`/`ApplyDefaultCostKits` helpers invoked by `StartProject.tsx`'s create call — see
`app md/StartProject/03-start-blank-project.md` for the exact columns, constraints, and triggers involved.

### Dependencies
- `app md/StartProject/03-start-blank-project.md` — the full technical trace for the blank-create flow itself
  (form fields, validation, `ProjectIn`/`CreateProject`, tables written, triggers). This ticket only covers how
  Portfolio opens into and returns from that flow.
- `../01-browse-filter-and-open-projects/01-browse-filter-and-open-projects.md` — the list that `refetch()`s after
  creation and the row the new project lands as.
- `../05-import-a-project-from-excel-template/05-import-a-project-from-excel-template.md` — shares this exact
  entry point (`/projects/new` → `<StartProject>`), differing only in which segment of the modal the user picks.

### Notes / edge cases
- Because the toggle is route-driven (`/projects` vs `/projects/new`) rather than local component state, a direct
  link or bookmark to `/projects/new` opens straight into the modal with no portfolio data interaction required
  first — the underlying `useQuery(['projects'])` still fires in the background so the list is ready the moment
  the modal closes.
- `Portfolio` passes the *same* `onClose`/`onCreated` callbacks regardless of which mode (`blank`/`import`) the
  user ends up using inside `StartProject` — this ticket and `05` are two documentation entries for the same pair
  of callbacks, not two different code paths in `Portfolio.tsx`.
