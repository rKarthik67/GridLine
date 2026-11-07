using Dapper;
using Gridline.Infrastructure;
using Npgsql;

namespace Gridline.Calc;

/// <summary>
/// Alternates & VE (PriceOption), allowances (ProvisionalSum) and unit prices (UnitRate), calculated like the original app:
/// sp_calcAlternates, sp_getAllowanceItemsGrid / PutAllowanceApplyToTrade and sp_calcUnitPrices. None of them change the contract total;
/// allowance lines live in the trades' scope match grids, so they count in the bids like any other line.
/// </summary>
public static class PricingExtras
{
    /// <summary>The indirect costs an alternate or unit price can carry, in the original's order. Code = roll-up markup code or cost line.</summary>
    public static readonly (string code, string name)[] AltIndirects =
    [
        ("genreq", "Project overheads"), ("precon", "Precon services"), ("genconds", "General conditions"),
        ("reserveproject", "Project reserve"), ("reservedesign", "Design reserve"), ("reserveescalation", "Escalation reserve"),
        ("bond", "P & P bond"), ("cover", "Works cover"), ("fee", "Fee"), ("liability", "L & P insurance"),
    ];
    public static readonly (string code, string name)[] UnitIndirects =
    [
        ("precon", "Precon services"), ("genconds", "General conditions"),
        ("reserveproject", "Project reserve"), ("reservedesign", "Design reserve"), ("reserveescalation", "Escalation reserve"),
        ("bond", "P & P bond"), ("cover", "Works cover"), ("fee", "Fee"), ("liability", "L & P insurance"),
    ];
    static readonly Dictionary<string, string> CostLineCodes = new() { ["Project overheads"] = "genreq", ["Precon services"] = "precon", ["General conditions"] = "genconds" };

    /// <summary>The version's stored roll-up: amount per markup code / cost line, the direct cost total and the contract total.</summary>
    public sealed record Rollup(Dictionary<string, decimal> Amounts, HashSet<string> Present, decimal DirectCost, decimal Contract);
    public static async Task<Rollup> LoadRollup(NpgsqlConnection c, NpgsqlTransaction? t, long versionId)
    {
        var rows = (await c.QueryAsync<(string name, string kind, string? code, decimal? rate, decimal amount)>(
            "SELECT LineName, Kind::text, MarkupCode, RatePct, Amount FROM RollupLine WHERE VersionId = @versionId ORDER BY SortOrder", new { versionId }, t)).ToList();
        var amounts = new Dictionary<string, decimal>(); var present = new HashSet<string>();
        foreach (var r in rows)
        {
            var code = r.code ?? (CostLineCodes.TryGetValue(r.name, out var cc) ? cc : null);
            if (code is null) continue;
            amounts[code] = r.amount;
            if (r.kind == "cost" || r.amount != 0 || (r.rate ?? 0) != 0) present.Add(code);
        }
        decimal Named(string n) => rows.FirstOrDefault(r => r.name == n).amount;
        return new Rollup(amounts, present, Named("Direct cost total"), Named("Contract total"));
    }

    // ---------------------------------------------------------------------------------------------- alternates & VE
    public sealed class OptionRow
    {
        public long OptionId { get; set; } public string Kind { get; set; } = ""; public int OptionIndex { get; set; } public string OptionNo { get; set; } = "";
        public string Description { get; set; } = ""; public string Status { get; set; } = ""; public decimal QuotedPct { get; set; } public string? Comments { get; set; }
        public string[] IncludeCodes { get; set; } = []; public DateTime ChangedAt { get; set; } public string? ChangedByName { get; set; } public int RowRev { get; set; }
    }
    public sealed class OptionTradeRow { public long OptionId { get; set; } public long TradeId { get; set; } public string CsiCode { get; set; } = ""; public string TradeName { get; set; } = ""; public decimal? Amount { get; set; } public string? Note { get; set; } public string? Carried { get; set; } }

