## Title
Price roll-up — Estimator what-if modal

### Summary
The "Estimator" button opens a sandbox modal (`EstimatorModal`, legacy name "Calculation test") seeded from the
current roll-up's real numbers, where the user can freely change trades/overheads/precon totals, rates, fixed
amounts, custom indirects, fee carve-outs, and a "shared savings" buyout/reserve model, and see every line's
estimate and delta against the current saved values recompute live — entirely client-side, nothing is saved.

### User story
As an estimator, I want to try different inputs (rates, totals, buyout assumptions, shared-savings terms) without
touching the saved project data, so that I can explore pricing scenarios and export them before committing to real changes.

### Acceptance criteria
- [ ] Opened from the Price roll-up page's "Estimator" button (`estOpen` state); renders a wide `Modal` titled
      "Estimator" with footer buttons "Reset to project", "Export to Excel (CSV)", and "Done"; closing via Done, the
      modal's × button, clicking the scrim, or Escape all discard every change with no save prompt.
- [ ] On open, every input is seeded from the loaded `RollUpData` (`seedFrom`): trades/genreq/precon totals as
      strings, each markup's rate (and fixed amount if one is set), each indirect item's amount/in-fee flag, the
      fee carve-out selections, and the reserve-precon toggles; "shared savings" starts off with buyout% at 0.
- [ ] The main table lists every estimator line (same line set the roll-up produces) with columns Item, Input,
      Current, Estimate, Delta:
      - "Trades total", "genreq" (Project overheads), and "precon" (Precon services) get a free-text numeric input.
      - Markup/tax lines get either a percentage input, or — if that code has a fixed-dollar override — a fixed
        amount input plus a "use %" button that clears the override and switches back to a percentage input.
      - Custom (indirect) lines get an amount input plus an "in fee" checkbox.
      - `calculated`/`trades` kind lines (subtotals/contract total) show no input, bold name, and roll up the
        others automatically.
      - "Current" always shows the real saved amount for that line name; "Delta" shows `+`/`−` formatted money, or
        a muted "–" when the rounded difference is zero.
- [ ] The sidebar has three sections:
      - **Fee carve-outs**: a master "Apply carve-outs" checkbox, then one "Leave out {part}" checkbox per fee part
        (Project overheads / Precon services / Reserves / P & P bond / Works cover), each disabled (and visually
        muted) unless the master checkbox is on.
      - **Reserve base**: one checkbox per reserve (Project / Design / Escalation) labeled "{Reserve}: + precon
        services" (adds precon services into that reserve's base), plus a "Leave project overheads out of direct
        cost" checkbox.
      - **Shared savings** ("was: Do you have Shared Savings?"): an "Anticipated buyout (% of trades)" percentage
        input; a "This contract shares savings with the client" checkbox that, when on, reveals three more
        percentage inputs — "Buyout savings back to client", "Expected spend of reserves", "Unspent reserves back
        to client" — and a definitions list showing Buyout savings, Reserves spent/unspent, Back to client, Fee,
        "Expected margin (fee + kept savings)", and Contract total (with sales tax label swapped in when a sales
        tax line exists), each with its delta against the current saved figure.
- [ ] "Reset to project" re-seeds every input back to the live `data` the modal was opened with (does not refetch —
      uses the same `data` prop, so it only undoes in-modal edits, not reverts to a server refresh).
- [ ] "Export to Excel (CSV)" downloads a browser-generated `estimator.csv` (UTF-8 BOM, CRLF, quoted fields) with
      columns Item, Input, Current, Estimate, Delta for every line, and — only when "shared savings" is on — an
      extra block with Buyout savings (%, amount), "Returned to client", and "Kept (buyout + unspent reserves)".
      This is a pure client-side Blob download, not a server export endpoint.
- [ ] Nothing in this modal calls a save/mutation API; no `canEdit`/locked-version gating applies to opening or
      using it — even a viewer or a locked version can open the Estimator and try numbers (it never writes anywhere).

