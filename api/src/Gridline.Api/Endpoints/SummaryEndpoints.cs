using Dapper;
using Gridline.Calc;
using Gridline.Infrastructure;

namespace Gridline.Endpoints;

public static class SummaryEndpoints
{
    public sealed record IndirectIn(string? ItemName, decimal? Amount, bool? OutsideFee, short? SortOrder, string? Description, bool? IsOff);
    /// <summary>Fee carve-outs (was: fee exclusions) and bid total options for a version.</summary>
    public sealed record CarveOutsIn(bool? ApplyFeeExcludes, List<string>? FeeExcludes, bool? ExcludeGenreqFromDirect, List<string>? ReservePrecon, List<string>? ReserveGenconds, decimal? FeePct);
    static readonly string[] FeeParts = ["genreq", "precon", "genconds", "reserves", "bond", "cover"];
    static readonly string[] Reserves = ["reserveproject", "reservedesign", "reserveescalation"];
    public static Task<dynamic> CarveOuts(Work w, long versionId) => w.C.QueryFirstAsync("""
        SELECT v.ApplyFeeExcludes AS "applyFeeExcludes", v.FeeExcludes AS "feeExcludes", v.ExcludeGenreqFromDirect AS "excludeGenreqFromDirect",
               v.ReservePrecon AS "reservePrecon", v.ReserveGenconds AS "reserveGenconds",
               coalesce((SELECT m.RatePct FROM VersionMarkup m WHERE m.VersionId = v.VersionId AND m.MarkupCode = 'fee'), (SELECT t.DefaultRatePct * 100 FROM MarkupType t WHERE t.MarkupCode = 'fee')) AS "feePct",
               coalesce((SELECT m.IsOff FROM VersionMarkup m WHERE m.VersionId = v.VersionId AND m.MarkupCode = 'fee'), false) AS "feeOff"
        FROM ProjectVersion v WHERE v.VersionId = @versionId
        """, new { versionId });

