## Title
Price roll-up — View the price roll-up (build-up cards, zone table, trade drill-in)

### Summary
The Price roll-up tab (`pageKey=price-roll-up`, legacy name "Executive summary") shows how the contract total
builds up from trades, project overheads, precon services, indirects and markups, broken out by zone. It is the
estimator's final sanity check before a bid goes out: a visual build-up strip at the top, then a full line-by-line
table with per-zone columns and an optional heat map, plus a read-only roll-up of every carried trade so the user
can jump straight to Scope match for anything unpriced.

### User story
As an estimator or project owner, I want to see the full price build-up (trades, overheads, precon, indirects,
fee/insurance, tax) broken down by zone, so that I can verify the contract total and spot zones or trades that are
driving the number before I submit a bid.

### Acceptance criteria
- [ ] On load, the page fetches `GET /versions/{versionId}/price-roll-up` and `GET /versions/{versionId}/bid-board`
      in parallel; while either is loading the page shows `Loading the price roll-up`; on error it shows
      `Couldn't load: {message}`.
- [ ] The build-up strip shows four stacked rows in order — Trades, Project overheads, Indirects, Insurance & fee —
      each with its dollar amount and its percentage of Contract total (`pct(v / CT * 100)`, blank "–" when
      `CT` is 0), followed by a highlighted "Contract total" row showing `$/SF` (`CT / grossArea`, blank when
      `grossArea` is 0).
      - "Indirects" = sum of markup lines whose code is NOT one of `fee`/`liability`/`bond`/`cover`, plus the
        "Precon services" cost line, plus every `custom` (indirect item) line.
      - "Insurance & fee" = sum of markup lines whose code IS one of `fee`/`liability`/`bond`/`cover`.
- [ ] The zones toolbar offers three independent two-way toggles (`Seg` controls), all client-side/local state
      (not persisted): **Total / + $/SF** (adds a `$/SF` column per zone), **Heat map / Plain** (colors trade and
      line zone cells by intensity), **With trades / Build-up only** (shows/hides the per-trade rows above the
      roll-up lines).
- [ ] A caption next to the toggles reads "Gross area {grossArea} SF · recalculating…" while any rate edit is in
      flight (draft state non-empty) or "… confirmed by the server" otherwise.
- [ ] The zone table header always shows: Line, Rate, Amount, % contract, $/SF, Budget, Δ budget, then one column
      pair per zone (zone name + floor area or "site" if `floorArea` is 0, and a `$/SF` sub-column only in `+ $/SF`
      mode).
- [ ] When "With trades" is on, one row per trade from the bid-board response appears above the roll-up lines,
      showing: CSI code, trade name as a link to `../scope-match?trade={tradeId}`, the picked bidder's name (or
      "plug" if a `plugAmount` is used with no carried bid, or a yellow "Nothing picked" tag if neither), the
      trade's value (`carriedValue ?? lowestValue ?? 0`), its % of contract, $/SF, budget value, Δ budget (colored
      red if over budget, "good" color otherwise), and its amount in each zone (`value × zoneShares[zoneCode]`).
- [ ] Each roll-up line row shows: a "Custom" tag for indirect-item lines; the line name (bold+uppercase for
      `calculated`/`trades` kind rows, which also get a highlighted background); a rate cell (percentage for
      `markup`/`tax` kinds, or an "off" tag when the line's markup code is switched off on Project profile →
      Rates & indirects); an amount cell; % of contract; $/SF; budget amount; Δ budget (amount − budget, blank if
      no budget); and one amount cell per zone.
- [ ] Lines with `kind === 'cost'` (Project overheads / Precon services) show an "open" link that navigates to
      `../overheads` (when `code === 'genreq'`) or `../precon-services` (otherwise), relative to the current path.
- [ ] Zone reserve-rate lines (`code` present in `data.zoneReserves`) show their rate as read-only text with
      " by zone" and a tooltip "Weighted from each zone's reserve % (Project profile → Zones)" — never an edit
      control, regardless of `canEdit`.
