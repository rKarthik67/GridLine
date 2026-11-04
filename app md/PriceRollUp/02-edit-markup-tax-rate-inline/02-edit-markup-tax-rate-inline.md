## Title
Price roll-up — Edit a markup/tax rate inline

### Summary
Editable roll-up lines (markup and tax kinds, e.g. fee, bond, liability, sales tax, reserves) show their rate as an
inline editable percentage. Typing a new rate shows the browser's instant recalculation of every downstream line,
then saves to the server, which recalculates authoritatively and the page swaps back to the confirmed numbers.

### User story
As an estimator with edit access, I want to change a markup or tax rate directly on the roll-up and see the whole
build-up respond immediately, so that I can tune pricing without leaving the page or waiting on a full save round-trip.

### Acceptance criteria
- [ ] The rate cell is an editable `EditCell` (number input, saves on blur/Enter, Escape cancels) only when all of:
      the line's `kind` is `markup` or `tax`, it has a `code`, its code is not a zone-reserve code (`zoneReserves`),
      and `canEdit` is true (`project.access !== 'viewer' && version.status !== 'locked'`); otherwise the rate shows
      as read-only text (plain percentage, or " by zone" for reserve codes, or an "off" tag if the line is switched off).
- [ ] As soon as the user types a value, the page recomputes every line's amount locally via `calcSummary` (the
      shared `@gridline/calc` package) using the draft rate (with `fixedAmount` cleared), and the header caption
      changes to "… recalculating…" until the save round-trip completes.
- [ ] Saving sends `PUT /versions/{versionId}/markups` with body `[{ code, ratePct, clearFixed: true }]` — i.e. a
      fixed-amount override on that code is cleared as soon as the user edits the percentage rate here.
