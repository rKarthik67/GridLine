using System.Globalization;

namespace Gridline.Import;

public sealed record Issue(string Sheet, int? Row, string? Column, string Severity, string Message);

public sealed class ZoneRow { public int Row; public string Code = ""; public string Name = ""; public int Area; public decimal? Units; public decimal? SplitPct; }
public sealed class TradeRow
{
    public int Row; public string Code = ""; public string Title = ""; public decimal? Budget; public List<(string zone, decimal share)> Split = [];
    public string? Kit; public string? Estimator; public DateOnly? Due; public string? Notes;
}
public sealed class BidderRow
{
    public int Row; public string Code = ""; public string Partner = ""; public string? Contact; public string? Email; public string? Phone;
    public string State = "invited"; public decimal? Base; public string? Zone; public bool Diverse; public string? Notes;
}
public sealed class CrewRow { public int Row; public string Role = ""; public string? Member; public int Start; public int End; public decimal Fte = 1; public decimal? Rate; public bool PerDiem; public string Phase = "build"; }
public sealed class RequirementRow { public int Row; public string Section = ""; public string? Code; public string Text = ""; public string Desired = "included"; public bool Required; }

/// <summary>Everything read from one filled Gridline-Project-Import-Template.xlsx, plus what's wrong with it.</summary>
public sealed class ImportModel
{
    public Dictionary<string, object?> Project { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ZoneRow> Zones { get; } = [];
    public List<TradeRow> Trades { get; } = [];
    public List<BidderRow> Bidders { get; } = [];
    public List<CrewRow> Crew { get; } = [];
    public List<RequirementRow> Requirements { get; } = [];
    public List<Issue> Issues { get; } = [];
    public int ErrorCount => Issues.Count(i => i.Severity == "error");

    public string? P(string key) => Project.TryGetValue(key, out var v) && v is not null ? Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() : null;
    public decimal? PNum(string key) => Project.TryGetValue(key, out var v) ? Template.Num(v) : null;
    public DateOnly? PDate(string key) => Project.TryGetValue(key, out var v) ? Template.Date(v) : null;

    public Dictionary<string, int> Counts() => new()
    {
        ["Zones"] = Zones.Count, ["Trades"] = Trades.Count, ["Bidders"] = Bidders.Count, ["Crew"] = Crew.Count, ["Requirements"] = Requirements.Count,
    };
}

public static class Template
{
    const int MaxIssues = 2000;
    static readonly string[] Later = ["Qualifications", "Provisional Sums", "Options & Savings", "Unit Rates"];

    /// <param name="tradesOnly">true when importing trades into an existing version (only Trades and Bidders are read).</param>
    public static ImportModel Read(Stream file, bool tradesOnly = false)
    {
        var m = new ImportModel();
        XlsxReader x;
        try { x = new XlsxReader(file); }
        catch (Exception) { m.Issues.Add(new("Workbook", null, null, "error", "This file is not an Excel workbook (.xlsx). Save it as .xlsx and try again.")); return m; }
        using (x)
        {
            var names = x.SheetNames;
            if (!names.Contains("Trades", StringComparer.OrdinalIgnoreCase))
            {
                m.Issues.Add(new("Workbook", null, null, "error", "The Trades sheet is missing. Start from the Gridline template (Download template)."));
                return m;
            }
            if (!tradesOnly)
            {
                if (!names.Contains("Project", StringComparer.OrdinalIgnoreCase)) m.Issues.Add(new("Workbook", null, null, "error", "The Project sheet is missing."));
                ReadProject(x, m); ReadZones(x, m);
            }
            ReadTrades(x, m); ReadBidders(x, m);
            if (!tradesOnly)
            {
                ReadCrew(x, m); ReadRequirements(x, m);
                foreach (var s in Later)
                    if (names.Contains(s, StringComparer.OrdinalIgnoreCase) && Data(x, s).Any())
                        m.Issues.Add(new(s, null, null, "warning", $"{s} is not imported in this release; the sheet was skipped."));
            }
        }
        if (m.Issues.Count > MaxIssues) { var n = m.Issues.Count; m.Issues.RemoveRange(MaxIssues, n - MaxIssues); m.Issues.Add(new("Workbook", null, null, "error", $"…and {n - MaxIssues:N0} more problems. Fix the ones above first.")); }
        return m;
    }