- [ ] Heat map intensity for a zone cell is `sqrt(v / zmax[zoneCode]) * 0.45 + 0.04` opacity of the zone's colour
      (default `#6a32d4`), with white text once the cell exceeds 55% of that zone's max value; `zmax` is the max of
      every trade's carried/lowest value × its share in that zone (floor 1 to avoid divide-by-zero).
- [ ] Page actions include an "Estimator" button (opens the what-if modal — see `02-edit-markup-tax-rate-inline.md`
      sibling ticket `05-estimator-what-if-modal.md`), a "Fee carve-outs" button that navigates to
      `../profile?tab=fees` and shows a count badge when `carveOuts.applyFeeExcludes` is true and
      `feeExcludes.length > 0`, an "Add indirect item" button (editors only, see `03-manage-custom-indirect-items.md`),
      and the export menu (see `04-export-price-roll-up.md`).

### Technical spec
**Frontend:** `web/src/pages/project/PriceRollUp.tsx`, default export `PriceRollUp({ project, version })`.
- `useQuery({ queryKey: vkey('price-roll-up', vid), queryFn: () => api.get<RollUpData>(\`/versions/${vid}/price-roll-up\`) })`
- `board = useQuery({ queryKey: vkey('bid-board', vid), queryFn: () => api.get<BidBoardData>(\`/versions/${vid}/bid-board\`) })`
- Local UI state only: `cols` (`'total' | 'sf'`), `heat` (boolean), `showTrades` (boolean) — none persisted server-side.
- `canEdit = project.access !== 'viewer' && version.status !== 'locked'` gates the rate/indirect edit controls
  throughout (see `_Overview/Architecture.md`: Authorization/scoping for what `access`/`status` mean).
- Response shape `RollUpData` (`web/src/api/types.ts`): `{ versionId, totalsHash, grossArea, lines: SummaryLineDto[],
  zones: Zone[], indirect: IndirectRow[], markups: MarkupRow[], zoneReserves: string[], carveOuts: CarveOuts, input }`.
  `SummaryLineDto = { name, kind, code?, ratePct?, amount, perArea, pctOfContract, zones, budgetAmount?, editable?, isOff? }`.

**Backend:** `GET /versions/{id:long}/price-roll-up` → `SummaryEndpoints.Build` (`api/src/Gridline.Api/Endpoints/SummaryEndpoints.cs`).
- Auth/scope: `Access.Version(w, id, "viewer")` — any project member (or admin/editor fallback) can view; read-only,
  `Work.Begin` opens a transaction that is never committed ("read only: never committed").
- Business logic: calls `Recalc.SummaryInput(w, versionId)` to assemble a `SummaryIn` from live trade/cost data,
  then `Rules.Summary(input)` (pure calc, mirrors the TypeScript `@gridline/calc` the browser uses) to build the
  line list; computes `Rules.TotalsHash(...)`; also loads zone info (`ProjectEndpoints.Zones`), `IndirectItem` rows,
  switched-off `VersionMarkup` codes, `ProjectEndpoints.Markups`, `Recalc.ZoneReserveCodes`, and fee carve-outs
  (`CarveOuts`). Nothing is written to `RollupLine`/`RollupZone`/`CalcRun` by this GET — those tables are only
  populated by the mutating endpoints that call `Recalc.Summary` (see sibling tickets).
- Bid-board data for the trade rows comes from the separate `GET /versions/{id}/bid-board` endpoint
  (`TradeEndpoints`, not re-documented here — see `BidBoard/_index.md`).

**Database:** reads only, no writes:
- `Trade` (`CarriedValue`, `LowestValue`, `ArchivedAt`) summed for `tradesTotal`.
- `CostLine` (`QuotedTotal`, `Area`) summed per `Area IN ('genreq','precon')` for overheads/precon totals.
- `VersionMarkup` (`VersionId`, `MarkupCode`, `RatePct`, `FixedAmount`, `BudgetAmount`, `IsOff`) joined to
  `MarkupType` (`MarkupCode` PK, `MarkupName`, `AppliesTo` CHECK IN `('direct','subtotal','contract','tax')`,
  `DefaultRatePct`) for rates.
