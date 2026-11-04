## Title
Price roll-up — Add, edit and delete custom indirect items

### Summary
Besides the standard markup/tax lines, an estimator can add arbitrary one-off "indirect" cost lines (e.g. "Owner IT
& AV coordination") directly on the roll-up — give it a name and amount, mark whether fee is charged on it, and it
flows into the contract total and the Indirects bucket on the build-up strip. Existing custom lines can be renamed,
re-amounted, toggled in/out of fee, or removed inline.

### User story
As an estimator with edit access, I want to add, adjust, or remove custom indirect cost lines on the roll-up, so
that I can capture project-specific costs that don't fit the standard markup types without leaving the pricing summary.

### Acceptance criteria
- [ ] "Add indirect item" button appears in the page header only when `canEdit` is true; clicking it opens
      `IndirectModal` (imported from `BidBoard.tsx`) with fields Name (required, autofocus) and Amount (defaults to
      `$0` placeholder), plus a checkbox "Outside the fee (fee is not charged on this item)".
- [ ] The modal's "Add" button is disabled while the name is blank or a save is in flight; on submit it posts
      `{ itemName, amount: toNum(amt) ?? 0, outsideFee }`; on success it shows toast "Indirect item added", closes
      the modal, and the parent invalidates the `price-roll-up` query so the new line appears.
- [ ] Every custom-indirect roll-up line (`kind === 'custom'`) shows a "Custom" tag, then (when `canEdit`) an
      inline-editable name field, an "outside fee" checkbox, and a delete (×) button; when not `canEdit`, the line
      shows the same information as read-only text with no controls.
- [ ] Renaming via the inline name field only saves if the trimmed value is non-empty (`v.trim() && saveIndirect(...)`);
      an empty/whitespace-only name is silently ignored (no save, no error).
- [ ] The amount cell is inline-editable (`EditCell number`) when `canEdit`; parses with `toNum`, and only saves
      when the result is a valid number (`n != null`).
- [ ] Toggling "outside fee" immediately calls `PATCH /indirect/{indirectId}` with `{ outsideFee: checked }` — no
      separate "save" step, it fires on `onChange`.
- [ ] Clicking delete calls `DELETE /indirect/{indirectId}` and on completion invalidates the `price-roll-up` query
      and shows toast "Indirect item deleted"; there is no confirmation dialog before deleting.
- [ ] All three inline actions (rename, amount, outside-fee toggle) route through the same `saveIndirect(name, body)`
      helper, which looks up the indirect item by its current display `name` in `data.indirect`; if the lookup
      fails (name mismatch, e.g. stale closure during a rapid rename) the save silently no-ops.
- [ ] Server validation: `ItemName` cannot be blank on create (`"Give the line a name."`) or when explicitly set to
      blank on update (`"The line needs a name."`); both return 400.
- [ ] Locked-version / viewer behavior: with `canEdit` false, no add button, no inline edit controls, and no delete
      button are rendered — mutation is unreachable from the UI (server still enforces `editor`/unlocked via
      `Access.Indirect`/`Access.Version` regardless).

### Technical spec
**Frontend:**
- Add modal: `IndirectModal` in `web/src/pages/project/BidBoard.tsx` (reused from the Bid board page), rendered
  from `PriceRollUp.tsx` as `{addInd && <IndirectModal versionId={vid} onClose={...} onDone={...} />}`.
  `api.post(\`/versions/${versionId}/indirect\`, { itemName, amount, outsideFee })`.
- Inline edit/delete: `PriceRollUp.tsx`, `saveIndirect(name, body)` → `api.patch(\`/indirect/${it.indirectId}\`, body)`;
  delete button → `api.del(\`/indirect/${ind.indirectId}\`)` then `qc.invalidateQueries({ queryKey: key })` where
  `key = vkey('price-roll-up', vid)`.
- Uses shared `EditCell` (text/number inline editor, saves on blur/Enter) and `useAction().run(...)` for toasts.
- Data shape: `IndirectRow { indirectId, itemName, amount, outsideFee, sortOrder, description, isOff }`
  (`web/src/api/types.ts`); the roll-up line for a custom item is matched to its `IndirectRow` via
  `data.indirect.find(i => i.itemName === l.name)`.

**Backend:** all in `api/src/Gridline.Api/Endpoints/SummaryEndpoints.cs`.
- `POST /versions/{id:long}/indirect` — request `IndirectIn(ItemName, Amount, OutsideFee, SortOrder, Description,
  IsOff)`. `Access.Version(w, id)` (editor, unlocked). Validates `ItemName` non-blank. Inserts into `IndirectItem`
  with `SortOrder` defaulting to `max(SortOrder)+1` for the version when not supplied. Calls
  `Recalc.Summary(w, id, "indirect.create")`, logs `indirect.create` on subject `"indirect"`/`iid`, commits, returns
  the full rebuilt `Build(w, id)` payload (ignored by this page — it refetches via query invalidation instead).
- `PATCH /indirect/{id:long}` — same `IndirectIn` shape. `Access.Indirect(w, id)` (editor, unlocked — resolved via
  `IndirectItem → ProjectVersion → Project`). Each field is `coalesce`d so only supplied fields change (description
  uses `nullif(trim(@d), '')` so an explicit blank string clears it to `NULL`, but a missing field leaves it
  untouched). Calls `Recalc.Summary(w, s.VersionId, "indirect.update")`, logs, commits.
- `DELETE /indirect/{id:long}` — `Access.Indirect(w, id)`. Hard-deletes the row, calls
  `Recalc.Summary(w, s.VersionId, "indirect.delete")`, logs, commits.
- All three verbs log `w.Log(s.ProjectId, versionId, verb, "indirect", id, ...)`, giving other open tabs the toasts
  "added an indirect item" / "changed an indirect item" / "deleted an indirect item" (`web/src/app/verbs.ts`).

**Database:**
- `IndirectItem` (`IndirectId bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY`, `VersionId bigint NOT NULL
  REFERENCES ProjectVersion ON DELETE CASCADE`, `ItemName text NOT NULL`, `Amount numeric(16,2) NOT NULL DEFAULT 0`,
  `OutsideFee boolean NOT NULL DEFAULT false`, `SortOrder smallint NOT NULL DEFAULT 1`) — insert/update/delete all
  target this table directly; no unique constraint on `(VersionId, ItemName)`, so duplicate names are allowed and
  `saveIndirect`'s name-based lookup can become ambiguous if two items share a name (first match wins, per JS
  `Array.find`).
  - `Description text` and `IsOff boolean NOT NULL DEFAULT false` are added later by
    `db/migrations/007_fee_carveouts.sql` ("Indirect items ... description, and an on/off switch") — they are not
    present in the original `001_init.sql` definition.
