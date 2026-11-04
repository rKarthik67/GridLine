using System.Diagnostics;
using Gridline.Infrastructure;

namespace Gridline.Calc;

/// <summary>
/// Server side of the hybrid calculation: recalculates only the trade that changed plus the roll-up,
/// stores the results and logs a CalcRun (did the browser's totals match?).
/// </summary>
public static class Recalc
{
    sealed class TradeRow { public long TradeId { get; set; } public long VersionId { get; set; } public decimal Adjustment { get; set; } public decimal? PlugAmount { get; set; } public decimal? BudgetValue { get; set; } }
    sealed class ZoneShareRow { public string ZoneCode { get; set; } = ""; public decimal Share { get; set; } }

    public static async Task<TradeOut> Trade(Work w, long tradeId)
    {
        var t = await w.One<TradeRow>("SELECT TradeId, VersionId, Adjustment, PlugAmount, BudgetValue FROM Trade WHERE TradeId = @tradeId", new { tradeId })
                ?? throw ApiException.NotFound("Trade");
        var input = new TradeIn
        {
            TradeId = t.TradeId, Adjustment = t.Adjustment, PlugAmount = t.PlugAmount, BudgetValue = t.BudgetValue,
            Bids = (await w.Query<BidIn>("SELECT BidId, IsPosted, IsCarried, State::text AS State, BaseAmount FROM Bid WHERE TradeId = @tradeId AND ArchivedAt IS NULL ORDER BY LaneNo", new { tradeId })).ToList(),
            Lines = (await w.Query<ScopeLineIn>("SELECT LineId, Section::text AS Section, IsHeading, SkipInAdjustments, ArchivedAt IS NOT NULL AS Archived FROM ScopeLine WHERE TradeId = @tradeId", new { tradeId })).ToList(),
            Answers = (await w.Query<AnswerIn>("SELECT a.LineId, a.BidId, a.Kind::text AS Kind, a.Amount FROM ScopeAnswer a JOIN Bid b ON b.BidId = a.BidId WHERE b.TradeId = @tradeId", new { tradeId })).ToList(),
            ZoneShares = (await w.Query<ZoneShareRow>("SELECT z.ZoneCode, tz.Share FROM TradeZone tz JOIN Zone z ON z.ZoneId = tz.ZoneId WHERE tz.TradeId = @tradeId", new { tradeId }))
                         .ToDictionary(r => r.ZoneCode, r => r.Share),
        };
        var o = Rules.Trade(input);
        foreach (var b in o.Bids)
            await w.Exec("UPDATE Bid SET ScopeAdjustments = @adj, TotalAmount = @tot WHERE BidId = @id AND (ScopeAdjustments IS DISTINCT FROM @adj OR TotalAmount IS DISTINCT FROM @tot)",
                new { adj = b.ScopeAdjustments, tot = b.TotalAmount, id = b.BidId });
        if (await DeriveZoneSplit(w, t.VersionId, tradeId))
        {
            // the split now follows the bid's zone amounts (was: breakout area values in trade scope)
            var shares = (await w.Query<ZoneShareRow>("SELECT z.ZoneCode, tz.Share FROM TradeZone tz JOIN Zone z ON z.ZoneId = tz.ZoneId WHERE tz.TradeId = @tradeId", new { tradeId }))
                         .ToDictionary(r => r.ZoneCode, r => r.Share);
            var basis = o.CarriedValue ?? o.LowestValue ?? 0;
            o.ZoneAmounts = shares.ToDictionary(kv => kv.Key, kv => Rules.R2(basis * kv.Value));
        }
        decimal? buyout = o.CarriedValue is decimal c && t.BudgetValue is decimal bv && bv != 0 ? Rules.R2((bv - c) / bv * 100m) : null;
        await w.Exec("""
            UPDATE Trade SET LowestValue = @low, CarriedValue = @carried, BudgetDelta = @delta, BuyoutPct = @buyout, NeedsRecalc = false
            WHERE TradeId = @id AND (LowestValue IS DISTINCT FROM @low OR CarriedValue IS DISTINCT FROM @carried OR BudgetDelta IS DISTINCT FROM @delta
                                     OR BuyoutPct IS DISTINCT FROM @buyout OR NeedsRecalc)
            """, new { low = o.LowestValue, carried = o.CarriedValue, delta = o.BudgetDelta, buyout, id = tradeId });
        return o;
    }

