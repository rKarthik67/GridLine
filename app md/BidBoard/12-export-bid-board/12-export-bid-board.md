## Title
Bid board — Export bid board to Excel or PDF

### Summary
The page header's export menu offers two downloads: "Bid board (Excel)" — a spreadsheet of every trade with its full bid lane-by-lane breakdown — and "Bid quotes (PDF)" — a printable, one-card-per-trade summary of all bids for sharing or filing.

### User story
As an estimator, I want to export the current bid board as an Excel workbook or a PDF of bid quotes so that I can share it outside Gridline or keep a point-in-time record.

### Acceptance criteria
- [ ] The export menu offers exactly "Bid board (Excel)" and "Bid quotes (PDF)", each a direct link with the current `versionId`.
- [ ] Downloading either requires capability `export.run`; the caller needs at least viewer access to the version.
- [ ] The Excel export is one row per (non-archived) trade: CSI, Trade, Budget, Lowest, Picked value, Δ budget, Plug, Adjustment, Picked bidder, Client note, Team note, then two columns ("Bidder X"/"Total X") per bid lane up to the widest trade's lane count; a carried bid's pair of cells is highlighted green and bold; a totals row sums Budget/Lowest/Picked value/Δ budget.
- [ ] The PDF export is one bordered card per trade (CSI/name, picked value vs. budget), with a table of every bid on the trade (lane letter, bidder name, status, base, adjustments, total, rank), the picked bid's row highlighted; a trade with no bids shows "No bids yet" (plus the plug amount if any) instead of a table.
- [ ] Both exports are named `{ProjectNo} v{VersionNo} bid board.xlsx` / `{ProjectNo} v{VersionNo} bid quotes.pdf` and download via the browser's normal file-save flow (filename read from `Content-Disposition`).
- [ ] Every export (regardless of kind) is recorded with its byte size and generation time, independent of success/failure of the download reaching the browser.

### Technical spec
**Frontend:** `web/src/pages/project/BidBoard.tsx`, `ExportMenu` component (`web/src/components/ui`) with `items={[['Bid board (Excel)', '/exports/bid-board.xlsx?versionId=${vid}'], ['Bid quotes (PDF)', '/exports/bid-quotes.pdf?versionId=${vid}']]}`. Uses `api.download()` (per Architecture.md: Frontend conventions) to fetch and save the file, reading the filename from the response's `Content-Disposition` header.
**Backend:** `GET /exports/{kind}` in `Export/ExportEndpoints.cs` (single generic endpoint for every export kind in the app, gated by `ExportEndpoints.Kinds` containing `"bid-board.xlsx"`/`"bid-quotes.pdf"`). `ctx.Me().Require("export.run")`. Resolves the version from `versionId` via `Access.Version(w, vid, "viewer")` (or from `tradeId` for trade-scoped exports, not used by this page), loads a `HeadRow` (project/version header info: `ProjectNo`, `ProjectName`, `ClientName`, `City`/`StateCode`, `VersionNo`, `VersionLabel`, `GrossArea`). Dispatches to `TradeSummaryXlsx(w, versionId, head)` or `TradeQuotesPdf(w, versionId, head)`, writes an `ExportRun` row, commits, and returns the bytes as `Results.File(...)` with the computed filename (sanitised of invalid filename characters).
`TradeSummaryXlsx`/`TradeQuotesPdf` both call `TradeEndpoints.LoadTrades(w.C, w.Tx, versionId)` — the exact same trade/bid loader used by the live bid board's `GET /versions/{id}/bid-board` — so the export always reflects the same `LowestValue`/`CarriedValue`/bid data currently on screen (not a separately computed snapshot). Excel is built with ClosedXML (`XLWorkbook`); PDF with QuestPDF.
**Database:** Read-only against `Trade`/`Bid`/`TradeZone`/`TradePartner`/`PartnerPerson` (same tables as "Browse, search, and filter the bid board") plus the `HeadRow` query over `ProjectVersion`/`Project`. Writes one row to `ExportRun` (migration 001): `ProjectId`, `VersionId`, `ExportKind`, `FileName`, `ByteSize`, `DurationMs`, `RequestedBy`, `RequestedAt` — an audit-only insert, not referenced by any other read path.

### Dependencies
Depends on every other Bid board ticket for the data it exports (it reads live `Trade`/`Bid` state, not a cached copy). Shares the `/exports/{kind}` endpoint and `ExportRun` logging pattern with every other page's exports (Price roll-up, Scope match, Crew plan, Costs). See Architecture.md: Import/Export, Authorization/scoping.

### Notes / edge cases
- The export reuses the live `LoadTrades` query, so it is only as fresh as the database at request time — if the user has unsaved optimistic UI state (e.g. mid-click on a pick that hasn't been confirmed by the server yet), the export won't reflect it; it always reflects the last *committed* state.
- The Excel totals row only sums Budget/Lowest/Picked value/Δ budget — it does not include a Contract total or $/SF figure the way the on-screen footer mini-bar does; those live only in the Price roll-up export.
- `ExportEndpoints.Kinds` is a single shared allow-list for every export across the whole app; a typo'd or unsupported `kind` string returns a 404 with the kind quoted back, not a generic 400.
- Because the filename is derived from `ProjectNo`/`VersionNo`/`VersionLabel` at export time, two exports of the same version taken minutes apart (e.g. before/after a bid is picked) produce identically-named files — there is no timestamp in the filename to distinguish them, unlike the `portfolio.xlsx` export which does include the export date.
