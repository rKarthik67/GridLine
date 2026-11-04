using Dapper;
using Gridline.Calc;
using Gridline.Infrastructure;
using Npgsql;

namespace Gridline.Endpoints;

/// <summary>
/// Precon services team (was: Preconstruction → Team Members): people grouped by team, an actual posting rate,
/// the role's quoted posting rate, and hours per precon task. Tasks (was: Change Precon Dept Tasks) come from
/// the company library, plus ad-hoc tasks for one project.
/// </summary>
public static class PreconEndpoints
{
    public sealed record MemberIn(string? TeamGroup, long? StaffId, int? StaffRoleId, decimal? PostingRate, long? MoveAfterId, int? RowRev);
    public sealed record TaskCell(int ActivityId, decimal Hours);
    public sealed record TasksPut(List<int> ActivityIds, List<string>? Adhoc);
    public sealed record IdsIn(List<long> Ids);
    static readonly string[] Groups = ["operations", "siteops", "corporate"];

    public sealed class Row
    {
        public long PreconMemberId { get; set; } public long? StaffId { get; set; } public string? StaffName { get; set; } public int? StaffRoleId { get; set; } public string? RoleName { get; set; }
        public string RoleLabel { get; set; } = ""; public string TeamGroup { get; set; } = "corporate"; public decimal SortKey { get; set; } public decimal PostingRate { get; set; }
        public decimal? QuotedRate { get; set; } public int RowRev { get; set; }
        public Dictionary<int, decimal> Hours { get; set; } = []; public decimal TotalHours { get; set; } public decimal Labour { get; set; }
        public bool IsTba => StaffId is null;
    }

