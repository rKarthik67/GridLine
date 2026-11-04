using Dapper;
using Gridline.Infrastructure;
using Npgsql;

namespace Gridline.Calc;

/// <summary>
/// Crew plan (was: Project team / Manpower) costs, the same way today's project team works:
///  * cost of a week = hours × posting rate × (1 + salary escalation %) ^ (year − first year of the schedule)
///  * a member's actual = construction + closeout cost + per diem + relocation
///  * quoted = actual × % quoted (was: RateDiscountPC); the crew's quoted total is the roll-up's "General conditions"
///  * only construction and closeout weeks count here; precon hours live on Precon services (per task)
/// </summary>
public static class Crew
{
    public sealed class WeekRow { public long WeekId { get; set; } public int WeekNo { get; set; } public DateTime StartsOn { get; set; } public string Phase { get; set; } = ""; public int MonthNo { get; set; } }
    public sealed class MemberRow
    {
        public long TeamMemberId { get; set; } public long? StaffId { get; set; } public string? StaffName { get; set; } public int? StaffRoleId { get; set; } public string? RoleName { get; set; }
        public string RoleLabel { get; set; } = ""; public string TeamGroup { get; set; } = "operations"; public decimal SortKey { get; set; }
        public decimal? HourlyCost { get; set; } public decimal? StaffRate { get; set; } public decimal? RoleRate { get; set; } public decimal QuotedPct { get; set; } = 100;
        public long? PerDiemPlaceId { get; set; } public string? PerDiemPlace { get; set; } public string? PerDiemStart { get; set; } public string? PerDiemEnd { get; set; }
        public string PerDiemDays { get; set; } = "weekdays"; public string PerDiemKind { get; set; } = "both"; public int PerDiemDaysCount { get; set; } public decimal PerDiemCost { get; set; }
        public decimal RelocationCost { get; set; } public string? Note { get; set; } public int RowRev { get; set; }
        // calculated
        public Dictionary<long, decimal> Hours { get; set; } = [];
        public int ConstructionWeeks { get; set; } public int CloseoutWeeks { get; set; }
        public decimal ConstructionHours { get; set; } public decimal CloseoutHours { get; set; } public decimal TotalHours { get; set; }
        public decimal ConstructionTimePct { get; set; }
        public decimal ConstructionCost { get; set; } public decimal CloseoutCost { get; set; } public decimal TotalCost { get; set; }
        public decimal ConstructionQuoted { get; set; } public decimal CloseoutQuoted { get; set; } public decimal TotalQuoted { get; set; }
        public decimal? FinalRate { get; set; } public decimal? WeeklyRate { get; set; } public decimal? QuotedRate { get; set; } public decimal? QuotedWeeklyRate { get; set; }
        public decimal? QuotedOfCostPct { get; set; } public bool IsTba => StaffId is null;
    }
    public sealed record Totals(decimal Hours, decimal Cost, decimal Quoted);

    public static decimal Multiplier(DateTime week, int baseYear, decimal escalationPct) =>
        (decimal)Math.Pow(1 + (double)escalationPct / 100, week.Year - baseYear);

