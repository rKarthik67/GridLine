using Dapper;
using Gridline.Calc;
using Gridline.Infrastructure;
using Npgsql;

namespace Gridline.Endpoints;

public static class TradeEndpoints
{
    public sealed class BidDto
    {
        public long BidId { get; set; } public long TradeId { get; set; } public int LaneNo { get; set; } public string BidderName { get; set; } = "";
        public long? PartnerId { get; set; } public string State { get; set; } = ""; public bool IsPosted { get; set; } public bool IsCarried { get; set; }
        public decimal? BaseAmount { get; set; } public decimal ScopeAdjustments { get; set; } public decimal? TotalAmount { get; set; }
        public bool AddedByUser { get; set; } public string? BaseSource { get; set; } public string? Email { get; set; } public string? Contact { get; set; }
        public int? Rank { get; set; } public int RowRev { get; set; } public bool HiddenInScope { get; set; }
    }
    public sealed class Benchmark { public string CsiCode { get; set; } = ""; public decimal LowPerSf { get; set; } public decimal MedianPerSf { get; set; } public decimal HighPerSf { get; set; } public int Projects { get; set; } }
    public sealed class TradeDto
    {
        public long TradeId { get; set; } public long VersionId { get; set; } public string CsiCode { get; set; } = ""; public int? CodeId { get; set; }
        public string TradeName { get; set; } = ""; public int ScopeNo { get; set; } public long? KitId { get; set; } public long? EstimatorId { get; set; } public DateOnly? BidDueOn { get; set; }
        public decimal? LowestValue { get; set; } public decimal? CarriedValue { get; set; } public decimal Adjustment { get; set; } public decimal? BudgetValue { get; set; }
        public decimal? BudgetDelta { get; set; } public decimal? BuyoutPct { get; set; } public decimal? PlugAmount { get; set; }
        public string? ClientNote { get; set; } public string? TeamNote { get; set; } public int? SortOrder { get; set; } public int RowRev { get; set; }
        public DateTime ChangedAt { get; set; } public string? ChangedByName { get; set; } public bool ZonesFromScope { get; set; }
        public List<BidDto> Bids { get; set; } = [];
        public Dictionary<string, decimal> ZoneShares { get; set; } = [];
        public Benchmark? Benchmark { get; set; }
    }

    const string TradeCols = """
        t.TradeId, t.VersionId, t.CsiCode, t.CodeId, t.TradeName, t.ScopeNo, t.KitId, t.EstimatorId, t.BidDueOn, t.LowestValue, t.CarriedValue, t.Adjustment,
        t.BudgetValue, t.BudgetDelta, t.BuyoutPct, t.PlugAmount, t.ClientNote, t.TeamNote, t.SortOrder, t.RowRev, t.ChangedAt, t.ZonesFromScope,
        (SELECT u.GivenName || ' ' || u.FamilyName FROM AppUser u WHERE u.UserId = t.ChangedBy) AS ChangedByName
        """;
    const string BidCols = """
        b.BidId, b.TradeId, b.LaneNo, b.BidderName, b.PartnerId, b.State::text AS State, b.IsPosted, b.IsCarried, b.BaseAmount, b.ScopeAdjustments, b.TotalAmount,
        coalesce(tp.AddedByUser, false) AS AddedByUser, b.BaseSource, b.RowRev, b.HiddenInScope,
        (SELECT pp.Email::text FROM PartnerPerson pp WHERE pp.PersonId = b.PersonId OR (b.PersonId IS NULL AND pp.PartnerId = b.PartnerId) ORDER BY pp.IsPrimary DESC LIMIT 1) AS Email,
        (SELECT pp.FullName FROM PartnerPerson pp WHERE pp.PersonId = b.PersonId OR (b.PersonId IS NULL AND pp.PartnerId = b.PartnerId) ORDER BY pp.IsPrimary DESC LIMIT 1) AS Contact
        """;

