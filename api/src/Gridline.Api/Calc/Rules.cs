using System.Globalization;
using System.Text.RegularExpressions;

namespace Gridline.Calc;

// The C# copy of @gridline/calc (web/packages/calc/src/index.ts). Both must pass test/vectors.json.
// Money is decimal; rounding matches JavaScript's Math.round (half toward +infinity) to cents.

public sealed class ScopeLineIn { public long LineId { get; set; } public string Section { get; set; } = "tradeitem"; public bool IsHeading { get; set; } public bool SkipInAdjustments { get; set; } public bool Archived { get; set; } }
public sealed class AnswerIn { public long LineId { get; set; } public long BidId { get; set; } public string Kind { get; set; } = "blank"; public decimal? Amount { get; set; } }
public sealed class BidIn { public long BidId { get; set; } public bool IsPosted { get; set; } public bool IsCarried { get; set; } public string State { get; set; } = "invited"; public decimal? BaseAmount { get; set; } }
public sealed class TradeIn
{
    public long TradeId { get; set; }
    public decimal Adjustment { get; set; }
    public decimal? PlugAmount { get; set; }
    public decimal? BudgetValue { get; set; }
    public List<BidIn> Bids { get; set; } = [];
    public List<ScopeLineIn>? Lines { get; set; }
    public List<AnswerIn>? Answers { get; set; }
    public Dictionary<string, decimal>? ZoneShares { get; set; }
}
public sealed class BidOut { public long BidId { get; set; } public decimal ScopeAdjustments { get; set; } public decimal? TotalAmount { get; set; } public int? Rank { get; set; } }
public sealed class TradeOut
{
    public long TradeId { get; set; }
    public List<BidOut> Bids { get; set; } = [];
    public decimal? LowestValue { get; set; }
    public decimal? CarriedValue { get; set; }
    public decimal? BudgetDelta { get; set; }
    public bool UsesPlug { get; set; }
    public Dictionary<string, decimal> ZoneAmounts { get; set; } = [];
    public decimal Contribution => CarriedValue ?? LowestValue ?? 0;
}

public sealed class MarkupIn { public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string AppliesTo { get; set; } = "subtotal"; public decimal RatePct { get; set; } public decimal? FixedAmount { get; set; } public int SortOrder { get; set; } }
public sealed class IndirectIn { public string Name { get; set; } = ""; public decimal Amount { get; set; } public bool OutsideFee { get; set; } }
public sealed class SummaryIn
{
    public decimal TradesTotal { get; set; }
    public decimal GenreqTotal { get; set; }
    public decimal PreconTotal { get; set; }
    public decimal GencondsTotal { get; set; }
    public List<MarkupIn> Markups { get; set; } = [];
    public List<IndirectIn> Indirect { get; set; } = [];
    public decimal GrossArea { get; set; }
    public Dictionary<string, decimal>? ZoneShares { get; set; }
    public SummaryOptions? Options { get; set; }
}
/// <summary>Fee carve-outs (was: fee exclusions) and bid total options — see the TypeScript SummaryOptions.</summary>
public sealed class SummaryOptions
{
    public List<string>? FeeExcludes { get; set; }
    public bool ExcludeGenreqFromDirect { get; set; }
    public List<string>? ReservePrecon { get; set; }
    public List<string>? ReserveGenconds { get; set; }
}
public sealed class SummaryLine
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? Code { get; set; }
    public decimal? RatePct { get; set; }
    public decimal Amount { get; set; }
    public decimal? PerArea { get; set; }
    public decimal? PctOfContract { get; set; }
    public Dictionary<string, decimal> Zones { get; set; } = [];
}
public sealed record ParsedAnswer(string Kind, decimal? Amount, string? Text, string? Formula);

public static class Rules
{
    public static decimal R2(decimal n) => Math.Floor(n * 100m + 0.5m) / 100m;

    public static Dictionary<long, decimal> ScopeAdjustments(IEnumerable<ScopeLineIn>? lines, IEnumerable<AnswerIn>? answers)
    {
        var counted = (lines ?? []).Where(l => !l.Archived && !l.IsHeading && !l.SkipInAdjustments && (l.Section is "baserequirement" or "tradeitem"))
                                   .Select(l => l.LineId).ToHashSet();
        var o = new Dictionary<long, decimal>();
        foreach (var a in answers ?? [])
        {
            if (a.Kind != "amount" || a.Amount is null || !counted.Contains(a.LineId)) continue;
            o[a.BidId] = R2(o.GetValueOrDefault(a.BidId) + a.Amount.Value);
        }
        return o;
    }

