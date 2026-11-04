## Title
Price roll-up — Export the roll-up (Excel / PDF)

### Summary
The page header's Export menu lets the user download the current price roll-up as an Excel workbook or a PDF, both
generated fresh from the server's saved roll-up data (not the browser's live draft), for sharing outside Gridline.

### User story
As an estimator, I want to export the price roll-up to Excel or PDF, so that I can share the build-up with people
who don't have Gridline access or attach it to a bid package.

### Acceptance criteria
- [ ] The page header shows an "Export" menu (`ExportMenu`) with two items: "Price roll-up (Excel)" and "Price
      roll-up (PDF)".
- [ ] Clicking an item downloads `GET /exports/price-roll-up.xlsx?versionId={vid}` or
      `GET /exports/price-roll-up.pdf?versionId={vid}` respectively, using the browser's `Authorization` header
      (not a plain link), and saves the file using the name from the response's `Content-Disposition` header
      (falling back to the menu label if absent).
- [ ] A success toast shows "Exported {filename}"; a failure toast shows the error message.
- [ ] The exported Excel workbook contains one row per roll-up line with columns: Line, Rate %, Amount, % of
      contract, $/SF, Budget, Δ budget, then one column per zone; `calculated`-kind rows (Direct cost total,
      Subtotal, Contract total, Contract total with sales tax) are bolded with a shaded header-color background and
      a top border; a footer caption shows "Gross area {N} SF".
- [ ] The exported PDF shows the same line list with columns Line, Rate, Amount, % contract, $/SF, and (only when
      the version has 1–5 zones) one column per zone; `calculated` rows are bold with a highlighted background;
      landscape orientation is used only when zone columns are shown; a footer caption shows "Gross area {N} SF".
- [ ] Both exports require the `export.run` capability; a user without it gets a 403 and the download fails with a
      toast showing the server's error message.
- [ ] Exporting while the version is locked still succeeds (this is a read action, not gated by `Access.Check`'s
      lock rule, only by `"viewer"` rank).

### Technical spec
**Frontend:** `web/src/pages/project/PriceRollUp.tsx` — `<ExportMenu items={[['Price roll-up (Excel)',
`/exports/price-roll-up.xlsx?versionId=${vid}`], ['Price roll-up (PDF)', `/exports/price-roll-up.pdf?versionId=${vid}`]]} />`
(component defined in `web/src/components/ui.tsx`). `ExportMenu` calls `api.download(url, label)`
(`web/src/api/client.ts`) — a `fetch` with the auth/tab headers (not `api.get`, since the response is a binary
file), reads the filename from `Content-Disposition` via regex, creates a Blob URL and an auto-clicked `<a download>`.

**Backend:** `GET /exports/{kind}` → `Export.ExportEndpoints.Map` (`api/src/Gridline.Api/Export/ExportEndpoints.cs`),
`kind` restricted to the `Kinds` allowlist (includes `"price-roll-up.xlsx"`, `"price-roll-up.pdf"`).
- Auth: `ctx.Me().Require("export.run")` (capability check, throws if missing — see `_Overview/Architecture.md`: Auth).
- Scope: since `kind != "portfolio.xlsx"` and no `tradeId` is passed, resolves `Access.Version(w, versionId,
  "viewer")` — any project viewer can export, including on a locked version.
- Loads a shared `HeadRow` (project/version header info: `ProjectNo`, `ProjectName`, `ClientName`, `City`,
  `StateCode`, `VersionNo`, `VersionLabel`, `GrossArea`) via one query joining `ProjectVersion`/`Project`.
- `"price-roll-up.xlsx"` → `SummaryXlsx(w, versionId, head)` (ClosedXML workbook); `"price-roll-up.pdf"` →
  `SummaryPdf(w, versionId, head)` (QuestPDF document) — both call a private `Summary(w, versionId)` helper that
  re-derives `(lines, zones, budgets)` the same way `SummaryEndpoints.Build` does (via `Recalc.SummaryInput` +
  `Rules.Summary`), i.e. the export reflects the version's current saved state at request time, independent of any
  unsaved draft in the browser.
- After generating bytes, inserts one `ExportRun` row and commits; response is `Results.File(bytes, contentType,
  Safe(name))` with filename `"{ProjectNo} v{VersionNo} price roll-up.xlsx"` / `.pdf`.

**Database:**
- Reads: same tables as `01-view-the-price-roll-up.md` (`Trade`, `CostLine`, `VersionMarkup`/`MarkupType`,
  `IndirectItem`, `Zone`, `TradeZone`, `ProjectVersion`) via `Recalc.SummaryInput`/`Rules.Summary`.
- Writes: one row to `ExportRun` (`ProjectId`, `VersionId`, `ExportKind`, `FileName`, `ByteSize`, `DurationMs`,
  `RequestedBy`) per download, used to audit every export a user generates; no roll-up tables are mutated (export
  does not call `Recalc.Summary`, so it does not refresh `RollupLine`/`RollupZone`/`CalcRun` — it recomputes the
  lines in memory for formatting only).

### Dependencies
- `01-view-the-price-roll-up.md` — exports the same computed line set that page displays.
- `_Overview/Architecture.md`: Import/Export (`ExportRun` logging convention), Authorization/scoping
  (capability vs scope checks), Recalculation engine (shared `Recalc.SummaryInput`/`Rules.Summary`).

### Notes / edge cases
- The export always reflects the last **saved** server state, not any in-progress rate edit draft in the browser —
  if a user is mid-edit (draft recalculating) and exports immediately, the file will not include that unsaved change.
- Exporting does not call `Recalc.Summary`, so it is safe to run frequently without generating extra `CalcRun`/
  `RollupLine` churn, but it also means the export can momentarily disagree with `RollupLine`'s last-persisted
  values if a recalculation trigger (e.g. a bid change elsewhere) hasn't run yet — the export always recomputes
  fresh rather than reading the stored `RollupLine` table.
- The PDF's zone columns are suppressed entirely once a version has more than 5 zones (`zones.Count is > 0 and <=
  5`); for larger projects the PDF silently drops to the build-up-only columns while the Excel export always
  includes every zone column.
- `export.run` is a capability (checked via `ctx.Me().Require`), independent of the per-project `Access` scope rank
  — a user could have the capability but still lack project access (and get a different 403 from `Access.Version`),
  or vice versa.
