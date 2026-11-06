using Dapper;
using Gridline.Calc;
using Gridline.Infrastructure;
using Npgsql;

namespace Gridline.Endpoints;

/// <summary>
/// Crew plan (was: Project team) — Team members and Resource loader — the project's schedule dates, and the expense
/// pages: Precon services expenses and Project overheads (was: Preconstruction / General requirements).
/// </summary>
public static class TeamEndpoints
{
    public sealed record MemberIn(long? StaffId, int? StaffRoleId, string? RoleLabel, string? TeamGroup, decimal? HourlyCost, decimal? QuotedPct,
                                  decimal? RelocationCost, string? Note, long? MoveAfterId, int? RowRev, bool? ClearStaff,
                                  decimal? BuildHours, decimal? CloseoutHours);
    public sealed record HoursIn(List<HourCell> Cells);
    public sealed record HourCell(long WeekId, decimal Hours);
    public sealed record MonthIn(int MonthNo, decimal Hours);
    public sealed record PhaseHoursIn(string Phase, decimal Hours, int? Weeks);
    public sealed record PerDiemIn(long? PlaceId, DateOnly? Start, DateOnly? End, string? Days, string? Kind, bool? Clear);
    public sealed record ScheduleIn(DateOnly StartsOn, int PreconWeeks, int BuildWeeks, int CloseoutWeeks);
    /// <summary>Schedule dates (was: Project setup dates). Preview = only report how many weeks would be added / removed.</summary>
    public sealed record DatesIn(DateOnly? PreconStartOn, DateOnly? PreconEndOn, DateOnly? BuildStartOn, DateOnly? BuildEndOn, DateOnly? CloseoutOn, decimal? SalaryEscalationPct, bool? Preview);
    public sealed record WeekChange(string Phase, int Added, int Removed, decimal HoursLost);
    public sealed record CrewSettingsIn(decimal? SalaryEscalationPct);
    public sealed record ColumnsIn(List<string> Columns, bool? AllProjects, long? ProjectId);
    public sealed record CostIn(string? Area, string? CategoryName, string? ItemName, decimal? Qty, string? UomCode, decimal? Rate,
                                bool? IsBillable, string? Note, string? CsiCode, int? RowRev);
    public sealed record CostKitIn(long CostKitId);
    public sealed record QuotedIn(string Area, decimal? QuotedPct, decimal? QuotedTotal);
    public sealed record IdsIn(List<long> Ids);
    static readonly string[] Groups = ["operations", "siteops", "corporate"];

    // ------------------------------------------------------------------ crew plan
    public static async Task<object> LoadTeam(NpgsqlConnection c, NpgsqlTransaction? t, long projectId, long? userId = null)
    {
        var (weeks, members, esc) = await Crew.Load(c, t, projectId);
        Crew.Totals Sum(Func<Crew.MemberRow, decimal> h, Func<Crew.MemberRow, decimal> cost, Func<Crew.MemberRow, decimal> q) => new(members.Sum(h), members.Sum(cost), members.Sum(q));
        var columns = userId is long u ? await c.QueryFirstOrDefaultAsync<string[]>("""
            SELECT Columns FROM ColumnChoice WHERE UserId = @u AND GridKey = 'crew' AND ProjectId IN (@projectId, 0) ORDER BY ProjectId DESC LIMIT 1
            """, new { u, projectId }, t) : null;
        var dates = await c.QueryFirstAsync("""
            SELECT to_char(PreconStartOn, 'YYYY-MM-DD') AS "preconStartOn", to_char(PreconEndOn, 'YYYY-MM-DD') AS "preconEndOn", to_char(BuildStartOn, 'YYYY-MM-DD') AS "buildStartOn", to_char(BuildEndOn, 'YYYY-MM-DD') AS "buildEndOn", to_char(CloseoutOn, 'YYYY-MM-DD') AS "closeoutOn",
                   (SELECT count(*) FROM ProjectWeek w WHERE w.ProjectId = p.ProjectId AND w.Phase = 'precon')::int AS "preconWeeks",
                   (SELECT count(*) FROM ProjectWeek w WHERE w.ProjectId = p.ProjectId AND w.Phase = 'build')::int AS "buildWeeks",
                   (SELECT count(*) FROM ProjectWeek w WHERE w.ProjectId = p.ProjectId AND w.Phase = 'closeout')::int AS "closeoutWeeks"
            FROM Project p WHERE p.ProjectId = @projectId
            """, new { projectId }, t);
        var first = weeks.Count > 0 ? weeks.Min(x => x.StartsOn.Year) : 0;
        return new
        {
            projectId, salaryEscalationPct = esc, schedule = dates,
            weeks = weeks.Select(w => new { weekId = w.WeekId, weekNo = w.WeekNo, startsOn = DateOnly.FromDateTime(w.StartsOn), phase = w.Phase, monthNo = w.MonthNo,
                                             year = w.StartsOn.Year, multiplier = Math.Round(Crew.Multiplier(w.StartsOn, first, esc), 6) }),
            members,
            totals = new
            {
                construction = Sum(m => m.ConstructionHours, m => m.ConstructionCost, m => m.ConstructionQuoted),
                closeout = Sum(m => m.CloseoutHours, m => m.CloseoutCost, m => m.CloseoutQuoted),
                total = Sum(m => m.TotalHours, m => m.TotalCost, m => m.TotalQuoted),
                perDiem = members.Sum(m => m.PerDiemCost), relocation = members.Sum(m => m.RelocationCost),
            },
            columns,
        };
    }

