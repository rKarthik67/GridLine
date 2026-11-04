using System.Diagnostics;
using Dapper;
using Gridline.Import;
using Gridline.Infrastructure;

namespace Gridline.Endpoints;

/// <summary>Excel template import: upload → check (nothing is written except the batch record) → create in one transaction.</summary>
public static class ImportEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/template.xlsx", (IWebHostEnvironment env) =>
        {
            var path = Path.Combine(env.ContentRootPath, "Assets", "Gridline-Project-Import-Template.xlsx");
            if (!File.Exists(path)) path = Path.Combine(AppContext.BaseDirectory, "Assets", "Gridline-Project-Import-Template.xlsx");
            return Results.File(path, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Gridline-Project-Import-Template.xlsx");
        }).AllowAnonymous();

        api.MapPost("/imports/check", (HttpContext ctx, Storage st) => Check(ctx, st, null)).DisableAntiforgery();
        api.MapPost("/versions/{id:long}/imports/check", (long id, HttpContext ctx, Storage st) => Check(ctx, st, id)).DisableAntiforgery();

        api.MapPost("/imports/{token:guid}/create", async (Guid token, HttpContext ctx, Storage st) =>
        {
            ctx.Me().Require("import.run");
            var sw = Stopwatch.StartNew();
            await using var w = await Work.Begin(ctx);
            var b = await w.One<(long id, string mode, long? vid, string status, long? by)>(
                "SELECT BatchId, Mode, TargetVersionId, Status, StartedBy FROM ImportBatch WHERE PublicKey = @token FOR UPDATE", new { token });
            if (b.id == 0) throw ApiException.NotFound("Import");
            if (b.by != w.User.UserId) throw ApiException.Forbidden("Only the person who uploaded this workbook can create from it.");
            if (b.status == "created") throw new ApiException(409, "This workbook was already imported.", "duplicate");
            if (b.status != "checked") throw ApiException.Bad("Fix the problems in the workbook and upload it again.");
            ImportModel m;
            await using (var f = st.Open($"imports/{token}.xlsx")) m = Template.Read(f, tradesOnly: b.mode == "tradesintoversion");
            if (m.ErrorCount > 0) throw ApiException.Bad("The workbook has problems. Upload it again to see them.");

            ImportService.Result r;
            if (b.mode == "newproject") r = await ImportService.CreateProject(w, m);
            else
            {
                var s = await Access.Version(w, b.vid!.Value);
                r = await ImportService.IntoVersion(w, s.VersionId, m);
            }
            await w.Exec("UPDATE ImportBatch SET Status = 'created', TargetProjectId = @p, TargetVersionId = @v, FinishedAt = now() WHERE BatchId = @id",
                new { p = r.ProjectId, v = r.VersionId, id = b.id });
            await w.Log(r.ProjectId, r.VersionId, b.mode == "newproject" ? "import.create" : "import.trades", "import", b.id,
                after: new { r.TradesAdded, r.TradesUpdated, r.BidsAdded, r.PartnersAdded, ms = sw.ElapsedMilliseconds });
            await w.Commit();
            return Results.Ok(new { r.ProjectId, r.ProjectNo, r.VersionId, r.VersionNo, r.TradesAdded, r.TradesUpdated, r.BidsAdded, r.PartnersAdded, ms = sw.ElapsedMilliseconds });
        });

        api.MapGet("/imports/{token:guid}", async (Guid token, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            var b = await c.QueryFirstOrDefaultAsync("SELECT BatchId AS \"batchId\", FileName AS \"fileName\", Mode AS \"mode\", Status AS \"status\", RowCounts::text AS \"rowCounts\", StartedAt AS \"startedAt\", FinishedAt AS \"finishedAt\", TargetProjectId AS \"targetProjectId\" FROM ImportBatch WHERE PublicKey = @token AND StartedBy = @u",
                new { token, u = ctx.Me().UserId }) ?? throw ApiException.NotFound("Import");
            return b;
        });
    }

    static async Task<IResult> Check(HttpContext ctx, Storage st, long? versionId)
    {
        var me = ctx.Me(); me.Require("import.run");
        if (!ctx.Request.HasFormContentType) throw ApiException.Bad("Upload the workbook as a file.");
        var form = await ctx.Request.ReadFormAsync();
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault() ?? throw ApiException.Bad("Choose the filled template (.xlsx).");
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) throw ApiException.Bad("Only .xlsx workbooks can be imported. In Excel, use Save As → Excel Workbook.");

        await using var w = await Work.Begin(ctx);
        if (versionId is long vid) await Access.Version(w, vid);
        var token = Guid.NewGuid();
        var (rel, size) = await st.Save(file, "imports", $"{token}.xlsx");
        var sw = Stopwatch.StartNew();
        ImportModel m;
        await using (var f = st.Open(rel)) m = Template.Read(f, tradesOnly: versionId is not null);

        // database checks: project number free, state code, existing trades (for "into version")
        if (versionId is null)
        {
            if (m.P("Project number") is string no && await w.Scalar<bool>("SELECT EXISTS (SELECT 1 FROM Project WHERE ProjectNo = @no)", new { no }))
                m.Issues.Add(new("Project", null, "Project number", "error", $"Project number {no} is already used. Leave it blank to auto-number."));
            if (m.P("State") is string state && state.Length == 2 && !await w.Scalar<bool>("SELECT EXISTS (SELECT 1 FROM UsState WHERE StateCode = upper(@state))", new { state }))
                m.Issues.Add(new("Project", null, "State", "error", $"'{state}' is not a US state code."));
            foreach (var k in new[] { "Estimator", "Project manager" })
                if (m.P(k) is string who && !await w.Scalar<bool>("SELECT EXISTS (SELECT 1 FROM AppUser WHERE LoginEmail = @who::citext OR split_part(LoginEmail::text, '@', 1) = lower(split_part(@who, '@', 1)) OR lower(GivenName || ' ' || FamilyName) = lower(@who))", new { who }))
                    m.Issues.Add(new("Project", null, k, "warning", $"{who} is not a Gridline user; {k.ToLowerInvariant()} will be left empty."));
        }
        var kits = (await w.Query<string>("SELECT lower(KitName) FROM ScopeKit WHERE IsActive")).ToHashSet();
        foreach (var t in m.Trades.Where(t => t.Kit is not null && !kits.Contains(t.Kit.ToLowerInvariant())))
            m.Issues.Add(new("Trades", t.Row, "Scope kit", "warning", $"Scope kit '{t.Kit}' was not found; the trade starts without kit lines."));
        var existing = versionId is long v2
            ? (await w.Query<string>("SELECT CsiCode FROM Trade WHERE VersionId = @v2 AND ArchivedAt IS NULL", new { v2 })).ToHashSet() : [];
        var names = m.Bidders.Select(b => b.Partner.ToLowerInvariant()).Distinct().ToArray();
        var known = (await w.Query<string>("SELECT lower(CompanyName) FROM TradePartner WHERE lower(CompanyName) = ANY(@names)", new { names })).ToHashSet();

        var bidders = m.Bidders.GroupBy(b => b.Code).ToDictionary(g => g.Key, g => g.Count());
        var ok = m.ErrorCount == 0;
        var counts = m.Counts();
        var batchId = await w.Scalar<long>("""
            INSERT INTO ImportBatch (PublicKey, FileName, Mode, TargetVersionId, Status, RowCounts, StartedBy)
            VALUES (@token, @fn, @mode, @versionId, @status, @counts::jsonb, @u) RETURNING BatchId
            """, new { token, fn = file.FileName, mode = versionId is null ? "newproject" : "tradesintoversion", versionId, status = ok ? "checked" : "failed",
                       counts = Jsonb.Text(counts), u = me.UserId });
        foreach (var i in m.Issues.Take(2000))
            await w.Exec("INSERT INTO ImportIssue (BatchId, SheetName, RowNo, ColumnName, Severity, Message) VALUES (@batchId, @Sheet, @Row, @Column, @Severity, @Message)",
                new { batchId, i.Sheet, i.Row, i.Column, i.Severity, i.Message });
        await w.Commit();

        return Results.Ok(new
        {
            token, ok, fileName = file.FileName, bytes = size, ms = sw.ElapsedMilliseconds, counts,
            issues = m.Issues.OrderBy(i => i.Severity == "error" ? 0 : 1).Take(500)
                     .Select(i => new { sheet = i.Sheet, row = i.Row, column = i.Column, severity = i.Severity, message = i.Message }),
            errors = m.ErrorCount, warnings = m.Issues.Count - m.ErrorCount,
            project = new { name = m.P("Project name") ?? "(trades only)", number = m.P("Project number"), client = m.P("Client") },
            trades = m.Trades.Take(1000).Select(t => new { code = t.Code, title = t.Title, budget = t.Budget, bidders = bidders.GetValueOrDefault(t.Code), match = existing.Contains(t.Code) ? "Update" : "New" }),
            partners = new { matched = known.Count, created = names.Length - known.Count },
        });
    }
}