    /// <summary>
    /// Zones work like today's breakout areas: each bidder types an amount per zone in Scope match, and the
    /// "remainder of base bid" zone gets the bid total minus the other zones. When the bid that counts (the picked
    /// one, otherwise the lowest) has zone amounts — or the version has a remainder zone — the trade's split is
    /// taken from them; otherwise the manual / default split stays. Set-based, so it also runs for whole versions.
    /// Returns true when the given trade's split came from scope.
    /// </summary>
    public static async Task<bool> DeriveZoneSplit(Work w, long versionId, long? tradeId = null)
    {
        await w.Exec("""
            DROP TABLE IF EXISTS zbasis; DROP TABLE IF EXISTS zsplit;
            CREATE TEMP TABLE zbasis ON COMMIT DROP AS
            SELECT t.TradeId, b.BidId, b.TotalAmount
            FROM Trade t
            JOIN LATERAL (SELECT b.BidId, b.TotalAmount FROM Bid b
                          WHERE b.TradeId = t.TradeId AND b.ArchivedAt IS NULL AND b.TotalAmount IS NOT NULL
                            AND (b.IsCarried OR (b.IsPosted AND b.State <> 'disqualified'))
                          ORDER BY b.IsCarried DESC, b.TotalAmount, b.LaneNo LIMIT 1) b ON true
            WHERE t.VersionId = @versionId AND t.ArchivedAt IS NULL AND (@tradeId::bigint IS NULL OR t.TradeId = @tradeId)
              AND b.TotalAmount <> 0;
            CREATE TEMP TABLE zsplit ON COMMIT DROP AS
            WITH za AS (
                SELECT zb.TradeId, l.ZoneId, sum(a.Amount) AS Amount
                FROM zbasis zb JOIN ScopeLine l ON l.TradeId = zb.TradeId AND l.Section = 'zone' AND l.ArchivedAt IS NULL
                JOIN Zone z ON z.ZoneId = l.ZoneId AND NOT z.IsRemainder
                JOIN ScopeAnswer a ON a.LineId = l.LineId AND a.BidId = zb.BidId AND a.Kind = 'amount' AND a.Amount IS NOT NULL
                GROUP BY zb.TradeId, l.ZoneId),
            rem AS (SELECT ZoneId FROM Zone WHERE VersionId = @versionId AND IsRemainder),
            pick AS (SELECT zb.* FROM zbasis zb WHERE EXISTS (SELECT 1 FROM za WHERE za.TradeId = zb.TradeId) OR EXISTS (SELECT 1 FROM rem))
            SELECT p.TradeId, za.ZoneId, round(za.Amount / p.TotalAmount, 4) AS Share FROM pick p JOIN za ON za.TradeId = p.TradeId
            UNION ALL
            SELECT p.TradeId, r.ZoneId, round(1 - coalesce((SELECT sum(za.Amount) FROM za WHERE za.TradeId = p.TradeId), 0) / p.TotalAmount, 4)
            FROM pick p CROSS JOIN rem r;
            DELETE FROM TradeZone tz WHERE tz.TradeId IN (SELECT DISTINCT TradeId FROM zsplit);
            INSERT INTO TradeZone (TradeId, ZoneId, Share) SELECT TradeId, ZoneId, greatest(-999, least(999, Share)) FROM zsplit WHERE Share <> 0;
            UPDATE Trade t SET ZonesFromScope = (t.TradeId IN (SELECT TradeId FROM zsplit))
            WHERE t.VersionId = @versionId AND t.ArchivedAt IS NULL AND (@tradeId::bigint IS NULL OR t.TradeId = @tradeId)
              AND t.ZonesFromScope IS DISTINCT FROM (t.TradeId IN (SELECT TradeId FROM zsplit));
            """, new { versionId, tradeId });
        return tradeId is not null && await w.Scalar<bool>("SELECT EXISTS (SELECT 1 FROM zsplit WHERE TradeId = @tradeId)", new { tradeId });
    }