- `Recalc.Summary` (shared with `02-edit-markup-tax-rate-inline.md`) deletes/reinserts `RollupLine`/`RollupZone`,
  updates `ProjectVersion.TotalsHash` if changed, inserts a `CalcRun` row with the matching trigger string
  (`indirect.create`/`indirect.update`/`indirect.delete`).
- `ActivityEvent` row per call via `w.Log`.

### Dependencies
- `01-view-the-price-roll-up.md` — custom lines render inside that table and feed the "Indirects" build-up figure.
- `_Overview/Architecture.md`: Authorization/scoping (`Access.Indirect`, `Access.Version`), Recalculation engine,
  Unit of work/realtime broadcast.
- Shares the `IndirectModal` component with the Bid board page (`BidBoard.tsx`) — a change to that modal's fields
  affects both pages.
- `BidBoard/11-add-an-indirect-cost-item/11-add-an-indirect-cost-item.md` — documents the exact same
  `POST /versions/{id}/indirect` endpoint and the same shared `IndirectModal` component from its other UI entry
  point (the Bid board page's own "Add indirect item" action). Same backend handler, same `IndirectItem` table,
  same `Recalc.Summary(..., "indirect.create")` call — the two tickets describe one feature reachable from two
  pages, not two different features; a change to the endpoint or modal must be reflected in both tickets.

### Notes / edge cases
- `saveIndirect` resolves the target row by matching the *displayed line name* (`l.name`) against
  `data.indirect[].itemName`, not by a stored `indirectId` on the line object — renaming a line and then
  immediately editing another field before the refetch lands can look up the wrong (stale) `indirectId` if two
  items briefly share a name during the rename.
- Deleting an indirect item has no confirmation step and no undo — it's a hard delete, not an archive.
- `OutsideFee = true` ("outside fee") removes the item from the fee markup's base (handled in `Rules.Summary`'s
  `insideFee` calculation on the backend / `calcSummary` on the frontend) but does **not** remove it from the
  "Indirects" bucket on the build-up strip — those are two independent effects of the same flag, easy to conflate.
- `IsOff` and `Description`/`SortOrder` fields exist in the `IndirectIn` DTO and SQL but have no UI on this page
  (sort order and off-switch aren't exposed here) — only `ItemName`, `Amount`, and `OutsideFee` are reachable from
  the Price roll-up UI.