    public static TradeOut Trade(TradeIn t)
    {
        var adj = ScopeAdjustments(t.Lines, t.Answers);
        var bids = t.Bids.Select(b => new BidOut
        {
            BidId = b.BidId, ScopeAdjustments = adj.GetValueOrDefault(b.BidId),
            TotalAmount = b.BaseAmount is null ? null : R2(b.BaseAmount.Value + adj.GetValueOrDefault(b.BidId)),
        }).ToList();
        var eligible = bids.Where((o, i) => t.Bids[i].IsPosted && t.Bids[i].State != "disqualified" && o.TotalAmount != null).ToList();
        var sorted = eligible.OrderBy(o => o.TotalAmount).ToList();   // stable, like Array.sort
        for (var i = 0; i < sorted.Count; i++)
            sorted[i].Rank = i > 0 && sorted[i - 1].TotalAmount == sorted[i].TotalAmount ? sorted[i - 1].Rank : i + 1;
        decimal? lowest = sorted.Count > 0 ? sorted[0].TotalAmount : null;
        var ci = t.Bids.FindIndex(b => b.IsCarried);
        decimal? carried = ci >= 0 ? bids[ci].TotalAmount : null;
        var usesPlug = carried is null && t.PlugAmount is not null && t.PlugAmount != 0;
        decimal? carriedValue = carried is not null ? R2(carried.Value + t.Adjustment) : usesPlug ? R2(t.PlugAmount!.Value) : null;
        var basis = carriedValue ?? lowest ?? 0;
        var zones = new Dictionary<string, decimal>();
        foreach (var (z, s) in t.ZoneShares ?? []) zones[z] = R2(basis * s);
        return new TradeOut
        {
            TradeId = t.TradeId, Bids = bids, LowestValue = lowest, CarriedValue = carriedValue, UsesPlug = usesPlug, ZoneAmounts = zones,
            BudgetDelta = carriedValue is not null && t.BudgetValue is not null ? R2(carriedValue.Value - t.BudgetValue.Value) : null,
        };
    }

    public static List<SummaryLine> Summary(SummaryIn s)
    {
        var lines = new List<SummaryLine>();
        decimal Amt(MarkupIn m, decimal b) => R2(m.FixedAmount ?? b * (m.RatePct / 100m));
        List<MarkupIn> By(string a) => s.Markups.Where(m => m.AppliesTo == a).OrderBy(m => m.SortOrder).ToList();
        void Add(string name, string kind, decimal amount, string? code = null, decimal? rate = null) =>
            lines.Add(new SummaryLine { Name = name, Kind = kind, Code = code, RatePct = rate, Amount = amount });

        var o = s.Options ?? new SummaryOptions();
        var ex = (o.FeeExcludes ?? []).ToHashSet(); var rp = (o.ReservePrecon ?? []).ToHashSet(); var rg = (o.ReserveGenconds ?? []).ToHashSet(); var gc = s.GencondsTotal;
        Add("Trades total", "trades", R2(s.TradesTotal));
        Add("Project overheads", "cost", R2(s.GenreqTotal), "genreq");
        var direct = R2(s.TradesTotal + (o.ExcludeGenreqFromDirect ? 0 : s.GenreqTotal));
        Add("Direct cost total", "calculated", direct);
        Add("Precon services", "cost", R2(s.PreconTotal), "precon");
        Add("General conditions", "cost", R2(gc), "genconds");
        var subtotal = s.TradesTotal + s.GenreqTotal + s.PreconTotal + gc; var reserves = 0m;
        foreach (var m in By("subtotal")) { var a = Amt(m, direct + (rp.Contains(m.Code) ? s.PreconTotal : 0) + (rg.Contains(m.Code) ? gc : 0)); subtotal += a; if (m.Code.StartsWith("reserve")) reserves += a; Add(m.Name, "markup", a, m.Code, m.RatePct); }
        subtotal = R2(subtotal);
        Add("Subtotal", "calculated", subtotal);
        var contract = subtotal;
        var cm = By("contract");
        var carved = (ex.Contains("genreq") ? s.GenreqTotal : 0) + (ex.Contains("precon") ? s.PreconTotal : 0) + (ex.Contains("genconds") ? gc : 0) + (ex.Contains("reserves") ? reserves : 0);
        foreach (var m in cm.Where(m => m.Code != "fee")) { var a = Amt(m, subtotal); contract += a; if (ex.Contains(m.Code)) carved += a; Add(m.Name, "markup", a, m.Code, m.RatePct); }
        var insideFee = s.Indirect.Where(i => !i.OutsideFee).Sum(i => i.Amount);
        foreach (var m in cm.Where(m => m.Code == "fee")) { var a = Amt(m, contract + insideFee - carved); contract += a; Add(m.Name, "markup", a, m.Code, m.RatePct); }
        foreach (var i in s.Indirect) { contract += i.Amount; Add(i.Name, "custom", R2(i.Amount)); }
        contract = R2(contract);
        Add("Contract total", "calculated", contract);
        var withTax = contract;
        foreach (var m in By("tax")) { var a = Amt(m, contract); withTax += a; Add(m.Name, "tax", a, m.Code, m.RatePct); }
        Add("Contract total with sales tax", "calculated", R2(withTax));

        var shares = s.ZoneShares ?? [];
        foreach (var l in lines)
        {
            l.PerArea = s.GrossArea > 0 ? R2(l.Amount / s.GrossArea) : null;
            l.PctOfContract = contract != 0 ? R2(l.Amount / contract * 100m) : null;
            l.Zones = shares.ToDictionary(kv => kv.Key, kv => R2(l.Amount * kv.Value));
        }
        return lines;
    }