    public sealed class SummaryResult
    {
        public List<SummaryLine> Lines { get; set; } = [];
        public string Hash { get; set; } = "";
        public bool? Matched { get; set; }
        public Dictionary<string, decimal> ZoneShares { get; set; } = [];
    }
    public sealed class MarkupRow { public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string AppliesTo { get; set; } = ""; public decimal RatePct { get; set; } public decimal? FixedAmount { get; set; } public int SortOrder { get; set; } public decimal? BudgetAmount { get; set; } }
    sealed class ZoneRow { public long ZoneId { get; set; } public string ZoneCode { get; set; } = ""; public int FloorArea { get; set; } }

    /// <summary>Inputs to the executive summary for a version (also sent to the browser so it can recalculate live).</summary>
    public static async Task<(SummaryIn input, List<ZoneRow2> zones, Dictionary<string, decimal?> budgets)> SummaryInput(Work w, long versionId)
    {
        var tradesTotal = await w.Scalar<decimal?>("SELECT sum(coalesce(CarriedValue, LowestValue, 0)) FROM Trade WHERE VersionId = @versionId AND ArchivedAt IS NULL", new { versionId }) ?? 0;
        var genreq = await w.Scalar<decimal?>("SELECT sum(QuotedTotal) FROM CostLine WHERE VersionId = @versionId AND Area = 'genreq'", new { versionId }) ?? 0;
        var precon = await PreconQuoted(w, versionId);
        var genconds = await Crew.GencondsTotal(w, versionId);
        // Rates page switches: a cost total, rate or indirect item that is switched off counts as 0
        var offCosts = (await w.Query<string>("SELECT MarkupCode FROM VersionMarkup WHERE VersionId = @versionId AND IsOff AND MarkupCode IN ('genreq','precon','genconds')", new { versionId })).ToHashSet();
        if (offCosts.Contains("genreq")) genreq = 0;
        if (offCosts.Contains("precon")) precon = 0;
        if (offCosts.Contains("genconds")) genconds = 0;
        var opt = await w.One<OptionsRow>("SELECT ApplyFeeExcludes, FeeExcludes, ExcludeGenreqFromDirect, ReservePrecon, ReserveGenconds FROM ProjectVersion WHERE VersionId = @versionId", new { versionId }) ?? new OptionsRow();
        var markups = (await w.Query<MarkupRow>("""
            SELECT t.MarkupCode AS Code, t.MarkupName AS Name, t.AppliesTo, CASE WHEN m.IsOff THEN 0 ELSE m.RatePct END AS RatePct,
                   CASE WHEN m.IsOff THEN NULL ELSE m.FixedAmount END AS FixedAmount, t.SortOrder, m.BudgetAmount
            FROM VersionMarkup m JOIN MarkupType t ON t.MarkupCode = m.MarkupCode
            WHERE m.VersionId = @versionId AND t.AppliesTo IN ('subtotal','contract','tax') AND t.MarkupCode NOT IN ('precon','genreq','genconds')
            """, new { versionId })).ToList();
        var indirect = (await w.Query<IndirectIn>("SELECT ItemName AS Name, CASE WHEN IsOff THEN 0 ELSE Amount END AS Amount, OutsideFee FROM IndirectItem WHERE VersionId = @versionId ORDER BY SortOrder, IndirectId", new { versionId })).ToList();
        var area = await w.Scalar<int>("SELECT GrossArea FROM ProjectVersion WHERE VersionId = @versionId", new { versionId });
        var zones = (await w.Query<ZoneRow2>("SELECT ZoneId, ZoneCode, FloorArea FROM Zone WHERE VersionId = @versionId ORDER BY SortOrder", new { versionId })).ToList();
        // share of the trades total in each zone = Σ trade contribution × trade share / trades total
        var zoneAmounts = (await w.Query<(string code, decimal amt)>("""
            SELECT z.ZoneCode, sum(coalesce(t.CarriedValue, t.LowestValue, 0) * tz.Share)
            FROM TradeZone tz JOIN Trade t ON t.TradeId = tz.TradeId AND t.ArchivedAt IS NULL JOIN Zone z ON z.ZoneId = tz.ZoneId
            WHERE t.VersionId = @versionId GROUP BY z.ZoneCode
            """, new { versionId })).ToDictionary(x => x.code, x => x.amt);
        var shares = zones.ToDictionary(z => z.ZoneCode, z => tradesTotal > 0 ? Math.Round(zoneAmounts.GetValueOrDefault(z.ZoneCode) / tradesTotal, 6) : 0m);
        // reserve % per zone (was: contingency per breakout area): the version's rate becomes the zones' rates weighted by each zone's share
        var zr = (await w.Query<(string code, decimal p, decimal d, decimal e)>("SELECT ZoneCode, ProjectReservePct, DesignReservePct, EscalationReservePct FROM Zone WHERE VersionId = @versionId", new { versionId })).ToList();
        foreach (var (code, pick) in new (string, Func<(string code, decimal p, decimal d, decimal e), decimal>)[] { ("reserveproject", z => z.p), ("reservedesign", z => z.d), ("reserveescalation", z => z.e) })
        {
            if (!zr.Any(z => pick(z) != 0) || tradesTotal <= 0) continue;
            var rate = Math.Round(zr.Sum(z => shares.GetValueOrDefault(z.code) * pick(z)), 5);
            var m = markups.FirstOrDefault(x => x.Code == code);
            if (m is null)
            {
                m = await w.One<MarkupRow>("SELECT MarkupCode AS Code, MarkupName AS Name, AppliesTo, SortOrder FROM MarkupType WHERE MarkupCode = @code", new { code });
                if (m is null) continue;
                markups.Add(m);
            }
            m.RatePct = rate; m.FixedAmount = null;
        }
        var input = new SummaryIn
        {
            TradesTotal = tradesTotal, GenreqTotal = genreq, PreconTotal = precon, GencondsTotal = genconds, GrossArea = area,
            Markups = markups.Select(m => new MarkupIn { Code = m.Code, Name = m.Name, AppliesTo = m.AppliesTo, RatePct = m.RatePct, FixedAmount = m.FixedAmount, SortOrder = m.SortOrder }).ToList(),
            Indirect = indirect, ZoneShares = zones.Count > 0 ? shares : null,
            Options = new SummaryOptions
            {
                FeeExcludes = opt.ApplyFeeExcludes ? [.. opt.FeeExcludes] : [],
                ExcludeGenreqFromDirect = opt.ExcludeGenreqFromDirect,
                ReservePrecon = [.. opt.ReservePrecon],
                ReserveGenconds = [.. opt.ReserveGenconds],
            },
        };
        return (input, zones, markups.ToDictionary(m => m.Code, m => m.BudgetAmount));
    }
    /// <summary>Reserve codes whose rate comes from the zones' reserve % (the roll-up page locks those rates).</summary>
    public static async Task<List<string>> ZoneReserveCodes(Work w, long versionId) => (await w.Query<string>("""
        SELECT c FROM (VALUES ('reserveproject'), ('reservedesign'), ('reserveescalation')) v(c)
        WHERE EXISTS (SELECT 1 FROM Zone z WHERE z.VersionId = @versionId AND CASE v.c WHEN 'reserveproject' THEN z.ProjectReservePct WHEN 'reservedesign' THEN z.DesignReservePct ELSE z.EscalationReservePct END <> 0)
        """, new { versionId })).ToList();
    public sealed class OptionsRow { public bool ApplyFeeExcludes { get; set; } public string[] FeeExcludes { get; set; } = []; public bool ExcludeGenreqFromDirect { get; set; } public string[] ReservePrecon { get; set; } = []; public string[] ReserveGenconds { get; set; } = []; }
    public sealed class ZoneRow2 { public long ZoneId { get; set; } public string ZoneCode { get; set; } = ""; public int FloorArea { get; set; } }

