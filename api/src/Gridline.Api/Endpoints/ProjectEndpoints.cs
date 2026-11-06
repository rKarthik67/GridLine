using Dapper;
using Gridline.Calc;
using Gridline.Infrastructure;
using Gridline.Live;

namespace Gridline.Endpoints;

public static class ProjectEndpoints
{
    public sealed class ProjectIn
    {
        public string? ProjectName { get; set; }
        public string? ProjectNo { get; set; }
        public string? Stage { get; set; }
        public string? ClientName { get; set; }
        public string? ProjectType { get; set; }
        public string? ContractType { get; set; }
        public int? DivisionId { get; set; }
        public int? BranchId { get; set; }
        public string? Street { get; set; }
        public string? City { get; set; }
        public string? StateCode { get; set; }
        public string? CountyName { get; set; }
        public string? PostalCode { get; set; }
        public DateOnly? BidDueOn { get; set; }
        public string? CrmRef { get; set; }
        public long? EstimatorId { get; set; }
        public long? ManagerId { get; set; }
        public int? GrossArea { get; set; }
        public int? RowRev { get; set; }
    }
    public sealed class VersionRow
    {
        public long VersionId { get; set; } public int VersionNo { get; set; } public string? VersionLabel { get; set; } public bool IsPrimary { get; set; }
        public string Status { get; set; } = ""; public int GrossArea { get; set; } public decimal? BuildMonths { get; set; } public decimal FeePct { get; set; }
        public string? BaselineName { get; set; } public long? CopiedFromVersionId { get; set; } public DateTime CreatedAt { get; set; } public int RowRev { get; set; }
        public bool ZonesSetArea { get; set; } public string PrimaryAreaUnit { get; set; } = "SF"; public string PrimaryUnitLabel { get; set; } = "N/A"; public decimal TotalUnits { get; set; }
    }
    public const string VersionCols = "VersionId, VersionNo, VersionLabel, IsPrimary, Status, GrossArea, BuildMonths, FeePct, BaselineName, CopiedFromVersionId, CreatedAt, RowRev, ZonesSetArea, PrimaryAreaUnit, PrimaryUnitLabel, TotalUnits";
    public static readonly string[] AreaUnits = ["SF", "RSF", "GSF", "USF"];
    public static readonly string[] UnitLabels = ["N/A", "Unit", "MW", "Key", "Space"];

    /// <summary>Gross area = the zones' SF (minus zones marked "exclude SF") when "zones equal total project area" is on;
    /// total units = the units of zones that use the primary unit of quantity.</summary>
    public static Task SyncZoneTotals(Work w, long versionId) => w.Exec("""
        UPDATE ProjectVersion v SET
          GrossArea = CASE WHEN v.ZonesSetArea AND EXISTS (SELECT 1 FROM Zone z WHERE z.VersionId = v.VersionId)
                           THEN (SELECT coalesce(sum(z.FloorArea) FILTER (WHERE NOT z.ExcludeFromTotal), 0) FROM Zone z WHERE z.VersionId = v.VersionId) ELSE v.GrossArea END,
          TotalUnits = (SELECT coalesce(sum(z.UnitCount), 0) FROM Zone z WHERE z.VersionId = v.VersionId AND z.UnitLabel = v.PrimaryUnitLabel AND v.PrimaryUnitLabel <> 'N/A')
        WHERE v.VersionId = @versionId
        """, new { versionId });