    /// <summary>Crew quotes are General conditions on every unlocked version's roll-up.</summary>
    static async Task RecalcProject(Work w, long projectId, string trigger)
    {
        foreach (var vid in await w.Query<long>("SELECT VersionId FROM ProjectVersion WHERE ProjectId = @projectId AND Status <> 'locked'", new { projectId }))
            await Recalc.Summary(w, vid, trigger);
    }
    static async Task<IResult> Done(Work w, long projectId, string verb, long id, object? after = null)
    {
        await RecalcProject(w, projectId, verb);
        await w.Log(projectId, null, verb, "team", id, after: after);
        await w.Commit();
        return Results.Ok(await LoadTeam(w.C, null, projectId, w.User.UserId));
    }
    static async Task SetPhaseHours(Work w, long memberId, long projectId, string phase, decimal hours, int? weeks)
    {
        if (phase is not ("build" or "closeout")) throw ApiException.Bad("Phase must be build or closeout.");
        if (hours is < 0 or > 80) throw ApiException.Bad("Hours per week must be between 0 and 80.");
        await w.Exec("""
            DELETE FROM TeamMemberWeek mw USING ProjectWeek wk WHERE mw.WeekId = wk.WeekId AND mw.TeamMemberId = @memberId AND wk.Phase = @phase::TeamPhase;
            INSERT INTO TeamMemberWeek (TeamMemberId, WeekId, Hours)
            SELECT @memberId, WeekId, @hours FROM (SELECT WeekId, row_number() OVER (ORDER BY WeekNo) AS n FROM ProjectWeek WHERE ProjectId = @projectId AND Phase = @phase::TeamPhase) x
            WHERE @hours > 0 AND (@weeks::int IS NULL OR x.n <= @weeks);
            """, new { memberId, projectId, phase, hours, weeks });
    }