    // ---------------------------------------------------------------- helpers
    /// <summary>Data rows under the header row (the row whose first cell ends with '*' or matches a known header), keyed by header text.</summary>
    static IEnumerable<(int row, Func<string, object?> get)> Data(XlsxReader x, string sheet)
    {
        Dictionary<string, int>? header = null;
        foreach (var (rowNo, cells) in x.Rows(sheet))
        {
            if (header is null)
            {
                var texts = cells.Where(c => c.Value is string).ToDictionary(c => c.Key, c => (string)c.Value!);
                if (texts.Count >= 2 && texts.Values.Any(t => t.EndsWith('*')))
                    header = texts.ToDictionary(t => Clean(t.Value), t => t.Key, StringComparer.OrdinalIgnoreCase);
                continue;
            }
            var h = header;
            yield return (rowNo, name => h.TryGetValue(name, out var col) && cells.TryGetValue(col, out var v) ? v : null);
        }
    }
    static string Clean(string header) => header.Replace("*", "").Trim();

    public static string? Str(object? v) => v switch { null => null, string s => s.Trim().Length == 0 ? null : s.Trim(), DateTime d => d.ToString("yyyy-MM-dd"), decimal d => d.ToString(CultureInfo.InvariantCulture), _ => v.ToString()?.Trim() };
    public static decimal? Num(object? v) => v switch
    {
        null => null, decimal d => d, double d => (decimal)d, int i => i,
        string s when decimal.TryParse(s.Replace("$", "").Replace(",", "").Replace("%", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
        _ => null,
    };
    public static DateOnly? Date(object? v) => v switch
    {
        DateTime d => DateOnly.FromDateTime(d),
        decimal d when d is > 20000 and < 80000 => DateOnly.FromDateTime(DateTime.FromOADate((double)d)),
        string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) => DateOnly.FromDateTime(d),
        string s when DateTime.TryParse(s, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out var d) => DateOnly.FromDateTime(d),
        _ => null,
    };
    static bool Yes(object? v) => Str(v)?.ToLowerInvariant() is "yes" or "y" or "true" or "1" or "x";
    static bool NotNumber(object? v) => v is not null && Num(v) is null;

    public static string? Csi(object? v)
    {
        var s = Str(v); if (s is null) return null;
        var digits = new string(s.Where(char.IsDigit).ToArray());
        return digits.Length == 6 ? $"{digits[..2]} {digits[2..4]} {digits[4..]}" : s;
    }

    public static string? BidState(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        null or "" => "invited", "invited" => "invited", "bidding" => "bidding", "bid received" or "received" or "posted" => "posted",
        "declined" => "declined", "late" => "late", "incomplete" => "incomplete", "disqualified" => "disqualified", "undecided" => "undecided",
        "not plans & specs" or "not plans and specs" or "notplansspecs" => "notplansspecs", _ => null,
    };
    public static string? Desired(string? s) => s?.Trim().ToLowerInvariant() switch { "included" or "include" or "yes" => "included", "excluded" or "exclude" or "no" => "excluded", "price" => "price", "text" => "text", _ => null };
    public static string? Stage(string? s) => s?.Trim().ToLowerInvariant() switch { null or "" => "lead", "lead" => "lead", "pursuit" => "pursuit", "bidding" => "bidding", "submitted" => "submitted", "awarded" => "awarded", "lost" => "lost", _ => null };
    public static string Phase(string? s) => s?.Trim().ToLowerInvariant() switch { "preconstruction" or "precon" or "pre-construction" => "precon", "closeout" or "close-out" => "closeout", _ => "build" };

    // ---------------------------------------------------------------- sheets
    static void ReadProject(XlsxReader x, ImportModel m)
    {
        var started = false;
        foreach (var (rowNo, cells) in x.Rows("Project"))
        {
            var key = Str(cells.GetValueOrDefault(1));
            if (key is null) continue;
            if (!started) { if (key.StartsWith("Field", StringComparison.OrdinalIgnoreCase)) started = true; continue; }
            m.Project[key.Replace("*", "").Trim()] = cells.GetValueOrDefault(2);
        }
        if (m.P("Project name") is null) m.Issues.Add(new("Project", null, "Project name", "error", "Project name is required."));
        if (m.P("State") is string st && st.Length != 2) m.Issues.Add(new("Project", null, "State", "error", $"State '{st}' must be a 2-letter code like VA."));
        if (Stage(m.P("Stage")) is null) m.Issues.Add(new("Project", null, "Stage", "error", $"Stage '{m.P("Stage")}' is not one of Lead, Pursuit, Bidding, Submitted, Awarded, Lost."));
        foreach (var k in new[] { "Gross SF", "Duration (months)", "Fee %", "Reserve (contingency) %", "Works cover (builders risk) rate" })
            if (m.Project.TryGetValue(k, out var v) && NotNumber(v)) m.Issues.Add(new("Project", null, k, "error", $"{k} must be a number (found '{Str(v)}')."));
        foreach (var k in new[] { "Bid due date", "Construction start" })
            if (m.Project.TryGetValue(k, out var v) && v is not null && Date(v) is null) m.Issues.Add(new("Project", null, k, "error", $"{k} must be a date (found '{Str(v)}')."));
        if (m.PNum("Fee %") is > 50) m.Issues.Add(new("Project", null, "Fee %", "warning", "Fee % looks high. Enter 4.5 for 4.5%."));
    }

    static void ReadZones(XlsxReader x, ImportModel m)
    {
        foreach (var (row, get) in Data(x, "Zones"))
        {
            var z = new ZoneRow { Row = row, Code = Str(get("Zone code"))?.ToUpperInvariant() ?? "", Name = Str(get("Zone name")) ?? "", Area = (int)(Num(get("SF")) ?? 0), Units = Num(get("Units")), SplitPct = Num(get("Default split %")) };
            if (z.Code == "" && z.Name == "") continue;
            if (z.Code == "") m.Issues.Add(new("Zones", row, "Zone code", "error", "Zone code is required."));
            if (z.Name == "") m.Issues.Add(new("Zones", row, "Zone name", "error", "Zone name is required."));
            if (NotNumber(get("SF"))) m.Issues.Add(new("Zones", row, "SF", "error", "SF must be a number."));
            if (z.Code != "" && m.Zones.Any(o => o.Code == z.Code)) m.Issues.Add(new("Zones", row, "Zone code", "error", $"Zone code {z.Code} is used twice."));
            m.Zones.Add(z);
        }
        var split = m.Zones.Sum(z => z.SplitPct ?? 0);
        if (m.Zones.Any(z => z.SplitPct is not null) && Math.Abs(split - 100) > 0.5m) m.Issues.Add(new("Zones", null, "Default split %", "warning", $"Default splits add up to {split:0.##}%, not 100%."));
    }

    static void ReadTrades(XlsxReader x, ImportModel m)
    {
        var seen = new Dictionary<string, int>();
        var zoneCodes = m.Zones.Select(z => z.Code).ToHashSet();
        foreach (var (row, get) in Data(x, "Trades"))
        {
            var t = new TradeRow { Row = row, Code = Csi(get("CSI code")) ?? "", Title = Str(get("Trade")) ?? "", Budget = Num(get("Budget")), Kit = Str(get("Scope kit")), Estimator = Str(get("Estimator")), Due = Date(get("Bid due")), Notes = Str(get("Notes")) };
            if (t.Code == "" && t.Title == "") continue;
            if (t.Code == "") m.Issues.Add(new("Trades", row, "CSI code", "error", "CSI code is required."));
            else if (!System.Text.RegularExpressions.Regex.IsMatch(t.Code, @"^\d{2} \d{2} \d{2}(\.\d+)?$")) m.Issues.Add(new("Trades", row, "CSI code", "warning", $"'{t.Code}' doesn't look like a CSI code (00 00 00)."));
            if (t.Title == "") m.Issues.Add(new("Trades", row, "Trade", "error", "Trade name is required."));
            if (NotNumber(get("Budget"))) m.Issues.Add(new("Trades", row, "Budget", "error", $"Budget must be a number (found '{Str(get("Budget"))}')."));
            if (get("Bid due") is { } due && t.Due is null) m.Issues.Add(new("Trades", row, "Bid due", "error", $"Bid due must be a date (found '{Str(due)}')."));
            if (t.Code != "" && seen.TryGetValue(t.Code, out var first)) m.Issues.Add(new("Trades", row, "CSI code", "error", $"CSI code {t.Code} is already used on row {first}. CSI codes must be unique."));
            else if (t.Code != "") seen[t.Code] = row;
            if (Str(get("Zone split")) is string split)
            {
                foreach (var part in split.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var kv = part.Split(':', 2, StringSplitOptions.TrimEntries);
                    if (kv.Length != 2 || Num(kv[1]) is not decimal pct) { m.Issues.Add(new("Trades", row, "Zone split", "error", $"Zone split '{part}' should look like WH:75.")); continue; }
                    var code = kv[0].ToUpperInvariant();
                    if (!zoneCodes.Contains(code)) m.Issues.Add(new("Trades", row, "Zone split", zoneCodes.Count == 0 ? "warning" : "error", $"Zone {code} is not on the Zones sheet."));
                    else t.Split.Add((code, pct / 100m));
                }
                var sum = t.Split.Sum(s => s.share);
                if (t.Split.Count > 0 && Math.Abs(sum - 1) > 0.005m) m.Issues.Add(new("Trades", row, "Zone split", "error", $"Zone split adds up to {sum * 100:0.##}%, not 100%."));
            }
            m.Trades.Add(t);
        }
        if (m.Trades.Count == 0) m.Issues.Add(new("Trades", null, null, "error", "Add at least one trade on the Trades sheet."));
    }

    static void ReadBidders(XlsxReader x, ImportModel m)
    {
        var codes = m.Trades.Select(t => t.Code).ToHashSet();
        var seen = new HashSet<(string, string)>();
        foreach (var (row, get) in Data(x, "Bidders"))
        {
            var b = new BidderRow { Row = row, Code = Csi(get("CSI code")) ?? "", Partner = Str(get("Trade partner")) ?? "", Contact = Str(get("Contact")), Email = Str(get("Email")),
                                    Phone = Str(get("Phone")), Base = Num(get("Base bid")), Zone = Str(get("Zone")), Diverse = Yes(get("Diverse business")), Notes = Str(get("Notes")) };
            if (b.Code == "" && b.Partner == "") continue;
            if (b.Code == "") { m.Issues.Add(new("Bidders", row, "CSI code", "error", "CSI code is required.")); continue; }
            if (!codes.Contains(b.Code)) m.Issues.Add(new("Bidders", row, "CSI code", "error", $"CSI code {b.Code} is not on the Trades sheet."));
            if (b.Partner == "") m.Issues.Add(new("Bidders", row, "Trade partner", "error", "Trade partner is required."));
            if (NotNumber(get("Base bid"))) m.Issues.Add(new("Bidders", row, "Base bid", "error", $"Base bid must be a number (found '{Str(get("Base bid"))}')."));
            var st = BidState(Str(get("Invite status")));
            if (st is null) { m.Issues.Add(new("Bidders", row, "Invite status", "warning", $"Invite status '{Str(get("Invite status"))}' is unknown; using Invited.")); st = "invited"; }
            if (b.Base is not null && st is "invited" or "bidding") st = "posted";
            b.State = st;
            if (b.Email is string e && !e.Contains('@')) m.Issues.Add(new("Bidders", row, "Email", "warning", $"'{e}' doesn't look like an email address."));
            if (b.Partner != "" && !seen.Add((b.Code, b.Partner.ToLowerInvariant()))) { m.Issues.Add(new("Bidders", row, "Trade partner", "warning", $"{b.Partner} is listed twice on {b.Code}; the second row is skipped.")); continue; }
            m.Bidders.Add(b);
        }
    }

    static void ReadCrew(XlsxReader x, ImportModel m)
    {
        foreach (var (row, get) in Data(x, "Crew"))
        {
            var c = new CrewRow { Row = row, Role = Str(get("Role")) ?? "", Member = Str(get("Member")), Start = (int)(Num(get("Start month")) ?? 0), End = (int)(Num(get("End month")) ?? 0),
                                  Fte = Num(get("FTE")) ?? 1, Rate = Num(get("Hourly rate")), PerDiem = Yes(get("Per diem")), Phase = Phase(Str(get("Phase"))) };
            if (c.Role == "" && c.Member is null) continue;
            if (c.Role == "") m.Issues.Add(new("Crew", row, "Role", "error", "Role is required."));
            if (c.Start < 1 || c.End < c.Start) m.Issues.Add(new("Crew", row, "Start month", "error", "Start and end month are required, and the end can't be before the start."));
            if (c.Fte is <= 0 or > 2) m.Issues.Add(new("Crew", row, "FTE", "error", "FTE must be more than 0 and at most 2."));
            m.Crew.Add(c);
        }
    }

    static void ReadRequirements(XlsxReader x, ImportModel m)
    {
        var codes = m.Trades.Select(t => t.Code).ToHashSet();
        foreach (var (row, get) in Data(x, "Base Requirements"))
        {
            var r = new RequirementRow { Row = row, Section = Str(get("Section")) ?? "", Code = Csi(get("CSI code")), Text = Str(get("Requirement")) ?? "", Required = Yes(get("Required")) };
            if (r.Section == "" && r.Text == "") continue;
            if (r.Text == "") m.Issues.Add(new("Base Requirements", row, "Requirement", "error", "Requirement text is required."));
            if (r.Section == "") r.Section = "General";
            var d = Desired(Str(get("Desired answer")));
            if (d is null) m.Issues.Add(new("Base Requirements", row, "Desired answer", "error", $"Desired answer must be Included, Excluded, Price or Text."));
            r.Desired = d ?? "included";
            if (r.Code is string c && !codes.Contains(c)) m.Issues.Add(new("Base Requirements", row, "CSI code", "warning", $"CSI code {c} is not on the Trades sheet; the line will not appear on any trade."));
            m.Requirements.Add(r);
        }
        // Lines without a CSI code are copied onto every trade, so a huge general list multiplies fast.
        var general = m.Requirements.Count(r => r.Code is null);
        if (general > MaxGeneralRequirements)
            m.Issues.Add(new("Base Requirements", null, "CSI code", "error",
                $"{general:N0} base requirements have no CSI code, so each would be copied onto all {m.Trades.Count:N0} trades ({(long)general * Math.Max(1, m.Trades.Count):N0} scope lines). Keep the general list to {MaxGeneralRequirements:N0} lines or fewer, or give trade-specific lines a CSI code."));
    }
    public const int MaxGeneralRequirements = 1000;
}
