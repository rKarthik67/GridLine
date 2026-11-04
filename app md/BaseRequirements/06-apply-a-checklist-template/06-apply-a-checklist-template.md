## Title
Base requirements — Apply a company checklist template

### Summary
Lets an editor bulk-add every line from a reusable company checklist template (a `RequirementSet` marked `IsTemplate`) onto the current version's base requirements, skipping any line whose text already exists, via the "Add from checklist" toolbar menu.

### User story
As an estimator starting a new version's checklist (or extending an existing one), I want to pull in a pre-built company checklist in one click so that I don't have to retype standard requirement lines by hand.

### Acceptance criteria
- [ ] "Add from checklist" (toolbar menu button, only visible when editable) lists every available template, each labelled "{setName} · {n} lines".
- [ ] Clicking a template entry appends all of that template's lines to the end of the current checklist and shows a toast "Lines added from {setName}".
- [ ] A template line whose text already exists in the current checklist (case-insensitive exact match) is skipped — it is not duplicated.
- [ ] Every newly-added line is immediately propagated onto every active trade's Scope match grid as a `baserequirement` row, same as a manually inserted row, but only for trades that don't already have a `ScopeLine` for that requirement line.
- [ ] The action is unavailable (menu not rendered) when the version isn't editable (viewer access or locked version).
- [ ] If the version has no checklist yet (`data.set === null`), applying a template is one of the two ways to start one (the other being inserting a row manually) — the empty-state copy on the page explicitly calls this out ("No checklist yet — add a requirement or copy one from a checklist.").

### Technical spec
**Frontend:** `web/src/pages/project/BaseRequirements.tsx`. The `<Menu label="Add from checklist" icon="copy" items={...} />` in the `PageHead actions` is built from `data.templates` (from the same `GET /versions/{vid}/requirements` response as ticket 01 — `{ setId, setName, lines }[]`, every `RequirementSet` with `IsTemplate = true`). Each menu item's `onClick` calls `put(await run(() => api.post<ReqData>(\`/versions/${vid}/requirements/apply-template\`, { setId: t.setId }), \`Lines added from ${t.setName}\`))`, writing the result into the query cache and invalidating `'scope'`-prefixed queries, same as every other mutation on this page.
**Backend:** `POST /api/versions/{id}/requirements/apply-template` in `Endpoints/ScopeExtrasEndpoints.cs`, body `SetIn(long SetId)`. Calls `Access.Version(w, id)` (editor rank, 423 if locked) then `OwnSet(w, id)` (copy-on-write, see ticket 02's Notes). Inserts new `RequirementLine` rows into the version's own set: `INSERT INTO RequirementLine (...) SELECT @set, @start + row_number() OVER (ORDER BY l.SortKey), l.SectionName, l.IsHeading, l.LineText, l.Desired FROM RequirementLine l WHERE l.RequirementSetId = @tpl AND NOT EXISTS (SELECT 1 FROM RequirementLine x WHERE x.RequirementSetId = @set AND lower(x.LineText) = lower(l.LineText))` — `@start` is the current max `SortKey` in the owned set, so new lines are appended in the template's own order, and the `NOT EXISTS` clause is the dedup-by-text guard. Then propagates onto trades: `INSERT INTO ScopeLine (...) SELECT t.TradeId, 'baserequirement', r.RequirementLineId, r.SortKey, r.IsHeading, r.LineText, r.Desired, @u FROM Trade t JOIN RequirementLine r ON r.RequirementSetId = @set WHERE t.VersionId = @id AND t.ArchivedAt IS NULL AND NOT EXISTS (SELECT 1 FROM ScopeLine l WHERE l.TradeId = t.TradeId AND l.RequirementLineId = r.RequirementLineId)` — this scans *all* lines in the owned set (not just newly-added ones) but the `NOT EXISTS` means only trades missing a given line actually get a new row, so it safely backfills any trade that was somehow missing an existing line too. Logs `w.Log(projectId, id, "requirement.template", "version", id, after: new { body.SetId, added = n })` (where `n` is the row count from the first insert), commits, returns `Requirements(...)`.
**Database:** Reads source lines from `RequirementLine WHERE RequirementSetId = @tpl` (the template set chosen by the user) and writes new rows into `RequirementLine WHERE RequirementSetId = @set` (the version's own set) plus `ScopeLine (TradeId, Section, RequirementLineId, SortKey, IsHeading, LineText, Desired, ChangedBy)` for every active `Trade` on the version. `RequirementSet.IsTemplate` is what makes a set eligible to appear in the "Add from checklist" list in the first place (filtered in the `Requirements()` helper's third query, see ticket 01). Index `IxRequirementLineSet (RequirementSetId, SortKey)` backs both the source scan and the `@start` max lookup; `IxScopeLineTrade (TradeId, Section, SortKey) WHERE ArchivedAt IS NULL` backs the trade-presence `NOT EXISTS` check.

### Dependencies
- Depends on ticket 01's `templates` list (the set of eligible templates) and shares `OwnSet` copy-on-write with tickets 02–05.
- Does **not** call `Calc.Recalc` (same reasoning as ticket 02's insert — brand-new lines start with no answers, nothing to recalc).
- See `_Overview/Architecture.md`: Authorization/scoping, Unit of work, Database conventions (`RequirementSet`/`RequirementLine` template model).

### Notes / edge cases
- Deduplication is by **exact lowercase text match only** — two lines with the same meaning but different wording (or trailing punctuation/whitespace differences not already trimmed) will both be added; there is no fuzzy matching.
- Because the second `INSERT ... ScopeLine` statement scans the entire owned set (not just the rows just inserted), running "Apply template" is also implicitly a repair/backfill step for any pre-existing requirement line that a trade is missing a `ScopeLine` row for, for whatever reason (e.g. a trade created after a manual insert that somehow didn't get synced). This is a side effect, not something surfaced to the user.
- Section headings inside a template are copied too (`IsHeading`/`Desired` carried through as-is) and are included in the "lines" count shown in the template picker — the count is a raw row count, not a "requirements excluding headings" count.
- As with every other mutating endpoint on this page, the very first use of this feature on a version still bound to a shared template checklist forces `OwnSet`'s copy-on-write, cloning the entire current checklist before appending the new template's lines.