    /// <summary>FNV-1a over the cent-formatted values, identical to the TypeScript totalsHash.</summary>
    public static string TotalsHash(IEnumerable<decimal?> values)
    {
        uint h = 2166136261;
        foreach (var v in values)
        {
            var s = v is null ? "~" : v.Value.ToString("0.00", CultureInfo.InvariantCulture);
            foreach (var ch in s) { h ^= ch; h *= 16777619; }
            h ^= 124;
        }
        return h.ToString("x8");
    }

    static readonly Regex Incl = new(@"^(included|incl|inc|✓|y|yes)$", RegexOptions.IgnoreCase);
    static readonly Regex Excl = new(@"^(excluded|excl|exc|✕|x|n|no)$", RegexOptions.IgnoreCase);
    static readonly Regex Num = new(@"^[+-]?\d+(\.\d+)?$");
    static readonly Regex Paren = new(@"^\(\d+(\.\d+)?\)$");
    static readonly Regex Strip = new(@"[$,\s]");

    public static ParsedAnswer ParseAnswer(string? input)
    {
        var s = (input ?? "").Trim();
        if (s.Length == 0) return new("blank", null, null, null);
        if (Incl.IsMatch(s)) return new("included", null, null, null);
        if (Excl.IsMatch(s)) return new("excluded", null, null, null);
        if (s.Equals("price", StringComparison.OrdinalIgnoreCase)) return new("price", null, null, null);
        if (s.StartsWith('='))
        {
            var expr = Strip.Replace(s[1..], "");
            if (Regex.IsMatch(expr, @"^[0-9+\-*/().]+$") && Arithmetic.Eval(expr) is decimal v) return new("amount", R2(v), null, s);
            return new("text", null, s, null);
        }
        var n = Strip.Replace(s, "");
        if (Num.IsMatch(n)) return new("amount", R2(decimal.Parse(n, CultureInfo.InvariantCulture)), null, null);
        if (Paren.IsMatch(n)) return new("amount", -R2(decimal.Parse(n[1..^1], CultureInfo.InvariantCulture)), null, null);
        return new("text", null, s, null);
    }
}

/// <summary>+ - * / and brackets, no eval. Returns null for anything else (or divide by zero).</summary>
public static class Arithmetic
{
    public static decimal? Eval(string expr)
    {
        var i = 0;
        char Peek() => i < expr.Length ? expr[i] : '\0';
        decimal? Num()
        {
            if (Peek() == '(') { i++; var v = Add(); if (Peek() != ')') return null; i++; return v; }
            if (Peek() == '-') { i++; var v = Num(); return v is null ? null : -v; }
            var m = Regex.Match(expr[i..], @"^\d+(\.\d+)?");
            if (!m.Success) return null;
            i += m.Length; return decimal.Parse(m.Value, CultureInfo.InvariantCulture);
        }
        decimal? Mul()
        {
            var v = Num(); if (v is null) return null;
            while (Peek() is '*' or '/')
            {
                var op = expr[i++]; var r = Num(); if (r is null) return null;
                if (op == '/' && r == 0) return null;
                v = op == '*' ? v * r : v / r;
            }
            return v;
        }
        decimal? Add()
        {
            var v = Mul(); if (v is null) return null;
            while (Peek() is '+' or '-') { var op = expr[i++]; var r = Mul(); if (r is null) return null; v = op == '+' ? v + r : v - r; }
            return v;
        }
        try { var v = Add(); return v is not null && i == expr.Length ? v : null; }
        catch (OverflowException) { return null; }
    }
}
