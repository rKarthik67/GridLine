using System.Diagnostics;
using ClosedXML.Excel;
using Dapper;
using Gridline.Endpoints;
using Gridline.Infrastructure;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Gridline.Export;

/// <summary>Every export the pages offer. Each one is built from the saved data and recorded in ExportRun.</summary>
public static class ExportEndpoints
{
    const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    const string Money = "$#,##0;[Red]-$#,##0";
    const string Money2 = "$#,##0.00;[Red]-$#,##0.00";
    static readonly XLColor Brand = XLColor.FromHtml("#6A32D4"), Head = XLColor.FromHtml("#EFE9FB"), Band = XLColor.FromHtml("#FAF8FE");
    static readonly string BrandHex = "#6A32D4";

    public static readonly string[] Kinds =
    [
        "portfolio.xlsx", "bid-board.xlsx", "bid-quotes.pdf", "scope-match.xlsx", "trade-package.pdf",
        "price-roll-up.xlsx", "price-roll-up.pdf", "crew-plan.xlsx", "precon-services.xlsx", "project-overheads.xlsx",
    ];

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/exports/{kind}", async (string kind, long? versionId, long? tradeId, HttpContext ctx) =>
        {
            if (!Kinds.Contains(kind)) throw ApiException.NotFound($"Export '{kind}'");
            ctx.Me().Require("export.run");
            var sw = Stopwatch.StartNew();
            await using var w = await Work.Begin(ctx);
            Scope? s = null;
            if (kind != "portfolio.xlsx")
            {
                if (tradeId is long tid) { s = await Access.Trade(w, tid, "viewer"); versionId = s.VersionId; }
                else if (versionId is long vid) s = await Access.Version(w, vid, "viewer");
                else throw ApiException.Bad("versionId is required.");
                if (kind is "scope-match.xlsx" or "trade-package.pdf" && tradeId is null) throw ApiException.Bad("tradeId is required.");
            }
            var head = s is null ? null : await w.One<HeadRow>("""
                SELECT p.ProjectNo, p.ProjectName, p.ClientName, p.City, p.StateCode, v.VersionNo, coalesce(v.VersionLabel, 'Version ' || v.VersionNo) AS VersionLabel, v.GrossArea
                FROM ProjectVersion v JOIN Project p ON p.ProjectId = v.ProjectId WHERE v.VersionId = @vid
                """, new { vid = s.VersionId });
            byte[] bytes; string name;
            switch (kind)
            {
                case "portfolio.xlsx": (bytes, name) = (await Portfolio(w), $"Gridline portfolio {DateTime.Today:yyyy-MM-dd}.xlsx"); break;
                case "bid-board.xlsx": (bytes, name) = (await TradeSummaryXlsx(w, s!.VersionId, head!), $"{head!.ProjectNo} v{head.VersionNo} bid board.xlsx"); break;
                case "bid-quotes.pdf": (bytes, name) = (await TradeQuotesPdf(w, s!.VersionId, head!), $"{head!.ProjectNo} v{head.VersionNo} bid quotes.pdf"); break;
                case "scope-match.xlsx": (bytes, name) = await ScopeXlsx(w, tradeId!.Value, head!); break;
                case "trade-package.pdf": (bytes, name) = await TradePackagePdf(w, tradeId!.Value, head!); break;
                case "price-roll-up.xlsx": (bytes, name) = (await SummaryXlsx(w, s!.VersionId, head!), $"{head!.ProjectNo} v{head.VersionNo} price roll-up.xlsx"); break;
                case "price-roll-up.pdf": (bytes, name) = (await SummaryPdf(w, s!.VersionId, head!), $"{head!.ProjectNo} v{head.VersionNo} price roll-up.pdf"); break;
                case "crew-plan.xlsx": (bytes, name) = (await TeamXlsx(w, s!.ProjectId, head!), $"{head!.ProjectNo} crew plan.xlsx"); break;
                default:
                    var area = kind.StartsWith("precon") ? "precon" : "genreq";
                    (bytes, name) = (await CostsXlsx(w, s!.VersionId, area, head!), $"{head!.ProjectNo} v{head.VersionNo} {(area == "precon" ? "precon services" : "project overheads")}.xlsx");
                    break;
            }
            await w.Exec("INSERT INTO ExportRun (ProjectId, VersionId, ExportKind, FileName, ByteSize, DurationMs, RequestedBy) VALUES (@p, @v, @kind, @name, @size, @ms, @u)",
                new { p = s?.ProjectId, v = s?.VersionId, kind, name, size = bytes.LongLength, ms = (int)sw.ElapsedMilliseconds, u = w.User.UserId });
            await w.Commit();
            return Results.File(bytes, kind.EndsWith(".pdf") ? "application/pdf" : Xlsx, Safe(name));
        });
    }

    public sealed class HeadRow { public string ProjectNo { get; set; } = ""; public string ProjectName { get; set; } = ""; public string? ClientName { get; set; } public string? City { get; set; } public string? StateCode { get; set; } public int VersionNo { get; set; } public string VersionLabel { get; set; } = ""; public int GrossArea { get; set; } }
    static string Safe(string n) => string.Concat(n.Split(Path.GetInvalidFileNameChars()));
    static string Where(HeadRow h) => string.Join(", ", new[] { h.City, h.StateCode }.Where(x => !string.IsNullOrEmpty(x)));

    // ------------------------------------------------------------------ Excel helpers
    static IXLWorksheet Sheet(XLWorkbook wb, string name, string title, HeadRow? h, int cols)
    {
        var ws = wb.Worksheets.Add(name);
        ws.Cell(1, 1).Value = title; ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(15).Font.SetFontColor(Brand);
        ws.Cell(2, 1).Value = h is null ? $"Exported {DateTime.Now:d MMM yyyy HH:mm}" : $"{h.ProjectNo} · {h.ProjectName} · {h.VersionLabel}{(Where(h) is { Length: > 0 } w ? " · " + w : "")} · exported {DateTime.Now:d MMM yyyy HH:mm}";
        ws.Cell(2, 1).Style.Font.SetFontColor(XLColor.Gray);
        ws.Range(1, 1, 1, Math.Max(cols, 1)).Merge(); ws.Range(2, 1, 2, Math.Max(cols, 1)).Merge();
        return ws;
    }
    static void Header(IXLWorksheet ws, int row, params string[] titles)
    {
        for (var i = 0; i < titles.Length; i++) ws.Cell(row, i + 1).Value = titles[i];
        var r = ws.Range(row, 1, row, titles.Length);
        r.Style.Font.SetBold().Fill.SetBackgroundColor(Head).Border.SetBottomBorder(XLBorderStyleValues.Thin).Border.SetBottomBorderColor(Brand);
        ws.SheetView.FreezeRows(row);
    }
    static void Set(IXLCell c, object? v, string? fmt = null)
    {
        c.Value = v switch { null => Blank.Value, decimal d => d, int i => i, long l => l, double d => d, DateTime dt => dt, DateOnly d => d.ToDateTime(TimeOnly.MinValue), bool b => b ? "Yes" : "", _ => v.ToString() };
        if (fmt is not null) c.Style.NumberFormat.Format = fmt;
    }
    static byte[] Save(XLWorkbook wb) { foreach (var ws in wb.Worksheets) ws.Columns().AdjustToContents(8, 60); using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray(); }

    // ------------------------------------------------------------------ portfolio
    static async Task<byte[]> Portfolio(Work w)
    {
        var me = w.User;
        var rows = (await w.Query("""
            SELECT s.ProjectNo, s.ProjectName, s.Stage::text AS Stage, s.ClientName, s.ProjectType, s.City, s.StateCode, s.BidDueOn, s.PrimaryVersionNo,
                   s.GrossArea, s.Trades, s.TradesCarried, s.TradesTotal, s.ContractTotal
            FROM ProjectSummary s WHERE @all OR EXISTS (SELECT 1 FROM ProjectMember m WHERE m.ProjectId = s.ProjectId AND m.UserId = @u)
            ORDER BY s.BidDueOn NULLS LAST, s.ProjectNo
            """, new { all = me.IsAdmin || me.Can("project.edit") || me.Can("project.view"), u = me.UserId })).Cast<IDictionary<string, object?>>().ToList();
        using var wb = new XLWorkbook();
        var ws = Sheet(wb, "Portfolio", "Portfolio", null, 13);
        Header(ws, 4, "Project no.", "Project", "Stage", "Client", "Type", "City", "State", "Bid due", "Version", "Gross SF", "Trades picked", "Trades total", "Contract total");
        var r = 5;
        foreach (var p in rows)
        {
            Set(ws.Cell(r, 1), p["projectno"]); Set(ws.Cell(r, 2), p["projectname"]); Set(ws.Cell(r, 3), p["stage"]); Set(ws.Cell(r, 4), p["clientname"]);
            Set(ws.Cell(r, 5), p["projecttype"]); Set(ws.Cell(r, 6), p["city"]); Set(ws.Cell(r, 7), p["statecode"]); Set(ws.Cell(r, 8), p["biddueon"], "d mmm yyyy");
            Set(ws.Cell(r, 9), p["primaryversionno"]); Set(ws.Cell(r, 10), p["grossarea"], "#,##0"); Set(ws.Cell(r, 11), $"{p["tradescarried"]}/{p["trades"]}");
            Set(ws.Cell(r, 12), p["tradestotal"], Money); Set(ws.Cell(r, 13), p["contracttotal"], Money); r++;
        }
        ws.Range(4, 1, Math.Max(r - 1, 4), 13).SetAutoFilter();
        return Save(wb);
    }

    // ------------------------------------------------------------------ trade summary (bids side by side)
    static async Task<byte[]> TradeSummaryXlsx(Work w, long versionId, HeadRow h)
    {
        var trades = await TradeEndpoints.LoadTrades(w.C, w.Tx, versionId);
        var lanes = Math.Max(1, trades.Select(t => t.Bids.Count).DefaultIfEmpty(0).Max());
        using var wb = new XLWorkbook();
        var fixedCols = new[] { "CSI", "Trade", "Budget", "Lowest", "Picked value", "Δ budget", "Plug", "Adjustment", "Picked bidder", "Client note", "Team note" };
        var ws = Sheet(wb, "Bid board", "Bid board", h, fixedCols.Length + lanes * 2);
        var titles = fixedCols.ToList();
        for (var i = 0; i < lanes; i++) { titles.Add($"Bidder {(char)('A' + i % 26)}"); titles.Add($"Total {(char)('A' + i % 26)}"); }
        Header(ws, 4, titles.ToArray());
        var r = 5;
        foreach (var t in trades)
        {
            Set(ws.Cell(r, 1), t.CsiCode); Set(ws.Cell(r, 2), t.TradeName); Set(ws.Cell(r, 3), t.BudgetValue, Money); Set(ws.Cell(r, 4), t.LowestValue, Money);
            Set(ws.Cell(r, 5), t.CarriedValue, Money); Set(ws.Cell(r, 6), t.BudgetDelta, Money); Set(ws.Cell(r, 7), t.PlugAmount, Money); Set(ws.Cell(r, 8), t.Adjustment == 0 ? null : t.Adjustment, Money);
            Set(ws.Cell(r, 9), t.Bids.FirstOrDefault(b => b.IsCarried)?.BidderName ?? (t.PlugAmount is not null ? "Plug" : null));
            Set(ws.Cell(r, 10), t.ClientNote); Set(ws.Cell(r, 11), t.TeamNote);
            var col = fixedCols.Length + 1;
            foreach (var b in t.Bids)
            {
                Set(ws.Cell(r, col), $"{b.BidderName}{(b.State is "posted" ? "" : $" ({b.State})")}");
                Set(ws.Cell(r, col + 1), b.TotalAmount, Money);
                if (b.IsCarried) ws.Range(r, col, r, col + 1).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E6F6EC")).Font.SetBold();
                col += 2;
            }
            if (r % 2 == 0) ws.Range(r, 1, r, fixedCols.Length).Style.Fill.SetBackgroundColor(Band);
            r++;
        }
        ws.Cell(r, 2).Value = "Trades total"; ws.Cell(r, 2).Style.Font.SetBold();
        foreach (var c in new[] { 3, 4, 5, 6 })
        {
            ws.Cell(r, c).FormulaA1 = $"SUM({ws.Cell(5, c).Address}:{ws.Cell(Math.Max(5, r - 1), c).Address})";
            ws.Cell(r, c).Style.NumberFormat.Format = Money; ws.Cell(r, c).Style.Font.SetBold();
        }
        ws.Range(r, 1, r, fixedCols.Length).Style.Border.SetTopBorder(XLBorderStyleValues.Thin);
        ws.SheetView.FreezeColumns(2);
        return Save(wb);
    }

    static async Task<byte[]> TradeQuotesPdf(Work w, long versionId, HeadRow h)
    {
        var trades = await TradeEndpoints.LoadTrades(w.C, w.Tx, versionId);
        var total = trades.Sum(t => t.CarriedValue ?? t.LowestValue ?? 0);
        return Document.Create(doc => doc.Page(page =>
        {
            PageSetup(page, "Bid quotes", h);
            page.Content().PaddingTop(8).Column(col =>
            {
                col.Spacing(10);
                foreach (var t in trades)
                    col.Item().ShowEntire().Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                    {
                        c.Item().Row(r =>
                        {
                            r.RelativeItem().Text(x => { x.Span($"{t.CsiCode}  ").FontColor(Colors.Grey.Darken1); x.Span(t.TradeName).SemiBold(); });
                            r.ConstantItem(220).AlignRight().Text($"Picked {M(t.CarriedValue)} · Budget {M(t.BudgetValue)}").FontSize(8.5f);
                        });
                        if (t.Bids.Count == 0) { c.Item().PaddingTop(4).Text(t.PlugAmount is not null ? $"No bids yet · plug {M(t.PlugAmount)}" : "No bids yet").Italic().FontColor(Colors.Grey.Darken1); return; }
                        c.Item().PaddingTop(4).Table(tb =>
                        {
                            tb.ColumnsDefinition(d => { d.ConstantColumn(22); d.RelativeColumn(3); d.RelativeColumn(1.4f); d.RelativeColumn(1.2f); d.RelativeColumn(1.2f); d.RelativeColumn(1.3f); d.ConstantColumn(34); });
                            tb.Header(x => { foreach (var hd in new[] { "", "Trade partner", "Status", "Base", "Adjustments", "Total", "Rank" }) x.Cell().Element(HeadCell).Text(hd); });
                            foreach (var b in t.Bids)
                            {
                                var bg = b.IsCarried ? "#E6F6EC" : "#FFFFFF";
                                tb.Cell().Background(bg).Element(BodyCell).Text(((char)('A' + (b.LaneNo - 1) % 26)).ToString());
                                tb.Cell().Background(bg).Element(BodyCell).Text(b.BidderName + (b.IsCarried ? "  (picked)" : ""));
                                tb.Cell().Background(bg).Element(BodyCell).Text(b.State);
                                tb.Cell().Background(bg).Element(BodyCell).AlignRight().Text(M(b.BaseAmount));
                                tb.Cell().Background(bg).Element(BodyCell).AlignRight().Text(b.ScopeAdjustments == 0 ? "–" : M(b.ScopeAdjustments));
                                tb.Cell().Background(bg).Element(BodyCell).AlignRight().Text(M(b.TotalAmount)).SemiBold();
                                tb.Cell().Background(bg).Element(BodyCell).AlignCenter().Text(b.Rank?.ToString() ?? "–");
                            }
                        });
                        if (!string.IsNullOrEmpty(t.ClientNote)) c.Item().PaddingTop(3).Text($"Note: {t.ClientNote}").FontSize(8);
                    });
                col.Item().AlignRight().Text($"Trades total {M(total)}").SemiBold().FontSize(11);
            });
        })).GeneratePdf();
    }

    // ------------------------------------------------------------------ scope match
    static async Task<(byte[], string)> ScopeXlsx(Work w, long tradeId, HeadRow h)
    {
        var t = await TradeEndpoints.One(w.C, w.Tx, tradeId);
        var lines = (await w.Query<ScopeEndpoints.LineDto>("SELECT LineId, Section::text AS Section, SortKey, IsHeading, LineText, Desired::text AS Desired, SkipInAdjustments, Indent, LineNote, ZoneId, RequirementLineId, RowRev FROM ScopeLine WHERE TradeId = @tradeId AND ArchivedAt IS NULL ORDER BY CASE Section WHEN 'baserequirement' THEN 1 WHEN 'tradeitem' THEN 2 ELSE 3 END, SortKey", new { tradeId })).ToList();
        var answers = (await w.Query<ScopeEndpoints.AnswerDto>("SELECT a.LineId, a.BidId, a.Kind::text AS Kind, a.Amount, a.AnswerText, a.Formula, a.CellColour, a.Note, a.Source, a.RowRev, a.ChangedAt FROM ScopeAnswer a JOIN Bid b ON b.BidId = a.BidId WHERE b.TradeId = @tradeId", new { tradeId }))
                      .ToDictionary(a => (a.LineId, a.BidId));
        using var wb = new XLWorkbook();
        var ws = Sheet(wb, "Scope match", $"Scope match · {t.CsiCode} {t.TradeName}", h, 3 + t.Bids.Count);
        Header(ws, 4, new[] { "Section", "Scope line", "Requested" }.Concat(t.Bids.Select(b => $"{(char)('A' + (b.LaneNo - 1) % 26)} · {b.BidderName}")).ToArray());
        var r = 5;
        ws.Cell(r, 2).Value = "Base bid"; ws.Cell(r, 2).Style.Font.SetBold();
        for (var i = 0; i < t.Bids.Count; i++) Set(ws.Cell(r, 4 + i), t.Bids[i].BaseAmount, Money);
        r++;
        string Sec(string s) => s switch { "baserequirement" => "Base requirements", "tradeitem" => "Trade items", _ => "Zones" };
        foreach (var l in lines)
        {
            Set(ws.Cell(r, 1), Sec(l.Section)); Set(ws.Cell(r, 2), new string(' ', l.Indent * 3) + l.LineText); Set(ws.Cell(r, 3), l.Desired);
            if (l.IsHeading) ws.Range(r, 1, r, 3 + t.Bids.Count).Style.Font.SetBold().Fill.SetBackgroundColor(Band);
            else
                for (var i = 0; i < t.Bids.Count; i++)
                {
                    if (!answers.TryGetValue((l.LineId, t.Bids[i].BidId), out var a)) continue;
                    var c = ws.Cell(r, 4 + i);
                    switch (a.Kind)
                    {
                        case "amount": Set(c, a.Amount, Money); break;
                        case "included": c.Value = "Included"; break;
                        case "excluded": c.Value = "Excluded"; c.Style.Font.SetFontColor(XLColor.FromHtml("#B42318")); break;
                        case "price": c.Value = "Price"; break;
                        case "text": c.Value = a.AnswerText; break;
                    }
                    if (a.CellColour is string col) c.Style.Fill.SetBackgroundColor(XLColor.FromHtml(col == "red" ? "#FDE2E1" : col == "yellow" ? "#FEF3C7" : "#DCFCE7"));
                    if (!string.IsNullOrEmpty(a.Note)) c.CreateComment().AddText(a.Note);
                }
            if (!string.IsNullOrEmpty(l.LineNote)) ws.Cell(r, 2).CreateComment().AddText(l.LineNote);
            r++;
        }
        foreach (var (label, pick) in new (string, Func<TradeEndpoints.BidDto, decimal?>)[] { ("Scope adjustments", b => b.ScopeAdjustments), ("Total", b => b.TotalAmount) })
        {
            ws.Cell(r, 2).Value = label; ws.Cell(r, 2).Style.Font.SetBold();
            for (var i = 0; i < t.Bids.Count; i++) { Set(ws.Cell(r, 4 + i), pick(t.Bids[i]), Money); ws.Cell(r, 4 + i).Style.Font.SetBold(); }
            r++;
        }
        ws.SheetView.FreezeColumns(3);
        return (Save(wb), $"{h.ProjectNo} {t.CsiCode} {t.TradeName} scope match.xlsx");
    }

    /// <summary>What a sub needs to price the trade: the scope lines and what's requested, with room to answer.</summary>
    static async Task<(byte[], string)> TradePackagePdf(Work w, long tradeId, HeadRow h)
    {
        var t = await TradeEndpoints.One(w.C, w.Tx, tradeId);
        var lines = (await w.Query<ScopeEndpoints.LineDto>("SELECT LineId, Section::text AS Section, SortKey, IsHeading, LineText, Desired::text AS Desired, SkipInAdjustments, Indent, LineNote, ZoneId, RequirementLineId, RowRev FROM ScopeLine WHERE TradeId = @tradeId AND ArchivedAt IS NULL AND Section <> 'zone' ORDER BY CASE Section WHEN 'baserequirement' THEN 1 ELSE 2 END, SortKey", new { tradeId })).ToList();
        var zones = (await w.Query<(string name, decimal share)>("SELECT z.ZoneName, tz.Share FROM TradeZone tz JOIN Zone z ON z.ZoneId = tz.ZoneId WHERE tz.TradeId = @tradeId ORDER BY z.SortOrder", new { tradeId })).ToList();
        var bytes = Document.Create(doc => doc.Page(page =>
        {
            PageSetup(page, $"Trade package · {t.CsiCode} {t.TradeName}", h);
            page.Content().PaddingTop(8).Column(col =>
            {
                col.Spacing(6);
                col.Item().Text(x =>
                {
                    x.Span("Bid due: ").SemiBold(); x.Span(t.BidDueOn?.ToString("d MMM yyyy") ?? "see invitation");
                    if (zones.Count > 0) { x.Span("    Zones: ").SemiBold(); x.Span(string.Join(", ", zones.Select(z => $"{z.name} {z.share:P0}"))); }
                });
                foreach (var g in lines.GroupBy(l => l.Section))
                {
                    col.Item().PaddingTop(6).Text(g.Key == "baserequirement" ? "Base requirements" : "Trade items").FontSize(11).SemiBold().FontColor(BrandHex);
                    col.Item().Table(tb =>
                    {
                        tb.ColumnsDefinition(d => { d.ConstantColumn(26); d.RelativeColumn(6); d.RelativeColumn(1.4f); d.RelativeColumn(2); });
                        tb.Header(x => { foreach (var hd in new[] { "#", "Scope", "Requested", "Your answer" }) x.Cell().Element(HeadCell).Text(hd); });
                        var n = 0;
                        foreach (var l in g)
                        {
                            if (l.IsHeading) { tb.Cell().ColumnSpan(4).Background(Colors.Grey.Lighten4).Element(BodyCell).Text(l.LineText).SemiBold(); continue; }
                            tb.Cell().Element(BodyCell).Text((++n).ToString()).FontColor(Colors.Grey.Darken1);
                            tb.Cell().Element(BodyCell).PaddingLeft(l.Indent * 8).Text(l.LineText);
                            tb.Cell().Element(BodyCell).Text(l.Desired ?? "");
                            tb.Cell().Element(BodyCell).Text("");
                        }
                    });
                }
                col.Item().PaddingTop(10).Text("Base bid: ____________________      Company: ______________________________      Date: ____________").FontSize(9);
            });
        })).GeneratePdf();
        return (bytes, $"{h.ProjectNo} {t.CsiCode} {t.TradeName} trade package.pdf");
    }

    // ------------------------------------------------------------------ executive summary
    static async Task<(List<Calc.SummaryLine> lines, List<Recalc2.Z> zones, Dictionary<string, decimal?> budgets)> Summary(Work w, long versionId)
    {
        var (input, zones, budgets) = await Calc.Recalc.SummaryInput(w, versionId);
        var names = (await w.Query<Recalc2.Z>("SELECT ZoneCode AS Code, ZoneName AS Name, FloorArea AS Area FROM Zone WHERE VersionId = @versionId ORDER BY SortOrder", new { versionId })).ToList();
        return (Calc.Rules.Summary(input), names, budgets);
    }
    public static class Recalc2 { public sealed class Z { public string Code { get; set; } = ""; public string Name { get; set; } = ""; public int Area { get; set; } } }

    static async Task<byte[]> SummaryXlsx(Work w, long versionId, HeadRow h)
    {
        var (lines, zones, budgets) = await Summary(w, versionId);
        using var wb = new XLWorkbook();
        var cols = new List<string> { "Line", "Rate %", "Amount", "% of contract", "$/SF", "Budget", "Δ budget" };
        cols.AddRange(zones.Select(z => z.Name));
        var ws = Sheet(wb, "Price roll-up", "Price roll-up", h, cols.Count);
        Header(ws, 4, cols.ToArray());
        var r = 5;
        foreach (var l in lines)
        {
            var budget = l.Code is null ? null : budgets.GetValueOrDefault(l.Code);
            Set(ws.Cell(r, 1), l.Name); Set(ws.Cell(r, 2), l.RatePct, "0.000"); Set(ws.Cell(r, 3), l.Amount, Money); Set(ws.Cell(r, 4), l.PctOfContract / 100m, "0.00%");
            Set(ws.Cell(r, 5), l.PerArea, Money2); Set(ws.Cell(r, 6), budget, Money); Set(ws.Cell(r, 7), budget is decimal b ? l.Amount - b : null, Money);
            for (var i = 0; i < zones.Count; i++) Set(ws.Cell(r, 8 + i), l.Zones.GetValueOrDefault(zones[i].Code), Money);
            if (l.Kind == "calculated") ws.Range(r, 1, r, cols.Count).Style.Font.SetBold().Fill.SetBackgroundColor(Head).Border.SetTopBorder(XLBorderStyleValues.Thin);
            r++;
        }
        ws.Cell(r + 1, 1).Value = $"Gross area {h.GrossArea:N0} SF";
        ws.Cell(r + 1, 1).Style.Font.SetFontColor(XLColor.Gray);
        return Save(wb);
    }

    static async Task<byte[]> SummaryPdf(Work w, long versionId, HeadRow h)
    {
        var (lines, zones, _) = await Summary(w, versionId);
        var showZones = zones.Count is > 0 and <= 5;
        return Document.Create(doc => doc.Page(page =>
        {
            PageSetup(page, "Price roll-up", h, landscape: showZones);
            page.Content().PaddingTop(10).Column(col =>
            {
                col.Item().Table(tb =>
                {
                    tb.ColumnsDefinition(d =>
                    {
                        d.RelativeColumn(3.4f); d.RelativeColumn(0.9f); d.RelativeColumn(1.5f); d.RelativeColumn(1); d.RelativeColumn(1);
                        if (showZones) foreach (var _ in zones) d.RelativeColumn(1.3f);
                    });
                    var heads = new List<string> { "Line", "Rate", "Amount", "% contract", "$/SF" };
                    if (showZones) heads.AddRange(zones.Select(z => z.Name));
                    tb.Header(x => { foreach (var hd in heads) x.Cell().Element(HeadCell).Text(hd); });
                    foreach (var l in lines)
                    {
                        var calc = l.Kind == "calculated";
                        IContainer C(IContainer c) => calc ? BodyCell(c.Background("#EFE9FB")) : BodyCell(c);
                        B(tb.Cell().Element(C).Text(l.Name).FontSize(calc ? 9.5f : 9), calc);
                        tb.Cell().Element(C).AlignRight().Text(l.RatePct is decimal rp ? $"{rp:0.###}%" : "");
                        B(tb.Cell().Element(C).AlignRight().Text(M(l.Amount)), calc);
                        tb.Cell().Element(C).AlignRight().Text(l.PctOfContract is decimal pc ? $"{pc:0.00}%" : "");
                        tb.Cell().Element(C).AlignRight().Text(l.PerArea is decimal pa ? $"${pa:N2}" : "");
                        if (showZones) foreach (var z in zones) tb.Cell().Element(C).AlignRight().Text(M(l.Zones.GetValueOrDefault(z.Code)));
                    }
                });
                col.Item().PaddingTop(8).Text($"Gross area {h.GrossArea:N0} SF").FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        })).GeneratePdf();
    }

    // ------------------------------------------------------------------ team and costs
    static async Task<byte[]> TeamXlsx(Work w, long projectId, HeadRow h)
    {
        var (weeks, members, _) = await Calc.Crew.Load(w.C, w.Tx, projectId);
        using var wb = new XLWorkbook();
        string G(string g) => g switch { "siteops" => "Site operations", "corporate" => "Corporate resources", _ => "Operations" };
        // Team members (was: Project team → Team Members)
        var cols = new[] { "Team", "Team member", "Employee role", "Posting rate", "Contr weeks", "Closeout weeks", "Contr hrs", "Closeout hrs", "Total hrs", "Per diem", "Relocation",
                           "Act final rate", "% quoted", "Quoted rate", "Constr actual", "Closeout actual", "Total actual", "Constr quoted", "Closeout quoted", "Total quoted" };
        var ws = Sheet(wb, "Team members", "Crew plan · team members", h, cols.Length);
        Header(ws, 4, cols);
        var r = 5;
        foreach (var m in members)
        {
            object?[] v = [G(m.TeamGroup), m.StaffName ?? "TBA", m.RoleLabel, m.HourlyCost, m.ConstructionWeeks, m.CloseoutWeeks, m.ConstructionHours, m.CloseoutHours, m.TotalHours, m.PerDiemCost, m.RelocationCost,
                           m.FinalRate, m.QuotedPct, m.QuotedRate, m.ConstructionCost, m.CloseoutCost, m.TotalCost, m.ConstructionQuoted, m.CloseoutQuoted, m.TotalQuoted];
            for (var i = 0; i < v.Length; i++) Set(ws.Cell(r, i + 1), v[i], i is 3 or 11 or 13 ? Money2 : i >= 9 && i != 12 ? Money : "#,##0.#");
            r++;
        }
        ws.Cell(r, 2).Value = "Total";
        if (r > 5) for (var i = 6; i < cols.Length; i++) if (i is not (11 or 12 or 13)) ws.Cell(r, i + 1).FormulaA1 = $"SUM({ws.Cell(5, i + 1).Address}:{ws.Cell(r - 1, i + 1).Address})";
        ws.Range(r, 1, r, cols.Length).Style.Font.SetBold().Border.SetTopBorder(XLBorderStyleValues.Thin);
        ws.SheetView.FreezeColumns(3);
        // Resource loader (hours per week)
        var wl = Sheet(wb, "Resource loader", "Crew plan · resource loader (hours per week)", h, 3 + weeks.Count);
        Header(wl, 4, new[] { "Team", "Team member", "Role" }.Concat(weeks.Select(x => $"W{x.WeekNo} {x.StartsOn:d MMM yy}")).ToArray());
        for (var i = 0; i < weeks.Count; i++) { var c = wl.Cell(3, 4 + i); c.Value = weeks[i].Phase == "build" ? "Construction" : "Closeout"; c.Style.Font.SetFontColor(XLColor.Gray).Font.SetFontSize(8); }
        r = 5;
        foreach (var m in members)
        {
            Set(wl.Cell(r, 1), G(m.TeamGroup)); Set(wl.Cell(r, 2), m.StaffName ?? "TBA"); Set(wl.Cell(r, 3), m.RoleLabel);
            for (var i = 0; i < weeks.Count; i++) if (m.Hours.TryGetValue(weeks[i].WeekId, out var hrs)) Set(wl.Cell(r, 4 + i), hrs, "0.#");
            r++;
        }
        wl.SheetView.FreezeColumns(3);
        return Save(wb);
    }

    static async Task<byte[]> CostsXlsx(Work w, long versionId, string area, HeadRow h)
    {
        dynamic data = await TeamEndpoints.LoadCosts(w.C, w.Tx, versionId, area);
        using var wb = new XLWorkbook();
        var title = area == "precon" ? "Precon services" : "Project overheads";
        if (area == "precon")
        {
            dynamic team = await PreconEndpoints.Load(w.C, w.Tx, versionId);
            var tasks = ((IEnumerable<dynamic>)team.tasks).ToList();
            var tw = Sheet(wb, "Team members", "Precon services · team members", h, 6 + tasks.Count);
            Header(tw, 4, new[] { "Team member", "Employee role", "Act posting rate", "Quoted posting rate", "Total hours", "Labour" }.Concat(tasks.Select(t => (string)t.taskName)).ToArray());
            var tr = 5;
            foreach (var m in (List<PreconEndpoints.Row>)team.members)
            {
                Set(tw.Cell(tr, 1), m.StaffName ?? "TBA"); Set(tw.Cell(tr, 2), m.RoleLabel); Set(tw.Cell(tr, 3), m.PostingRate, Money2); Set(tw.Cell(tr, 4), m.QuotedRate, Money2);
                Set(tw.Cell(tr, 5), m.TotalHours, "#,##0.#"); Set(tw.Cell(tr, 6), m.Labour, Money);
                for (var i = 0; i < tasks.Count; i++) if (m.Hours.TryGetValue((int)tasks[i].activityId, out var hh)) Set(tw.Cell(tr, 7 + i), hh, "#,##0.#");
                tr++;
            }
            tw.Cell(tr, 1).Value = "Total"; tw.Range(tr, 1, tr, 6 + tasks.Count).Style.Font.SetBold().Border.SetTopBorder(XLBorderStyleValues.Thin);
            if (tr > 5) for (var col = 5; col <= 6 + tasks.Count; col++) tw.Cell(tr, col).FormulaA1 = $"SUM({tw.Cell(5, col).Address}:{tw.Cell(tr - 1, col).Address})";
        }
        var ws = Sheet(wb, "Expenses", title + " · expenses", h, 11);
        Header(ws, 4, "Category", "Item", "CSI code", "Billable", "Qty", "Unit", "Rate", "Subtotal", "Quoted rate", "Quoted subtotal", "Delta");
        var r = 5;
        foreach (var row in (IEnumerable<dynamic>)data.lines)
        {
            var l = (IDictionary<string, object?>)row;
            Set(ws.Cell(r, 1), l["categoryName"]); Set(ws.Cell(r, 2), l["itemName"]); Set(ws.Cell(r, 3), l["csiCode"]); Set(ws.Cell(r, 4), l["isBillable"]);
            Set(ws.Cell(r, 5), l["qty"], "#,##0.##"); Set(ws.Cell(r, 6), l["uomCode"]); Set(ws.Cell(r, 7), l["rate"], Money2); Set(ws.Cell(r, 8), l["actualTotal"], Money);
            Set(ws.Cell(r, 9), l["quotedRate"], Money2); Set(ws.Cell(r, 10), l["quotedTotal"], Money); Set(ws.Cell(r, 11), (decimal)l["quotedTotal"]! - (decimal)l["actualTotal"]!, Money); r++;
        }
        ws.Cell(r, 2).Value = "Total"; Set(ws.Cell(r, 8), (decimal)data.totals.expensesActual, Money);
        ws.Range(r, 1, r, 11).Style.Font.SetBold().Border.SetTopBorder(XLBorderStyleValues.Thin);
        r += 2;
        void Kv(string k, object v, string f) { ws.Cell(r, 2).Value = k; Set(ws.Cell(r, 8), v, f); r++; }
        if (area == "precon") Kv("Labour actual", (decimal)data.totals.labourActual, Money);
        Kv("Total actual", (decimal)data.totals.actual, Money); Kv("Quoted %", (decimal)data.quotedPct / 100m, "0.0%"); Kv("Total quoted", (decimal)data.totals.quoted, Money); Kv("Differential", (decimal)data.totals.difference, Money);
        ws.Range(4, 1, Math.Max(4, r - 1), 11).SetAutoFilter();
        return Save(wb);
    }

    // ------------------------------------------------------------------ PDF helpers
    static void B(TextBlockDescriptor t, bool bold) { if (bold) t.Bold(); }
    static string M(decimal? v) => v is null ? "–" : v < 0 ? $"-${-v:N0}" : $"${v:N0}";
    static IContainer HeadCell(IContainer c) => c.Background("#EFE9FB").BorderBottom(0.8f).BorderColor(BrandHex).PaddingVertical(3).PaddingHorizontal(4).DefaultTextStyle(x => x.SemiBold().FontSize(8));
    static IContainer BodyCell(IContainer c) => c.BorderBottom(0.4f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2.5f).PaddingHorizontal(4).DefaultTextStyle(x => x.FontSize(8.5f));
    static void PageSetup(PageDescriptor page, string title, HeadRow h, bool landscape = false)
    {
        page.Size(landscape ? PageSizes.Letter.Landscape() : PageSizes.Letter);
        page.Margin(32);
        page.DefaultTextStyle(x => x.FontSize(9).FontFamily(Fonts.Arial));
        page.Header().Column(c =>
        {
            c.Item().Row(r =>
            {
                r.RelativeItem().Text(title).FontSize(16).SemiBold().FontColor(BrandHex);
                r.ConstantItem(160).AlignRight().Text("Gridline").FontSize(12).SemiBold().FontColor("#FF7A2F");
            });
            c.Item().Text($"{h.ProjectNo} · {h.ProjectName} · {h.VersionLabel}{(Where(h) is { Length: > 0 } w ? " · " + w : "")}").FontColor(Colors.Grey.Darken1);
            c.Item().PaddingTop(4).LineHorizontal(1).LineColor(BrandHex);
        });
        page.Footer().Row(r =>
        {
            r.RelativeItem().Text($"Exported {DateTime.Now:d MMM yyyy HH:mm}").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
            r.RelativeItem().AlignRight().Text(x => { x.Span("Page ").FontSize(7.5f); x.CurrentPageNumber().FontSize(7.5f); x.Span(" of ").FontSize(7.5f); x.TotalPages().FontSize(7.5f); });
        });
    }
}
