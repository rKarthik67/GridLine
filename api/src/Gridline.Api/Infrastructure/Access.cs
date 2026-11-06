using Dapper;
using Npgsql;

namespace Gridline.Infrastructure;

public sealed class Scope
{
    public long ProjectId { get; set; }
    public long VersionId { get; set; }
    public long? TradeId { get; set; }
    public string Status { get; set; } = "draft";
    public string Access { get; set; } = "viewer";
}

/// <summary>
/// Who may read or change a project. A project outside the caller's company does not exist as far as they are
/// concerned (404). Inside the company a ProjectMember row decides; without one, admins are owners, people with
/// project.edit are editors and everyone else can view. Locked versions can't be changed.
/// </summary>
public static class Access
{
    const string LevelSql = """
        coalesce((SELECT m.Access::text FROM ProjectMember m WHERE m.ProjectId = p.ProjectId AND m.UserId = @uid),
                 CASE WHEN @admin THEN 'owner' WHEN @edit THEN 'editor' ELSE 'viewer' END)
        """;
    /// <summary>Appended to every lookup: the system user (CompanyId 0) sees every company, people see only their own.</summary>
    const string CompanySql = " AND (@cid = 0 OR p.CompanyId = @cid)";

    static object Args(CurrentUser u, object key) => new DynamicParameters(key).Also(d =>
    { d.Add("uid", u.UserId); d.Add("admin", u.IsAdmin); d.Add("edit", u.Can("project.edit")); d.Add("cid", u.CompanyId); });

    static async Task<Scope> Resolve(NpgsqlConnection c, NpgsqlTransaction? t, CurrentUser u, string from, object key, string what, string need)
    {
        var s = await c.QueryFirstOrDefaultAsync<Scope>($"SELECT p.ProjectId, v.VersionId, {(from.Contains(" tr ") || from.Contains("Trade tr") ? "tr.TradeId" : "NULL::bigint")} AS TradeId, v.Status, {LevelSql} AS Access {from}{CompanySql}", Args(u, key), t)
            ?? throw ApiException.NotFound(what);
        Check(s, need);
        return s;
    }

    public static void Check(Scope s, string need)
    {
        int Rank(string a) => a switch { "owner" => 3, "editor" => 2, _ => 1 };
        if (Rank(s.Access) < Rank(need)) throw ApiException.Forbidden(need == "viewer" ? "You're not on this project." : "You can view this project but not change it.");
        if (need != "viewer" && s.Status == "locked") throw new ApiException(423, "This version is locked. Unlock it or create a new version to make changes.", "locked");
    }

    public static Task<Scope> Version(Work w, long versionId, string need = "editor") =>
        Resolve(w.C, w.T, w.User, "FROM ProjectVersion v JOIN Project p ON p.ProjectId = v.ProjectId WHERE v.VersionId = @id", new { id = versionId }, "Version", need);
    public static Task<Scope> Version(NpgsqlConnection c, CurrentUser u, long versionId, string need = "viewer") =>
        Resolve(c, null, u, "FROM ProjectVersion v JOIN Project p ON p.ProjectId = v.ProjectId WHERE v.VersionId = @id", new { id = versionId }, "Version", need);

    public static Task<Scope> Trade(Work w, long tradeId, string need = "editor") =>
        Resolve(w.C, w.T, w.User, "FROM Trade tr JOIN ProjectVersion v ON v.VersionId = tr.VersionId JOIN Project p ON p.ProjectId = v.ProjectId WHERE tr.TradeId = @id", new { id = tradeId }, "Trade", need);
    public static Task<Scope> Trade(NpgsqlConnection c, CurrentUser u, long tradeId, string need = "viewer") =>
        Resolve(c, null, u, "FROM Trade tr JOIN ProjectVersion v ON v.VersionId = tr.VersionId JOIN Project p ON p.ProjectId = v.ProjectId WHERE tr.TradeId = @id", new { id = tradeId }, "Trade", need);

    public static Task<Scope> Bid(Work w, long bidId, string need = "editor") =>
        Resolve(w.C, w.T, w.User, "FROM Bid b JOIN Trade tr ON tr.TradeId = b.TradeId JOIN ProjectVersion v ON v.VersionId = tr.VersionId JOIN Project p ON p.ProjectId = v.ProjectId WHERE b.BidId = @id", new { id = bidId }, "Bid", need);
    public static Task<Scope> Bid(NpgsqlConnection c, CurrentUser u, long bidId, string need = "viewer") =>
        Resolve(c, null, u, "FROM Bid b JOIN Trade tr ON tr.TradeId = b.TradeId JOIN ProjectVersion v ON v.VersionId = tr.VersionId JOIN Project p ON p.ProjectId = v.ProjectId WHERE b.BidId = @id", new { id = bidId }, "Bid", need);