    public static async Task<List<TradeDto>> LoadTrades(NpgsqlConnection c, NpgsqlTransaction? tx, long versionId, long? tradeId = null)
    {
        var args = new { versionId, tradeId };
        var trades = (await c.QueryAsync<TradeDto>($"SELECT {TradeCols} FROM Trade t WHERE t.VersionId = @versionId AND t.ArchivedAt IS NULL AND (@tradeId IS NULL OR t.TradeId = @tradeId) ORDER BY t.SortOrder NULLS LAST, t.CsiCode, t.ScopeNo", args, tx)).ToList();
        var bids = (await c.QueryAsync<BidDto>($"SELECT {BidCols} FROM Bid b JOIN Trade t ON t.TradeId = b.TradeId LEFT JOIN TradePartner tp ON tp.PartnerId = b.PartnerId WHERE t.VersionId = @versionId AND b.ArchivedAt IS NULL AND (@tradeId IS NULL OR t.TradeId = @tradeId) ORDER BY b.LaneNo", args, tx)).ToLookup(b => b.TradeId);
        var shares = (await c.QueryAsync<(long tradeId, string zoneCode, decimal share)>("SELECT tz.TradeId, z.ZoneCode, tz.Share FROM TradeZone tz JOIN Zone z ON z.ZoneId = tz.ZoneId JOIN Trade t ON t.TradeId = tz.TradeId WHERE t.VersionId = @versionId AND (@tradeId IS NULL OR t.TradeId = @tradeId)", args, tx)).ToLookup(x => x.tradeId);
        var bench = (await c.QueryAsync<Benchmark>("""
            SELECT t.CsiCode, round(min(t.CarriedValue / v.GrossArea), 2) AS LowPerSf,
                   round((percentile_cont(0.5) WITHIN GROUP (ORDER BY t.CarriedValue / v.GrossArea))::numeric, 2) AS MedianPerSf,
                   round(max(t.CarriedValue / v.GrossArea), 2) AS HighPerSf, count(*)::int AS Projects
            FROM Trade t JOIN ProjectVersion v ON v.VersionId = t.VersionId AND v.IsPrimary JOIN Project p ON p.ProjectId = v.ProjectId
            JOIN ProjectVersion me ON me.VersionId = @versionId JOIN Project mp ON mp.ProjectId = me.ProjectId
            WHERE t.ArchivedAt IS NULL AND t.CarriedValue > 0 AND v.GrossArea > 0 AND p.ProjectId <> mp.ProjectId AND p.ArchivedAt IS NULL
              AND p.CompanyId = mp.CompanyId   -- a company's benchmarks come only from its own past projects
              AND p.ProjectType IS NOT DISTINCT FROM mp.ProjectType AND p.Stage IN ('submitted','awarded','lost','bidding')
            GROUP BY t.CsiCode
            """, args, tx)).ToDictionary(b => b.CsiCode);
        foreach (var t in trades)
        {
            t.Bids = bids[t.TradeId].ToList();
            var ranked = t.Bids.Where(b => b.IsPosted && b.State != "disqualified" && b.TotalAmount != null).OrderBy(b => b.TotalAmount).ToList();
            for (var i = 0; i < ranked.Count; i++) ranked[i].Rank = i > 0 && ranked[i - 1].TotalAmount == ranked[i].TotalAmount ? ranked[i - 1].Rank : i + 1;
            t.ZoneShares = shares[t.TradeId].ToDictionary(x => x.zoneCode, x => x.share);
            t.Benchmark = bench.GetValueOrDefault(t.CsiCode);
        }
        return trades;
    }

    public static async Task<TradeDto> One(NpgsqlConnection c, NpgsqlTransaction? tx, long tradeId)
    {
        var vid = await c.ExecuteScalarAsync<long>("SELECT VersionId FROM Trade WHERE TradeId = @tradeId", new { tradeId }, tx);
        return (await LoadTrades(c, tx, vid, tradeId)).FirstOrDefault() ?? throw ApiException.NotFound("Trade");
    }

    /// <summary>Standard reply to a trade edit: the trade as saved plus the confirmed roll-up fingerprint.</summary>
    static async Task<object> Reply(Work w, long tradeId, Recalc.SummaryResult s) =>
        new { trade = await One(w.C, w.Tx, tradeId), totalsHash = s.Hash, matched = s.Matched };