- `IndirectItem` (`IndirectId`, `VersionId`, `ItemName`, `Amount`, `OutsideFee`, `SortOrder`) for custom lines.
- `Zone` (`ZoneId`, `ZoneCode`, `ZoneName`, `FloorArea`, `SortOrder`, `Colour`, reserve-pct columns) for the zone
  columns and per-zone reserve-rate weighting.
- `TradeZone` joined to `Trade`/`Zone` for each trade's zone-share amounts.
- `ProjectVersion` (`GrossArea`, `ApplyFeeExcludes`, `FeeExcludes`, `ExcludeGenreqFromDirect`, `ReservePrecon`) for
  fee carve-out state and gross area.
- No triggers fire (read-only transaction). `RollupLine`/`RollupZone`/`CalcRun` (written elsewhere) are not touched here.

### Dependencies
- `_Overview/Architecture.md`: Authorization/scoping (`Access.Version`), Recalculation engine (`Recalc.SummaryInput`,
  `Rules.Summary`), Realtime (SignalR `changed` event invalidation).
- Depends on bid-board data populated by Bid board tickets (trade carried/lowest values, zone shares) and on Project
  profile → Zones/Rates for zone and markup configuration (not part of this page).
- Feeds `02-edit-markup-tax-rate-inline.md`, `03-manage-custom-indirect-items.md`, `04-export-price-roll-up.md`, and
  `05-estimator-what-if-modal.md`, which all build on this view's loaded `RollUpData`.

### Notes / edge cases
- The trades list and the roll-up lines come from two independent queries (`bid-board` and `price-roll-up`); they
  can momentarily disagree if one finishes loading before the other updates after an edit elsewhere — the page does
  not block rendering on both being in sync.
- `zmax` is computed purely client-side from the currently loaded bid-board trades; it is not reactive to roll-up
  line amounts, only to trade values, so very small trades can look visually "hot" if `zmax` for that zone happens
  to be small.
- Any other open tab's edit (rate change, indirect add/edit/delete, fee carve-out change) triggers the SignalR
  `changed` event; `ProjectLayout`'s `onChange` handler invalidates every query whose key contains `"v{versionId}"`,
  which includes both `price-roll-up` and `bid-board` keys here — this page refetches automatically without a
  manual reload (see `_Overview/Architecture.md`: Realtime).
- "off" rate/cost lines (switched off on Project profile → Rates & indirects) still render as a row (amount counts
  as $0) rather than being hidden — do not filter them out.
- Indirect items outside the fee are still summed into "Indirects" in the build-up strip; "outside fee" only
  affects whether the fee markup is charged on that amount inside `Rules.Summary`, not whether it shows in the
  Indirects bucket on this page.

### Update — General conditions (Crew plan)
- [ ] The roll-up order is: Trades total → Project overheads → Direct cost total → Precon services → **General conditions** → Reserve markups → Subtotal → Contract markups → Fee → Indirect items → Contract total → Tax.
- [ ] **General conditions** (`kind: cost`, `code: genconds`) equals the Crew plan's quoted total for the project; its “open” link goes to the Crew plan. It can be switched off on Project profile → Rates & indirects like the other cost lines.
- [ ] Reserves can be based on direct cost plus Precon services and/or General conditions (`ReservePrecon`, `ReserveGenconds` on Fee carve-outs), and `genconds` can be carved out of the fee base (`FeeExcludes`).
- Source: `web/packages/calc/src/index.ts` (`gencondsTotal`) and `Calc/Rules.cs`, `Calc/Crew.cs` (`Crew.GencondsTotal`), `Calc/Recalc.cs`; both pass `web/packages/calc/test/vectors.json`.
