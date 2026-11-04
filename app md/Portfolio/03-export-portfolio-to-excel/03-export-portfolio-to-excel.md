## Title
Portfolio — Export portfolio to Excel

### Summary
The "Export" button downloads a single-sheet `.xlsx` snapshot of every project the current user can see —
project number, stage, client, trades picked, and contract total — for offline sharing or reporting outside
Gridline.

### User story
As a manager, I want to export the whole portfolio to a spreadsheet, so that I can share a status snapshot with
people who don't have a Gridline login or build it into another report.

### Acceptance criteria
- [ ] Clicking "Export" (top-right action bar) calls the download helper; on success it shows a toast "Exported
      {actual downloaded file name}" (kind `down`); on failure it shows a toast with the error message (kind
      `alert`).
- [ ] The export requires the `export.run` capability; without it the request 403s and, because file downloads
      discard the server's JSON error body, the toast shows the generic text `Export failed (403)` rather than
      any specific message.
- [ ] The exported rows are **not** limited to the currently selected stage tab or search text — it always
      exports every non-archived project visible to the caller (same visibility rule as the on-screen list:
      admin, or `project.edit`/`project.view` capability, or an explicit `ProjectMember` row), regardless of what
      filters are applied on screen at the time.
- [ ] The workbook has one sheet, "Portfolio", titled "Portfolio" with a second line reading "Exported {date}
      {time}" (there is no project-specific header line for this export, unlike every other export kind).
- [ ] Column order/headers (row 4): `Project no., Project, Stage, Client, Type, City, State, Bid due, Version,
      Gross SF, Trades picked, Trades total, Contract total`. `Bid due` is formatted `d mmm yyyy`; `Gross SF` is
      `#,##0`; `Trades picked` is rendered as the string `"{tradesCarried}/{trades}"`; `Trades total` and
      `Contract total` use currency format `$#,##0;[Red]-$#,##0`. An Excel autofilter is applied over the header
      + data range; columns are auto-sized afterwards.
- [ ] Rows are ordered `BidDueOn NULLS LAST, ProjectNo` — **not** favourites-first like the on-screen list.
- [ ] The downloaded file name is `Gridline portfolio {yyyy-MM-dd}.xlsx` (today's date at generation time),
      sanitized of invalid filename characters; the browser-side fallback name if the response header is missing
      is `Portfolio.xlsx`.

### Technical spec
**Frontend:** `web/src/pages/Portfolio.tsx`, `async function exportList() { try { toast('Exported ' + await
api.download('/exports/portfolio.xlsx', 'Portfolio.xlsx'), 'down') } catch (e) { toast((e as Error).message,
'alert') } }`, wired to the "Export" button. `api.download` (`web/src/api/client.ts`) does a plain authenticated
`fetch` (not the JSON `request()` wrapper), reads the real file name out of the `Content-Disposition` header, and
triggers a browser download via an object-URL anchor click; on a non-2xx response it reads the body as text (not
JSON) and throws `ApiError(status, \`Export failed (${status})\`)` — the backend's structured `detail`/`title`
message is never surfaced for downloads.

**Backend:** `GET /api/exports/{kind}` with `kind = "portfolio.xlsx"` (one of 10 fixed values in
`ExportEndpoints.Kinds[]`; `Export/ExportEndpoints.cs`, `ExportEndpoints.Map`). `kind` must be one of that list or
the request 404s. `ctx.Me().Require("export.run")`. Because `kind == "portfolio.xlsx"`, the endpoint skips the
`tradeId`/`versionId` → `Access.Trade`/`Access.Version` scope resolution that every other export kind requires —
this is the only export with no version/trade scope check at all. Calls `Portfolio(Work w)`, which re-queries
`ProjectSummary` with the same admin/capability/`ProjectMember` visibility predicate as `GET /projects` and builds
the workbook with ClosedXML (`XLWorkbook`). The response is `Results.File(bytes,
"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Safe(name))`.

**Database:** Reads view `ProjectSummary` (see
`../01-browse-filter-and-open-projects/01-browse-filter-and-open-projects.md` for the tables/columns underlying
it, including the `004_names.sql` redefinition) and table `ProjectMember` for the visibility fallback. The
`Contract total` column read here is the same `RollupLine.LineName = 'Contract total'` (lower-case "total", per
`004_names.sql` and `Calc/Rules.cs`) value as the on-screen list — this export does not recompute it. Writes
exactly one row to `ExportRun` (`ExportId, ProjectId NULL, VersionId NULL, ExportKind = 'portfolio.xlsx', FileName,
ByteSize, DurationMs, RequestedBy, RequestedAt default now()`) inside the same `Work` transaction used to produce
the file. No `ActivityEvent` row is logged and no SignalR `changed` event fires for an export (`w.Log` is never
called here) — `w.Commit()` still runs (to persist the `ExportRun` insert) but there is nothing for it to
broadcast.

### Dependencies
- `../01-browse-filter-and-open-projects/01-browse-filter-and-open-projects.md` — same underlying visibility rule
  and `ProjectSummary` data.
- Architecture.md: Import/Export section (`Export/ExportEndpoints.cs`, `ExportRun` logging), Authorization/scoping
  (`export.run` capability).
- Out of scope / belongs elsewhere: the other 9 `kind` values on this same `GET /exports/{kind}` route
  (`bid-board.xlsx`, `bid-quotes.pdf`, `scope-match.xlsx`, `trade-package.pdf`, `price-roll-up.xlsx`,
  `price-roll-up.pdf`, `crew-plan.xlsx`, `precon-services.xlsx`, `project-overheads.xlsx`) are exports from inside
  a project (Bid board, Scope match, Price roll-up, Crew plan, Precon services, Project overheads pages), not from
  `/projects` — they belong in those pages' own ticket sets, not here.

### Notes / edge cases
- Export always reflects the full visible portfolio at generation time, even if the user has typed a search term
  or is sitting on a specific stage tab when they click "Export" — this is easy to assume is "export what I'm
  looking at" but it isn't.
- Unlike the on-screen list, favourite status has no bearing on the export's row order.
- Because `ExportRun.ProjectId`/`VersionId` are `NULL` for this export kind (there's no single project/version in
  scope), any future reporting built on `ExportRun` needs to handle portfolio exports as a distinct, project-less
  category rather than assuming every row has a project.