    public sealed record TradeIn(string? CsiCode, string? TradeName, int? CodeId, decimal? BudgetValue, long? KitId, long? EstimatorId, DateOnly? BidDueOn);
    public sealed class TradePatch
    {
        public string? TradeName { get; set; } public string? CsiCode { get; set; } public int? CodeId { get; set; } public decimal? Adjustment { get; set; }
        public decimal? PlugAmount { get; set; } public bool? ClearPlug { get; set; } public decimal? BudgetValue { get; set; } public bool? ClearBudget { get; set; }
        public string? ClientNote { get; set; } public string? TeamNote { get; set; } public long? EstimatorId { get; set; } public DateOnly? BidDueOn { get; set; }
        public int? SortOrder { get; set; } public int? RowRev { get; set; } public string? ClientHash { get; set; }
    }
    public sealed record CarryIn(long? BidId, string? ClientHash);
    public sealed record BidIn(string? BidderName, long? PartnerId, decimal? BaseAmount, string? State, string? Email, string? Contact);
    public sealed class BidPatch
    {
        public string? BidderName { get; set; } public decimal? BaseAmount { get; set; } public bool? ClearBase { get; set; } public string? State { get; set; }
        public int? RowRev { get; set; } public string? ClientHash { get; set; }
    }
    public sealed record ZonesIn(Dictionary<string, decimal> Shares);
    public sealed record LanesIn(List<long>? Order, List<long>? Hidden);
    public sealed record BudgetIn(string BudgetName, List<BudgetValueIn> Values);
    public sealed record BudgetValueIn(string CsiCode, decimal Amount);
    public sealed record CopyBudgetIn(long FromVersionId);

