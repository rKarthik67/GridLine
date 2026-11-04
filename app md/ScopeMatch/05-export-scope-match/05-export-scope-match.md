## Title
Scope match — Export scope match sheet and trade package

### Summary
Lets the user download the current trade's scope comparison as a formatted Excel workbook (for internal use/archiving) or as a blank PDF "trade package" (to send a subcontractor so they can price against the same scope list). Both are generated on demand from the live database, not cached.

### User story
As an estimator, I want to export the scope match grid to Excel and generate a blank scope PDF for a trade, so that I can archive the leveled comparison and hand bidders a consistent scope list to price against.

### Acceptance criteria
- [ ] The page header's "Export" menu (`ExportMenu`) offers exactly two items for this tab: "Scope match sheet (Excel)" and "Trade package (PDF)", both scoped to the currently selected trade (`tradeId` query param baked into the URL).
- [ ] Clicking either downloads the file (`api.download`), reading the suggested filename from the response's `Content-Disposition` header, and shows a toast "Exported {filename}" on success or the error message on failure.
- [ ] The Excel export ("Scope match sheet") contains: a title row ("Scope match · {csiCode} {tradeName}"), a project/version/export-time subtitle row, one header row per visible bid lane (lane letter + bidder name) plus Section/Scope line/Requested columns, a "Base bid" row with each bid's base amount, one row per scope line (grouped by section order: base requirements, trade items, zones) showing the line's indent-prefixed text and `desired` value, each bid's answer rendered as a dollar amount / "Included" / "Excluded" (red text) / "Price" / free text, the cell's colour reproduced as a cell fill (red/yellow/green pastel), any cell note or line note attached as an Excel cell comment, and trailing "Scope adjustments" and bold "Total" rows per bid. Heading rows are bold with a shaded fill. The first 3 columns are frozen.
- [ ] The PDF export ("Trade package") contains: a page header with project/version info, the bid-due date (or "see invitation" if unset) and, if the trade has any zones, the zone names with their share percentages, then one table per non-zone section (Base requirements, Trade items) with columns `#`, Scope, Requested, and a blank "Your answer" column for the recipient to fill in by hand, followed by a blank "Base bid / Company / Date" signature line. It deliberately omits bidder-specific answers (it's meant to be sent out, not a leveled comparison).
- [ ] Both exports require the `export.run` capability; without it the request is rejected before any data is read.
- [ ] Every export call, success or failure in content but not in auth/validation, is recorded as one `ExportRun` row (kind, file name, byte size, duration in ms, who requested it) once the file is generated.
- [ ] Requesting `scope-match.xlsx` or `trade-package.pdf` without a `tradeId` query parameter is rejected with 400 ("tradeId is required.").

### Technical spec
**Frontend:** `web/src/pages/project/ScopeMatch.tsx` — the header's `ExportMenu` (from `web/src/components/ui.tsx`) with `items={[['Scope match sheet (Excel)', '/exports/scope-match.xlsx?tradeId={tradeId}'], ['Trade package (PDF)', '/exports/trade-package.pdf?tradeId={tradeId}']]}`. `ExportMenu` calls `api.download(url, label)` per item on click.
**Backend:** `GET /exports/{kind}` (`Export/ExportEndpoints.cs`, `ExportEndpoints.Map`) — single route handling all export kinds in `ExportEndpoints.Kinds` (`"scope-match.xlsx"`, `"trade-package.pdf"`, and others for other pages); requires capability `export.run` (`ctx.Me().Require`); resolves `Scope` via `Access.Trade(w, tradeId, "viewer")` when `tradeId` is supplied (viewer access is enough to export); dispatches to `ScopeXlsx(w, tradeId, head)` (ClosedXML `XLWorkbook`) or `TradePackagePdf(w, tradeId, head)` (QuestPDF `Document`); both read the trade via `TradeEndpoints.One` plus `ScopeLine`/`ScopeAnswer` (and, for the PDF, `TradeZone` joined to `Zone`) directly with Dapper, independent of the `ScopeEndpoints.Load` used by the live page; writes one `ExportRun` row and commits via `Work`; returns `Results.File(bytes, contentType, safeFileName)`.
**Database:** Reads only — `Trade`, `Bid` (via `TradeEndpoints.LoadTrades`/`One`), `ScopeLine`, `ScopeAnswer`, `TradeZone`/`Zone` (PDF only). Writes one row to `ExportRun` (`ProjectId`, `VersionId`, `ExportKind`, `FileName`, `ByteSize`, `DurationMs`, `RequestedBy`) per export — no triggers fire on this table relevant to this ticket.

### Dependencies
- Reads the same `ScopeLine`/`ScopeAnswer`/`Bid` data as tickets 01–04; any row-structure or answer change is immediately reflected in the next export (no caching).
- Shares the `ExportMenu` component and `ExportRun` logging pattern with every other page's exports (Bid board, Price roll-up, Crew plan, Precon services/Overheads) — see Architecture.md: Import/Export.
- See `_Overview/Architecture.md`: Authorization/scoping (`Access.Trade` viewer-level is sufficient here, unlike most of this page's other mutations), Import/Export, Unit of work.

### Notes / edge cases
- Export access is **viewer**-level (`Access.Trade(w, tradeId, "viewer")`), deliberately looser than the editor-level checks everywhere else on this page — a read-only user can still export, as long as they hold the `export.run` capability.
- The Excel export always includes every bid lane regardless of `HiddenInScope` or archived state filtering done client-side for the live grid — it is not guaranteed to visually match what the current browser tab is showing (e.g. a hidden lane still appears in the spreadsheet).
- The PDF "trade package" never includes any bidder's actual answers (it's blank by design, for sending out) — do not confuse it with the Excel export, which is the leveled/filled-in comparison.
- `ExportEndpoints.Kinds` is a single shared whitelist for the whole app; adding a new export kind anywhere means updating this one array and the `switch` in `Map`, not just a scope-match-local file.
- The exported filename pattern is `"{ProjectNo} {CsiCode} {TradeName} scope match.xlsx"` / `"... trade package.pdf"` with invalid filename characters stripped (`Safe()`); don't assume it matches the on-screen trade label formatting exactly.