    public static void Map(RouteGroupBuilder api)
    {
        // ------------------------------------------------------------------ portfolio
        api.MapGet("/projects", async (HttpContext ctx, Db db, string? search, string? stage) =>
        {
            var me = ctx.Me();
            await using var c = await db.Open();
            return await c.QueryAsync("""
                SELECT s.ProjectId AS "projectId", s.ProjectNo AS "projectNo", s.ProjectName AS "projectName", s.Stage::text AS "stage",
                       s.ClientName AS "clientName", s.ProjectType AS "projectType", s.City AS "city", s.StateCode AS "stateCode", s.BidDueOn AS "bidDueOn",
                       s.PrimaryVersionNo AS "primaryVersionNo", coalesce(s.GrossArea, 0) AS "grossArea", s.Trades AS "trades", s.TradesCarried AS "tradesCarried",
                       s.TradesTotal AS "tradesTotal", s.ContractTotal AS "contractTotal",
                       (SELECT u.GivenName || ' ' || u.FamilyName FROM Project p JOIN AppUser u ON u.UserId = p.EstimatorId WHERE p.ProjectId = s.ProjectId) AS "estimator",
                       coalesce(r.IsFavourite, false) AS "isFavourite", r.OpenedAt AS "openedAt",
                       (SELECT count(DISTINCT w.UserId) FROM WhoIsHere w WHERE w.ProjectId = s.ProjectId) AS "peopleHere"
                FROM ProjectSummary s LEFT JOIN RecentProject r ON r.ProjectId = s.ProjectId AND r.UserId = @uid
                WHERE s.CompanyId = @cid
                  AND (@admin OR @edit OR EXISTS (SELECT 1 FROM ProjectMember m WHERE m.ProjectId = s.ProjectId AND m.UserId = @uid) OR @view)
                  AND (@stage IS NULL OR s.Stage::text = @stage)
                  AND (@q IS NULL OR s.ProjectName ILIKE '%' || @q || '%' OR s.ProjectNo ILIKE '%' || @q || '%' OR s.ClientName ILIKE '%' || @q || '%' OR s.City ILIKE '%' || @q || '%')
                ORDER BY coalesce(r.IsFavourite, false) DESC, r.OpenedAt DESC NULLS LAST, s.BidDueOn NULLS LAST, s.ProjectNo
                """, new { uid = me.UserId, cid = me.CompanyId, admin = me.IsAdmin, edit = me.Can("project.edit"), view = me.Can("project.view"),
                           stage = string.IsNullOrEmpty(stage) || stage == "all" ? null : stage, q = string.IsNullOrWhiteSpace(search) ? null : search.Trim() });
        });

        api.MapPut("/projects/{id:long}/favourite", async (long id, FavIn body, HttpContext ctx, Db db) =>
        {
            var me = ctx.Me();
            await using var c = await db.Open();
            await Access.Project(c, null, me, id, "viewer");
            await c.ExecuteAsync("""
                INSERT INTO RecentProject (UserId, ProjectId, IsFavourite) VALUES (@u, @p, @f)
                ON CONFLICT (UserId, ProjectId) DO UPDATE SET IsFavourite = EXCLUDED.IsFavourite
                """, new { u = me.UserId, p = id, f = body.IsFavourite });
            return Results.NoContent();
        });

        // ------------------------------------------------------------------ one project (by number, as in the page address)
        api.MapGet("/projects/{no}", async (string no, HttpContext ctx, Db db) =>
        {
            var me = ctx.Me();
            await using var c = await db.Open();
            var id = await c.ExecuteScalarAsync<long?>("SELECT ProjectId FROM Project WHERE (ProjectNo = @no OR ProjectId::text = @no) AND ArchivedAt IS NULL AND CompanyId = @cid", new { no, cid = me.CompanyId })
                     ?? throw ApiException.NotFound($"Project {no}");
            var s = await Access.Project(c, null, me, id, "viewer");
            return await Detail(c, id, s.Access);
        });

        api.MapPost("/projects", async (ProjectIn body, HttpContext ctx) =>
        {
            ctx.Me().Require("project.edit");
            if (string.IsNullOrWhiteSpace(body.ProjectName)) throw ApiException.Bad("Project name is required.");
            await using var w = await Work.Begin(ctx);
            var (pid, vid, no) = await CreateProject(w, body);
            await ApplyDefaultCostKits(w, vid);
            await Recalc.Summary(w, vid, "project.create");
            await w.Log(pid, vid, "project.create", "project", pid, after: new { body.ProjectName });
            await w.Commit();
            return Results.Created($"/api/projects/{no}", new { projectId = pid, projectNo = no, versionNo = 1 });
        });

        api.MapPatch("/projects/{id:long}", async (long id, ProjectIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            await Access.Project(w, id, "editor");
            var cur = await w.One<dynamic>("SELECT RowRev, (SELECT GivenName || ' ' || FamilyName FROM AppUser WHERE UserId = p.ChangedBy) AS who FROM Project p WHERE ProjectId = @id", new { id });
            if (body.RowRev is int rr && rr != (int)cur!.rowrev) throw ApiException.Clash((string?)cur.who ?? "Someone");
            if (body.ProjectName is not null && string.IsNullOrWhiteSpace(body.ProjectName)) throw ApiException.Bad("Project name can't be empty.");
            await w.Exec("""
                UPDATE Project SET
                  ProjectName = coalesce(@ProjectName, ProjectName), Stage = coalesce(@Stage::ProjectStage, Stage), ClientName = coalesce(@ClientName, ClientName),
                  ProjectType = coalesce(@ProjectType, ProjectType), ContractType = coalesce(@ContractType, ContractType),
                  DivisionId = coalesce(@DivisionId, DivisionId), BranchId = coalesce(@BranchId, BranchId), Street = coalesce(@Street, Street),
                  City = coalesce(@City, City), StateCode = coalesce(@StateCode, StateCode), CountyName = coalesce(@CountyName, CountyName),
                  PostalCode = coalesce(@PostalCode, PostalCode), BidDueOn = coalesce(@BidDueOn, BidDueOn), CrmRef = coalesce(@CrmRef, CrmRef),
                  EstimatorId = coalesce(@EstimatorId, EstimatorId), ManagerId = coalesce(@ManagerId, ManagerId), ChangedBy = @uid
                WHERE ProjectId = @id
                """, new { body.ProjectName, body.Stage, body.ClientName, body.ProjectType, body.ContractType, body.DivisionId, body.BranchId, body.Street, body.City,
                           StateCode = body.StateCode?.ToUpperInvariant(), body.CountyName, body.PostalCode, body.BidDueOn, body.CrmRef, body.EstimatorId, body.ManagerId, uid = w.User.UserId, id });
            await w.Log(id, null, "project.update", "project", id, after: body);
            await w.Commit();
            await using var c = await ctx.RequestServices.GetRequiredService<Db>().Open();
            return await Detail(c, id, "editor");
        });

        api.MapDelete("/projects/{id:long}", async (long id, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            await Access.Project(w, id, "owner");
            await w.Exec("UPDATE Project SET ArchivedAt = now(), ChangedBy = @u WHERE ProjectId = @id", new { id, u = w.User.UserId });
            await w.Log(id, null, "project.archive", "project", id);
            await w.Commit();
            return Results.NoContent();
        });

        api.MapPost("/projects/{id:long}/copy", async (long id, CopyProjectIn body, HttpContext ctx) =>
        {
            ctx.Me().Require("project.edit");
            await using var w = await Work.Begin(ctx);
            var src = await Access.Version(w, body.VersionId, "viewer");
            if (src.ProjectId != id) throw ApiException.Bad("That version belongs to another project.");
            var p = await w.One<ProjectIn>("SELECT ProjectName, ClientName, ProjectType, ContractType, DivisionId, BranchId, Street, City, StateCode, CountyName, PostalCode, BidDueOn FROM Project WHERE ProjectId = @id", new { id });
            p!.ProjectName = string.IsNullOrWhiteSpace(body.ProjectName) ? p.ProjectName + " (copy)" : body.ProjectName;
            p.Stage = "lead";
            var (pid, vid, no) = await CreateProject(w, p, withMarkups: false);
            await Versions.CopyContent(w, body.VersionId, vid);
            await Recalc.Version(w, vid, "project.copy");
            await w.Log(pid, vid, "project.copy", "project", pid, after: new { fromProject = id, fromVersion = body.VersionId });
            await w.Commit();
            return Results.Created($"/api/projects/{no}", new { projectId = pid, projectNo = no, versionNo = 1 });
        });

        // ------------------------------------------------------------------ versions
        api.MapGet("/projects/{id:long}/versions", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            await Access.Project(c, null, ctx.Me(), id, "viewer");
            return await c.QueryAsync<VersionRow>($"SELECT {VersionCols} FROM ProjectVersion WHERE ProjectId = @id ORDER BY VersionNo", new { id });
        });

