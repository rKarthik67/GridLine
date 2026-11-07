using Dapper;
using Gridline.Calc;
using Gridline.Infrastructure;

namespace Gridline.Endpoints;

/// <summary>
/// Alternates & value engineering (was: BidAlternates / TradeAlternates), allowances (was: BidAllowances / TradeAllowances)
/// and unit prices (was: BidUnitPrices / TradeUnitPrices). Same rules as the original app; see Calc/PricingExtras.
/// </summary>
public static class PricingExtrasEndpoints
{
    public sealed record AltIn(string? Kind, string? Description, decimal? QuotedPct, string? Status, string? Comments, string? IncludeCode, bool? Included, int? RowRev);
    public sealed record AltTradeIn(long OptionId, decimal? Amount, bool? ClearAmount, string? Note);
    public sealed record IdsIn(List<long> Ids, string? Status);
    public sealed record SumIn(string? Kind, string? Description, long? TradeId, int? RowRev);
    public sealed record SumTradeIn(bool Included);
    public sealed record SumBulkIn(List<SumTradeKey> Items, bool Included);
    public sealed record SumTradeKey(long SumId, long TradeId);
    public sealed record ApplyIn(long TradeId, decimal Amount);
    public sealed record UnitIn(string? Description, string? Unit, decimal? QuotedPct, string? Comments, string? IncludeCode, bool? Included, long? SelectTradeId, bool? ClearSelection, int? RowRev);
    public sealed record UnitTradeIn(long UnitRateId, decimal? Price, bool? ClearPrice, string? Note);

    static readonly string[] Statuses = ["proposed", "accepted", "rejected"];