    public static async Task<(List<WeekRow> weeks, List<MemberRow> members, decimal escalation)> Load(NpgsqlConnection c, NpgsqlTransaction? t, long projectId)
    {
        var weeks = (await c.QueryAsync<WeekRow>("SELECT WeekId, WeekNo, StartsOn, Phase::text AS Phase, MonthNo FROM ProjectWeek WHERE ProjectId = @projectId AND Phase <> 'precon' ORDER BY WeekNo", new { projectId }, t)).ToList();
        var esc = await c.ExecuteScalarAsync<decimal>("SELECT SalaryEscalationPct FROM Project WHERE ProjectId = @projectId", new { projectId }, t);
        var members = (await c.QueryAsync<MemberRow>("""
            SELECT m.TeamMemberId, m.StaffId, s.FullName AS StaffName, m.StaffRoleId, r.RoleName, m.RoleLabel, m.TeamGroup, m.SortKey, m.HourlyCost, s.HourlyCost AS StaffRate,
                   r.DefaultRate AS RoleRate, m.QuotedPct, m.PerDiemPlaceId,
                   (SELECT pl.City || coalesce(', ' || pl.StateCode, '') || coalesce(' · ' || pl.LocationDefined, '') FROM PerDiemPlace pl WHERE pl.PlaceId = m.PerDiemPlaceId) AS PerDiemPlace,
                   to_char(m.PerDiemStart, 'YYYY-MM-DD') AS PerDiemStart, to_char(m.PerDiemEnd, 'YYYY-MM-DD') AS PerDiemEnd, m.PerDiemDays, m.PerDiemKind, m.PerDiemDaysCount, m.PerDiemCost, m.RelocationCost, m.Note, m.RowRev
            FROM TeamMember m LEFT JOIN Staff s ON s.StaffId = m.StaffId LEFT JOIN StaffRole r ON r.StaffRoleId = m.StaffRoleId
            WHERE m.ProjectId = @projectId ORDER BY CASE m.TeamGroup WHEN 'operations' THEN 1 WHEN 'siteops' THEN 2 ELSE 3 END, m.SortKey, m.TeamMemberId
            """, new { projectId }, t)).ToList();
        var hours = (await c.QueryAsync<(long mid, long wid, decimal h)>("""
            SELECT mw.TeamMemberId, mw.WeekId, mw.Hours FROM TeamMemberWeek mw JOIN TeamMember m ON m.TeamMemberId = mw.TeamMemberId
            JOIN ProjectWeek wk ON wk.WeekId = mw.WeekId AND wk.Phase <> 'precon' WHERE m.ProjectId = @projectId AND mw.Hours > 0
            """, new { projectId }, t)).ToLookup(x => x.mid);
        var byId = weeks.ToDictionary(w => w.WeekId);
        var baseYear = weeks.Count > 0 ? weeks.Min(w => w.StartsOn.Year) : DateTime.Today.Year;
        var buildWeeks = weeks.Count(w => w.Phase == "build");
        foreach (var m in members)
        {
            m.Hours = hours[m.TeamMemberId].ToDictionary(x => x.wid, x => x.h);
            decimal cb = 0, cc = 0;
            foreach (var (wid, h) in m.Hours)
            {
                if (!byId.TryGetValue(wid, out var wk)) continue;
                var cost = h * (m.HourlyCost ?? 0) * Multiplier(wk.StartsOn, baseYear, esc);
                if (wk.Phase == "build") { m.ConstructionHours += h; m.ConstructionWeeks++; cb += cost; }
                else { m.CloseoutHours += h; m.CloseoutWeeks++; cc += cost; }
            }
            m.TotalHours = m.ConstructionHours + m.CloseoutHours;
            m.ConstructionCost = Rules.R2(cb); m.CloseoutCost = Rules.R2(cc);
            m.TotalCost = Rules.R2(m.ConstructionCost + m.CloseoutCost + m.PerDiemCost + m.RelocationCost);
            var q = m.QuotedPct / 100m;
            m.TotalQuoted = Rules.R2(m.TotalCost * q);
            // per diem and relocation are spread over the phases by hours, so the phase quotes add up to the total
            var extra = m.PerDiemCost + m.RelocationCost;
            var shareB = m.TotalHours > 0 ? m.ConstructionHours / m.TotalHours : 1m;
            m.ConstructionQuoted = Rules.R2((m.ConstructionCost + extra * shareB) * q);
            m.CloseoutQuoted = Rules.R2(m.TotalQuoted - m.ConstructionQuoted);
            m.ConstructionTimePct = buildWeeks > 0 ? Rules.R2(m.ConstructionHours / (buildWeeks * 40m) * 100m) : 0;
            m.FinalRate = m.TotalHours > 0 ? Rules.R2(m.TotalCost / m.TotalHours) : m.HourlyCost;
            m.WeeklyRate = m.FinalRate * 40;
            m.QuotedRate = m.FinalRate is decimal f ? Rules.R2(f * q) : null;
            m.QuotedWeeklyRate = m.QuotedRate * 40;
            m.QuotedOfCostPct = m.TotalCost > 0 ? Rules.R2(m.TotalQuoted / m.TotalCost * 100m) : null;
        }
        return (weeks, members, esc);
    }

    /// <summary>The crew's quoted total for the version's project (General conditions on the price roll-up).</summary>
    public static async Task<decimal> GencondsTotal(Work w, long versionId)
    {
        var pid = await w.Scalar<long>("SELECT ProjectId FROM ProjectVersion WHERE VersionId = @versionId", new { versionId });
        var (_, members, _) = await Load(w.C, w.Tx, pid);
        return members.Sum(m => m.TotalQuoted);
    }

    /// <summary>Per diem days and cost between two dates (was: Set Per Diem): hotel by the month's rate, meals per day.</summary>
    public static (int days, decimal cost) PerDiem(DateOnly start, DateOnly end, bool allDays, string kind, int[] lodging, int meals)
    {
        if (end < start) return (0, 0);
        int days = 0; decimal cost = 0;
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (!allDays && d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            days++;
            cost += (kind is "hotel" or "both" ? lodging[d.Month - 1] : 0) + (kind is "meals" or "both" ? meals : 0);
        }
        return (days, cost);
    }
}