        api.MapPost("/projects/{id:long}/versions", async (long id, NewVersionIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            await Access.Project(w, id, "editor");
            var from = body.FromVersionId ?? await w.Scalar<long>("SELECT VersionId FROM ProjectVersion WHERE ProjectId = @id AND IsPrimary", new { id });
            var src = await Access.Version(w, from, "viewer");
            if (src.ProjectId != id) throw ApiException.Bad("That version belongs to another project.");
            var no = await w.Scalar<int>("SELECT coalesce(max(VersionNo), 0) + 1 FROM ProjectVersion WHERE ProjectId = @id", new { id });
            var vid = await w.Scalar<long>("""
                INSERT INTO ProjectVersion (ProjectId, VersionNo, VersionLabel, IsPrimary, Status, CopiedFromVersionId, EditionId, RequirementSetId, GrossArea, BuildMonths, FeePct, BaselineName, CreatedBy, ChangedBy)
                SELECT ProjectId, @no, coalesce(@label, 'Version ' || @no), false, 'draft', VersionId, EditionId, RequirementSetId, GrossArea, BuildMonths, FeePct, BaselineName, @u, @u
                FROM ProjectVersion WHERE VersionId = @from RETURNING VersionId
                """, new { no, label = body.VersionLabel, from, u = w.User.UserId });
            await Versions.CopyContent(w, from, vid);
            await Recalc.Summary(w, vid, "version.create");
            await w.Log(id, vid, "version.create", "version", vid, after: new { versionNo = no, from });
            await w.Commit();
            return Results.Created("", new { versionId = vid, versionNo = no });
        });

        api.MapPatch("/versions/{id:long}", async (long id, VersionPatch body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            // unlocking is allowed on a locked version, everything else isn't
            var unlocking = body.Status is not null and not "locked";
            var s = unlocking ? await Access.Version(w, id, "viewer") : await Access.Version(w, id, "editor");
            if (unlocking || body.Status == "locked")
            {
                w.User.Require("version.lock");
                if (s.Access == "viewer") throw ApiException.Forbidden();
            }
            if (body.Status is not null and not ("draft" or "active" or "submitted" or "locked")) throw ApiException.Bad("Unknown version status.");
            await w.Exec("""
                UPDATE ProjectVersion SET VersionLabel = coalesce(@VersionLabel, VersionLabel), Status = coalesce(@Status, Status),
                  GrossArea = coalesce(@GrossArea, GrossArea), ZonesSetArea = CASE WHEN @GrossArea::int IS NOT NULL THEN false ELSE ZonesSetArea END, BuildMonths = coalesce(@BuildMonths, BuildMonths), FeePct = coalesce(@FeePct, FeePct),
                  BaselineName = CASE WHEN @SetBaseline THEN @BaselineName ELSE BaselineName END, ChangedBy = @u
                WHERE VersionId = @id
                """, new { body.VersionLabel, body.Status, body.GrossArea, body.BuildMonths, body.FeePct, SetBaseline = body.BaselineName is not null,
                           BaselineName = body.BaselineName == "" ? null : body.BaselineName, u = w.User.UserId, id });
            if (body.BaselineName is not null) await Budgets.ApplyBaseline(w, id);
            if (body.GrossArea is not null || body.BaselineName is not null) await Recalc.Version(w, id, "version.update");
            await w.Log(s.ProjectId, id, body.Status is not null ? $"version.{body.Status}" : "version.update", "version", id, after: body);
            await w.Commit();
            return Results.Ok(await w.C.QueryFirstAsync<VersionRow>($"SELECT {VersionCols} FROM ProjectVersion WHERE VersionId = @id", new { id }));
        });