    public static void Map(RouteGroupBuilder api)
    {
        // ================================================================== alternates & VE
        api.MapGet("/versions/{id:long}/alternates", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            await Access.Version(c, ctx.Me(), id);
            return await PricingExtras.Alternates(c, null, id);
        });
        api.MapPost("/versions/{id:long}/alternates", async (long id, AltIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            w.User.Require("scope.edit");
            var s = await Access.Version(w, id);
            var kind = (body.Kind ?? "option").ToLowerInvariant() switch { "option" or "alternate" => "option", "saving" or "ve" => "saving", _ => throw ApiException.Bad("Type must be Alternate or Value engineering.") };
            if (string.IsNullOrWhiteSpace(body.Description)) throw ApiException.Bad("Give it a description.");
            var status = Status(body.Status) ?? "proposed";
            CheckPct(body.QuotedPct);
            var oid = await w.Scalar<long>("""
                INSERT INTO PriceOption (VersionId, Kind, OptionNo, Description, Status, SortKey, QuotedPct, Comments, ChangedBy)
                VALUES (@id, @kind, 'new-' || gen_random_uuid(), @d, @status, coalesce((SELECT max(SortKey) FROM PriceOption WHERE VersionId = @id), 0) + 1, coalesce(@q, 100), nullif(trim(@c), ''), @u)
                RETURNING OptionId
                """, new { id, kind, d = body.Description.Trim(), status, q = body.QuotedPct, c = body.Comments, u = w.User.UserId });
            await PricingExtras.Renumber(w, id);
            await w.Log(s.ProjectId, id, "alternate.create", "alternate", oid, after: new { kind, body.Description });
            await w.Commit();
            return Results.Ok(await PricingExtras.Alternates(w.C, null, id));
        });
        api.MapPatch("/alternates/{id:long}", async (long id, AltIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            w.User.Require("scope.edit");
            var vid = await w.One<long?>("SELECT VersionId FROM PriceOption WHERE OptionId = @id", new { id }) ?? throw ApiException.NotFound("Alternate");
            var s = await Access.Version(w, vid);
            await CheckRev(w, "PriceOption", "OptionId", id, body.RowRev);
            if (body.Description is not null && string.IsNullOrWhiteSpace(body.Description)) throw ApiException.Bad("The description can't be empty.");
            var status = body.Status is null ? null : Status(body.Status) ?? throw ApiException.Bad("Status must be Proposed, Approved or Rejected.");
            CheckPct(body.QuotedPct);
            if (body.IncludeCode is string code && !PricingExtras.AltIndirects.Any(i => i.code == code)) throw ApiException.Bad("Unknown indirect cost.");
            await w.Exec("""
                UPDATE PriceOption SET Description = coalesce(trim(@d), Description), Status = coalesce(@status, Status), QuotedPct = coalesce(@q, QuotedPct),
                       Comments = CASE WHEN @c IS NULL THEN Comments ELSE nullif(trim(@c), '') END,
                       IncludeCodes = CASE WHEN @code IS NULL THEN IncludeCodes WHEN @inc THEN array(SELECT DISTINCT unnest(IncludeCodes || @code)) ELSE array_remove(IncludeCodes, @code) END,
                       ChangedAt = now(), ChangedBy = @u, RowRev = RowRev + 1
                WHERE OptionId = @id
                """, new { id, d = body.Description, status, q = body.QuotedPct, c = body.Comments, code = body.IncludeCode, inc = body.Included ?? true, u = w.User.UserId });
            await w.Log(s.ProjectId, vid, "alternate.update", "alternate", id, after: body);
            await w.Commit();
            return Results.Ok(await PricingExtras.Alternates(w.C, null, vid));
        });
        api.MapPost("/versions/{id:long}/alternates/bulk", async (long id, IdsIn body, HttpContext ctx) =>
        {   // delete (Status = null) or set a status on the ticked rows
            await using var w = await Work.Begin(ctx);
            w.User.Require("scope.edit");
            var s = await Access.Version(w, id);
            if (body.Ids is null || body.Ids.Count == 0) throw ApiException.Bad("Nothing selected.");
            if (body.Status is null) await w.Exec("DELETE FROM PriceOption WHERE VersionId = @id AND OptionId = ANY(@ids)", new { id, ids = body.Ids.ToArray() });
            else await w.Exec("UPDATE PriceOption SET Status = @st, ChangedAt = now(), ChangedBy = @u, RowRev = RowRev + 1 WHERE VersionId = @id AND OptionId = ANY(@ids)",
                new { id, ids = body.Ids.ToArray(), st = Status(body.Status) ?? throw ApiException.Bad("Status must be Proposed, Approved or Rejected."), u = w.User.UserId });
            await PricingExtras.Renumber(w, id);
            await w.Log(s.ProjectId, id, body.Status is null ? "alternate.delete" : "alternate.update", "alternate", string.Join(',', body.Ids), after: body);
            await w.Commit();
            return Results.Ok(await PricingExtras.Alternates(w.C, null, id));
        });
        api.MapGet("/trades/{id:long}/alternates", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            await Access.Trade(c, ctx.Me(), id);
            return await PricingExtras.TradeAlternates(c, null, id);
        });
        api.MapPut("/trades/{id:long}/alternates", async (long id, List<AltTradeIn> body, HttpContext ctx) =>
        {   // the trade's amount and note per alternate (was: Scope leveling → Alternates / VE tab); pasting several rows is one call
            await using var w = await Work.Begin(ctx);
            w.User.Require("scope.edit");
            var s = await Access.Trade(w, id);
            if (body is null || body.Count == 0) throw ApiException.Bad("Nothing to save.");
            foreach (var it in body)
            {
                if (await w.Scalar<long?>("SELECT OptionId FROM PriceOption o JOIN Trade t ON t.VersionId = o.VersionId WHERE o.OptionId = @o AND t.TradeId = @id", new { o = it.OptionId, id }) is null)
                    throw ApiException.Bad("That alternate is not on this version.");
                await w.Exec("""
                    INSERT INTO PriceOptionTrade (OptionId, TradeId, CarriedAmount, Note) VALUES (@o, @id, @a, nullif(trim(@n), ''))
                    ON CONFLICT (OptionId, TradeId) DO UPDATE SET
                      CarriedAmount = CASE WHEN @clear THEN NULL WHEN @a IS NULL THEN PriceOptionTrade.CarriedAmount ELSE @a END,
                      Note = CASE WHEN @n IS NULL THEN PriceOptionTrade.Note ELSE nullif(trim(@n), '') END
                    """, new { o = it.OptionId, id, a = it.Amount, clear = it.ClearAmount ?? false, n = it.Note });
            }
            await w.Exec("UPDATE PriceOption SET ChangedAt = now(), ChangedBy = @u WHERE OptionId = ANY(@ids)", new { ids = body.Select(b => b.OptionId).Distinct().ToArray(), u = w.User.UserId });
            await w.Log(s.ProjectId, s.VersionId, "alternate.trade", "trade", id, after: body.Count <= 20 ? body : new { count = body.Count });
            await w.Commit();
            return Results.Ok(await PricingExtras.TradeAlternates(w.C, null, id));
        });

        // ================================================================== allowances
        api.MapGet("/versions/{id:long}/allowances", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            await Access.Version(c, ctx.Me(), id);
            return await PricingExtras.Allowances(c, null, id);
        });
        api.MapPost("/versions/{id:long}/allowances", async (long id, SumIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            w.User.Require("scope.edit");
            var s = await Access.Version(w, id);
            if (string.IsNullOrWhiteSpace(body.Description)) throw ApiException.Bad("Give the allowance a description.");
            var kind = (body.Kind ?? "ownerprescribed").Replace(" ", "").ToLowerInvariant() switch { "ownerprescribed" => "ownerprescribed", "miscellaneous" or "misc" => "miscellaneous", _ => throw ApiException.Bad("Type must be Owner prescribed or Miscellaneous.") };
            if (kind == "miscellaneous" && body.TradeId is null) throw ApiException.Bad("Pick the trade for a miscellaneous allowance.");
            if (body.TradeId is long tid && await w.Scalar<long?>("SELECT TradeId FROM Trade WHERE TradeId = @tid AND VersionId = @id AND ArchivedAt IS NULL", new { tid, id }) is null)
                throw ApiException.Bad("That trade is not on this version.");
            var sumId = await w.Scalar<long>("""
                INSERT INTO ProvisionalSum (VersionId, TradeId, Kind, Description, Amount, SortKey, ChangedBy)
                VALUES (@id, @tid, @kind, @d, 0, coalesce((SELECT max(SortKey) FROM ProvisionalSum WHERE VersionId = @id), 0) + 1, @u) RETURNING SumId
                """, new { id, tid = kind == "miscellaneous" ? body.TradeId : null, kind, d = body.Description.Trim(), u = w.User.UserId });
            await PricingExtras.Renumber(w, id);
            var trades = (await w.Query<long>("SELECT TradeId FROM Trade WHERE VersionId = @id AND ArchivedAt IS NULL AND (@tid::bigint IS NULL OR TradeId = @tid)",
                new { id, tid = kind == "miscellaneous" ? body.TradeId : null })).ToList();
            foreach (var t in trades) await PricingExtras.AddAllowanceLines(w, t, sumId);
            await w.Log(s.ProjectId, id, "allowance.create", "allowance", sumId, after: new { kind, body.Description, trades = trades.Count });
            await w.Commit();
            return Results.Ok(await PricingExtras.Allowances(w.C, null, id));
        });
        api.MapPatch("/allowances/{id:long}", async (long id, SumIn body, HttpContext ctx) =>
        {   // the description is also the line text on every trade (PutAllowance updates BidTradeItems.Label)
            await using var w = await Work.Begin(ctx);
            w.User.Require("scope.edit");
            var vid = await w.One<long?>("SELECT VersionId FROM ProvisionalSum WHERE SumId = @id", new { id }) ?? throw ApiException.NotFound("Allowance");
            var s = await Access.Version(w, vid);
            await CheckRev(w, "ProvisionalSum", "SumId", id, body.RowRev);
            if (string.IsNullOrWhiteSpace(body.Description)) throw ApiException.Bad("The description can't be empty.");
            await w.Exec("""
                UPDATE ProvisionalSum SET Description = trim(@d), ChangedAt = now(), ChangedBy = @u, RowRev = RowRev + 1 WHERE SumId = @id;
                UPDATE ScopeLine SET LineText = trim(@d), ChangedBy = @u WHERE SumId = @id AND ArchivedAt IS NULL;
                """, new { id, d = body.Description, u = w.User.UserId });
            await w.Log(s.ProjectId, vid, "allowance.update", "allowance", id, after: body);
            await w.Commit();
            return Results.Ok(await PricingExtras.Allowances(w.C, null, vid));
        });
        api.MapPost("/versions/{id:long}/allowances/status", async (long id, SumBulkIn body, HttpContext ctx) =>
        {   // Included / Excluded per trade (one toggle or the bulk buttons)
            await using var w = await Work.Begin(ctx);
            w.User.Require("scope.edit");
            var s = await Access.Version(w, id);
            if (body.Items is null || body.Items.Count == 0) throw ApiException.Bad("Nothing selected.");
            foreach (var k in body.Items)
                await w.Exec("""
                    UPDATE ProvisionalSumTrade x SET Included = @inc, ChangedAt = now() FROM ProvisionalSum p
                    WHERE x.SumId = p.SumId AND p.VersionId = @id AND x.SumId = @sum AND x.TradeId = @trade
                    """, new { id, sum = k.SumId, trade = k.TradeId, inc = body.Included });
            await w.Exec("UPDATE ProvisionalSum SET ChangedAt = now(), ChangedBy = @u WHERE SumId = ANY(@ids)", new { ids = body.Items.Select(i => i.SumId).Distinct().ToArray(), u = w.User.UserId });
            await w.Log(s.ProjectId, id, "allowance.update", "allowance", string.Join(',', body.Items.Select(i => i.SumId).Distinct()), after: body);
            await w.Commit();
            return Results.Ok(await PricingExtras.Allowances(w.C, null, id));
        });
        api.MapPost("/allowances/{id:long}/apply", async (long id, ApplyIn body, HttpContext ctx) =>
        {   // Budget amount → every trade partner of a trade (PutAllowanceApplyToTrade), owner prescribed only
            await using var w = await Work.Begin(ctx);
            w.User.Require("scope.edit");
            var p = await w.One<(long vid, string kind)>("SELECT VersionId, Kind FROM ProvisionalSum WHERE SumId = @id", new { id });
            if (p.vid == 0) throw ApiException.NotFound("Allowance");
            if (p.kind != "ownerprescribed") throw ApiException.Bad("A budget amount can only be applied for an owner prescribed allowance.");
            var s = await Access.Trade(w, body.TradeId);
            if (s.VersionId != p.vid) throw ApiException.Bad("That trade is not on this version.");
            await PricingExtras.AddAllowanceLines(w, body.TradeId, id);
            var line = await w.Scalar<long>("SELECT LineId FROM ScopeLine WHERE SumId = @id AND TradeId = @t AND ArchivedAt IS NULL ORDER BY LineId LIMIT 1", new { id, t = body.TradeId });
            var n = await w.Exec("""
                INSERT INTO ScopeAnswer (LineId, BidId, Kind, Amount, Source, ChangedBy)
                SELECT @line, b.BidId, 'amount', @amt, 'allowance', @u FROM Bid b WHERE b.TradeId = @t AND b.ArchivedAt IS NULL
                ON CONFLICT (LineId, BidId) DO UPDATE SET Kind = 'amount', Amount = @amt, AnswerText = NULL, Formula = NULL, Source = 'allowance', ChangedBy = @u
                """, new { line, t = body.TradeId, amt = body.Amount, u = w.User.UserId });
            await Recalc.TradeAndSummary(w, body.TradeId, "allowance.apply");
            await w.Log(s.ProjectId, p.vid, "allowance.apply", "allowance", id, after: new { body.TradeId, body.Amount, bidders = n });
            await w.Commit();
            return Results.Ok(await PricingExtras.Allowances(w.C, null, p.vid));
        });
        api.MapPost("/versions/{id:long}/allowances/delete", async (long id, IdsIn body, HttpContext ctx) =>
        {   // removes the allowances and their lines from every trade; the trades are recalculated
            await using var w = await Work.Begin(ctx);
            w.User.Require("scope.edit");
            var s = await Access.Version(w, id);
            if (body.Ids is null || body.Ids.Count == 0) throw ApiException.Bad("Nothing selected.");
            var ids = body.Ids.ToArray();
            var trades = (await w.Query<long>("SELECT DISTINCT l.TradeId FROM ScopeLine l JOIN ProvisionalSum p ON p.SumId = l.SumId WHERE p.VersionId = @id AND l.SumId = ANY(@ids) AND l.ArchivedAt IS NULL", new { id, ids })).ToList();
            await w.Exec("""
                UPDATE ScopeLine SET ArchivedAt = now(), ChangedBy = @u WHERE SumId = ANY(@ids) AND ArchivedAt IS NULL AND TradeId IN (SELECT TradeId FROM Trade WHERE VersionId = @id);
                DELETE FROM ProvisionalSum WHERE VersionId = @id AND SumId = ANY(@ids);
                """, new { id, ids, u = w.User.UserId });
            await PricingExtras.Renumber(w, id);
            foreach (var t in trades) await Recalc.TradeAndSummary(w, t, "allowance.delete");
            await w.Log(s.ProjectId, id, "allowance.delete", "allowance", string.Join(',', ids));
            await w.Commit();
            return Results.Ok(await PricingExtras.Allowances(w.C, null, id));
        });

        // ================================================================== unit prices
        api.MapGet("/versions/{id:long}/unit-prices", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            await Access.Version(c, ctx.Me(), id);
            return await PricingExtras.UnitPrices(c, null, id);
        });
        api.MapPost("/versions/{id:long}/unit-prices", async (long id, UnitIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            w.User.Require("scope.edit");
            var s = await Access.Version(w, id);
            if (string.IsNullOrWhiteSpace(body.Description)) throw ApiException.Bad("Give the unit price a description.");
            CheckPct(body.QuotedPct);
            var uid = await w.Scalar<long>("""
                INSERT INTO UnitRate (VersionId, Description, Unit, QuotedPct, Comments, ChangedBy)
                VALUES (@id, trim(@d), nullif(trim(@unit), ''), coalesce(@q, 100), nullif(trim(@c), ''), @u) RETURNING UnitRateId
                """, new { id, d = body.Description, unit = body.Unit, q = body.QuotedPct, c = body.Comments, u = w.User.UserId });
            await PricingExtras.Renumber(w, id);
            await w.Log(s.ProjectId, id, "unitprice.create", "unitprice", uid, after: body);
            await w.Commit();
            return Results.Ok(await PricingExtras.UnitPrices(w.C, null, id));
        });
        api.MapPatch("/unit-prices/{id:long}", async (long id, UnitIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            w.User.Require("scope.edit");
            var vid = await w.One<long?>("SELECT VersionId FROM UnitRate WHERE UnitRateId = @id", new { id }) ?? throw ApiException.NotFound("Unit price");
            var s = await Access.Version(w, vid);
            await CheckRev(w, "UnitRate", "UnitRateId", id, body.RowRev);
            if (body.Description is not null && string.IsNullOrWhiteSpace(body.Description)) throw ApiException.Bad("The description can't be empty.");
            CheckPct(body.QuotedPct);
            if (body.IncludeCode is string code && !PricingExtras.UnitIndirects.Any(i => i.code == code)) throw ApiException.Bad("Unknown indirect cost.");
            await w.Exec("""
                UPDATE UnitRate SET Description = coalesce(trim(@d), Description), Unit = CASE WHEN @unit IS NULL THEN Unit ELSE nullif(trim(@unit), '') END,
                       QuotedPct = coalesce(@q, QuotedPct), Comments = CASE WHEN @c IS NULL THEN Comments ELSE nullif(trim(@c), '') END,
                       IncludeCodes = CASE WHEN @code IS NULL THEN IncludeCodes WHEN @inc THEN array(SELECT DISTINCT unnest(IncludeCodes || @code)) ELSE array_remove(IncludeCodes, @code) END,
                       ChangedAt = now(), ChangedBy = @u, RowRev = RowRev + 1
                WHERE UnitRateId = @id
                """, new { id, d = body.Description, unit = body.Unit, q = body.QuotedPct, c = body.Comments, code = body.IncludeCode, inc = body.Included ?? true, u = w.User.UserId });
            if (body.SelectTradeId is long st)
            {   // UnitPriceSelectTradePrice: this trade's price is the one carried; the others go back to proposed
                if (await w.Scalar<long?>("SELECT TradeId FROM Trade WHERE TradeId = @st AND VersionId = @vid AND ArchivedAt IS NULL", new { st, vid }) is null) throw ApiException.Bad("That trade is not on this version.");
                await w.Exec("""
                    UPDATE UnitRateTrade SET IsSelected = false WHERE UnitRateId = @id AND IsSelected;
                    INSERT INTO UnitRateTrade (UnitRateId, TradeId, IsSelected) VALUES (@id, @st, true) ON CONFLICT (UnitRateId, TradeId) DO UPDATE SET IsSelected = true, ChangedAt = now();
                    """, new { id, st });
            }
            else if (body.ClearSelection == true) await w.Exec("UPDATE UnitRateTrade SET IsSelected = false WHERE UnitRateId = @id", new { id });
            await w.Log(s.ProjectId, vid, "unitprice.update", "unitprice", id, after: body);
            await w.Commit();
            return Results.Ok(await PricingExtras.UnitPrices(w.C, null, vid));
        });
        api.MapPost("/versions/{id:long}/unit-prices/delete", async (long id, IdsIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            w.User.Require("scope.edit");
            var s = await Access.Version(w, id);
            if (body.Ids is null || body.Ids.Count == 0) throw ApiException.Bad("Nothing selected.");
            await w.Exec("DELETE FROM UnitRate WHERE VersionId = @id AND UnitRateId = ANY(@ids)", new { id, ids = body.Ids.ToArray() });
            await PricingExtras.Renumber(w, id);
            await w.Log(s.ProjectId, id, "unitprice.delete", "unitprice", string.Join(',', body.Ids));
            await w.Commit();
            return Results.Ok(await PricingExtras.UnitPrices(w.C, null, id));
        });
        api.MapGet("/trades/{id:long}/unit-prices", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            await Access.Trade(c, ctx.Me(), id);
            return await PricingExtras.TradeUnitPrices(c, null, id);
        });
        api.MapPut("/trades/{id:long}/unit-prices", async (long id, List<UnitTradeIn> body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            w.User.Require("scope.edit");
            var s = await Access.Trade(w, id);
            if (body is null || body.Count == 0) throw ApiException.Bad("Nothing to save.");
            foreach (var it in body)
            {
                if (it.Price is < 0) throw ApiException.Bad("A unit price can't be negative.");
                if (await w.Scalar<long?>("SELECT r.UnitRateId FROM UnitRate r JOIN Trade t ON t.VersionId = r.VersionId WHERE r.UnitRateId = @r AND t.TradeId = @id", new { r = it.UnitRateId, id }) is null)
                    throw ApiException.Bad("That unit price is not on this version.");
                await w.Exec("""
                    INSERT INTO UnitRateTrade (UnitRateId, TradeId, Price, Note) VALUES (@r, @id, @p, nullif(trim(@n), ''))
                    ON CONFLICT (UnitRateId, TradeId) DO UPDATE SET
                      Price = CASE WHEN @clear THEN NULL WHEN @p IS NULL THEN UnitRateTrade.Price ELSE @p END,
                      Note = CASE WHEN @n IS NULL THEN UnitRateTrade.Note ELSE nullif(trim(@n), '') END, ChangedAt = now()
                    """, new { r = it.UnitRateId, id, p = it.Price, clear = it.ClearPrice ?? false, n = it.Note });
            }
            await w.Exec("UPDATE UnitRate SET ChangedAt = now(), ChangedBy = @u WHERE UnitRateId = ANY(@ids)", new { ids = body.Select(b => b.UnitRateId).Distinct().ToArray(), u = w.User.UserId });
            await w.Log(s.ProjectId, s.VersionId, "unitprice.trade", "trade", id, after: body.Count <= 20 ? body : new { count = body.Count });
            await w.Commit();
            return Results.Ok(await PricingExtras.TradeUnitPrices(w.C, null, id));
        });
    }

    static string? Status(string? s) => s?.Trim().ToLowerInvariant() switch { "proposed" => "proposed", "accepted" or "approved" => "accepted", "rejected" => "rejected", _ => null };
    static void CheckPct(decimal? q) { if (q is < 0 or > 1000) throw ApiException.Bad("Quoted % must be between 0 and 1000."); }
    static async Task CheckRev(Work w, string table, string key, long id, int? rowRev)
    {
        if (rowRev is not int rr) return;
        var cur = await w.One<(int rev, string? who)>($"SELECT x.RowRev, (SELECT u.GivenName || ' ' || u.FamilyName FROM AppUser u WHERE u.UserId = x.ChangedBy) FROM {table} x WHERE x.{key} = @id", new { id });
        if (cur.rev != rr) throw ApiException.Clash(cur.who ?? "Someone");
    }
}