    public static async Task<object> Alternates(NpgsqlConnection c, NpgsqlTransaction? t, long versionId)
    {
        var opts = (await c.QueryAsync<OptionRow>("""
            SELECT o.OptionId, o.Kind, o.OptionIndex, o.OptionNo, o.Description, o.Status, o.QuotedPct, o.Comments, o.IncludeCodes, o.ChangedAt, o.RowRev,
                   (SELECT u.GivenName || ' ' || u.FamilyName FROM AppUser u WHERE u.UserId = o.ChangedBy) AS ChangedByName
            FROM PriceOption o WHERE o.VersionId = @versionId ORDER BY o.Kind, o.OptionIndex, o.OptionId
            """, new { versionId }, t)).ToList();
        var trades = (await c.QueryAsync<OptionTradeRow>("""
            SELECT x.OptionId, x.TradeId, tr.CsiCode, tr.TradeName, x.CarriedAmount AS Amount, x.Note,
                   (SELECT b.BidderName FROM Bid b WHERE b.TradeId = tr.TradeId AND b.IsCarried AND b.ArchivedAt IS NULL LIMIT 1) AS Carried
            FROM PriceOptionTrade x JOIN PriceOption o ON o.OptionId = x.OptionId JOIN Trade tr ON tr.TradeId = x.TradeId AND tr.ArchivedAt IS NULL
            WHERE o.VersionId = @versionId AND (coalesce(x.CarriedAmount, 0) <> 0 OR coalesce(x.Note, '') <> '')
            ORDER BY tr.CsiCode
            """, new { versionId }, t)).ToList();
        var ru = await LoadRollup(c, t, versionId);
        var items = opts.Select(o =>
        {
            var tr = trades.Where(x => x.OptionId == o.OptionId).ToList();
            var total = tr.Sum(x => x.Amount ?? 0);
            var subtotal = R2(total * o.QuotedPct / 100m);
            var ratio = ru.DirectCost > 0 ? subtotal / ru.DirectCost : 0m;
            var indirects = AltIndirects.Where(i => !i.code.StartsWith("reserve") || ru.Present.Contains(i.code)).Select(i =>
            {
                var included = o.IncludeCodes.Contains(i.code);
                // project overheads, precon and general conditions carry their stored budget in the original, which is 0
                var amount = i.code is "genreq" or "precon" or "genconds" ? 0m : R2(ru.Amounts.GetValueOrDefault(i.code) * ratio);
                return new { i.code, i.name, included, amount = included ? amount : 0m, budget = amount };
            }).ToList();
            return new
            {
                optionId = o.OptionId, kind = o.Kind, optionIndex = o.OptionIndex, code = o.OptionNo, description = o.Description, status = o.Status,
                quotedPct = o.QuotedPct, comments = o.Comments, includeCodes = o.IncludeCodes, changedAt = o.ChangedAt, changedByName = o.ChangedByName, rowRev = o.RowRev,
                total, subtotalQuoted = subtotal, indirects, totalQuoted = subtotal + indirects.Sum(x => x.amount),
                trades = tr.Select(x => new { tradeId = x.TradeId, csiCode = x.CsiCode, tradeName = x.TradeName, amount = x.Amount ?? 0, note = x.Note, carried = x.Carried }),
            };
        }).ToList();
        return new { items, directCost = ru.DirectCost };
    }

    /// <summary>Every trade has a row per alternate (like sp_addAlternateTradeItems); missing rows are added when read.</summary>
    public static async Task<object> TradeAlternates(NpgsqlConnection c, NpgsqlTransaction? t, long tradeId) =>
        await c.QueryAsync("""
            SELECT o.OptionId AS "optionId", o.Kind AS "kind", o.OptionNo AS "code", o.Description AS "description", o.Status AS "status",
                   x.CarriedAmount AS "amount", x.Note AS "note"
            FROM Trade tr JOIN PriceOption o ON o.VersionId = tr.VersionId LEFT JOIN PriceOptionTrade x ON x.OptionId = o.OptionId AND x.TradeId = tr.TradeId
            WHERE tr.TradeId = @tradeId ORDER BY o.Kind, o.OptionIndex, o.OptionId
            """, new { tradeId }, t);