### Technical spec
**Frontend:** `web/src/pages/project/Estimator.tsx`, exported `EstimatorModal({ data, onClose }: { data: RollUpData;
onClose(): void })`, rendered by `PriceRollUp.tsx` as `{estOpen && <EstimatorModal data={data} onClose={() =>
setEstOpen(false)} />}` — `data` is the same `RollUpData` already loaded by the parent's `GET
/versions/{vid}/price-roll-up` query (see `01-view-the-price-roll-up.md`); the modal makes **no API calls of its
own**.
- Local-only state `Inputs` (trades/genreq/precon/buyoutPct strings, `rates`/`fixed` maps per markup code, an
  `indirect` array mirroring custom lines, carve-out/reserve selections, shared-savings flags/percentages).
- `est = useMemo(() => calcSummary(toSummaryIn(data, x)), [data, x])` — recalculates the full line set on every
  keystroke using the shared `@gridline/calc` package (`calcSummary`, `SummaryIn` type), the exact same function the
  Price roll-up page itself uses for its live-draft preview.
- Shared-savings math is modal-local, not part of `@gridline/calc`: `buyout = trades × buyoutPct / 100`;
  `buyoutBack = shared ? buyout × buyoutBackPct / 100 : 0`; `spent = shared ? reserves × reserveSpendPct / 100 :
  reserves`; `unspent = reserves − spent`; `reserveBack = shared ? unspent × reserveBackPct / 100 : 0`;
  `kept = buyout − buyoutBack + unspent − reserveBack`; reserves total = sum of estimate lines whose `code` starts
  with `"reserve"`.
- CSV export (`exportCsv`) builds rows in-memory and triggers a download via `Blob`/`URL.createObjectURL`/a
  synthetic `<a download>` click — no network request.

**Backend:** none. This feature has no server endpoint, no request/response DTO, and no database read/write of its
own — it is a pure client-side recompute over data the parent page already fetched. (If the parent's
`price-roll-up` query is stale or fails, the modal would receive stale/missing `data` — see `01-view-the-price-roll-up.md`.)

**Database:** none touched directly by this feature. Indirectly depends on every table listed in
`01-view-the-price-roll-up.md`'s Database section, since its seed values come from that same `RollUpData` payload.

### Dependencies
- `01-view-the-price-roll-up.md` — the Estimator is only reachable from, and only seeded by, the already-loaded
  Price roll-up data; it does not independently fetch or revalidate.
- `02-edit-markup-tax-rate-inline.md` / `03-manage-custom-indirect-items.md` — the Estimator's "Current" column and
  initial seed reflect whatever those features last saved; the Estimator has no awareness of in-progress edits on
  the underlying page made after it was opened (it captures `data` once, at open time, via `useState(() =>
  seedFrom(data))`).
- `_Overview/Architecture.md`: Recalculation engine — `calcSummary` is the browser half of the same hybrid
  calculation described there (the C# twin is `Rules.Summary`); this ticket is the clearest example of that
  client-side engine running with zero server involvement.

### Notes / edge cases
- Because `data` is captured once via lazy `useState` initializer, if the underlying Price roll-up page refetches
  (e.g. another tab changed a rate) while the Estimator modal is still open, the modal keeps using its original
  snapshot — "Current" values in the modal can go stale relative to the page behind it until the modal is closed
  and reopened.
- The fixed-amount vs. percentage toggle per markup line is per-code local state (`x.fixed[code]`): switching "use
  %" clears the fixed value to `null` but does not restore the percentage the Estimator originally seeded from —
  it falls back to whatever is currently in `x.rates[code]`, which may still be the original project rate.
- "In fee" vs. "outside fee" is inverted between this modal's internal state (`inFee: boolean`) and the roll-up's
  `IndirectRow.outsideFee` field — `toSummaryIn` negates it (`outsideFee: !i.inFee`) when building the calc input;
  a developer touching this code must keep the inversion consistent in both directions (`seedFrom` and `toSummaryIn`).
- Reserve totals for the shared-savings math (`reserves`) are derived by filtering the **computed estimate lines**
  for `code?.startsWith('reserve')`, so any newly-added reserve-coded markup automatically participates without
  code changes here — but a markup whose code merely contains "reserve" as a substring elsewhere (not a prefix)
  would not match.
- The CSV export runs entirely in the browser and is not logged anywhere (no `ExportRun` row, unlike the real
  exports in `04-export-price-roll-up.md`) — it is explicitly a sandbox artifact, not an auditable export.
