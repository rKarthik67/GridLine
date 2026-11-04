## Title
Base requirements — View the base requirements checklist

### Summary
Shows the full checklist of requirement lines (and section headings) that every trade partner on this version must answer, along with how many trades currently carry each line. This is the read view of the single source of truth that Scope match displays (read-only) at the top of every trade.

### User story
As an estimator or project team member, I want to see the version's base requirements checklist — its sections, requirement text, desired answers, and how many trades have each line — so that I know what every trade partner is being asked to confirm before I make changes.

### Acceptance criteria
- [ ] On page load, the grid fetches and renders the checklist for the current version; while loading it shows a loading indicator ("base requirements"), and on failure it shows a failure state.
- [ ] Each non-heading row shows: a sequential line number (counting only non-heading rows, so headings don't consume a number), Section, Requirement text, Desired answer (`Included` / `Excluded` / `Price` / `Text`, or blank), and a Trades count.
- [ ] Each heading row spans the Section/Requirement/Desired columns in one bold cell on a tinted background, shows no line number, and still shows its own Trades count.
- [ ] If the version has zero requirement lines, the table body shows a single empty-state row ("No base requirements.") spanning all 5 columns instead of the normal rows.
- [ ] A status line above the grid reads "Checklist: **{setName}**" when a checklist is attached to the version; if that checklist `isTemplate` is still true (no version-specific copy exists yet) it appends "(company template — your first change makes a copy for this version)". If the version has no checklist at all, it reads "No checklist yet — add a requirement or copy one from a checklist."
- [ ] A hint "Right-click a row to insert, move or delete" is always shown next to the status line.
- [ ] The "Add from checklist" menu button and the "Add requirement" button are only rendered when the user can edit (see Permission behavior below); otherwise the toolbar actions are hidden entirely and rows render as plain text (no inline editors, no context menu).
- [ ] Permission behavior: editing (and thus the add/insert/edit/move/delete affordances) is enabled only when `project.access !== 'viewer'` AND `version.status !== 'locked'`. Locked versions and viewer-level access both fall back to the read-only rendering described above, with no error — this is a client-side UI gate, not a blocked fetch.

### Technical spec
**Frontend:** `web/src/pages/project/BaseRequirements.tsx`, default export `BaseRequirements({ project, version })`. Uses `useQuery({ queryKey: vkey('requirements', vid), queryFn: () => api.get<ReqData>(\`/versions/${vid}/requirements\`) })`. `ReqData = { set: { setId, setName, isTemplate } | null; lines: ReqLine[]; templates: { setId, setName, lines }[] }`, `ReqLine = { requirementLineId, sortKey, sectionName, isHeading, lineText, desired, trades }`. Renders via `PageHead` (title "Base requirements", `was="General scope"`), `Loading`/`Failed` for query states, and a plain `<table className="dg">`. No SignalR `focus` calls are made on this page; realtime refresh happens only through the generic `changed` → query-cache-invalidation path described in Architecture.md (any write by another tab that touches this version's `requirements` or `scope` query keys refetches this grid).
**Backend:** `GET /api/versions/{id}/requirements` in `Endpoints/ScopeExtrasEndpoints.cs`. Opens a plain connection (`db.Open()`, no `Work`, since it's read-only) and calls `Access.Version(c, ctx.Me(), id)` with the default `need = "viewer"` — any project member (including viewers) can load the page; locked versions are still viewable (lock only blocks mutations). Delegates to the shared `Requirements(c, t, versionId)` helper, which runs three queries: (1) the attached `RequirementSet` via `ProjectVersion.RequirementSetId`, (2) all `RequirementLine` rows for that set ordered by `SortKey, RequirementLineId`, with a correlated subquery counting non-archived `ScopeLine` rows (joined to `Trade` filtered to this version) per line as `"trades"`, (3) all `RequirementSet` rows where `IsTemplate` is true (the "Add from checklist" source list), each with its own line count.
**Database:** Reads `ProjectVersion.RequirementSetId` (FK `FkVersionRequirementSet` → `RequirementSet.RequirementSetId`), `RequirementSet (RequirementSetId, SetName, IsTemplate, DivisionId, ChangedAt)`, `RequirementLine (RequirementLineId, RequirementSetId, SortKey numeric(12,4), SectionName, IsHeading, LineText, Desired DesiredKind)` — uses index `IxRequirementLineSet ON RequirementLine (RequirementSetId, SortKey)` (`db/migrations/003_indexes.sql`). The per-line trade count reads `ScopeLine (LineId, TradeId, RequirementLineId, ArchivedAt)` joined to `Trade (TradeId, VersionId, ArchivedAt)`, using index `IxScopeLineTrade ON ScopeLine (TradeId, Section, SortKey) WHERE ArchivedAt IS NULL`. `DesiredKind` is the Postgres enum `('included','excluded','price','text')` defined in `001_init.sql`.

### Dependencies
- Depends on nothing else in this folder (it's the base read used by every other ticket's "return the updated checklist" response).
- Feeds `ScopeMatch.tsx`'s `baserequirement` section, which renders these same lines read-only on every trade and deep-links back here with "(Please edit item in Base requirements)" — see `app md/ScopeMatch` tickets for that side.
- See `_Overview/Architecture.md`: Authorization/scoping (`Access.Version`) and Realtime (`changed` event / query cache invalidation).

### Notes / edge cases
- The visible "#" column is purely a client-side derived count (`lines.slice(0, i+1).filter(x => !x.isHeading).length`); it is not a stored column and will re-derive correctly even after inserts/deletes/reorders change `sortKey` ordering.
- The "Trades" count can be lower than the version's total active trade count if a trade was created before a given requirement line was added (new trades get synced at creation time elsewhere) or if a trade was archived — this is a live reflection of `ScopeLine`, not a cached number.
- `data.set.isTemplate` stays true until the very first edit on this version (see the "copy-on-write" behavior documented in ticket 02); until then every version sharing that template checklist is reading (not necessarily editing) the exact same `RequirementSet`/`RequirementLine` rows.