    /// <summary>ALT-1, ALT-2 … and VE-1, VE-2 … in the order they were added (sp_calcAlternates).</summary>
    public static Task Renumber(Work w, long versionId) => w.Exec("""
        UPDATE PriceOption o SET OptionIndex = n.i, OptionNo = CASE o.Kind WHEN 'saving' THEN 'VE-' ELSE 'ALT-' END || n.i
        FROM (SELECT OptionId, row_number() OVER (PARTITION BY Kind ORDER BY OptionId) AS i FROM PriceOption WHERE VersionId = @versionId) n
        WHERE n.OptionId = o.OptionId AND (o.OptionIndex IS DISTINCT FROM n.i OR o.OptionNo IS DISTINCT FROM (CASE o.Kind WHEN 'saving' THEN 'VE-' ELSE 'ALT-' END || n.i));
        UPDATE ProvisionalSum p SET SumIndex = n.i FROM (SELECT SumId, row_number() OVER (ORDER BY SumId) AS i FROM ProvisionalSum WHERE VersionId = @versionId) n
        WHERE n.SumId = p.SumId AND p.SumIndex IS DISTINCT FROM n.i;
        UPDATE UnitRate r SET UnitIndex = n.i FROM (SELECT UnitRateId, row_number() OVER (ORDER BY UnitRateId) AS i FROM UnitRate WHERE VersionId = @versionId) n
        WHERE n.UnitRateId = r.UnitRateId AND r.UnitIndex IS DISTINCT FROM n.i;
        """, new { versionId });

    // ---------------------------------------------------------------------------------------------- allowances
    public sealed class SumRow { public long SumId { get; set; } public string Kind { get; set; } = ""; public int SumIndex { get; set; } public string Description { get; set; } = ""; public long? TradeId { get; set; } public DateTime ChangedAt { get; set; } public string? ChangedByName { get; set; } public int RowRev { get; set; } }
    public sealed class SumTradeRow { public long SumId { get; set; } public long TradeId { get; set; } public string CsiCode { get; set; } = ""; public string TradeName { get; set; } = ""; public bool Included { get; set; } public long? LineId { get; set; } public decimal Contribution { get; set; } public string? Carried { get; set; } public int Priced { get; set; } public int Bidders { get; set; } }