    static DateOnly Monday(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
    static IEnumerable<DateOnly> Mondays(DateOnly? from, DateOnly? to)
    {
        if (from is null || to is null || to < from) yield break;
        for (var m = Monday(from.Value); m <= to.Value; m = m.AddDays(7)) yield return m;
    }
    /// <summary>
    /// Weeks follow the dates like today's sp_synchWeeks: precon weeks from precon start to end, construction weeks from
    /// construction start to substantial completion, closeout weeks after that up to the closeout date. A week that stays
    /// (same phase and Monday) keeps its hours; weeks that drop out lose theirs. apply = false reports only.
    /// </summary>
    public static async Task<List<WeekChange>> RebuildWeeks(Work w, long projectId, bool apply)
    {
        var d = await w.One<(DateTime? ps, DateTime? pe, DateTime? bs, DateTime? be, DateTime? co)>(
            "SELECT PreconStartOn, PreconEndOn, BuildStartOn, BuildEndOn, CloseoutOn FROM Project WHERE ProjectId = @projectId", new { projectId });
        DateOnly? D(DateTime? x) => x is DateTime v ? DateOnly.FromDateTime(v) : null;
        var want = Mondays(D(d.ps), D(d.pe)).Select(m => ("precon", m))
            .Concat(Mondays(D(d.bs), D(d.be)).Select(m => ("build", m)))
            .Concat(D(d.be) is DateOnly be ? Mondays(Monday(be).AddDays(7), D(d.co)).Select(m => ("closeout", m)) : []).ToList();
        var have = (await w.Query<(long id, string phase, DateTime startsOn, decimal hours)>("""
            SELECT wk.WeekId, wk.Phase::text, wk.StartsOn, coalesce((SELECT sum(Hours) FROM TeamMemberWeek mw WHERE mw.WeekId = wk.WeekId), 0) FROM ProjectWeek wk WHERE wk.ProjectId = @projectId
            """, new { projectId })).ToList();
        var wantSet = want.ToHashSet();
        var gone = have.Where(h => !wantSet.Contains((h.phase, DateOnly.FromDateTime(h.startsOn)))).ToList();
        var haveSet = have.Select(h => (h.phase, DateOnly.FromDateTime(h.startsOn))).ToHashSet();
        var changes = new[] { "precon", "build", "closeout" }.Select(p => new WeekChange(p, want.Count(x => x.Item1 == p && !haveSet.Contains(x)), gone.Count(g => g.phase == p), gone.Where(g => g.phase == p).Sum(g => g.hours))).ToList();
        if (!apply) return changes;
        if (gone.Count > 0) await w.Exec("DELETE FROM ProjectWeek WHERE WeekId = ANY(@ids)", new { ids = gone.Select(g => g.id).ToArray() });
        await w.Exec("UPDATE ProjectWeek SET WeekNo = -WeekNo - 10000 WHERE ProjectId = @projectId", new { projectId });   // park numbers before renumbering
        var ordered = want.OrderBy(x => x.Item2).ThenBy(x => x.Item1 == "precon" ? 0 : x.Item1 == "build" ? 1 : 2).ToList();
        var first = ordered.Count > 0 ? ordered[0].Item2 : default;
        for (var i = 0; i < ordered.Count; i++)
        {
            var (phase, m) = ordered[i];
            var month = (m.Year - first.Year) * 12 + m.Month - first.Month + 1;
            var n = await w.Exec("UPDATE ProjectWeek SET WeekNo = @no, MonthNo = @month WHERE ProjectId = @projectId AND Phase = @phase::TeamPhase AND StartsOn = @m", new { no = i + 1, month, projectId, phase, m });
            if (n == 0) await w.Exec("INSERT INTO ProjectWeek (ProjectId, WeekNo, StartsOn, Phase, MonthNo) VALUES (@projectId, @no, @m, @phase::TeamPhase, @month)", new { projectId, no = i + 1, m, phase, month });
        }
        await w.Exec("UPDATE ProjectVersion SET BuildMonths = round(@b / 4.345, 1) WHERE ProjectId = @projectId AND Status <> 'locked'", new { projectId, b = (decimal)want.Count(x => x.Item1 == "build") });
        return changes;
    }

    // ------------------------------------------------------------------ precon services expenses / project overheads
    public static async Task<object> LoadCosts(NpgsqlConnection c, NpgsqlTransaction? t, long versionId, string area)
    {
        var lines = (await c.QueryAsync("""
            SELECT CostLineId AS "costLineId", CategoryName AS "categoryName", ItemName AS "itemName", SortKey AS "sortKey", Qty AS "qty", UomCode AS "uomCode",
                   Rate AS "rate", QuotedRate AS "quotedRate", IsBillable AS "isBillable", ActualTotal AS "actualTotal", QuotedTotal AS "quotedTotal", Note AS "note", CsiCode AS "csiCode",
                   (SELECT w.CodeTitle FROM WorkCode w WHERE w.CsiCode = cl.CsiCode LIMIT 1) AS "csiTitle", RowRev AS "rowRev",
                   ChangedAt AS "changedAt", (SELECT u.GivenName || ' ' || u.FamilyName FROM AppUser u WHERE u.UserId = cl.ChangedBy) AS "changedByName"
            FROM CostLine cl WHERE VersionId = @versionId AND Area = @area::CostArea ORDER BY SortKey, CostLineId
            """, new { versionId, area }, t)).ToList();
        var pct = await c.ExecuteScalarAsync<decimal>($"SELECT {(area == "precon" ? "PreconQuotedPct" : "GenreqQuotedPct")} FROM ProjectVersion WHERE VersionId = @versionId", new { versionId }, t);
        decimal actual = lines.Sum(l => (decimal)l.actualTotal), billable = lines.Where(l => (bool)l.isBillable).Sum(l => (decimal)l.actualTotal), quoted = lines.Sum(l => (decimal)l.quotedTotal);
        decimal labour = 0;
        if (area == "precon")
        {
            labour = await c.ExecuteScalarAsync<decimal?>("""
                SELECT sum(round(t.Hours * p.PostingRate, 2)) FROM PreconMember p JOIN PreconMemberTask t ON t.PreconMemberId = p.PreconMemberId
                JOIN VersionTask vt ON vt.VersionId = p.VersionId AND vt.ActivityId = t.ActivityId WHERE p.VersionId = @versionId
                """, new { versionId }, t) ?? 0;
            quoted = Math.Round((labour + billable) * pct / 100m, 0, MidpointRounding.AwayFromZero);
        }
        return new { versionId, area, lines, quotedPct = pct,
                     totals = new { labourActual = labour, expensesActual = actual, billableActual = billable, actual = labour + actual, quoted, difference = quoted - (labour + actual) } };
    }
    /// <summary>Quoted rate follows the Quoted %: billable lines get rate × %, the rest 0 (was: handleMarkupBlur).</summary>
    public static Task ApplyQuotedPct(Work w, long versionId, string area) => w.Exec($"""
        UPDATE CostLine c SET QuotedRate = CASE WHEN c.IsBillable THEN round(c.Rate * v.{(area == "precon" ? "PreconQuotedPct" : "GenreqQuotedPct")} / 100, 2) ELSE 0 END
        FROM ProjectVersion v WHERE v.VersionId = c.VersionId AND c.VersionId = @versionId AND c.Area = @area::CostArea
          AND c.QuotedRate IS DISTINCT FROM CASE WHEN c.IsBillable THEN round(c.Rate * v.{(area == "precon" ? "PreconQuotedPct" : "GenreqQuotedPct")} / 100, 2) ELSE 0 END
        """, new { versionId, area });
    static async Task<IResult> CostsDone(Work w, long versionId, long projectId, string area, string verb, object? key, object? after = null)
    {
        await ApplyQuotedPct(w, versionId, area);
        var sum = await Recalc.Summary(w, versionId, verb);
        await w.Log(projectId, versionId, verb, "cost", key, after: after);
        await w.Commit();
        return Results.Ok(new { costs = await LoadCosts(w.C, null, versionId, area), totalsHash = sum.Hash });
    }

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/projects/{id:long}/crew", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            await Access.Project(c, null, ctx.Me(), id, "viewer");
            return await LoadTeam(c, null, id, ctx.Me().UserId);
        });

        // Weeks from a start date and counts (the crew plan's Schedule button): sets the project dates, then rebuilds the weeks
        api.MapPut("/projects/{id:long}/schedule", async (long id, ScheduleIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            await Access.Project(w, id);
            if (body.PreconWeeks < 0 || body.BuildWeeks < 1 || body.CloseoutWeeks < 0 || body.PreconWeeks + body.BuildWeeks + body.CloseoutWeeks > 520)
                throw ApiException.Bad("The schedule needs at least 1 construction week and at most 520 weeks.");
            var start = Monday(body.StartsOn);
            var bs = start.AddDays(7 * body.PreconWeeks); var be = bs.AddDays(7 * body.BuildWeeks - 1);
            await w.Exec("""
                UPDATE Project SET PreconStartOn = CASE WHEN @pw > 0 THEN @start END, PreconEndOn = CASE WHEN @pw > 0 THEN @pe END,
                  BuildStartOn = @bs, BuildEndOn = @be, CloseoutOn = @co, ChangedBy = @u WHERE ProjectId = @id
                """, new { id, pw = body.PreconWeeks, start, pe = bs.AddDays(-1), bs, be, co = be.AddDays(7 * body.CloseoutWeeks), u = w.User.UserId });
            await RebuildWeeks(w, id, true);
            return await Done(w, id, "team.schedule", id, body);
        });

        // Project profile dates (was: Preconstruction start/end, Est construction start, Substantial completion, Closeout date)
        api.MapPut("/projects/{id:long}/dates", async (long id, DatesIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            await Access.Project(w, id);
            if (body.PreconStartOn is DateOnly ps && body.PreconEndOn is DateOnly pe && pe < ps) throw ApiException.Bad("Preconstruction end must be after its start date.");
            if ((body.PreconStartOn is null) != (body.PreconEndOn is null)) throw ApiException.Bad("Give both preconstruction dates, or neither.");
            if (body.BuildStartOn is DateOnly bs && body.BuildEndOn is DateOnly be && be < bs) throw ApiException.Bad("Substantial completion must be after the construction start date.");
            if ((body.BuildStartOn is null) != (body.BuildEndOn is null)) throw ApiException.Bad("Give both the construction start and substantial completion dates.");
            if (body.CloseoutOn is not null && body.BuildEndOn is null) throw ApiException.Bad("Set substantial completion before the closeout date.");
            if (body.BuildEndOn is DateOnly be2 && body.CloseoutOn is DateOnly co && co < be2) throw ApiException.Bad("The closeout date must be after substantial completion.");
            var s1 = body.PreconStartOn ?? body.BuildStartOn; var s2 = body.CloseoutOn ?? body.BuildEndOn;
            if (s1 is DateOnly a && s2 is DateOnly b && b.DayNumber - a.DayNumber > 3650) throw ApiException.Bad("The schedule can be at most 10 years.");
            if (body.SalaryEscalationPct is < 0 or > 25) throw ApiException.Bad("Salary escalation must be between 0 and 25% a year.");
            await w.Exec("""
                UPDATE Project SET PreconStartOn = @PreconStartOn, PreconEndOn = @PreconEndOn, BuildStartOn = @BuildStartOn, BuildEndOn = @BuildEndOn, CloseoutOn = @CloseoutOn,
                  SalaryEscalationPct = coalesce(@SalaryEscalationPct, SalaryEscalationPct), ChangedBy = @u
                WHERE ProjectId = @id
                """, new { id, body.PreconStartOn, body.PreconEndOn, body.BuildStartOn, body.BuildEndOn, body.CloseoutOn, body.SalaryEscalationPct, u = w.User.UserId });
            var changes = await RebuildWeeks(w, id, body.Preview != true);
            if (body.Preview == true) return Results.Ok(new { changes });   // never committed: nothing is saved
            await RecalcProject(w, id, "project.dates");
            await w.Log(id, null, "project.dates", "project", id, after: body);
            await w.Commit();
            return Results.Ok(new { changes });
        });

        api.MapPatch("/projects/{id:long}/crew-settings", async (long id, CrewSettingsIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            await Access.Project(w, id);
            if (body.SalaryEscalationPct is < 0 or > 25) throw ApiException.Bad("Salary escalation must be between 0 and 25% a year.");
            await w.Exec("UPDATE Project SET SalaryEscalationPct = coalesce(@e, SalaryEscalationPct) WHERE ProjectId = @id", new { id, e = body.SalaryEscalationPct });
            return await Done(w, id, "team.settings", id, body);
        });

        // Add team member (was: Add Team Member to project): team, role, person or TBA, posting rate, % quoted, default hours
        api.MapPost("/projects/{id:long}/crew", async (long id, MemberIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            await Access.Project(w, id);
            var group = body.TeamGroup ?? "operations";
            if (!Groups.Contains(group)) throw ApiException.Bad("Team must be operations, siteops or corporate.");
            if (body.QuotedPct is < 0 or > 1000) throw ApiException.Bad("% quoted must be between 0 and 1000.");
            if (body.HourlyCost is < 0) throw ApiException.Bad("The posting rate can't be negative.");
            var role = body.RoleLabel?.Trim(); decimal? cost = body.HourlyCost; int? roleId = body.StaffRoleId;
            if (body.StaffId is long sid)
            {
                var st = await w.One<(string name, int? roleId, decimal? cost, string? roleName)>("SELECT s.FullName, s.StaffRoleId, s.HourlyCost, r.RoleName FROM Staff s LEFT JOIN StaffRole r ON r.StaffRoleId = s.StaffRoleId WHERE s.StaffId = @sid AND s.CompanyId = @cid", new { sid, cid = w.User.CompanyId });
                if (st.name is null) throw ApiException.Bad("Unknown staff member.");
                role ??= st.roleName; cost ??= st.cost; roleId ??= st.roleId;
            }
            if (roleId is int rid)
            {
                var r = await w.One<(string name, decimal? rate)>("SELECT RoleName, DefaultRate FROM StaffRole WHERE StaffRoleId = @rid AND CompanyId = @cid", new { rid, cid = w.User.CompanyId });
                if (r.name is null) throw ApiException.Bad("Unknown employee role.");
                role ??= r.name; cost ??= r.rate;
            }
            if (string.IsNullOrWhiteSpace(role)) throw ApiException.Bad("Pick a role for the team member.");
            var key = (await w.Scalar<decimal?>("SELECT max(SortKey) FROM TeamMember WHERE ProjectId = @id", new { id }) ?? 0) + 1;
            var mid = await w.Scalar<long>("""
                INSERT INTO TeamMember (ProjectId, StaffId, StaffRoleId, RoleLabel, TeamGroup, SortKey, HourlyCost, BillRate, QuotedPct, RelocationCost, Note)
                VALUES (@id, @StaffId, @roleId, @role, @group, @key, @cost, @cost, coalesce(@QuotedPct, 100), coalesce(@RelocationCost, 0), @Note) RETURNING TeamMemberId
                """, new { id, body.StaffId, roleId, role, group, key, cost, body.QuotedPct, body.RelocationCost, body.Note });
            if (body.BuildHours is > 0) await SetPhaseHours(w, mid, id, "build", body.BuildHours.Value, null);
            if (body.CloseoutHours is > 0) await SetPhaseHours(w, mid, id, "closeout", body.CloseoutHours.Value, null);
            return await Done(w, id, "team.create", mid, new { role, body.StaffId, group });
        });

        // Edit a member: change employee, rate, % quoted, relocation, note, team, move (drag)
        api.MapPatch("/crew/{id:long}", async (long id, MemberIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.TeamMember(w, id);
            await TradeEndpoints.CheckRev(w, "TeamMember", "TeamMemberId", id, body.RowRev);
            if (body.TeamGroup is not null && !Groups.Contains(body.TeamGroup)) throw ApiException.Bad("Team must be operations, siteops or corporate.");
            if (body.QuotedPct is < 0 or > 1000) throw ApiException.Bad("% quoted must be between 0 and 1000.");
            if (body.HourlyCost is < 0 || body.RelocationCost is < 0) throw ApiException.Bad("Rates and costs can't be negative.");
            if (body.RoleLabel is not null && string.IsNullOrWhiteSpace(body.RoleLabel)) throw ApiException.Bad("The role can't be empty.");
            decimal? key = null;
            if (body.MoveAfterId is long after)
            {
                var a = after == 0 ? (decimal?)null : await w.Scalar<decimal?>("SELECT SortKey FROM TeamMember WHERE TeamMemberId = @after", new { after });
                var b = await w.Scalar<decimal?>("SELECT min(SortKey) FROM TeamMember WHERE ProjectId = @p AND SortKey > coalesce(@a, -1e9) AND TeamMemberId <> @id", new { p = s.ProjectId, a, id });
                key = a is null ? (b ?? 1) - 1 : b is null ? a + 1 : Math.Round((a.Value + b.Value) / 2, 4);
            }
            decimal? cost = body.HourlyCost; int? roleId = body.StaffRoleId; string? role = body.RoleLabel;
            if (body.StaffId is long sid && body.HourlyCost is null)
            {   // Change employee: take the person's posting rate and role (was: cmdSelectEmployee)
                var st = await w.One<(decimal? cost, int? roleId, string? roleName)>("SELECT s.HourlyCost, s.StaffRoleId, r.RoleName FROM Staff s LEFT JOIN StaffRole r ON r.StaffRoleId = s.StaffRoleId WHERE s.StaffId = @sid AND s.CompanyId = @cid", new { sid, cid = w.User.CompanyId });
                if (st.cost is null && st.roleId is null && st.roleName is null) throw ApiException.Bad("Unknown staff member.");
                cost = st.cost; roleId ??= st.roleId; role ??= st.roleName;
            }
            await w.Exec("""
                UPDATE TeamMember SET StaffId = CASE WHEN @ClearStaff THEN NULL ELSE coalesce(@StaffId, StaffId) END, StaffRoleId = coalesce(@roleId, StaffRoleId),
                  RoleLabel = coalesce(trim(@role), RoleLabel), TeamGroup = coalesce(@TeamGroup, TeamGroup), HourlyCost = coalesce(@cost, HourlyCost),
                  QuotedPct = coalesce(@QuotedPct, QuotedPct), RelocationCost = coalesce(@RelocationCost, RelocationCost),
                  Note = CASE WHEN @Note IS NULL THEN Note ELSE nullif(@Note, '') END, SortKey = coalesce(@key, SortKey), ChangedAt = now(), RowRev = RowRev + 1
                WHERE TeamMemberId = @id
                """, new { ClearStaff = body.ClearStaff ?? false, body.StaffId, roleId, role, body.TeamGroup, cost, body.QuotedPct, body.RelocationCost, body.Note, key, id });
            return await Done(w, s.ProjectId, "team.update", id, body);
        });

        api.MapDelete("/crew/{id:long}", async (long id, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.TeamMember(w, id);
            await w.Exec("DELETE FROM TeamMember WHERE TeamMemberId = @id", new { id });
            return await Done(w, s.ProjectId, "team.delete", id);
        });

        // Update posting rates (was: Update Posting Rates): every member back to the person's (or role's) current rate
        api.MapPost("/projects/{id:long}/crew/reset-rates", async (long id, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            await Access.Project(w, id);
            var n = await w.Exec("""
                UPDATE TeamMember m SET HourlyCost = coalesce(s.HourlyCost, r.DefaultRate, m.HourlyCost), RowRev = m.RowRev + 1
                FROM TeamMember x LEFT JOIN Staff s ON s.StaffId = x.StaffId LEFT JOIN StaffRole r ON r.StaffRoleId = x.StaffRoleId
                WHERE x.TeamMemberId = m.TeamMemberId AND m.ProjectId = @id AND m.HourlyCost IS DISTINCT FROM coalesce(s.HourlyCost, r.DefaultRate, m.HourlyCost)
                """, new { id });
            return await Done(w, id, "team.rates", id, new { updated = n });
        });

        // Week cells (resource loader by week): fill a range, paste, clear
        api.MapPut("/crew/{id:long}/hours", async (long id, HoursIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.TeamMember(w, id);
            if (body.Cells is null || body.Cells.Count == 0) throw ApiException.Bad("Nothing to save.");
            if (body.Cells.Any(c => c.Hours is < 0 or > 80)) throw ApiException.Bad("Hours per week must be between 0 and 80.");
            var weekIds = body.Cells.Select(c => c.WeekId).ToArray();
            var valid = await w.Scalar<int>("SELECT count(*) FROM ProjectWeek WHERE ProjectId = @p AND WeekId = ANY(@weekIds)", new { p = s.ProjectId, weekIds });
            if (valid != weekIds.Distinct().Count()) throw ApiException.Bad("Some weeks are not on this project's schedule.");
            foreach (var c in body.Cells)
            {
                if (c.Hours == 0) await w.Exec("DELETE FROM TeamMemberWeek WHERE TeamMemberId = @id AND WeekId = @wk", new { id, wk = c.WeekId });
                else await w.Exec("INSERT INTO TeamMemberWeek (TeamMemberId, WeekId, Hours) VALUES (@id, @wk, @h) ON CONFLICT (TeamMemberId, WeekId) DO UPDATE SET Hours = EXCLUDED.Hours", new { id, wk = c.WeekId, h = c.Hours });
            }
            await w.Exec("UPDATE TeamMember SET ChangedAt = now() WHERE TeamMemberId = @id", new { id });
            return await Done(w, s.ProjectId, "team.hours", id, new { cells = body.Cells.Count });
        });

        // Month cells (resource loader by month): the month's hours are spread evenly over its weeks
        api.MapPut("/crew/{id:long}/month-hours", async (long id, MonthIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.TeamMember(w, id);
            var weeks = (await w.Query<long>("SELECT WeekId FROM ProjectWeek WHERE ProjectId = @p AND MonthNo = @m AND Phase <> 'precon' ORDER BY WeekNo", new { p = s.ProjectId, m = body.MonthNo })).ToList();
            if (weeks.Count == 0) throw ApiException.Bad("That month is not on the construction or closeout schedule.");
            if (body.Hours < 0 || body.Hours > 80 * weeks.Count) throw ApiException.Bad($"A month here can take 0 to {80 * weeks.Count} hours.");
            var each = Math.Round(body.Hours / weeks.Count, 2);
            for (var i = 0; i < weeks.Count; i++)
            {
                var h = i == weeks.Count - 1 ? body.Hours - each * (weeks.Count - 1) : each;   // the last week takes the rounding
                if (h <= 0) await w.Exec("DELETE FROM TeamMemberWeek WHERE TeamMemberId = @id AND WeekId = @wk", new { id, wk = weeks[i] });
                else await w.Exec("INSERT INTO TeamMemberWeek (TeamMemberId, WeekId, Hours) VALUES (@id, @wk, @h) ON CONFLICT (TeamMemberId, WeekId) DO UPDATE SET Hours = EXCLUDED.Hours", new { id, wk = weeks[i], h });
            }
            return await Done(w, s.ProjectId, "team.hours", id, body);
        });

        // Set hours/budget (was: Set Team Member Budget): weekly hours for a phase (the browser turns a budget into hours)
        api.MapPut("/crew/{id:long}/phase-hours", async (long id, List<PhaseHoursIn> body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.TeamMember(w, id);
            if (body is null || body.Count == 0) throw ApiException.Bad("Nothing to save.");
            foreach (var p in body) await SetPhaseHours(w, id, s.ProjectId, p.Phase, p.Hours, p.Weeks);
            return await Done(w, s.ProjectId, "team.hours", id, body);
        });

        // Set per diem (was: Set Per Diem)
        api.MapPut("/crew/{id:long}/per-diem", async (long id, PerDiemIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.TeamMember(w, id);
            if (body.Clear == true)
            {
                await w.Exec("UPDATE TeamMember SET PerDiemPlaceId = NULL, PerDiemStart = NULL, PerDiemEnd = NULL, PerDiemDaysCount = 0, PerDiemCost = 0, RowRev = RowRev + 1 WHERE TeamMemberId = @id", new { id });
                return await Done(w, s.ProjectId, "team.perdiem", id, body);
            }
            var days = body.Days ?? "weekdays"; var kind = body.Kind ?? "both";
            if (days is not ("weekdays" or "alldays")) throw ApiException.Bad("Per diem days must be week days or all days.");
            if (kind is not ("hotel" or "meals" or "both")) throw ApiException.Bad("Expense type must be hotel, meals or both.");
            if (body.PlaceId is null || body.Start is null || body.End is null) throw ApiException.Bad("Pick a destination and the travel dates.");
            if (body.End < body.Start) throw ApiException.Bad("The travel end date is before the start date.");
            if (body.End.Value.DayNumber - body.Start.Value.DayNumber > 3660) throw ApiException.Bad("Travel can be at most 10 years.");
            var place = await w.One<(int[]? lodging, int meals)>("SELECT Lodging, Meals FROM PerDiemPlace WHERE PlaceId = @p", new { p = body.PlaceId });
            if (place.lodging is null) throw ApiException.Bad("Unknown per diem destination.");
            var (n, cost) = Crew.PerDiem(body.Start.Value, body.End.Value, days == "alldays", kind, place.lodging, place.meals);
            await w.Exec("""
                UPDATE TeamMember SET PerDiemPlaceId = @p, PerDiemStart = @st, PerDiemEnd = @en, PerDiemDays = @days, PerDiemKind = @kind, PerDiemDaysCount = @n, PerDiemCost = @cost, RowRev = RowRev + 1
                WHERE TeamMemberId = @id
                """, new { id, p = body.PlaceId, st = body.Start, en = body.End, days, kind, n, cost });
            return await Done(w, s.ProjectId, "team.perdiem", id, new { body.PlaceId, body.Start, body.End, days, kind, n, cost });
        });

        // Per diem destinations: city / county / zip / state search (was: Matching Rates)
        api.MapGet("/per-diem/places", async (string? q, string? state, string? zip, HttpContext ctx, Db db) =>
        {
            ctx.Me();
            await using var c = await db.Open();
            var year = await c.ExecuteScalarAsync<int?>("SELECT max(FiscalYear) FROM PerDiemPlace") ?? DateTime.Today.Year;
            return await c.QueryAsync("""
                SELECT p.PlaceId AS "placeId", p.City AS "city", p.County AS "county", p.StateCode AS "stateCode", p.LocationDefined AS "locationDefined",
                       p.Lodging AS "lodging", p.Meals AS "meals", p.FiscalYear AS "fiscalYear"
                FROM PerDiemPlace p
                WHERE p.FiscalYear = @year
                  AND (@state::text IS NULL OR p.StateCode = upper(@state) OR p.StateCode IS NULL)
                  AND (@zip::text IS NULL OR p.PlaceId IN (SELECT z.PlaceId FROM PerDiemZip z WHERE z.Zip = @zip))
                  AND (@q::text IS NULL OR p.City ILIKE '%' || @q || '%' OR p.County ILIKE '%' || @q || '%' OR p.LocationDefined ILIKE '%' || @q || '%' OR p.City = 'Standard Rate')
                ORDER BY (p.City = 'Standard Rate'), p.StateCode NULLS FIRST, p.City LIMIT 60
                """, new { year, q = string.IsNullOrWhiteSpace(q) ? null : q.Trim(), state = string.IsNullOrWhiteSpace(state) ? null : state.Trim(), zip = string.IsNullOrWhiteSpace(zip) ? null : zip.Trim() });
        });

        // Columns (was: Add Columns / display options): per person, for this project or every project
        api.MapPut("/columns/{grid}", async (string grid, ColumnsIn body, HttpContext ctx, Db db) =>
        {
            var me = ctx.Me();
            if (grid is not "crew") throw ApiException.Bad("Unknown grid.");
            if (body.Columns is null || body.Columns.Count > 40 || body.Columns.Any(x => x.Length > 40)) throw ApiException.Bad("Too many columns.");
            await using var c = await db.Open();
            var pid = body.AllProjects == true ? 0 : body.ProjectId ?? 0;
            if (body.AllProjects == true) await c.ExecuteAsync("DELETE FROM ColumnChoice WHERE UserId = @u AND GridKey = @grid", new { u = me.UserId, grid });
            await c.ExecuteAsync("""
                INSERT INTO ColumnChoice (UserId, GridKey, ProjectId, Columns) VALUES (@u, @grid, @pid, @cols)
                ON CONFLICT (UserId, GridKey, ProjectId) DO UPDATE SET Columns = EXCLUDED.Columns
                """, new { u = me.UserId, grid, pid, cols = body.Columns.Distinct().ToArray() });
            return Results.Ok(new { columns = body.Columns });
        });

        // ------------------------------------------------------------------ precon services expenses / project overheads
        api.MapGet("/versions/{id:long}/costs", async (long id, string area, HttpContext ctx, Db db) =>
        {
            CheckArea(area);
            await using var c = await db.Open();
            await Access.Version(c, ctx.Me(), id);
            return await LoadCosts(c, null, id, area);
        });

        api.MapPost("/versions/{id:long}/costs", async (long id, CostIn body, HttpContext ctx) =>
        {
            CheckArea(body.Area);
            await using var w = await Work.Begin(ctx);
            var s = await Access.Version(w, id);
            if (string.IsNullOrWhiteSpace(body.ItemName)) throw ApiException.Bad("Give the expense a description.");
            if (body.Qty < 0 || body.Rate < 0) throw ApiException.Bad("Quantity and rate can't be negative.");
            var cid = await w.Scalar<long>("""
                INSERT INTO CostLine (VersionId, Area, CategoryName, ItemName, SortKey, Qty, UomCode, Rate, IsBillable, Note, CsiCode, ChangedBy)
                VALUES (@id, @Area::CostArea, coalesce(nullif(trim(@CategoryName), ''), 'General'), trim(@ItemName),
                        (SELECT coalesce(max(SortKey), 0) + 1 FROM CostLine WHERE VersionId = @id AND Area = @Area::CostArea),
                        coalesce(@Qty, 1), nullif(upper(@UomCode), ''), coalesce(@Rate, 0), coalesce(@IsBillable, true), @Note, nullif(trim(@CsiCode), ''), @u) RETURNING CostLineId
                """, new { id, body.Area, body.CategoryName, body.ItemName, body.Qty, body.UomCode, body.Rate, body.IsBillable, body.Note, body.CsiCode, u = w.User.UserId });
            return await CostsDone(w, id, s.ProjectId, body.Area!, "cost.create", cid, body);
        });

        api.MapPatch("/costs/{id:long}", async (long id, CostIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Cost(w, id);
            await TradeEndpoints.CheckRev(w, "CostLine", "CostLineId", id, body.RowRev);
            if (body.Qty < 0 || body.Rate < 0) throw ApiException.Bad("Quantity and rate can't be negative.");
            var area = await w.Scalar<string>("SELECT Area::text FROM CostLine WHERE CostLineId = @id", new { id });
            await w.Exec("""
                UPDATE CostLine SET CategoryName = coalesce(nullif(trim(@CategoryName), ''), CategoryName), ItemName = CASE WHEN @ItemName IS NULL THEN ItemName ELSE trim(@ItemName) END,
                  Qty = coalesce(@Qty, Qty), UomCode = CASE WHEN @UomCode IS NULL THEN UomCode ELSE nullif(upper(@UomCode), '') END, Rate = coalesce(@Rate, Rate),
                  IsBillable = coalesce(@IsBillable, IsBillable), CsiCode = CASE WHEN @CsiCode IS NULL THEN CsiCode ELSE nullif(trim(@CsiCode), '') END,
                  Note = CASE WHEN @Note IS NULL THEN Note ELSE nullif(@Note, '') END, ChangedBy = @u, ChangedAt = now(), RowRev = RowRev + 1
                WHERE CostLineId = @id
                """, new { body.CategoryName, body.ItemName, body.Qty, body.UomCode, body.Rate, body.IsBillable, body.CsiCode, body.Note, u = w.User.UserId, id });
            return await CostsDone(w, s.VersionId, s.ProjectId, area, "cost.update", id, body);
        });

        api.MapDelete("/costs/{id:long}", async (long id, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Cost(w, id);
            var area = await w.Scalar<string>("SELECT Area::text FROM CostLine WHERE CostLineId = @id", new { id });
            await w.Exec("DELETE FROM CostLine WHERE CostLineId = @id", new { id });
            return await CostsDone(w, s.VersionId, s.ProjectId, area, "cost.delete", id);
        });

        // Delete selected (was: Delete Selected, with confirmation)
        api.MapPost("/versions/{id:long}/costs/delete", async (long id, string area, IdsIn body, HttpContext ctx) =>
        {
            CheckArea(area);
            await using var w = await Work.Begin(ctx);
            var s = await Access.Version(w, id);
            if (body.Ids is null || body.Ids.Count == 0) throw ApiException.Bad("Nothing selected.");
            var n = await w.Exec("DELETE FROM CostLine WHERE VersionId = @id AND Area = @area::CostArea AND CostLineId = ANY(@ids)", new { id, area, ids = body.Ids.ToArray() });
            return await CostsDone(w, id, s.ProjectId, area, "cost.delete", $"{n} lines", new { deleted = n });
        });

        // Quoted % or Quoted total (a total back-calculates the %, capped at 100%)
        api.MapPut("/versions/{id:long}/costs/quoted", async (long id, QuotedIn body, HttpContext ctx) =>
        {
            CheckArea(body.Area);
            await using var w = await Work.Begin(ctx);
            var s = await Access.Version(w, id);
            decimal pct;
            if (body.QuotedTotal is decimal qt)
            {
                if (qt < 0) throw ApiException.Bad("The quoted total can't be negative.");
                dynamic cur = await LoadCosts(w.C, w.Tx, id, body.Area);
                // the % applies to what's quoted to the client: billable lines (+ labour for precon), so divide by that
                decimal quotable = (decimal)cur.totals.billableActual + (decimal)cur.totals.labourActual;
                pct = quotable > 0 ? qt / quotable * 100m : 0;
            }
            else pct = body.QuotedPct ?? throw ApiException.Bad("Send a Quoted % or a Quoted total.");
            pct = Math.Round(Math.Clamp(pct, 0, 100), 3);
            await w.Exec($"UPDATE ProjectVersion SET {(body.Area == "precon" ? "PreconQuotedPct" : "GenreqQuotedPct")} = @pct WHERE VersionId = @id", new { id, pct });
            return await CostsDone(w, id, s.ProjectId, body.Area, "cost.quoted", id, new { pct });
        });

        // Copy expense template (was: Copy Expense Template)
        api.MapPost("/versions/{id:long}/costs/apply-kit", async (long id, CostKitIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Version(w, id);
            await Access.CostKit(w, body.CostKitId);
            var area = await w.Scalar<string?>("SELECT Area::text FROM CostKit WHERE CostKitId = @k", new { k = body.CostKitId }) ?? throw ApiException.Bad("Unknown cost template.");
            var n = await w.Exec("""
                INSERT INTO CostLine (VersionId, Area, CategoryName, ItemName, SortKey, Qty, UomCode, Rate, ChangedBy)
                SELECT @id, k.Area, i.CategoryName, i.ItemName,
                       (SELECT coalesce(max(SortKey), 0) FROM CostLine WHERE VersionId = @id AND Area = k.Area) + row_number() OVER (ORDER BY i.SortOrder, i.CostKitItemId),
                       coalesce(i.DefaultQty, 0), i.UomCode, coalesce(i.DefaultRate, 0), @u
                FROM CostKit k JOIN CostKitItem i ON i.CostKitId = k.CostKitId
                WHERE k.CostKitId = @k AND NOT EXISTS (SELECT 1 FROM CostLine c WHERE c.VersionId = @id AND c.Area = k.Area AND lower(c.ItemName) = lower(i.ItemName))
                """, new { id, k = body.CostKitId, u = w.User.UserId });
            await ApplyQuotedPct(w, id, area);
            var sum = await Recalc.Summary(w, id, "cost.kit");
            await w.Log(s.ProjectId, id, "cost.kit", "version", id, after: new { body.CostKitId, added = n });
            await w.Commit();
            return Results.Ok(new { added = n, costs = await LoadCosts(w.C, null, id, area), totalsHash = sum.Hash });
        });
    }

    static void CheckArea(string? area) { if (area is not ("precon" or "genreq")) throw ApiException.Bad("Area must be 'precon' or 'genreq'."); }
}