    /// <summary>
    /// Precon services quoted total (was: Calc_TotalPreconDeptQuoted) = (precon team labour + billable expenses) × Quoted %.
    /// Labour = task hours × each person's posting rate.
    /// </summary>
    public static async Task<decimal> PreconQuoted(Work w, long versionId)
    {
        var r = await w.One<(decimal labour, decimal billable, decimal pct)>("""
            SELECT coalesce((SELECT sum(round(t.Hours * p.PostingRate, 2)) FROM PreconMember p JOIN PreconMemberTask t ON t.PreconMemberId = p.PreconMemberId
                             JOIN VersionTask vt ON vt.VersionId = p.VersionId AND vt.ActivityId = t.ActivityId WHERE p.VersionId = v.VersionId), 0),
                   coalesce((SELECT sum(ActualTotal) FROM CostLine WHERE VersionId = v.VersionId AND Area = 'precon' AND IsBillable), 0),
                   v.PreconQuotedPct
            FROM ProjectVersion v WHERE v.VersionId = @versionId
            """, new { versionId });
        return Math.Round((r.labour + r.billable) * r.pct / 100m, 0, MidpointRounding.AwayFromZero);
    }

    public static async Task<SummaryResult> Summary(Work w, long versionId, string trigger, string? clientHash = null, long? tradeId = null)
    {
        var sw = Stopwatch.StartNew();
        var (input, zones, budgets) = await SummaryInput(w, versionId);
        var lines = Rules.Summary(input);
        await w.Exec("DELETE FROM RollupLine WHERE VersionId = @versionId", new { versionId });
        short order = 1;
        foreach (var l in lines)
        {
            var id = await w.Scalar<long>("""
                INSERT INTO RollupLine (VersionId, SortOrder, LineName, Kind, MarkupCode, RatePct, Amount, BudgetAmount, PerArea)
                VALUES (@versionId, @order, @name, @kind::RollupKind, @code, @rate, @amount, @budget, @perArea) RETURNING RollupLineId
                """, new { versionId, order = order++, name = l.Name, kind = l.Kind,
                           code = l.Kind is "markup" or "tax" ? l.Code : null, rate = l.RatePct, amount = l.Amount,
                           budget = l.Code is null ? null : budgets.GetValueOrDefault(l.Code), perArea = l.PerArea });
            foreach (var z in zones)
                if (l.Zones.TryGetValue(z.ZoneCode, out var za))
                    await w.Exec("INSERT INTO RollupZone (RollupLineId, ZoneId, Amount, PerArea) VALUES (@id, @zid, @za, @pa)",
                        new { id, zid = z.ZoneId, za, pa = z.FloorArea > 0 ? (decimal?)Rules.R2(za / z.FloorArea) : null });
        }
        var hash = Rules.TotalsHash(lines.Select(l => (decimal?)l.Amount));
        await w.Exec("UPDATE ProjectVersion SET TotalsHash = @hash WHERE VersionId = @versionId AND TotalsHash IS DISTINCT FROM @hash", new { hash, versionId });
        bool? matched = clientHash is null ? null : clientHash == hash;
        await w.Exec("INSERT INTO CalcRun (VersionId, TradeId, Trigger, ClientHash, ServerHash, Matched, DurationMs) VALUES (@versionId, @tradeId, @trigger, @clientHash, @hash, @matched, @ms)",
            new { versionId, tradeId, trigger, clientHash, hash, matched, ms = (int)sw.ElapsedMilliseconds });
        return new SummaryResult { Lines = lines, Hash = hash, Matched = matched, ZoneShares = input.ZoneShares ?? [] };
    }