    static readonly string[] States = ["invited", "bidding", "posted", "late", "incomplete", "notplansspecs", "undecided", "disqualified", "declined"];

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/versions/{id:long}/bid-board", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            await Access.Version(c, ctx.Me(), id);
            var version = await c.QueryFirstAsync<ProjectEndpoints.VersionRow>($"SELECT {ProjectEndpoints.VersionCols} FROM ProjectVersion WHERE VersionId = @id", new { id });
            return new
            {
                version,
                zones = await ProjectEndpoints.Zones(c, id),
                trades = await LoadTrades(c, null, id),
                budgets = await c.QueryAsync("SELECT bc.BudgetColumnId AS \"budgetColumnId\", bc.BudgetName AS \"budgetName\", (SELECT count(*) FROM BudgetValue x WHERE x.BudgetColumnId = bc.BudgetColumnId) AS \"values\" FROM BudgetColumn bc JOIN ProjectVersion v ON v.ProjectId = bc.ProjectId WHERE v.VersionId = @id ORDER BY bc.BudgetName", new { id }),
                baselineName = version.BaselineName,
                totalsHash = await c.ExecuteScalarAsync<string?>("SELECT TotalsHash FROM ProjectVersion WHERE VersionId = @id", new { id }),
            };
        });

        api.MapGet("/trades/{id:long}", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            await Access.Trade(c, ctx.Me(), id);
            return await One(c, null, id);
        });

        api.MapPost("/versions/{id:long}/trades", async (long id, TradeIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Version(w, id);
            var (code, title, codeId) = await ResolveCode(w, body.CsiCode, body.TradeName, body.CodeId);
            var scopeNo = await w.Scalar<int>("SELECT coalesce(max(ScopeNo), 0) + 1 FROM Trade WHERE VersionId = @id AND CsiCode = @code AND ArchivedAt IS NULL", new { id, code });
            var tid = await w.Scalar<long>("""
                INSERT INTO Trade (VersionId, CodeId, CsiCode, TradeName, ScopeNo, KitId, EstimatorId, BidDueOn, BudgetValue, SortOrder, ChangedBy)
                VALUES (@id, @codeId, @code, @title, @scopeNo, @KitId, @EstimatorId, @BidDueOn, @BudgetValue,
                        (SELECT coalesce(max(SortOrder), 0) + 10 FROM Trade WHERE VersionId = @id), @u) RETURNING TradeId
                """, new { id, codeId, code, title, scopeNo, body.KitId, body.EstimatorId, body.BidDueOn, body.BudgetValue, u = w.User.UserId });
            await ScopeSetup.AddStandardLines(w, tid);
            await ScopeSetup.DefaultZoneSplit(w, tid);
            if (body.KitId is long kit) await ScopeEndpoints.ApplyKit(w, tid, kit);
            var (_, sum) = await Recalc.TradeAndSummary(w, tid, "trade.create");
            await w.Log(s.ProjectId, id, "trade.create", "trade", tid, after: new { code, title });
            await w.Commit();
            return Results.Created($"/api/trades/{tid}", await Reply(w, tid, sum));
        });

        api.MapPatch("/trades/{id:long}", async (long id, TradePatch body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Trade(w, id);
            await CheckRev(w, "Trade", "TradeId", id, body.RowRev);
            string? code = null, title = body.TradeName; int? codeId = body.CodeId;
            if (body.CsiCode is not null) (code, title, codeId) = await ResolveCode(w, body.CsiCode, body.TradeName, body.CodeId);
            if (title is not null && string.IsNullOrWhiteSpace(title)) throw ApiException.Bad("Trade name can't be empty.");
            var before = await w.One<dynamic>("SELECT Adjustment, PlugAmount, BudgetValue, ClientNote, TeamNote, TradeName FROM Trade WHERE TradeId = @id", new { id });
            await w.Exec("""
                UPDATE Trade SET TradeName = coalesce(@title, TradeName), CsiCode = coalesce(@code, CsiCode), CodeId = coalesce(@codeId, CodeId),
                  Adjustment = coalesce(@Adjustment, Adjustment),
                  PlugAmount = CASE WHEN @ClearPlug THEN NULL ELSE coalesce(@PlugAmount, PlugAmount) END,
                  BudgetValue = CASE WHEN @ClearBudget THEN NULL ELSE coalesce(@BudgetValue, BudgetValue) END,
                  ClientNote = CASE WHEN @SetClient THEN nullif(@ClientNote, '') ELSE ClientNote END,
                  TeamNote = CASE WHEN @SetTeam THEN nullif(@TeamNote, '') ELSE TeamNote END,
                  EstimatorId = coalesce(@EstimatorId, EstimatorId), BidDueOn = coalesce(@BidDueOn, BidDueOn), SortOrder = coalesce(@SortOrder, SortOrder),
                  ChangedBy = @u
                WHERE TradeId = @id
                """, new { title, code, codeId, body.Adjustment, body.PlugAmount, ClearPlug = body.ClearPlug ?? false, body.BudgetValue, ClearBudget = body.ClearBudget ?? false,
                           SetClient = body.ClientNote is not null, body.ClientNote, SetTeam = body.TeamNote is not null, body.TeamNote,
                           body.EstimatorId, body.BidDueOn, body.SortOrder, u = w.User.UserId, id });
            var (_, sum) = await Recalc.TradeAndSummary(w, id, "trade.update", body.ClientHash);
            var verb = body.PlugAmount is not null || body.ClearPlug == true ? "trade.plug" : body.ClientNote is not null || body.TeamNote is not null ? "trade.note" : body.Adjustment is not null ? "trade.adjust" : "trade.update";
            await w.Log(s.ProjectId, s.VersionId, verb, "trade", id, (object?)before, body);
            await w.Commit();
            return Results.Ok(await Reply(w, id, sum));
        });

        api.MapDelete("/trades/{id:long}", async (long id, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Trade(w, id);
            await w.Exec("UPDATE Trade SET ArchivedAt = now(), ChangedBy = @u WHERE TradeId = @id", new { id, u = w.User.UserId });
            var sum = await Recalc.Summary(w, s.VersionId, "trade.delete");
            await w.Log(s.ProjectId, s.VersionId, "trade.delete", "trade", id);
            await w.Commit();
            return Results.Ok(new { totalsHash = sum.Hash });
        });

        api.MapPut("/trades/{id:long}/pick", async (long id, CarryIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            w.User.Require("trade.pick");
            var s = await Access.Trade(w, id);
            if (body.BidId is long bid)
            {
                var ok = await w.One<string>("SELECT State::text FROM Bid WHERE BidId = @bid AND TradeId = @id AND ArchivedAt IS NULL", new { bid, id })
                         ?? throw ApiException.Bad("That bid is not on this trade.");
                if (ok is "disqualified" or "declined") throw ApiException.Bad("A disqualified or declined bid can't be picked.");
            }
            await w.Exec("UPDATE Bid SET IsCarried = false, ChangedBy = @u WHERE TradeId = @id AND IsCarried", new { id, u = w.User.UserId });
            if (body.BidId is long b2) await w.Exec("UPDATE Bid SET IsCarried = true, ChangedBy = @u WHERE BidId = @b2", new { b2, u = w.User.UserId });
            await w.Exec("UPDATE Trade SET ChangedBy = @u WHERE TradeId = @id", new { id, u = w.User.UserId });
            var (_, sum) = await Recalc.TradeAndSummary(w, id, "trade.pick", body.ClientHash);
            await w.Log(s.ProjectId, s.VersionId, body.BidId is null ? "bid.unpick" : "bid.pick", "trade", id, after: new { body.BidId });
            await w.Commit();
            return Results.Ok(await Reply(w, id, sum));
        });

        api.MapPost("/trades/{id:long}/bids", async (long id, BidIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Trade(w, id);
            var bidId = await AddBid(w, id, body, "manual");
            var (_, sum) = await Recalc.TradeAndSummary(w, id, "bid.create");
            await w.Log(s.ProjectId, s.VersionId, "bid.create", "bid", bidId, after: body);
            await w.Commit();
            return Results.Created("", await Reply(w, id, sum));
        });

        api.MapPatch("/bids/{id:long}", async (long id, BidPatch body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Bid(w, id);
            await CheckRev(w, "Bid", "BidId", id, body.RowRev);
            if (body.State is not null && !States.Contains(body.State)) throw ApiException.Bad($"Unknown bid status '{body.State}'.");
            var before = await w.One<dynamic>("SELECT BidderName, BaseAmount, State::text AS State FROM Bid WHERE BidId = @id", new { id });
            await w.Exec("""
                UPDATE Bid SET BidderName = coalesce(nullif(trim(@BidderName), ''), BidderName),
                  BaseAmount = CASE WHEN @ClearBase THEN NULL ELSE coalesce(@BaseAmount, BaseAmount) END,
                  BaseSource = CASE WHEN @BaseAmount IS NOT NULL THEN 'manual' ELSE BaseSource END,
                  State = coalesce(@State::BidState, CASE WHEN @BaseAmount IS NOT NULL AND State IN ('invited','bidding') THEN 'posted'::BidState ELSE State END),
                  ChangedBy = @u
                WHERE BidId = @id
                """, new { body.BidderName, body.BaseAmount, ClearBase = body.ClearBase ?? false, body.State, u = w.User.UserId, id });
            await w.Exec("""
                UPDATE Bid SET IsPosted = (BaseAmount IS NOT NULL AND State NOT IN ('invited','bidding','declined')),
                               IsCarried = IsCarried AND State NOT IN ('disqualified','declined')
                WHERE BidId = @id
                """, new { id });
            var (_, sum) = await Recalc.TradeAndSummary(w, s.TradeId!.Value, "bid.update", body.ClientHash);
            await w.Log(s.ProjectId, s.VersionId, body.State is not null ? "bid.status" : "bid.update", "bid", id, (object?)before, body);
            await w.Commit();
            return Results.Ok(await Reply(w, s.TradeId.Value, sum));
        });

        api.MapDelete("/bids/{id:long}", async (long id, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Bid(w, id);
            await w.Exec("UPDATE Bid SET ArchivedAt = now(), IsCarried = false, LaneNo = -1 - (BidId % 30000)::int, ChangedBy = @u WHERE BidId = @id", new { id, u = w.User.UserId });
            var (_, sum) = await Recalc.TradeAndSummary(w, s.TradeId!.Value, "bid.delete");
            await w.Log(s.ProjectId, s.VersionId, "bid.delete", "bid", id);
            await w.Commit();
            return Results.Ok(await Reply(w, s.TradeId.Value, sum));
        });

        // Bidder columns on scope match: order (lane letters follow it) and which ones are hidden (was: hide subcontractor / column order)
        api.MapPut("/trades/{id:long}/lanes", async (long id, LanesIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Trade(w, id);
            var bids = (await w.Query<long>("SELECT BidId FROM Bid WHERE TradeId = @id AND ArchivedAt IS NULL ORDER BY LaneNo", new { id })).ToList();
            if (body.Order is { Count: > 0 } order)
            {
                if (order.Count != bids.Count || order.Except(bids).Any()) throw ApiException.Bad("The new order must list every bid on this trade once.");
                await w.Exec("UPDATE Bid SET LaneNo = LaneNo + 10000 WHERE TradeId = @id AND ArchivedAt IS NULL", new { id });
                for (var i = 0; i < order.Count; i++) await w.Exec("UPDATE Bid SET LaneNo = @n WHERE BidId = @b", new { n = (short)(i + 1), b = order[i] });
            }
            if (body.Hidden is not null)
                await w.Exec("UPDATE Bid SET HiddenInScope = (BidId = ANY(@hidden)) WHERE TradeId = @id AND ArchivedAt IS NULL", new { id, hidden = body.Hidden.ToArray() });
            await w.Log(s.ProjectId, s.VersionId, "trade.lanes", "trade", id, after: body);
            await w.Commit();
            return Results.Ok(await One(w.C, null, id));
        });

        api.MapPut("/trades/{id:long}/zones", async (long id, ZonesIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Trade(w, id);
            if (await w.Scalar<bool>("SELECT ZonesFromScope FROM Trade WHERE TradeId = @id", new { id }))
                throw ApiException.Bad("This trade's zone split comes from the zone amounts in Scope match. Change them there (or clear them) to split it by hand.");
            var total = body.Shares.Values.Sum();
            if (body.Shares.Values.Any(v => v < 0) || (body.Shares.Count > 0 && Math.Abs(total - 1m) > 0.0005m))
                throw ApiException.Bad($"The zone split must add up to 100% (it adds up to {total * 100:0.##}%).");
            await w.Exec("DELETE FROM TradeZone WHERE TradeId = @id", new { id });
            foreach (var (code, share) in body.Shares.Where(kv => kv.Value > 0))
            {
                var n = await w.Exec("INSERT INTO TradeZone (TradeId, ZoneId, Share) SELECT @id, ZoneId, @share FROM Zone WHERE VersionId = @v AND ZoneCode = @code", new { id, share, v = s.VersionId, code });
                if (n == 0) throw ApiException.Bad($"Zone '{code}' is not in this version.");
            }
            var (_, sum) = await Recalc.TradeAndSummary(w, id, "trade.zones");
            await w.Log(s.ProjectId, s.VersionId, "trade.zones", "trade", id, after: body.Shares);
            await w.Commit();
            return Results.Ok(await Reply(w, id, sum));
        });

        // ------------------------------------------------------------------ budgets
        api.MapGet("/projects/{id:long}/budgets", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            await Access.Project(c, null, ctx.Me(), id, "viewer");
            var cols = (await c.QueryAsync<(long id, string name)>("SELECT BudgetColumnId, BudgetName FROM BudgetColumn WHERE ProjectId = @id ORDER BY BudgetName", new { id })).ToList();
            var vals = (await c.QueryAsync<(long id, string code, decimal amount)>("SELECT v.BudgetColumnId, v.CsiCode, v.Amount FROM BudgetValue v JOIN BudgetColumn b ON b.BudgetColumnId = v.BudgetColumnId WHERE b.ProjectId = @id", new { id })).ToLookup(x => x.id);
            return cols.Select(x => new { budgetColumnId = x.id, budgetName = x.name, values = vals[x.id].ToDictionary(v => v.code, v => v.amount) });
        });
        api.MapPost("/projects/{id:long}/budgets", async (long id, BudgetIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Project(w, id);
            if (string.IsNullOrWhiteSpace(body.BudgetName)) throw ApiException.Bad("Give the budget a name.");
            var bid = await w.Scalar<long>("INSERT INTO BudgetColumn (ProjectId, BudgetName) VALUES (@id, @n) ON CONFLICT (ProjectId, BudgetName) DO UPDATE SET BudgetName = EXCLUDED.BudgetName RETURNING BudgetColumnId", new { id, n = body.BudgetName.Trim() });
            foreach (var v in body.Values ?? [])
                await w.Exec("INSERT INTO BudgetValue (BudgetColumnId, CsiCode, Amount) VALUES (@bid, @c, @a) ON CONFLICT (BudgetColumnId, CsiCode) DO UPDATE SET Amount = EXCLUDED.Amount", new { bid, c = v.CsiCode.Trim(), a = v.Amount });
            foreach (var vid in await w.Query<long>("SELECT VersionId FROM ProjectVersion WHERE ProjectId = @id AND BaselineName = @n AND Status <> 'locked'", new { id, n = body.BudgetName.Trim() }))
            { await Budgets.ApplyBaseline(w, vid); await Recalc.Version(w, vid, "budget.update"); }
            await w.Log(id, null, "budget.update", "budget", bid, after: new { body.BudgetName, count = body.Values?.Count ?? 0 });
            await w.Commit();
            return Results.Ok(new { budgetColumnId = bid });
        });
        api.MapPost("/versions/{id:long}/budgets/copy", async (long id, CopyBudgetIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Version(w, id);
            var src = await Access.Version(w, body.FromVersionId, "viewer");
            if (src.ProjectId != s.ProjectId) throw ApiException.Bad("Budgets can only be copied between versions of the same project.");
            var n = await w.Exec("""
                UPDATE Trade t SET BudgetValue = f.BudgetValue FROM Trade f
                WHERE f.VersionId = @from AND f.ArchivedAt IS NULL AND t.VersionId = @id AND t.ArchivedAt IS NULL AND t.CsiCode = f.CsiCode AND t.ScopeNo = f.ScopeNo
                """, new { id, from = body.FromVersionId });
            await Recalc.Version(w, id, "budget.copy");
            await w.Log(s.ProjectId, id, "budget.copy", "version", id, after: new { from = body.FromVersionId, trades = n });
            await w.Commit();
            return Results.Ok(new { trades = n });
        });

        api.MapGet("/benchmarks", async (string? csi, string? type, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            return await c.QueryAsync("""
                SELECT CsiCode AS "csiCode", ProjectType AS "projectType", Projects AS "projects", LowPerSf AS "lowPerSf", MedianPerSf AS "medianPerSf", HighPerSf AS "highPerSf"
                FROM TradeBenchmark WHERE CompanyId = @cid AND (@csi IS NULL OR CsiCode = @csi) AND (@type IS NULL OR ProjectType = @type) ORDER BY CsiCode, ProjectType
                """, new { csi, type, cid = ctx.Me().CompanyId });
        });
    }

    /// <summary>The company a trade belongs to (through its project), for directory lookups.</summary>
    public static Task<long> CompanyOfTrade(Work w, long tradeId) =>
        w.Scalar<long>("SELECT p.CompanyId FROM Trade t JOIN Project p ON p.ProjectId = t.ProjectId WHERE t.TradeId = @tradeId", new { tradeId });

    public static async Task<long> AddBid(Work w, long tradeId, BidIn body, string source)
    {
        var cid = await CompanyOfTrade(w, tradeId);
        var name = body.BidderName?.Trim();
        long? partnerId = body.PartnerId;
        if (partnerId is long pid) name ??= await w.One<string>("SELECT CompanyName FROM TradePartner WHERE PartnerId = @pid AND CompanyId = @cid", new { pid, cid }) ?? throw ApiException.Bad("Unknown trade partner.");
        if (string.IsNullOrWhiteSpace(name)) throw ApiException.Bad("Give the bidder a name.");
        partnerId ??= await w.Scalar<long?>("SELECT PartnerId FROM TradePartner WHERE CompanyId = @cid AND lower(CompanyName) = lower(@name)", new { name, cid });
        partnerId ??= await w.Scalar<long>("INSERT INTO TradePartner (CompanyId, CompanyName, AddedByUser) VALUES (@cid, @name, true) RETURNING PartnerId", new { name, cid });
        long? personId = null;
        if (!string.IsNullOrWhiteSpace(body.Email) || !string.IsNullOrWhiteSpace(body.Contact))
        {
            personId = await w.Scalar<long?>("SELECT PersonId FROM PartnerPerson WHERE PartnerId = @partnerId AND (Email = @e::citext OR (@e IS NULL AND FullName = @n)) LIMIT 1",
                new { partnerId, e = body.Email?.Trim(), n = body.Contact?.Trim() });
            personId ??= await w.Scalar<long>("INSERT INTO PartnerPerson (PartnerId, FullName, Email, IsPrimary) VALUES (@partnerId, coalesce(@n, @e), @e, NOT EXISTS (SELECT 1 FROM PartnerPerson WHERE PartnerId = @partnerId)) RETURNING PersonId",
                new { partnerId, e = body.Email?.Trim(), n = body.Contact?.Trim() });
        }
        if (await w.Scalar<bool>("SELECT EXISTS (SELECT 1 FROM Bid WHERE TradeId = @tradeId AND PartnerId = @partnerId AND ArchivedAt IS NULL)", new { tradeId, partnerId }))
            throw new ApiException(409, $"{name} is already bidding on this trade.", "duplicate");
        var state = body.State is null ? (body.BaseAmount is null ? "invited" : "posted") : body.State;
        if (!States.Contains(state)) throw ApiException.Bad($"Unknown bid status '{state}'.");
        return await w.Scalar<long>("""
            INSERT INTO Bid (TradeId, PartnerId, PersonId, BidderName, LaneNo, State, IsPosted, BaseAmount, BaseSource, ChangedBy)
            VALUES (@tradeId, @partnerId, @personId, @name, (SELECT coalesce(max(LaneNo), 0) + 1 FROM Bid WHERE TradeId = @tradeId AND LaneNo > 0),
                    @state::BidState, @posted, @BaseAmount, @source, @u) RETURNING BidId
            """, new { tradeId, partnerId, personId, name, state, posted = body.BaseAmount is not null && state is not ("invited" or "bidding" or "declined"), body.BaseAmount, source, u = w.User.UserId });
    }

    static async Task<(string code, string title, int? codeId)> ResolveCode(Work w, string? csi, string? title, int? codeId)
    {
        var code = NormaliseCsi(csi);
        if (codeId is int cid)
        {
            var row = await w.One<(string, string)>("SELECT CsiCode, CodeTitle FROM WorkCode WHERE CodeId = @cid", new { cid });
            if (row.Item1 is null) throw ApiException.Bad("Unknown CSI code.");
            return (row.Item1, string.IsNullOrWhiteSpace(title) ? row.Item2 : title.Trim(), cid);
        }
        if (code is null) throw ApiException.Bad("A CSI code like '08 44 00' is required.");
        var match = await w.One<(int id, string t)>("""
            SELECT w.CodeId, w.CodeTitle FROM WorkCode w JOIN CodeGroup g ON g.GroupId = w.GroupId JOIN CodeEdition e ON e.EditionId = g.EditionId
            WHERE w.CsiCode = @code ORDER BY e.IsCurrent DESC LIMIT 1
            """, new { code });
        var name = string.IsNullOrWhiteSpace(title) ? match.t : title.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw ApiException.Bad("Give the trade a name.");
        return (code, name, match.id == 0 ? null : match.id);
    }

    /// <summary>"084400", "08-44-00", "08 44 00" → "08 44 00". Anything else is kept as typed.</summary>
    public static string? NormaliseCsi(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var digits = new string(s.Where(char.IsDigit).ToArray());
        return digits.Length == 6 ? $"{digits[..2]} {digits[2..4]} {digits[4..]}" : s.Trim();
    }

    public static async Task CheckRev(Work w, string table, string key, long id, int? rowRev)
    {
        if (rowRev is null) return;
        var cur = await w.One<(int rev, string? who)>($"SELECT x.RowRev, (SELECT u.GivenName || ' ' || u.FamilyName FROM AppUser u WHERE u.UserId = x.ChangedBy) FROM {table} x WHERE x.{key} = @id FOR UPDATE", new { id });
        if (cur.rev != 0 && cur.rev != rowRev) throw ApiException.Clash(cur.who ?? "Someone");
    }
}