    public static async Task<object> Allowances(NpgsqlConnection c, NpgsqlTransaction? t, long versionId)
    {
        var sums = (await c.QueryAsync<SumRow>("""
            SELECT p.SumId, p.Kind, p.SumIndex, p.Description, p.TradeId, p.ChangedAt, p.RowRev,
                   (SELECT u.GivenName || ' ' || u.FamilyName FROM AppUser u WHERE u.UserId = p.ChangedBy) AS ChangedByName
            FROM ProvisionalSum p WHERE p.VersionId = @versionId ORDER BY p.SumIndex, p.SumId
            """, new { versionId }, t)).ToList();
        // a trade's contribution = the picked bid's amount on the allowance's line in that trade (PutAllowanceApplyToTrade)
        var trades = (await c.QueryAsync<SumTradeRow>("""
            SELECT x.SumId, x.TradeId, tr.CsiCode, tr.TradeName, x.Included, l.LineId,
                   coalesce((SELECT CASE WHEN a.Kind = 'amount' THEN a.Amount ELSE 0 END FROM ScopeAnswer a JOIN Bid b ON b.BidId = a.BidId
                             WHERE a.LineId = l.LineId AND b.IsCarried AND b.ArchivedAt IS NULL LIMIT 1), 0) AS Contribution,
                   (SELECT b.BidderName FROM Bid b WHERE b.TradeId = tr.TradeId AND b.IsCarried AND b.ArchivedAt IS NULL LIMIT 1) AS Carried,
                   (SELECT count(*) FROM ScopeAnswer a JOIN Bid b ON b.BidId = a.BidId WHERE a.LineId = l.LineId AND a.Kind = 'amount' AND b.ArchivedAt IS NULL)::int AS Priced,
                   (SELECT count(*) FROM Bid b WHERE b.TradeId = tr.TradeId AND b.ArchivedAt IS NULL)::int AS Bidders
            FROM ProvisionalSumTrade x JOIN ProvisionalSum p ON p.SumId = x.SumId JOIN Trade tr ON tr.TradeId = x.TradeId AND tr.ArchivedAt IS NULL
            LEFT JOIN LATERAL (SELECT LineId FROM ScopeLine s WHERE s.SumId = x.SumId AND s.TradeId = x.TradeId AND s.ArchivedAt IS NULL ORDER BY LineId LIMIT 1) l ON true
            WHERE p.VersionId = @versionId ORDER BY tr.CsiCode
            """, new { versionId }, t)).ToList();
        var items = sums.Select(s =>
        {
            var tr = trades.Where(x => x.SumId == s.SumId).ToList();
            var trade = s.TradeId is long tid ? tr.FirstOrDefault(x => x.TradeId == tid) : null;
            return new
            {
                sumId = s.SumId, kind = s.Kind, index = s.SumIndex, description = s.Description, tradeId = s.TradeId,
                tradeName = trade is null ? null : $"{trade.CsiCode} {trade.TradeName}", changedAt = s.ChangedAt, changedByName = s.ChangedByName, rowRev = s.RowRev,
                total = tr.Where(x => x.Included).Sum(x => x.Contribution),
                trades = tr.Select(x => new { tradeId = x.TradeId, csiCode = x.CsiCode, tradeName = x.TradeName, included = x.Included, lineId = x.LineId,
                                              contribution = x.Contribution, carried = x.Carried, priced = x.Priced, bidders = x.Bidders }),
            };
        }).ToList();
        return new
        {
            items,
            summary = new
            {
                ownerPrescribed = items.Where(i => i.kind == "ownerprescribed").Sum(i => i.total),
                miscellaneous = items.Where(i => i.kind != "ownerprescribed").Sum(i => i.total),
                total = items.Sum(i => i.total),
            },
        };
    }

    /// <summary>Owner prescribed allowances get a line on every trade (and a new trade gets them too); a miscellaneous one only on its trade.</summary>
    public static Task AddAllowanceLines(Work w, long tradeId, long? sumId = null) => w.Exec("""
        INSERT INTO ProvisionalSumTrade (SumId, TradeId)
        SELECT p.SumId, t.TradeId FROM Trade t JOIN ProvisionalSum p ON p.VersionId = t.VersionId AND (p.TradeId IS NULL OR p.TradeId = t.TradeId)
        WHERE t.TradeId = @tradeId AND (@sumId::bigint IS NULL OR p.SumId = @sumId) ON CONFLICT DO NOTHING;
        INSERT INTO ScopeLine (TradeId, Section, SortKey, LineText, SumId, ChangedBy)
        SELECT t.TradeId, 'tradeitem', coalesce((SELECT max(SortKey) FROM ScopeLine s WHERE s.TradeId = t.TradeId), 0) + 1000 + p.SumIndex, p.Description, p.SumId, @u
        FROM Trade t JOIN ProvisionalSum p ON p.VersionId = t.VersionId AND (p.TradeId IS NULL OR p.TradeId = t.TradeId)
        WHERE t.TradeId = @tradeId AND (@sumId::bigint IS NULL OR p.SumId = @sumId)
          AND NOT EXISTS (SELECT 1 FROM ScopeLine s WHERE s.TradeId = t.TradeId AND s.SumId = p.SumId AND s.ArchivedAt IS NULL);
        """, new { tradeId, sumId, u = w.User.UserId });

    // ---------------------------------------------------------------------------------------------- unit prices
    public sealed class UnitRow { public long UnitRateId { get; set; } public int UnitIndex { get; set; } public string Description { get; set; } = ""; public string? Unit { get; set; } public decimal QuotedPct { get; set; } public string? Comments { get; set; } public string[] IncludeCodes { get; set; } = []; public DateTime ChangedAt { get; set; } public string? ChangedByName { get; set; } public int RowRev { get; set; } }
    public sealed class UnitTradeRow { public long UnitRateId { get; set; } public long TradeId { get; set; } public string CsiCode { get; set; } = ""; public string TradeName { get; set; } = ""; public decimal? Price { get; set; } public string? Note { get; set; } public bool IsSelected { get; set; } }