        api.MapPost("/versions/{id:long}/primary", async (long id, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Version(w, id, "viewer");
            if (s.Access == "viewer") throw ApiException.Forbidden();
            await w.Exec("UPDATE ProjectVersion SET IsPrimary = false WHERE ProjectId = @p AND IsPrimary", new { p = s.ProjectId });
            await w.Exec("UPDATE ProjectVersion SET IsPrimary = true WHERE VersionId = @id", new { id });
            await w.Log(s.ProjectId, id, "version.primary", "version", id);
            await w.Commit();
            return Results.NoContent();
        });

        api.MapGet("/versions/compare", async (long a, long b, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            var sa = await Access.Version(c, ctx.Me(), a); var sb = await Access.Version(c, ctx.Me(), b);
            if (sa.ProjectId != sb.ProjectId) throw ApiException.Bad("Both versions must be from the same project.");
            var trades = await c.QueryAsync("""
                WITH x AS (SELECT CsiCode, TradeName, coalesce(CarriedValue, LowestValue) AS v FROM Trade WHERE VersionId = @a AND ArchivedAt IS NULL),
                     y AS (SELECT CsiCode, TradeName, coalesce(CarriedValue, LowestValue) AS v FROM Trade WHERE VersionId = @b AND ArchivedAt IS NULL)
                SELECT coalesce(x.CsiCode, y.CsiCode) AS "csiCode", coalesce(y.TradeName, x.TradeName) AS "tradeName", x.v AS "a", y.v AS "b",
                       coalesce(y.v, 0) - coalesce(x.v, 0) AS "delta",
                       CASE WHEN x.CsiCode IS NULL THEN 'added' WHEN y.CsiCode IS NULL THEN 'removed' WHEN x.v IS DISTINCT FROM y.v THEN 'changed' ELSE 'same' END AS "change"
                FROM x FULL JOIN y ON y.CsiCode = x.CsiCode ORDER BY 1
                """, new { a, b });
            var summary = await c.QueryAsync("""
                SELECT coalesce(x.LineName, y.LineName) AS "name", x.Amount AS "a", y.Amount AS "b", coalesce(y.Amount, 0) - coalesce(x.Amount, 0) AS "delta"
                FROM (SELECT * FROM RollupLine WHERE VersionId = @a) x FULL JOIN (SELECT * FROM RollupLine WHERE VersionId = @b) y ON y.LineName = x.LineName
                ORDER BY coalesce(x.SortOrder, y.SortOrder)
                """, new { a, b });
            return new { trades, summary };
        });