    /// <summary>The executive summary as the page shows it, plus the inputs so the browser can recalculate as you type.</summary>
    public static async Task<object> Build(Work w, long versionId)
    {
        var (input, zones, budgets) = await Recalc.SummaryInput(w, versionId);
        var lines = Rules.Summary(input);
        var hash = Rules.TotalsHash(lines.Select(l => (decimal?)l.Amount));
        var zoneInfo = await ProjectEndpoints.Zones(w.C, versionId);
        var indirect = await w.Query("SELECT IndirectId AS \"indirectId\", ItemName AS \"itemName\", Amount AS \"amount\", OutsideFee AS \"outsideFee\", SortOrder AS \"sortOrder\", Description AS \"description\", IsOff AS \"isOff\" FROM IndirectItem WHERE VersionId = @versionId ORDER BY SortOrder, IndirectId", new { versionId });
        var offCodes = (await w.Query<string>("SELECT MarkupCode FROM VersionMarkup WHERE VersionId = @versionId AND IsOff", new { versionId })).ToHashSet();
        var area = input.GrossArea;
        return new
        {
            versionId, totalsHash = hash, grossArea = area,
            lines = lines.Select(l => new
            {
                l.Name, l.Kind, l.Code, l.RatePct, l.Amount, l.PerArea, l.PctOfContract, l.Zones,
                isOff = l.Code is not null && offCodes.Contains(l.Code),
                budgetAmount = l.Code is null ? null : budgets.GetValueOrDefault(l.Code),
                editable = l.Kind is "markup" or "tax" or "custom",
            }),
            input, zones = zoneInfo, indirect,
            markups = await ProjectEndpoints.Markups(w.C, versionId),
            zoneReserves = await Recalc.ZoneReserveCodes(w, versionId),
            carveOuts = await CarveOuts(w, versionId),
        };
    }

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/versions/{id:long}/price-roll-up", async (long id, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);       // read only: never committed
            await Access.Version(w, id, "viewer");
            return await Build(w, id);
        });

        api.MapGet("/versions/{id:long}/fee-carve-outs", async (long id, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            await Access.Version(w, id, "viewer");
            return await CarveOuts(w, id);
        });
        api.MapPut("/versions/{id:long}/fee-carve-outs", async (long id, CarveOutsIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Version(w, id);
            if (body.FeeExcludes?.Any(x => !FeeParts.Contains(x)) == true) throw ApiException.Bad("Fee carve-outs can only be: genreq, precon, genconds, reserves, bond, cover.");
            if (body.ReservePrecon?.Any(x => !Reserves.Contains(x)) == true || body.ReserveGenconds?.Any(x => !Reserves.Contains(x)) == true) throw ApiException.Bad("Unknown reserve.");
            if (body.FeePct is < 0 or > 100) throw ApiException.Bad("Fee must be between 0 and 100%.");
            await w.Exec("""
                UPDATE ProjectVersion SET ApplyFeeExcludes = coalesce(@a, ApplyFeeExcludes), FeeExcludes = coalesce(@fe, FeeExcludes),
                  ExcludeGenreqFromDirect = coalesce(@eg, ExcludeGenreqFromDirect), ReservePrecon = coalesce(@rp, ReservePrecon), ReserveGenconds = coalesce(@rg, ReserveGenconds), ChangedBy = @u
                WHERE VersionId = @id
                """, new { id, a = body.ApplyFeeExcludes, fe = body.FeeExcludes?.Distinct().ToArray(), eg = body.ExcludeGenreqFromDirect, rp = body.ReservePrecon?.Distinct().ToArray(), rg = body.ReserveGenconds?.Distinct().ToArray(), u = w.User.UserId });
            if (body.FeePct is decimal fee)
            {
                await w.Exec("""
                    INSERT INTO VersionMarkup (VersionId, MarkupCode, RatePct) VALUES (@id, 'fee', @fee)
                    ON CONFLICT (VersionId, MarkupCode) DO UPDATE SET RatePct = @fee, FixedAmount = NULL
                    """, new { id, fee });
                await w.Exec("UPDATE ProjectVersion SET FeePct = @fee WHERE VersionId = @id", new { fee, id });
            }
            await Recalc.Summary(w, id, "feecarveouts.update");
            await w.Log(s.ProjectId, id, "feecarveouts.update", "version", id, after: body);
            await w.Commit();
            return Results.Ok(await Build(w, id));
        });

        api.MapPost("/versions/{id:long}/indirect", async (long id, IndirectIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Version(w, id);
            if (string.IsNullOrWhiteSpace(body.ItemName)) throw ApiException.Bad("Give the line a name.");
            var iid = await w.Scalar<long>("""
                INSERT INTO IndirectItem (VersionId, ItemName, Amount, OutsideFee, SortOrder, Description, IsOff)
                VALUES (@id, @n, coalesce(@a, 0), coalesce(@o, false), coalesce(@so, (SELECT coalesce(max(SortOrder), 0) + 1 FROM IndirectItem WHERE VersionId = @id)), nullif(trim(@d), ''), coalesce(@off, false)) RETURNING IndirectId
                """, new { id, n = body.ItemName.Trim(), a = body.Amount, o = body.OutsideFee, so = body.SortOrder, d = body.Description, off = body.IsOff });
            await Recalc.Summary(w, id, "indirect.create");
            await w.Log(s.ProjectId, id, "indirect.create", "indirect", iid, after: body);
            await w.Commit();
            return Results.Ok(await Build(w, id));
        });

        api.MapPatch("/indirect/{id:long}", async (long id, IndirectIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Indirect(w, id);
            if (body.ItemName is not null && string.IsNullOrWhiteSpace(body.ItemName)) throw ApiException.Bad("The line needs a name.");
            await w.Exec("""
                UPDATE IndirectItem SET ItemName = coalesce(trim(@n), ItemName), Amount = coalesce(@a, Amount), OutsideFee = coalesce(@o, OutsideFee), SortOrder = coalesce(@so, SortOrder),
                  Description = CASE WHEN @d IS NULL THEN Description ELSE nullif(trim(@d), '') END, IsOff = coalesce(@off, IsOff)
                WHERE IndirectId = @id
                """, new { id, n = body.ItemName, a = body.Amount, o = body.OutsideFee, so = body.SortOrder, d = body.Description, off = body.IsOff });
            await Recalc.Summary(w, s.VersionId, "indirect.update");
            await w.Log(s.ProjectId, s.VersionId, "indirect.update", "indirect", id, after: body);
            await w.Commit();
            return Results.Ok(await Build(w, s.VersionId));
        });

        api.MapDelete("/indirect/{id:long}", async (long id, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Indirect(w, id);
            await w.Exec("DELETE FROM IndirectItem WHERE IndirectId = @id", new { id });
            await Recalc.Summary(w, s.VersionId, "indirect.delete");
            await w.Log(s.ProjectId, s.VersionId, "indirect.delete", "indirect", id);
            await w.Commit();
            return Results.Ok(await Build(w, s.VersionId));
        });
    }
}