    public static async Task<object> UnitPrices(NpgsqlConnection c, NpgsqlTransaction? t, long versionId)
    {
        var rows = (await c.QueryAsync<UnitRow>("""
            SELECT r.UnitRateId, r.UnitIndex, r.Description, r.Unit, r.QuotedPct, r.Comments, r.IncludeCodes, r.ChangedAt, r.RowRev,
                   (SELECT u.GivenName || ' ' || u.FamilyName FROM AppUser u WHERE u.UserId = r.ChangedBy) AS ChangedByName
            FROM UnitRate r WHERE r.VersionId = @versionId ORDER BY r.UnitIndex, r.UnitRateId
            """, new { versionId }, t)).ToList();
        var trades = (await c.QueryAsync<UnitTradeRow>("""
            SELECT x.UnitRateId, x.TradeId, tr.CsiCode, tr.TradeName, x.Price, x.Note, x.IsSelected
            FROM UnitRateTrade x JOIN UnitRate r ON r.UnitRateId = x.UnitRateId JOIN Trade tr ON tr.TradeId = x.TradeId AND tr.ArchivedAt IS NULL
            WHERE r.VersionId = @versionId AND (coalesce(x.Price, 0) > 0 OR x.IsSelected) ORDER BY tr.CsiCode
            """, new { versionId }, t)).ToList();
        var ru = await LoadRollup(c, t, versionId);
        var items = rows.Select(r =>
        {
            var tr = trades.Where(x => x.UnitRateId == r.UnitRateId).ToList();
            var sel = tr.FirstOrDefault(x => x.IsSelected);
            var price = sel?.Price ?? 0;
            var discounted = price * r.QuotedPct / 100m;
            // each indirect is that roll-up line's share of the contract (the original's "…QuotedPCContract" properties)
            var indirects = UnitIndirects.Where(i => !i.code.StartsWith("reserve") || ru.Present.Contains(i.code)).Select(i =>
            {
                var pct = ru.Contract != 0 ? ru.Amounts.GetValueOrDefault(i.code) / ru.Contract * 100m : 0m;
                var included = r.IncludeCodes.Contains(i.code);
                return new { i.code, i.name, pct = Math.Round(pct, 4), included, amount = included ? Math.Round(discounted * pct / 100m, 4) : 0m };
            }).ToList();
            return new
            {
                unitRateId = r.UnitRateId, index = r.UnitIndex, description = r.Description, unit = r.Unit, quotedPct = r.QuotedPct, comments = r.Comments,
                includeCodes = r.IncludeCodes, changedAt = r.ChangedAt, changedByName = r.ChangedByName, rowRev = r.RowRev,
                selectedTradeId = sel?.TradeId, price, discountedPrice = Math.Round(discounted, 4), indirects,
                quotedPrice = Math.Round(discounted + indirects.Sum(x => x.amount), 4),
                trades = tr.Select(x => new { tradeId = x.TradeId, csiCode = x.CsiCode, tradeName = x.TradeName, price = x.Price ?? 0, note = x.Note, isSelected = x.IsSelected }),
            };
        }).ToList();
        return new { items };
    }

    public static async Task<object> TradeUnitPrices(NpgsqlConnection c, NpgsqlTransaction? t, long tradeId) =>
        await c.QueryAsync("""
            SELECT r.UnitRateId AS "unitRateId", r.UnitIndex AS "index", r.Description AS "description", r.Unit AS "unit",
                   x.Price AS "price", x.Note AS "note", coalesce(x.IsSelected, false) AS "isSelected"
            FROM Trade tr JOIN UnitRate r ON r.VersionId = tr.VersionId LEFT JOIN UnitRateTrade x ON x.UnitRateId = r.UnitRateId AND x.TradeId = tr.TradeId
            WHERE tr.TradeId = @tradeId ORDER BY r.Description, r.UnitRateId
            """, new { tradeId }, t);

    static decimal R2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