    /// <summary>After an edit to one trade: that trade, then the roll-up.</summary>
    public static async Task<(TradeOut trade, SummaryResult summary)> TradeAndSummary(Work w, long tradeId, string trigger, string? clientHash = null)
    {
        var t = await Trade(w, tradeId);
        var vid = await w.Scalar<long>("SELECT VersionId FROM Trade WHERE TradeId = @tradeId", new { tradeId });
        return (t, await Summary(w, vid, trigger, clientHash, tradeId));
    }

    /// <summary>
    /// The same trade rules as <see cref="Rules.Trade"/>, written set-based for a whole version at once
    /// (imports of thousands of trades). Kept identical by the API test that compares both on the seed data.
    /// </summary>
    public static async Task<SummaryResult> VersionBulk(Work w, long versionId, string trigger)
    {
        await w.Exec("""
            UPDATE Bid b SET ScopeAdjustments = x.adj, TotalAmount = CASE WHEN b.BaseAmount IS NULL THEN NULL ELSE b.BaseAmount + x.adj END
            FROM (SELECT b2.BidId, coalesce(sum(a.Amount) FILTER (WHERE a.Kind = 'amount' AND a.Amount IS NOT NULL AND l.Section IN ('baserequirement','tradeitem')
                                                              AND NOT l.SkipInAdjustments AND NOT l.IsHeading AND l.ArchivedAt IS NULL), 0) AS adj
                  FROM Bid b2 JOIN Trade t ON t.TradeId = b2.TradeId AND t.VersionId = @versionId
                  LEFT JOIN ScopeAnswer a ON a.BidId = b2.BidId LEFT JOIN ScopeLine l ON l.LineId = a.LineId
                  WHERE b2.ArchivedAt IS NULL GROUP BY b2.BidId) x
            WHERE b.BidId = x.BidId AND (b.ScopeAdjustments IS DISTINCT FROM x.adj OR b.TotalAmount IS DISTINCT FROM CASE WHEN b.BaseAmount IS NULL THEN NULL ELSE b.BaseAmount + x.adj END);

            UPDATE Trade t SET LowestValue = x.low, CarriedValue = x.carried, BudgetDelta = x.carried - t.BudgetValue,
                   BuyoutPct = CASE WHEN x.carried IS NOT NULL AND t.BudgetValue <> 0 THEN floor((t.BudgetValue - x.carried) / t.BudgetValue * 10000 + 0.5) / 100 END, NeedsRecalc = false
            FROM (SELECT t2.TradeId,
                         (SELECT min(b.TotalAmount) FROM Bid b WHERE b.TradeId = t2.TradeId AND b.ArchivedAt IS NULL AND b.IsPosted AND b.State <> 'disqualified') AS low,
                         coalesce((SELECT b.TotalAmount + t2.Adjustment FROM Bid b WHERE b.TradeId = t2.TradeId AND b.ArchivedAt IS NULL AND b.IsCarried AND b.TotalAmount IS NOT NULL LIMIT 1),
                                  CASE WHEN NOT EXISTS (SELECT 1 FROM Bid b WHERE b.TradeId = t2.TradeId AND b.ArchivedAt IS NULL AND b.IsCarried AND b.TotalAmount IS NOT NULL)
                                       THEN nullif(t2.PlugAmount, 0) END) AS carried
                  FROM Trade t2 WHERE t2.VersionId = @versionId AND t2.ArchivedAt IS NULL) x
            WHERE t.TradeId = x.TradeId;
            """, new { versionId });
        await DeriveZoneSplit(w, versionId);
        return await Summary(w, versionId, trigger);
    }

    /// <summary>Every trade of a version (imports, version copies, budget changes).</summary>
    public static async Task<SummaryResult> Version(Work w, long versionId, string trigger)
    {
        if (await w.Scalar<int>("SELECT count(*) FROM Trade WHERE VersionId = @versionId AND ArchivedAt IS NULL", new { versionId }) > 40)
            return await VersionBulk(w, versionId, trigger);
        foreach (var id in await w.Query<long>("SELECT TradeId FROM Trade WHERE VersionId = @versionId AND ArchivedAt IS NULL", new { versionId }))
            await Trade(w, id);
        return await Summary(w, versionId, trigger);
    }
}