    public static async Task<object> Load(NpgsqlConnection c, NpgsqlTransaction? t, long versionId)
    {
        var tasks = (await c.QueryAsync("""
            SELECT a.ActivityId AS "activityId", a.ActivityName AS "taskName", a.ProjectId IS NOT NULL AS "isAdhoc"
            FROM VersionTask vt JOIN Activity a ON a.ActivityId = vt.ActivityId WHERE vt.VersionId = @versionId ORDER BY vt.SortOrder, a.ActivityId
            """, new { versionId }, t)).ToList();
        var members = (await c.QueryAsync<Row>("""
            SELECT p.PreconMemberId, p.StaffId, s.FullName AS StaffName, p.StaffRoleId, r.RoleName, p.RoleLabel, p.TeamGroup, p.SortKey, p.PostingRate,
                   coalesce(p.QuotedRate, r.DefaultRate) AS QuotedRate, p.RowRev
            FROM PreconMember p LEFT JOIN Staff s ON s.StaffId = p.StaffId LEFT JOIN StaffRole r ON r.StaffRoleId = p.StaffRoleId
            WHERE p.VersionId = @versionId ORDER BY CASE p.TeamGroup WHEN 'operations' THEN 1 WHEN 'siteops' THEN 2 ELSE 3 END, p.SortKey, p.PreconMemberId
            """, new { versionId }, t)).ToList();
        var hours = (await c.QueryAsync<(long mid, int aid, decimal h)>("""
            SELECT t.PreconMemberId, t.ActivityId, t.Hours FROM PreconMemberTask t JOIN PreconMember p ON p.PreconMemberId = t.PreconMemberId
            JOIN VersionTask vt ON vt.VersionId = p.VersionId AND vt.ActivityId = t.ActivityId WHERE p.VersionId = @versionId AND t.Hours > 0
            """, new { versionId }, t)).ToLookup(x => x.mid);
        foreach (var m in members)
        {
            m.Hours = hours[m.PreconMemberId].ToDictionary(x => x.aid, x => x.h);
            m.TotalHours = m.Hours.Values.Sum();
            m.Labour = m.Hours.Values.Sum(h => Rules.R2(h * m.PostingRate));
        }
        return new { versionId, tasks, members, totals = new { hours = members.Sum(m => m.TotalHours), labour = members.Sum(m => m.Labour) } };
    }
    static async Task<IResult> Done(Work w, long versionId, long projectId, string verb, object key, object? after = null)
    {
        var sum = await Recalc.Summary(w, versionId, verb);
        await w.Log(projectId, versionId, verb, "precon", key, after: after);
        await w.Commit();
        return Results.Ok(new { team = await Load(w.C, null, versionId), totalsHash = sum.Hash });
    }

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/versions/{id:long}/precon-team", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            await Access.Version(c, ctx.Me(), id);
            return await Load(c, null, id);
        });

        // Add Team member in Preconstruction: team, role, person (or TBA), posting rate
        api.MapPost("/versions/{id:long}/precon-team", async (long id, MemberIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Version(w, id);
            var group = body.TeamGroup ?? "corporate";
            if (!Groups.Contains(group)) throw ApiException.Bad("Team must be operations, siteops or corporate.");
            if (body.PostingRate is < 0) throw ApiException.Bad("The posting rate can't be negative.");
            int? roleId = body.StaffRoleId; decimal? rate = body.PostingRate;
            if (body.StaffId is long sid)
            {
                var st = await w.One<(string name, int? roleId, decimal? cost)>("SELECT FullName, StaffRoleId, HourlyCost FROM Staff WHERE StaffId = @sid", new { sid });
                if (st.name is null) throw ApiException.Bad("Unknown staff member.");
                roleId ??= st.roleId; rate ??= st.cost;
            }
            if (roleId is null) throw ApiException.Bad("Pick an employee role.");
            var r = await w.One<(string name, decimal? rate)>("SELECT RoleName, DefaultRate FROM StaffRole WHERE StaffRoleId = @roleId", new { roleId });
            var role = r.name ?? throw ApiException.Bad("Unknown employee role.");
            var key = (await w.Scalar<decimal?>("SELECT max(SortKey) FROM PreconMember WHERE VersionId = @id", new { id }) ?? 0) + 1;
            var mid = await w.Scalar<long>("""
                INSERT INTO PreconMember (VersionId, StaffId, StaffRoleId, RoleLabel, TeamGroup, SortKey, PostingRate, QuotedRate)
                VALUES (@id, @StaffId, @roleId, @role, @group, @key, coalesce(@rate, 0), @quoted) RETURNING PreconMemberId
                """, new { id, body.StaffId, roleId, role, group, key, rate = rate ?? r.rate, quoted = r.rate });
            return await Done(w, id, s.ProjectId, "precon.create", mid, new { role, body.StaffId, group });
        });

        // Change employee / posting rate / team / move
        api.MapPatch("/precon-team/{id:long}", async (long id, MemberIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.PreconMember(w, id);
            await TradeEndpoints.CheckRev(w, "PreconMember", "PreconMemberId", id, body.RowRev);
            if (body.TeamGroup is not null && !Groups.Contains(body.TeamGroup)) throw ApiException.Bad("Team must be operations, siteops or corporate.");
            if (body.PostingRate is < 0) throw ApiException.Bad("The posting rate can't be negative.");
            decimal? rate = body.PostingRate; int? roleId = body.StaffRoleId; string? role = null; decimal? quoted = null;
            if (body.StaffId is long sid)
            {
                var st = await w.One<(decimal? cost, int? roleId)>("SELECT HourlyCost, StaffRoleId FROM Staff WHERE StaffId = @sid", new { sid });
                rate ??= st.cost; roleId ??= st.roleId;
            }
            if (roleId is int rid)
            {
                var r = await w.One<(string name, decimal? rate)>("SELECT RoleName, DefaultRate FROM StaffRole WHERE StaffRoleId = @rid", new { rid });
                role = r.name; quoted = r.rate;
            }
            decimal? key = null;
            if (body.MoveAfterId is long after)
            {
                var a = after == 0 ? (decimal?)null : await w.Scalar<decimal?>("SELECT SortKey FROM PreconMember WHERE PreconMemberId = @after", new { after });
                var b = await w.Scalar<decimal?>("SELECT min(SortKey) FROM PreconMember WHERE VersionId = @v AND SortKey > coalesce(@a, -1e9) AND PreconMemberId <> @id", new { v = s.VersionId, a, id });
                key = a is null ? (b ?? 1) - 1 : b is null ? a + 1 : Math.Round((a.Value + b.Value) / 2, 4);
            }
            await w.Exec("""
                UPDATE PreconMember SET StaffId = coalesce(@StaffId, StaffId), StaffRoleId = coalesce(@roleId, StaffRoleId), RoleLabel = coalesce(@role, RoleLabel),
                  QuotedRate = coalesce(@quoted, QuotedRate), PostingRate = coalesce(@rate, PostingRate), TeamGroup = coalesce(@TeamGroup, TeamGroup),
                  SortKey = coalesce(@key, SortKey), RowRev = RowRev + 1
                WHERE PreconMemberId = @id
                """, new { body.StaffId, roleId, role, quoted, rate, body.TeamGroup, key, id });
            return await Done(w, s.VersionId, s.ProjectId, "precon.update", id, body);
        });

        api.MapDelete("/precon-team/{id:long}", async (long id, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.PreconMember(w, id);
            await w.Exec("DELETE FROM PreconMember WHERE PreconMemberId = @id", new { id });
            return await Done(w, s.VersionId, s.ProjectId, "precon.delete", id);
        });
        api.MapPost("/versions/{id:long}/precon-team/delete", async (long id, IdsIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Version(w, id);
            if (body.Ids is null || body.Ids.Count == 0) throw ApiException.Bad("Nothing selected.");
            var n = await w.Exec("DELETE FROM PreconMember WHERE VersionId = @id AND PreconMemberId = ANY(@ids)", new { id, ids = body.Ids.ToArray() });
            return await Done(w, id, s.ProjectId, "precon.delete", $"{n} people");
        });

        // Hours per task (one cell, a paste, a fill)
        api.MapPut("/precon-team/{id:long}/tasks", async (long id, List<TaskCell> cells, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.PreconMember(w, id);
            if (cells is null || cells.Count == 0) throw ApiException.Bad("Nothing to save.");
            if (cells.Any(c => c.Hours is < 0 or > 10000)) throw ApiException.Bad("Hours on a task must be between 0 and 10,000.");
            var ok = (await w.Query<int>("SELECT ActivityId FROM VersionTask WHERE VersionId = @v", new { v = s.VersionId })).ToHashSet();
            if (cells.Any(c => !ok.Contains(c.ActivityId))) throw ApiException.Bad("That task is not on this version. Use Change tasks to add it.");
            foreach (var c in cells)
                await w.Exec("""
                    INSERT INTO PreconMemberTask (PreconMemberId, ActivityId, Hours) VALUES (@id, @a, @h)
                    ON CONFLICT (PreconMemberId, ActivityId) DO UPDATE SET Hours = EXCLUDED.Hours
                    """, new { id, a = c.ActivityId, h = c.Hours });
            return await Done(w, s.VersionId, s.ProjectId, "precon.hours", id, new { cells = cells.Count });
        });

        // Change tasks: library on the left, this version's tasks on the right, plus ad-hoc tasks
        api.MapGet("/versions/{id:long}/precon-tasks", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            var s = await Access.Version(c, ctx.Me(), id);
            return await c.QueryAsync("""
                SELECT a.ActivityId AS "activityId", a.ActivityName AS "taskName", a.IsDefault AS "isDefault", a.ProjectId IS NOT NULL AS "isAdhoc",
                       vt.SortOrder AS "sortOrder", vt.VersionId IS NOT NULL AS "selected"
                FROM Activity a LEFT JOIN VersionTask vt ON vt.ActivityId = a.ActivityId AND vt.VersionId = @id
                WHERE a.Phase = 'precon' AND a.IsActive AND (a.ProjectId IS NULL OR a.ProjectId = @p)
                ORDER BY vt.SortOrder NULLS LAST, a.SortOrder, a.ActivityId
                """, new { id, p = s.ProjectId });
        });
        api.MapPut("/versions/{id:long}/precon-tasks", async (long id, TasksPut body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Version(w, id);
            var adhoc = (body.Adhoc ?? []).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if ((body.ActivityIds?.Count ?? 0) == 0 && adhoc.Count == 0) throw ApiException.Bad("You must either select a task from the library or add an ad-hoc one.");
            if (adhoc.Any(x => x.Length > 120)) throw ApiException.Bad("Task names can be at most 120 characters.");
            var allowed = (await w.Query<int>("SELECT ActivityId FROM Activity WHERE Phase = 'precon' AND IsActive AND (ProjectId IS NULL OR ProjectId = @p)", new { p = s.ProjectId })).ToHashSet();
            if ((body.ActivityIds ?? []).Any(a => !allowed.Contains(a))) throw ApiException.Bad("Unknown task.");
            var ids = new List<int>();
            foreach (var name in adhoc)   // ad-hoc tasks go first, like the original
                ids.Add(await w.Scalar<int?>("SELECT ActivityId FROM Activity WHERE Phase = 'precon' AND ProjectId = @p AND lower(ActivityName) = lower(@name)", new { p = s.ProjectId, name })
                        ?? await w.Scalar<int>("INSERT INTO Activity (Phase, ActivityName, SortOrder, ProjectId) VALUES ('precon', @name, 0, @p) RETURNING ActivityId", new { p = s.ProjectId, name }));
            ids.AddRange((body.ActivityIds ?? []).Where(a => !ids.Contains(a)));
            await w.Exec("DELETE FROM VersionTask WHERE VersionId = @id", new { id });
            for (var i = 0; i < ids.Count; i++)
                await w.Exec("INSERT INTO VersionTask (VersionId, ActivityId, SortOrder) VALUES (@id, @a, @o)", new { id, a = ids[i], o = (short)(i + 1) });
            return await Done(w, id, s.ProjectId, "precon.tasks", id, new { tasks = ids.Count });
        });
    }
}