        // ------------------------------------------------------------------ zones
        api.MapGet("/versions/{id:long}/zones", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            await Access.Version(c, ctx.Me(), id);
            return await Zones(c, id);
        });
        api.MapPut("/versions/{id:long}/zones", async (long id, ZonesPut put, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Version(w, id);
            var body = put.Zones ?? [];
            var codes = body.Select(z => (z.ZoneCode ?? "").Trim().ToUpperInvariant()).ToList();
            if (codes.Any(string.IsNullOrEmpty) || body.Any(z => string.IsNullOrWhiteSpace(z.ZoneName))) throw ApiException.Bad("Every zone needs a code and a name.");
            if (codes.Distinct().Count() != codes.Count) throw ApiException.Bad("Zone codes must be unique.");
            if (body.Count(z => z.IsRemainder == true) > 1) throw ApiException.Bad("Only one zone can be the remainder of the base bid.");
            if (body.Any(z => z.AreaUnit is not null && !AreaUnits.Contains(z.AreaUnit))) throw ApiException.Bad("Unit of area must be SF, RSF, GSF or USF.");
            if (body.Any(z => z.UnitLabel is not null && !UnitLabels.Contains(z.UnitLabel))) throw ApiException.Bad("Unit of quantity must be N/A, Units, MW, Keys or Spaces.");
            if (body.Any(z => z.FloorArea < 0 || z.UnitCount < 0)) throw ApiException.Bad("Zone area and quantity can't be negative.");
            if (body.Any(z => z.ProjectReservePct is < 0 or > 100 || z.DesignReservePct is < 0 or > 100 || z.EscalationReservePct is < 0 or > 100)) throw ApiException.Bad("Reserve % must be between 0 and 100.");
            if (put.PrimaryAreaUnit is not null && !AreaUnits.Contains(put.PrimaryAreaUnit)) throw ApiException.Bad("Primary unit of area must be SF, RSF, GSF or USF.");
            if (put.PrimaryUnitLabel is not null && !UnitLabels.Contains(put.PrimaryUnitLabel)) throw ApiException.Bad("Primary unit of quantity must be N/A, Units, MW, Keys or Spaces.");
            var keep = body.Where(z => z.ZoneId is > 0).Select(z => z.ZoneId!.Value).ToArray();
            await w.Exec("DELETE FROM Zone WHERE VersionId = @id AND NOT (ZoneId = ANY(@keep))", new { id, keep });
            // park codes and the remainder flag first so swapping two doesn't trip the unique indexes
            await w.Exec("UPDATE Zone SET ZoneCode = '~' || ZoneId, IsRemainder = false WHERE VersionId = @id", new { id });
            short order = 1;
            foreach (var z in body)
            {
                var args = new { id, zid = z.ZoneId ?? 0, code = z.ZoneCode!.Trim().ToUpperInvariant(), name = z.ZoneName!.Trim(), order = order++,
                                 area = z.FloorArea ?? 0, z.UnitCount, unitLabel = z.UnitLabel ?? "N/A", share = z.DefaultShare, z.Colour,
                                 areaUnit = z.AreaUnit ?? "SF", rem = z.IsRemainder ?? false, excl = z.ExcludeFromTotal ?? false,
                                 pr = z.ProjectReservePct ?? 0, dr = z.DesignReservePct ?? 0, er = z.EscalationReservePct ?? 0 };
                if (z.ZoneId is > 0)
                    await w.Exec("""
                        UPDATE Zone SET ZoneCode = @code, ZoneName = @name, SortOrder = @order, FloorArea = @area, UnitCount = @UnitCount, UnitLabel = @unitLabel,
                          DefaultShare = @share, Colour = @Colour, AreaUnit = @areaUnit, IsRemainder = @rem, ExcludeFromTotal = @excl,
                          ProjectReservePct = @pr, DesignReservePct = @dr, EscalationReservePct = @er
                        WHERE ZoneId = @zid AND VersionId = @id
                        """, args);
                else
                {
                    var newId = await w.Scalar<long>("""
                        INSERT INTO Zone (VersionId, ZoneCode, ZoneName, SortOrder, FloorArea, UnitCount, UnitLabel, DefaultShare, Colour, AreaUnit, IsRemainder, ExcludeFromTotal,
                                          ProjectReservePct, DesignReservePct, EscalationReservePct)
                        VALUES (@id, @code, @name, @order, @area, @UnitCount, @unitLabel, @share, @Colour, @areaUnit, @rem, @excl, @pr, @dr, @er) RETURNING ZoneId
                        """, args);
                    await ScopeSetup.AddZoneLineToTrades(w, id, newId);
                }
            }
            // zone lines in every trade follow the zone's name and order
            await w.Exec("UPDATE ScopeLine l SET LineText = z.ZoneName, SortKey = z.SortOrder FROM Zone z WHERE l.ZoneId = z.ZoneId AND z.VersionId = @id AND (l.LineText <> z.ZoneName OR l.SortKey <> z.SortOrder)", new { id });
            await w.Exec("""
                UPDATE ProjectVersion SET ZonesSetArea = coalesce(@set, ZonesSetArea), PrimaryAreaUnit = coalesce(@pau, PrimaryAreaUnit),
                  PrimaryUnitLabel = coalesce(@pul, PrimaryUnitLabel), GrossArea = CASE WHEN NOT coalesce(@set, ZonesSetArea) AND @ga::int IS NOT NULL THEN @ga ELSE GrossArea END, ChangedBy = @u
                WHERE VersionId = @id
                """, new { id, set = put.ZonesSetArea, pau = put.PrimaryAreaUnit, pul = put.PrimaryUnitLabel, ga = put.GrossArea, u = w.User.UserId });
            await SyncZoneTotals(w, id);
            await Recalc.Version(w, id, "zones.update");
            await w.Log(s.ProjectId, id, "zones.update", "version", id, after: put);
            await w.Commit();
            return Results.Ok(await Zones(w.C, id));
        });

        // ------------------------------------------------------------------ markup rates
        api.MapGet("/versions/{id:long}/markups", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            await Access.Version(c, ctx.Me(), id);
            return new { markups = await Markups(c, id), costs = await CostSwitches(c, id) };
        });
        api.MapPut("/versions/{id:long}/markups", async (long id, List<MarkupPut> body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Version(w, id);
            foreach (var m in body)
            {
                if (!await w.Scalar<bool>("SELECT EXISTS (SELECT 1 FROM MarkupType WHERE MarkupCode = @c)", new { c = m.Code })) throw ApiException.Bad($"Unknown rate '{m.Code}'.");
                if (m.RatePct is < 0 or > 100) throw ApiException.Bad($"Rate for {m.Code} must be between 0 and 100%.");
                await w.Exec("""
                    INSERT INTO VersionMarkup (VersionId, MarkupCode, RatePct, FixedAmount, BudgetAmount, IsOff)
                    VALUES (@id, @Code, coalesce(@RatePct, (SELECT CASE WHEN t.MarkupCode = 'salestax' THEN coalesce((SELECT s.SalesTaxRate FROM ProjectVersion v JOIN Project p ON p.ProjectId = v.ProjectId JOIN UsState s ON s.StateCode = p.StateCode WHERE v.VersionId = @id), 0) ELSE t.DefaultRatePct END * 100 FROM MarkupType t WHERE t.MarkupCode = @Code), 0), @FixedAmount, @BudgetAmount, coalesce(@IsOff, false))
                    ON CONFLICT (VersionId, MarkupCode) DO UPDATE SET RatePct = coalesce(@RatePct, VersionMarkup.RatePct), IsOff = coalesce(@IsOff, VersionMarkup.IsOff),
                      FixedAmount = CASE WHEN @ClearFixed THEN NULL ELSE coalesce(@FixedAmount, VersionMarkup.FixedAmount) END,
                      BudgetAmount = coalesce(@BudgetAmount, VersionMarkup.BudgetAmount)
                    """, new { id, m.Code, m.RatePct, m.FixedAmount, m.BudgetAmount, ClearFixed = m.ClearFixed ?? false, m.IsOff });
                if (m.Code == "fee" && m.RatePct is decimal fee) await w.Exec("UPDATE ProjectVersion SET FeePct = @fee WHERE VersionId = @id", new { fee, id });
            }
            var r = await Recalc.Summary(w, id, "markups.update");
            await w.Log(s.ProjectId, id, "markups.update", "version", id, after: body);
            await w.Commit();
            return Results.Ok(new { markups = await Markups(w.C, id), costs = await CostSwitches(w.C, id), hash = r.Hash });
        });

        // ------------------------------------------------------------------ members
        api.MapGet("/projects/{id:long}/members", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            await Access.Project(c, null, ctx.Me(), id, "viewer");
            return await Members(c, id);
        });
        api.MapPut("/projects/{id:long}/members", async (long id, List<MemberIn> body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            await Access.Project(w, id, "owner");
            if (!body.Any(m => m.Access == "owner")) throw ApiException.Bad("A project needs at least one owner.");
            if (body.Any(m => m.Access is not ("viewer" or "editor" or "owner"))) throw ApiException.Bad("Access must be viewer, editor or owner.");
            var ids = body.Select(m => m.UserId).Distinct().ToArray();
            if (await w.Scalar<int>("SELECT count(*) FROM AppUser WHERE UserId = ANY(@ids) AND CompanyId = @cid", new { ids, cid = w.User.CompanyId }) != ids.Length)
                throw ApiException.Bad("Only people in your company can be members of a project.");
            await w.Exec("DELETE FROM ProjectMember WHERE ProjectId = @id AND NOT (UserId = ANY(@ids))", new { id, ids });
            foreach (var m in body)
                await w.Exec("INSERT INTO ProjectMember (ProjectId, UserId, Access) VALUES (@id, @UserId, @Access::AccessLevel) ON CONFLICT (ProjectId, UserId) DO UPDATE SET Access = EXCLUDED.Access",
                    new { id, m.UserId, m.Access });
            await w.Log(id, null, "members.update", "project", id, after: body);
            await w.Commit();
            return Results.Ok(await Members(w.C, id));
        });

        // ------------------------------------------------------------------ activity feed
        api.MapGet("/projects/{id:long}/activity", async (long id, long? before, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            await Access.Project(c, null, ctx.Me(), id, "viewer");
            return await c.QueryAsync("""
                SELECT e.EventId AS "eventId", e.HappenedAt AS "happenedAt", e.Verb AS "verb", e.SubjectType AS "subjectType", e.SubjectKey AS "subjectKey",
                       e.VersionId AS "versionId", u.GivenName || ' ' || u.FamilyName AS "personName", u.Initials AS "initials", u.Colour AS "colour"
                FROM ActivityEvent e LEFT JOIN AppUser u ON u.UserId = e.UserId
                WHERE e.ProjectId = @id AND (@before IS NULL OR e.EventId < @before) ORDER BY e.EventId DESC LIMIT 60
                """, new { id, before });
        });

        api.MapGet("/projects/{id:long}/presence", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            await Access.Project(c, null, ctx.Me(), id, "viewer");
            return await ProjectHub.Rows(c, id);
        });
    }

    public sealed record FavIn(bool IsFavourite);
    public sealed record CopyProjectIn(long VersionId, string? ProjectName);
    public sealed record NewVersionIn(long? FromVersionId, string? VersionLabel);
    public sealed record VersionPatch(string? VersionLabel, string? Status, int? GrossArea, decimal? BuildMonths, decimal? FeePct, string? BaselineName);
    public sealed record ZoneIn(long? ZoneId, string? ZoneCode, string? ZoneName, int? FloorArea, decimal? UnitCount, string? UnitLabel, decimal? DefaultShare, string? Colour,
                                string? AreaUnit, bool? IsRemainder, bool? ExcludeFromTotal, decimal? ProjectReservePct, decimal? DesignReservePct, decimal? EscalationReservePct);
    public sealed record ZonesPut(List<ZoneIn>? Zones, bool? ZonesSetArea, string? PrimaryAreaUnit, string? PrimaryUnitLabel, int? GrossArea);
    public sealed record MarkupPut(string Code, decimal? RatePct, decimal? FixedAmount, decimal? BudgetAmount, bool? ClearFixed, bool? IsOff);
    public sealed record MemberIn(long UserId, string Access);

    public static async Task<object> Detail(Npgsql.NpgsqlConnection c, long id, string access)
    {
        var p = await c.QueryFirstAsync("""
            SELECT ProjectId AS "projectId", ProjectNo AS "projectNo", ProjectName AS "projectName", Stage::text AS "stage", ClientName AS "clientName",
                   ProjectType AS "projectType", ContractType AS "contractType", DivisionId AS "divisionId", BranchId AS "branchId", Street AS "street",
                   City AS "city", StateCode AS "stateCode", CountyName AS "countyName", PostalCode AS "postalCode", BidDueOn AS "bidDueOn", CrmRef AS "crmRef",
                   EstimatorId AS "estimatorId", ManagerId AS "managerId", RowRev AS "rowRev", CreatedAt AS "createdAt", ChangedAt AS "changedAt",
                   to_char(PreconStartOn, 'YYYY-MM-DD') AS "preconStartOn", to_char(PreconEndOn, 'YYYY-MM-DD') AS "preconEndOn", to_char(BuildStartOn, 'YYYY-MM-DD') AS "buildStartOn", to_char(BuildEndOn, 'YYYY-MM-DD') AS "buildEndOn", to_char(CloseoutOn, 'YYYY-MM-DD') AS "closeoutOn",
                   SalaryEscalationPct AS "salaryEscalationPct"
            FROM Project WHERE ProjectId = @id
            """, new { id });
        var d = (IDictionary<string, object?>)p;
        d["versions"] = await c.QueryAsync<VersionRow>($"SELECT {VersionCols} FROM ProjectVersion WHERE ProjectId = @id ORDER BY VersionNo", new { id });
        d["access"] = access;
        return d;
    }

    public static Task<IEnumerable<dynamic>> Zones(Npgsql.NpgsqlConnection c, long versionId) => c.QueryAsync("""
        SELECT ZoneId AS "zoneId", ZoneCode AS "zoneCode", ZoneName AS "zoneName", SortOrder AS "sortOrder", FloorArea AS "floorArea",
               UnitCount AS "unitCount", UnitLabel AS "unitLabel", DefaultShare AS "defaultShare", Colour AS "colour",
               AreaUnit AS "areaUnit", IsRemainder AS "isRemainder", ExcludeFromTotal AS "excludeFromTotal",
               ProjectReservePct AS "projectReservePct", DesignReservePct AS "designReservePct", EscalationReservePct AS "escalationReservePct"
        FROM Zone WHERE VersionId = @versionId ORDER BY SortOrder
        """, new { versionId });

    public static Task<IEnumerable<dynamic>> Markups(Npgsql.NpgsqlConnection c, long versionId) => c.QueryAsync("""
        SELECT t.MarkupCode AS "code", t.MarkupName AS "name", t.AppliesTo AS "appliesTo", t.SortOrder AS "sortOrder",
               coalesce(m.RatePct, CASE WHEN t.MarkupCode = 'salestax' THEN coalesce(s.SalesTaxRate, 0) ELSE t.DefaultRatePct END * 100) AS "ratePct",
               m.FixedAmount AS "fixedAmount", m.BudgetAmount AS "budgetAmount", (CASE WHEN t.MarkupCode = 'salestax' THEN s.SalesTaxRate * 100 ELSE t.DefaultRatePct * 100 END) AS "defaultRatePct",
               coalesce(m.IsOff, false) AS "isOff"
        FROM MarkupType t
        JOIN ProjectVersion v ON v.VersionId = @versionId JOIN Project p ON p.ProjectId = v.ProjectId LEFT JOIN UsState s ON s.StateCode = p.StateCode
        LEFT JOIN VersionMarkup m ON m.VersionId = v.VersionId AND m.MarkupCode = t.MarkupCode
        WHERE t.MarkupCode NOT IN ('precon','genreq','genconds') ORDER BY t.SortOrder
        """, new { versionId });
    /// <summary>On/off switches for the two cost totals (Project overheads, Precon services) shown on the Rates page.</summary>
    public static Task<IEnumerable<dynamic>> CostSwitches(Npgsql.NpgsqlConnection c, long versionId) => c.QueryAsync("""
        SELECT t.MarkupCode AS "code", t.MarkupName AS "name", coalesce(m.IsOff, false) AS "isOff"
        FROM MarkupType t LEFT JOIN VersionMarkup m ON m.VersionId = @versionId AND m.MarkupCode = t.MarkupCode
        WHERE t.MarkupCode IN ('genreq','precon','genconds') ORDER BY t.SortOrder
        """, new { versionId });

    static Task<IEnumerable<dynamic>> Members(Npgsql.NpgsqlConnection c, long id) => c.QueryAsync("""
        SELECT m.UserId AS "userId", u.GivenName || ' ' || u.FamilyName AS "name", u.Initials AS "initials", u.Colour AS "colour", u.LoginEmail::text AS "email", m.Access::text AS "access", m.AddedAt AS "addedAt"
        FROM ProjectMember m JOIN AppUser u ON u.UserId = m.UserId WHERE m.ProjectId = @id ORDER BY m.Access DESC, u.GivenName
        """, new { id });

    /// <summary>Creates Project + Version 1 (primary) + default rates + the creator as owner.</summary>
    public static async Task<(long projectId, long versionId, string projectNo)> CreateProject(Work w, ProjectIn p, bool withMarkups = true)
    {
        var cid = w.User.CompanyId;
        if (cid == 0) throw ApiException.Forbidden("Projects are created by a signed-in person of a company.");
        var no = string.IsNullOrWhiteSpace(p.ProjectNo)
            ? await w.Scalar<string>("UPDATE Company SET NextProjectNo = NextProjectNo + 1 WHERE CompanyId = @cid RETURNING ProjectNoPrefix || (NextProjectNo - 1)", new { cid })
            : p.ProjectNo.Trim();
        if (await w.Scalar<bool>("SELECT EXISTS (SELECT 1 FROM Project WHERE ProjectNo = @no AND CompanyId = @cid)", new { no, cid }))
            throw new ApiException(409, $"Project number {no} is already used.", "duplicate");
        // estimator / manager must be people of this company; anyone else is simply not set
        if (p.EstimatorId is long e0 && !await w.Scalar<bool>("SELECT EXISTS (SELECT 1 FROM AppUser WHERE UserId = @e0 AND CompanyId = @cid)", new { e0, cid })) p.EstimatorId = null;
        if (p.ManagerId is long m0 && !await w.Scalar<bool>("SELECT EXISTS (SELECT 1 FROM AppUser WHERE UserId = @m0 AND CompanyId = @cid)", new { m0, cid })) p.ManagerId = null;
        var state = string.IsNullOrWhiteSpace(p.StateCode) ? null : p.StateCode.Trim().ToUpperInvariant();
        if (state is not null && !await w.Scalar<bool>("SELECT EXISTS (SELECT 1 FROM UsState WHERE StateCode = @state)", new { state }))
            throw ApiException.Bad($"'{state}' is not a US state code.");
        var pid = await w.Scalar<long>("""
            INSERT INTO Project (CompanyId, ProjectNo, ProjectName, Stage, ClientName, ProjectType, ContractType, DivisionId, BranchId, Street, City, StateCode, CountyName, PostalCode,
                                 BidDueOn, CrmRef, EstimatorId, ManagerId, CreatedBy, ChangedBy)
            VALUES (@cid, @no, @ProjectName, coalesce(@Stage, 'lead')::ProjectStage, @ClientName, @ProjectType, @ContractType,
                    (SELECT DivisionId FROM Division WHERE DivisionId = @DivisionId AND CompanyId = @cid), (SELECT BranchId FROM Branch WHERE BranchId = @BranchId AND CompanyId = @cid),
                    @Street, @City, @state, @CountyName, @PostalCode, @BidDueOn, @CrmRef, coalesce(@EstimatorId, @u), @ManagerId, @u, @u) RETURNING ProjectId
            """, new { cid, no, p.ProjectName, Stage = string.IsNullOrWhiteSpace(p.Stage) ? null : p.Stage.Trim().ToLowerInvariant(), p.ClientName, p.ProjectType, p.ContractType,
                       p.DivisionId, p.BranchId, p.Street, p.City, state, p.CountyName, p.PostalCode, p.BidDueOn, p.CrmRef, p.EstimatorId, p.ManagerId, u = w.User.UserId });
        var vid = await w.Scalar<long>("""
            INSERT INTO ProjectVersion (ProjectId, VersionNo, VersionLabel, IsPrimary, Status, EditionId, RequirementSetId, GrossArea, CreatedBy, ChangedBy)
            VALUES (@pid, 1, 'Version 1', true, 'active', (SELECT EditionId FROM CodeEdition WHERE IsCurrent LIMIT 1),
                    (SELECT RequirementSetId FROM RequirementSet WHERE IsTemplate AND CompanyId = @cid ORDER BY RequirementSetId LIMIT 1), @area, @u, @u) RETURNING VersionId
            """, new { pid, cid, area = p.GrossArea ?? 0, u = w.User.UserId });
        if (withMarkups)
            await w.Exec("""
                INSERT INTO VersionMarkup (VersionId, MarkupCode, RatePct)
                SELECT @vid, t.MarkupCode, CASE WHEN t.MarkupCode = 'salestax' THEN coalesce(s.SalesTaxRate, 0) ELSE t.DefaultRatePct END * 100
                FROM MarkupType t LEFT JOIN UsState s ON s.StateCode = @state WHERE t.MarkupCode NOT IN ('precon','genreq','genconds')
                """, new { vid, state });
        await PreconSetup.DefaultTasks(w, vid);
        await w.Exec("UPDATE ProjectVersion SET FeePct = coalesce((SELECT RatePct FROM VersionMarkup WHERE VersionId = @vid AND MarkupCode = 'fee'), 0) WHERE VersionId = @vid", new { vid });
        await w.Exec("INSERT INTO ProjectMember (ProjectId, UserId, Access) VALUES (@pid, @u, 'owner') ON CONFLICT DO NOTHING", new { pid, u = w.User.UserId });
        if (p.EstimatorId is long est && est != w.User.UserId)
            await w.Exec("INSERT INTO ProjectMember (ProjectId, UserId, Access) VALUES (@pid, @est, 'editor') ON CONFLICT DO NOTHING", new { pid, est });
        if (p.ManagerId is long mgr && mgr != w.User.UserId)
            await w.Exec("INSERT INTO ProjectMember (ProjectId, UserId, Access) VALUES (@pid, @mgr, 'editor') ON CONFLICT DO NOTHING", new { pid, mgr });
        await w.Exec("INSERT INTO RecentProject (UserId, ProjectId) VALUES (@u, @pid) ON CONFLICT DO NOTHING", new { pid, u = w.User.UserId });
        return (pid, vid, no);
    }

    public static async Task ApplyDefaultCostKits(Work w, long versionId)
    {
        await w.Exec("""
            INSERT INTO CostLine (VersionId, Area, CategoryName, ItemName, SortKey, Qty, UomCode, Rate, ChangedBy)
            SELECT @versionId, k.Area, i.CategoryName, i.ItemName, row_number() OVER (PARTITION BY k.Area ORDER BY i.SortOrder, i.CostKitItemId),
                   coalesce(i.DefaultQty, 0), i.UomCode, coalesce(i.DefaultRate, 0), @u
            FROM CostKit k JOIN CostKitItem i ON i.CostKitId = k.CostKitId
            WHERE k.IsDefault AND k.CompanyId = (SELECT p.CompanyId FROM ProjectVersion v JOIN Project p ON p.ProjectId = v.ProjectId WHERE v.VersionId = @versionId)
            """, new { versionId, u = w.User.UserId });
    }
}