    public static Task<Scope> Line(Work w, long lineId, string need = "editor") =>
        Resolve(w.C, w.T, w.User, "FROM ScopeLine l JOIN Trade tr ON tr.TradeId = l.TradeId JOIN ProjectVersion v ON v.VersionId = tr.VersionId JOIN Project p ON p.ProjectId = v.ProjectId WHERE l.LineId = @id", new { id = lineId }, "Scope line", need);

    public static Task<Scope> Cost(Work w, long costLineId, string need = "editor") =>
        Resolve(w.C, w.T, w.User, "FROM CostLine cl JOIN ProjectVersion v ON v.VersionId = cl.VersionId JOIN Project p ON p.ProjectId = v.ProjectId WHERE cl.CostLineId = @id", new { id = costLineId }, "Cost line", need);

    public static Task<Scope> Indirect(Work w, long id, string need = "editor") =>
        Resolve(w.C, w.T, w.User, "FROM IndirectItem i JOIN ProjectVersion v ON v.VersionId = i.VersionId JOIN Project p ON p.ProjectId = v.ProjectId WHERE i.IndirectId = @id", new { id }, "Indirect item", need);

    /// <summary>Project level (team, members, documents): checks against the primary version's lock is not needed.</summary>
    public static async Task<Scope> Project(NpgsqlConnection c, NpgsqlTransaction? t, CurrentUser u, long projectId, string need = "viewer")
    {
        var s = await c.QueryFirstOrDefaultAsync<Scope>($"""
            SELECT p.ProjectId, coalesce(v.VersionId, 0) AS VersionId, NULL::bigint AS TradeId, 'draft' AS Status, {LevelSql} AS Access
            FROM Project p LEFT JOIN ProjectVersion v ON v.ProjectId = p.ProjectId AND v.IsPrimary WHERE p.ProjectId = @id AND p.ArchivedAt IS NULL{CompanySql}
            """, Args(u, new { id = projectId }), t) ?? throw ApiException.NotFound("Project");
        Check(s, need);
        return s;
    }
    public static Task<Scope> Project(Work w, long projectId, string need = "editor") => Project(w.C, w.T, w.User, projectId, need);

    public static Task<Scope> TeamMember(Work w, long id, string need = "editor") =>
        w.Scalar<long?>("SELECT ProjectId FROM TeamMember WHERE TeamMemberId = @id", new { id })
         .ContinueWith(async t => t.Result is long pid ? await Project(w, pid, need) : throw ApiException.NotFound("Team member")).Unwrap();

    public static Task<Scope> PreconMember(Work w, long id, string need = "editor") =>
        Resolve(w.C, w.T, w.User, "FROM PreconMember pm JOIN ProjectVersion v ON v.VersionId = pm.VersionId JOIN Project p ON p.ProjectId = v.ProjectId WHERE pm.PreconMemberId = @id", new { id }, "Precon team member", need);

    // ---- company-owned reference rows (templates, people, partners): a row from another company is "not found"
    static async Task<long> Owned(NpgsqlConnection c, NpgsqlTransaction? t, CurrentUser u, string table, string idCol, long id, string what)
    {
        var found = await c.ExecuteScalarAsync<long?>($"SELECT {idCol} FROM {table} WHERE {idCol} = @id AND (@cid = 0 OR CompanyId = @cid)", new { id, cid = u.CompanyId }, t);
        return found ?? throw ApiException.NotFound(what);
    }
    public static Task<long> ScopeKit(Work w, long kitId) => Owned(w.C, w.T, w.User, "ScopeKit", "KitId", kitId, "Scope kit");
    public static Task<long> CostKit(Work w, long kitId) => Owned(w.C, w.T, w.User, "CostKit", "CostKitId", kitId, "Cost kit");
    public static Task<long> RequirementSet(Work w, long setId) => Owned(w.C, w.T, w.User, "RequirementSet", "RequirementSetId", setId, "Requirements checklist");
    public static Task<long> QualificationSet(Work w, long setId) => Owned(w.C, w.T, w.User, "QualificationSet", "QualificationSetId", setId, "Qualification set");
    public static Task<long> Staff(Work w, long staffId) => Owned(w.C, w.T, w.User, "Staff", "StaffId", staffId, "Staff member");
    public static Task<long> StaffRole(Work w, long roleId) => Owned(w.C, w.T, w.User, "StaffRole", "StaffRoleId", roleId, "Employee role");
    public static Task<long> Partner(Work w, long partnerId) => Owned(w.C, w.T, w.User, "TradePartner", "PartnerId", partnerId, "Trade partner");
    public static Task<long> User(Work w, long userId) => Owned(w.C, w.T, w.User, "AppUser", "UserId", userId, "User");

    static T Also<T>(this T x, Action<T> f) { f(x); return x; }
}