- [ ] On success, a toast reads "Rate saved · roll-up recalculated"; the `price-roll-up` query is invalidated and
      refetched, the local draft value for that code is cleared, and the `bid-board` query is also invalidated
      (because trade zone totals can shift the roll-up's zone breakdown).
- [ ] On failure, `useAction`'s `run` wrapper shows the server's error message as a toast (or "Could not save" if
      none) and does not clear the draft, leaving the typed value visible with `recalculating…` still showing.
- [ ] Server validation: the rate must correspond to a real `MarkupType.MarkupCode` (400 `Unknown rate '{code}'.`
      otherwise) and must be between 0 and 100 inclusive (400 `Rate for {code} must be between 0 and 100%.` otherwise).
- [ ] Locked-version / viewer behavior: when `canEdit` is false, the cell renders as plain text — there is no way to
      submit a rate change from this page at all (not just a disabled input).
- [ ] Editing the `fee` markup's rate also updates `ProjectVersion.FeePct` (kept in sync with `VersionMarkup`), used
      elsewhere in the app as the version's headline fee percentage.

### Technical spec
**Frontend:** `web/src/pages/project/PriceRollUp.tsx`, function `saveRate(code, v)`.
- Parses the typed string with `toNum` (strips `$,%` and handles `(123)` as negative); no-ops if unparseable.
- Sets local `draft[code]` immediately so `lines` (a `useMemo`) recomputes via `calcSummary(input)` where `input`
  is `data.input` with the edited code's `ratePct` overridden and `fixedAmount: null`; recomputed lines also carry
  forward `budgetAmount` from the last loaded data and `editable` flags.
- `api.put<...>(\`/versions/${vid}/markups\`, [{ code, ratePct: r, clearFixed: true }])` via `useAction().run(...)`.
- After the call (success or not need not matter for invalidation — code calls `qc.invalidateQueries({ queryKey: key })`
  unconditionally, then clears the draft entry for that code), and only invalidates `bid-board` if `run` returned
  truthy (i.e. the save actually succeeded).
- Request type: `MarkupPut`-shaped body (see backend `MarkupPut` record); no typed request DTO is exported in
  `web/src/api/types.ts` for this PUT — it's an inline array literal.

**Backend:** `PUT /versions/{id:long}/markups` → `ProjectEndpoints.cs` (inline handler, route group tag "Portfolio,
versions and project profile", not `SummaryEndpoints` despite serving the roll-up page).
- Request DTO: `List<MarkupPut>` where `MarkupPut(string Code, decimal? RatePct, decimal? FixedAmount, decimal?
  BudgetAmount, bool? ClearFixed, bool? IsOff)`.
- Auth/scope: `Access.Version(w, id)` — default `need = "editor"`, so viewers get 403 and locked versions get 423
  (`Access.Check`, see `_Overview/Architecture.md`: Authorization/scoping).
- Per markup in the body: validates the code exists in `MarkupType` and the rate is in `[0,100]` (see acceptance
  criteria), then upserts `VersionMarkup` — on insert, a null `RatePct` falls back to the sales-tax rate for
  `salestax` codes or the `MarkupType.DefaultRatePct` otherwise; on conflict, updates `RatePct`/`IsOff`, and sets
  `FixedAmount` to `NULL` when `ClearFixed` is true (always true from this page) or else coalesces to the existing
  value. If `code == "fee"` and a rate was given, also updates `ProjectVersion.FeePct`.
- Calls `Recalc.Summary(w, id, "markups.update")` (recomputes and persists `RollupLine`/`RollupZone`/`CalcRun` — see
  `_Overview/Architecture.md`: Recalculation engine), logs `w.Log(s.ProjectId, id, "markups.update", "version", id,
  after: body)` (toast verb "changed rates" on other tabs — `web/src/app/verbs.ts`), then `w.Commit()` (commits the
  transaction and broadcasts the SignalR `changed` event — see Architecture.md: Unit of work).
- Response: `{ markups: Markups(w.C,id), costs: CostSwitches(w.C,id), hash: r.Hash }` — the page ignores this body
  and instead invalidates/refetches `GET /versions/{id}/price-roll-up`.

**Database:**
- `VersionMarkup` (PK `VersionId, MarkupCode`; `RatePct numeric(8,5) NOT NULL DEFAULT 0`; `FixedAmount numeric(16,2)`
  nullable "overrides the rate when set"; `BudgetAmount numeric(16,2)`) — upserted via `INSERT ... ON CONFLICT
  (VersionId, MarkupCode) DO UPDATE`.
- `MarkupType` (PK `MarkupCode`) — existence check only, not written.
- `ProjectVersion.FeePct` — updated only when `code == 'fee'`.
- `Recalc.Summary` deletes and reinserts every row in `RollupLine` (unique `(VersionId, SortOrder)`) and
  `RollupZone` (PK `RollupLineId, ZoneId`) for the version, updates `ProjectVersion.TotalsHash` when it changed, and
  inserts one `CalcRun` row (`Trigger = 'markups.update'`, `ClientHash` null here since this call path doesn't pass
  one, `ServerHash`, `Matched` null).
- `ActivityEvent` gains a row via `w.Log` (verb `markups.update`).
- No explicit trigger fires on `VersionMarkup` (it has no updated-at column eligible for `TrgTouch`); `RollupLine`/
  `RollupZone` are pure calculation outputs, also not covered by `TrgTouch`.

### Dependencies
- `01-view-the-price-roll-up.md` — this feature edits one cell of that view and shares its loaded `RollUpData`/`draft` state.
- `_Overview/Architecture.md`: Recalculation engine (hybrid client/server calc, `CalcRun` mismatch detection),
  Authorization/scoping (editor/locked checks), Unit of work (`Work.Log`/`Commit`/broadcast).
- Project profile → Rates & indirects page owns the "off" switch and default rates for these same `VersionMarkup`
  rows (not documented here).

### Notes / edge cases
- Because `clearFixed: true` is always sent from this page, editing the percentage here silently discards any
  fixed-dollar override that may have been set via the Estimator or Rates page — there's no confirmation prompt.
- The client-side recalculation (`calcSummary`) and the server's `Rules.Summary` are two independent
  implementations (TypeScript vs C#) that are expected to agree bit-for-bit; `CalcRun.Matched` exists precisely to
  catch drift between them, but this endpoint doesn't pass a `clientHash` so `Matched` is always recorded as `null`
  for `markups.update` triggers (unlike some trade-level save paths that do pass one).
- Zone-reserve rate codes (`reserveproject`, `reservedesign`, `reserveescalation` when any zone has a non-zero
  reserve %) are deliberately excluded from this inline editor even when `canEdit` is true — their rate is derived
  from Zone configuration and must not be hand-edited here.
- A rate edit changes `Trade`-independent lines only, but still invalidates `bid-board` because the zone-share
  math and heat-map `zmax` both depend on trade values that could be redrawn once the roll-up's percentages shift
  visible emphasis — in practice the trades themselves don't change, only the roll-up's zone columns do.
- Multi-tab realtime: another tab editing the same rate broadcasts `markups.update`, which invalidates this tab's
  `price-roll-up` query automatically — if the local user is mid-edit (draft set) when that refetch lands, the
  draft value still wins in the `lines` memo until it's cleared by this tab's own save completing.
