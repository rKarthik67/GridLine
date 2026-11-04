using Gridline.Calc;
using Gridline.Endpoints;
using Gridline.Infrastructure;
using NpgsqlTypes;

namespace Gridline.Import;

/// <summary>
/// Writes a checked workbook in one transaction. Rows are streamed into temporary tables with COPY and
/// then inserted set-based, so a 100k+ row workbook takes seconds, not minutes.
/// </summary>
public static class ImportService
{
    public sealed record Result(long ProjectId, long VersionId, string ProjectNo, int VersionNo, int TradesAdded, int TradesUpdated, int BidsAdded, int PartnersAdded);

    public static async Task<Result> CreateProject(Work w, ImportModel m)
    {
        var p = new ProjectEndpoints.ProjectIn
        {
            ProjectName = m.P("Project name"), ProjectNo = m.P("Project number"), ClientName = m.P("Client"), ProjectType = m.P("Project type"),
            ContractType = m.P("Contract type"), Stage = Template.Stage(m.P("Stage")), Street = m.P("Address"), City = m.P("City"), StateCode = m.P("State"),
            CountyName = m.P("County"), BidDueOn = m.PDate("Bid due date"), GrossArea = (int?)m.PNum("Gross SF"),
            DivisionId = m.P("Business unit") is string bu ? await w.Scalar<int?>("SELECT DivisionId FROM Division WHERE lower(DivisionName) = lower(@bu) OR lower(ShortCode) = lower(@bu)", new { bu }) : null,
            EstimatorId = await FindUser(w, m.P("Estimator")), ManagerId = await FindUser(w, m.P("Project manager")),
        };
        var (pid, vid, no) = await ProjectEndpoints.CreateProject(w, p);
        await w.Exec("UPDATE ProjectVersion SET BuildMonths = @months WHERE VersionId = @vid", new { months = m.PNum("Duration (months)"), vid });
        async Task Rate(string code, decimal? pct) { if (pct is decimal r) await w.Exec("UPDATE VersionMarkup SET RatePct = @r WHERE VersionId = @vid AND MarkupCode = @code", new { r, vid, code }); }
        await Rate("fee", m.PNum("Fee %")); await Rate("reserveproject", m.PNum("Reserve (contingency) %")); await Rate("cover", m.PNum("Works cover (builders risk) rate"));
        await w.Exec("UPDATE ProjectVersion SET FeePct = coalesce((SELECT RatePct FROM VersionMarkup WHERE VersionId = @vid AND MarkupCode = 'fee'), FeePct) WHERE VersionId = @vid", new { vid });

        short order = 1;
        foreach (var z in m.Zones)
            await w.Exec("INSERT INTO Zone (VersionId, ZoneCode, ZoneName, SortOrder, FloorArea, UnitCount, UnitLabel, DefaultShare) VALUES (@vid, @Code, @Name, @order, @Area, @Units, CASE WHEN @Units > 0 THEN 'Unit' ELSE 'N/A' END, @share)",
                new { vid, z.Code, z.Name, order = order++, z.Area, z.Units, share = z.SplitPct / 100m });
        if (p.GrossArea is null or 0 && m.Zones.Count > 0)
            await w.Exec("UPDATE ProjectVersion SET GrossArea = (SELECT coalesce(sum(FloorArea), 0) FROM Zone WHERE VersionId = @vid), ZonesSetArea = true WHERE VersionId = @vid", new { vid });
        else if (p.GrossArea is > 0)
            await w.Exec("UPDATE ProjectVersion SET ZonesSetArea = false WHERE VersionId = @vid", new { vid });

        if (m.Requirements.Count > 0)
        {
            var setId = await w.Scalar<long>("INSERT INTO RequirementSet (SetName, IsTemplate, DivisionId) VALUES (@n, false, @d) RETURNING RequirementSetId", new { n = $"{p.ProjectName} – base requirements", d = p.DivisionId });
            await w.Exec("UPDATE ProjectVersion SET RequirementSetId = @setId WHERE VersionId = @vid", new { setId, vid });
        }

        var (added, updated, bids, partners) = await Trades(w, vid, m, newProject: true);
        await Crew(w, pid, m);
        await ProjectEndpoints.ApplyDefaultCostKits(w, vid);
        await Recalc.VersionBulk(w, vid, "import.create");
        return new Result(pid, vid, no, 1, added, updated, bids, partners);
    }

    /// <summary>Trades + Bidders sheets into an existing version: new CSI codes are added, existing ones get their budget and any new bidders.</summary>
    public static async Task<Result> IntoVersion(Work w, long versionId, ImportModel m)
    {
        var (added, updated, bids, partners) = await Trades(w, versionId, m, newProject: false);
        await Recalc.VersionBulk(w, versionId, "import.trades");
        var v = await w.One<(long pid, string no, int vno)>("SELECT p.ProjectId, p.ProjectNo, v.VersionNo FROM ProjectVersion v JOIN Project p ON p.ProjectId = v.ProjectId WHERE v.VersionId = @versionId", new { versionId });
        return new Result(v.pid, versionId, v.no, v.vno, added, updated, bids, partners);
    }

    static async Task<long?> FindUser(Work w, string? who)
    {
        if (string.IsNullOrWhiteSpace(who)) return null;
        var s = who.Trim();
        var local = s.Contains('@') ? s[..s.IndexOf('@')] : null;
        return await w.Scalar<long?>("""
            SELECT UserId FROM AppUser WHERE IsActive AND (LoginEmail = @s::citext OR split_part(LoginEmail::text, '@', 1) = lower(@local)
                OR lower(GivenName || ' ' || FamilyName) = lower(@s) OR lower(left(GivenName, 1) || '. ' || FamilyName) = lower(@s) OR lower(left(GivenName, 1) || '.' || FamilyName) = lower(@s))
            ORDER BY UserId LIMIT 1
            """, new { s, local });
    }

    static async Task<(int added, int updated, int bids, int partners)> Trades(Work w, long vid, ImportModel m, bool newProject)
    {
        var u = w.User.UserId;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        void Lap(string what) { Console.WriteLine($"[import] {what}: {sw.ElapsedMilliseconds} ms"); sw.Restart(); }
        await w.Exec("""
            DROP TABLE IF EXISTS imptrade, impbid, impsplit, impreq;
            CREATE TEMP TABLE imptrade (rowno int, code text, title text, budget numeric, kit text, estimator text, due date, notes text) ON COMMIT DROP;
            CREATE TEMP TABLE impbid (rowno int, code text, partner text, contact text, email text, phone text, state text, base numeric, diverse boolean, notes text) ON COMMIT DROP;
            CREATE TEMP TABLE impsplit (code text, zone text, share numeric) ON COMMIT DROP;
            CREATE TEMP TABLE impreq (sortkey numeric, section text, code text, linetext text, desired text, isheading boolean) ON COMMIT DROP;
            """);
        await using (var c = await w.C.BeginBinaryImportAsync("COPY imptrade FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var t in m.Trades)
            {
                await c.StartRowAsync();
                await c.WriteAsync(t.Row, NpgsqlDbType.Integer); await c.WriteAsync(t.Code, NpgsqlDbType.Text); await c.WriteAsync(t.Title, NpgsqlDbType.Text);
                await W(c, t.Budget, NpgsqlDbType.Numeric); await W(c, t.Kit, NpgsqlDbType.Text); await W(c, t.Estimator, NpgsqlDbType.Text);
                await W(c, t.Due, NpgsqlDbType.Date); await W(c, t.Notes, NpgsqlDbType.Text);
            }
            await c.CompleteAsync();
        }
        await using (var c = await w.C.BeginBinaryImportAsync("COPY impbid FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var b in m.Bidders)
            {
                await c.StartRowAsync();
                await c.WriteAsync(b.Row, NpgsqlDbType.Integer); await c.WriteAsync(b.Code, NpgsqlDbType.Text); await c.WriteAsync(b.Partner, NpgsqlDbType.Text);
                await W(c, b.Contact, NpgsqlDbType.Text); await W(c, b.Email, NpgsqlDbType.Text); await W(c, b.Phone, NpgsqlDbType.Text);
                await c.WriteAsync(b.State, NpgsqlDbType.Text); await W(c, b.Base, NpgsqlDbType.Numeric); await c.WriteAsync(b.Diverse, NpgsqlDbType.Boolean); await W(c, b.Notes, NpgsqlDbType.Text);
            }
            await c.CompleteAsync();
        }
        await using (var c = await w.C.BeginBinaryImportAsync("COPY impsplit FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var t in m.Trades) foreach (var (zone, share) in t.Split)
            { await c.StartRowAsync(); await c.WriteAsync(t.Code, NpgsqlDbType.Text); await c.WriteAsync(zone, NpgsqlDbType.Text); await c.WriteAsync(share, NpgsqlDbType.Numeric); }
            await c.CompleteAsync();
        }
        if (m.Requirements.Count > 0)
            await using (var c = await w.C.BeginBinaryImportAsync("COPY impreq FROM STDIN (FORMAT BINARY)"))
            {
                decimal key = 0; string? section = null;
                // one heading per section: lines are grouped by section in the order sections first appear
                var order = m.Requirements.Select(r => r.Section.ToLowerInvariant()).Distinct().Select((sec, i) => (sec, i)).ToDictionary(x => x.sec, x => x.i);
                foreach (var r in m.Requirements.OrderBy(r => order[r.Section.ToLowerInvariant()]))   // OrderBy is stable
                {
                    if (!string.Equals(r.Section, section, StringComparison.OrdinalIgnoreCase))
                    {
                        section = r.Section;
                        await c.StartRowAsync(); await c.WriteAsync(++key, NpgsqlDbType.Numeric); await c.WriteAsync(r.Section, NpgsqlDbType.Text); await c.WriteNullAsync();
                        await c.WriteAsync(r.Section, NpgsqlDbType.Text); await c.WriteNullAsync(); await c.WriteAsync(true, NpgsqlDbType.Boolean);
                    }
                    await c.StartRowAsync(); await c.WriteAsync(++key, NpgsqlDbType.Numeric); await c.WriteAsync(r.Section, NpgsqlDbType.Text); await W(c, r.Code, NpgsqlDbType.Text);
                    await c.WriteAsync(r.Text, NpgsqlDbType.Text); await c.WriteAsync(r.Desired, NpgsqlDbType.Text); await c.WriteAsync(false, NpgsqlDbType.Boolean);
                }
                await c.CompleteAsync();
            }

        await w.Exec("CREATE INDEX ON imptrade (code); CREATE INDEX ON impbid (code); CREATE INDEX ON impreq (code); ANALYZE imptrade; ANALYZE impbid; ANALYZE impsplit; ANALYZE impreq;");
        Lap("copy to staging");
        var p = new { vid, u };
        var updated = newProject ? 0 : await w.Exec("""
            UPDATE Trade t SET BudgetValue = coalesce(i.budget, t.BudgetValue), TeamNote = coalesce(t.TeamNote, i.notes), BidDueOn = coalesce(i.due, t.BidDueOn), ChangedBy = @u
            FROM imptrade i WHERE t.VersionId = @vid AND t.ArchivedAt IS NULL AND t.ScopeNo = 1 AND t.CsiCode = i.code
            """, p);
        var added = await w.Exec("""
            INSERT INTO Trade (VersionId, CodeId, CsiCode, TradeName, ScopeNo, KitId, EstimatorId, BidDueOn, BudgetValue, TeamNote, SortOrder, ChangedBy)
            SELECT @vid, wc.CodeId, i.code, i.title, 1, k.KitId, eu.UserId, i.due, i.budget, i.notes,
                   (SELECT coalesce(max(SortOrder), 0) FROM Trade WHERE VersionId = @vid) + row_number() OVER (ORDER BY i.rowno) * 10, @u
            FROM imptrade i
            LEFT JOIN LATERAL (SELECT x.CodeId FROM WorkCode x JOIN CodeGroup g ON g.GroupId = x.GroupId JOIN CodeEdition e ON e.EditionId = g.EditionId WHERE x.CsiCode = i.code ORDER BY e.IsCurrent DESC LIMIT 1) wc ON true
            LEFT JOIN LATERAL (SELECT KitId FROM ScopeKit WHERE lower(KitName) = lower(i.kit) LIMIT 1) k ON true
            LEFT JOIN LATERAL (SELECT UserId FROM AppUser WHERE i.estimator IS NOT NULL AND (LoginEmail = i.estimator::citext OR lower(GivenName || ' ' || FamilyName) = lower(i.estimator)
                                OR lower(left(GivenName, 1) || '. ' || FamilyName) = lower(i.estimator) OR split_part(LoginEmail::text, '@', 1) = lower(split_part(i.estimator, '@', 1))) LIMIT 1) eu ON true
            WHERE NOT EXISTS (SELECT 1 FROM Trade t WHERE t.VersionId = @vid AND t.CsiCode = i.code AND t.ArchivedAt IS NULL)
            """, p);

        Lap("trades");
        // trade partners and their contacts
        var partners = await w.Exec("""
            INSERT INTO TradePartner (CompanyName, AddedByUser, DiverseTags)
            SELECT DISTINCT ON (lower(partner)) partner, true, CASE WHEN diverse THEN ARRAY['diverse'] ELSE '{}'::text[] END FROM impbid ORDER BY lower(partner), rowno
            ON CONFLICT ((lower(CompanyName))) DO NOTHING
            """);
        await w.Exec("""
            INSERT INTO PartnerPerson (PartnerId, FullName, Email, Phone, IsPrimary)
            SELECT DISTINCT ON (tp.PartnerId, lower(coalesce(i.email, i.contact))) tp.PartnerId, coalesce(i.contact, i.email), i.email::citext, i.phone,
                   NOT EXISTS (SELECT 1 FROM PartnerPerson x WHERE x.PartnerId = tp.PartnerId)
            FROM impbid i JOIN TradePartner tp ON lower(tp.CompanyName) = lower(i.partner)
            WHERE coalesce(i.email, i.contact) IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM PartnerPerson x WHERE x.PartnerId = tp.PartnerId AND (x.Email = i.email::citext OR (i.email IS NULL AND x.FullName = i.contact)))
            ORDER BY tp.PartnerId, lower(coalesce(i.email, i.contact)), i.rowno
            """);
        var bids = await w.Exec("""
            INSERT INTO Bid (TradeId, PartnerId, PersonId, BidderName, LaneNo, State, IsPosted, BaseAmount, BaseSource, ChangedBy)
            SELECT t.TradeId, tp.PartnerId, pp.PersonId, tp.CompanyName,
                   coalesce((SELECT max(LaneNo) FROM Bid x WHERE x.TradeId = t.TradeId AND x.LaneNo > 0), 0) + row_number() OVER (PARTITION BY t.TradeId ORDER BY i.rowno),
                   i.state::BidState, i.base IS NOT NULL AND i.state NOT IN ('invited','bidding','declined'), i.base, 'import', @u
            FROM impbid i
            JOIN Trade t ON t.VersionId = @vid AND t.CsiCode = i.code AND t.ScopeNo = 1 AND t.ArchivedAt IS NULL
            JOIN TradePartner tp ON lower(tp.CompanyName) = lower(i.partner)
            LEFT JOIN LATERAL (SELECT x.PersonId FROM PartnerPerson x WHERE x.PartnerId = tp.PartnerId AND (x.Email = i.email::citext OR x.FullName = i.contact) ORDER BY x.IsPrimary DESC LIMIT 1) pp ON true
            WHERE NOT EXISTS (SELECT 1 FROM Bid b WHERE b.TradeId = t.TradeId AND b.PartnerId = tp.PartnerId AND b.ArchivedAt IS NULL)
            """, p);

        Lap("partners and bids");
        // zone split: from the sheet, otherwise the zones' default split
        await w.Exec("""
            DELETE FROM TradeZone tz USING Trade t WHERE tz.TradeId = t.TradeId AND t.VersionId = @vid AND t.CsiCode IN (SELECT code FROM impsplit);
            INSERT INTO TradeZone (TradeId, ZoneId, Share)
            SELECT t.TradeId, z.ZoneId, s.share FROM impsplit s JOIN Trade t ON t.VersionId = @vid AND t.CsiCode = s.code AND t.ScopeNo = 1 AND t.ArchivedAt IS NULL
            JOIN Zone z ON z.VersionId = @vid AND z.ZoneCode = s.zone;
            INSERT INTO TradeZone (TradeId, ZoneId, Share)
            SELECT t.TradeId, z.ZoneId, z.DefaultShare FROM Trade t JOIN Zone z ON z.VersionId = t.VersionId
            WHERE t.VersionId = @vid AND t.ArchivedAt IS NULL AND z.DefaultShare > 0 AND NOT EXISTS (SELECT 1 FROM TradeZone x WHERE x.TradeId = t.TradeId);
            """, p);

        await w.Exec("""
            DROP TABLE IF EXISTS needreq, tradesec;
            CREATE TEMP TABLE needreq ON COMMIT DROP AS
            SELECT t.TradeId, t.CsiCode, t.KitId FROM Trade t WHERE t.VersionId = @vid AND t.ArchivedAt IS NULL
              AND NOT EXISTS (SELECT 1 FROM ScopeLine x WHERE x.TradeId = t.TradeId AND x.Section = 'baserequirement');
            CREATE INDEX ON needreq (CsiCode); ANALYZE needreq;
            """, p);
        // base requirements: the workbook's own list (per trade when a CSI code is given), otherwise the version's requirement set
        if (m.Requirements.Count > 0)
            await w.Exec("""
                ALTER TABLE impreq ADD COLUMN rlid bigint;
                INSERT INTO RequirementLine (RequirementSetId, SortKey, SectionName, IsHeading, LineText, Desired)
                SELECT v.RequirementSetId, r.sortkey, r.section, r.isheading, r.linetext, r.desired::DesiredKind FROM impreq r JOIN ProjectVersion v ON v.VersionId = @vid
                WHERE r.code IS NULL OR r.isheading;
                UPDATE impreq r SET rlid = rl.RequirementLineId FROM RequirementLine rl JOIN ProjectVersion v ON v.RequirementSetId = rl.RequirementSetId
                WHERE v.VersionId = @vid AND rl.SortKey = r.sortkey;
                ANALYZE impreq; ANALYZE imptrade;
                -- general lines: every new trade
                INSERT INTO ScopeLine (TradeId, Section, RequirementLineId, SortKey, IsHeading, LineText, Desired, ChangedBy)
                SELECT t.TradeId, 'baserequirement', r.rlid, r.sortkey, r.isheading, r.linetext, r.desired::DesiredKind, @u
                FROM needreq t CROSS JOIN impreq r WHERE r.code IS NULL AND NOT r.isheading;
                -- section headings, only on trades that have a line in that section
                CREATE TEMP TABLE tradesec ON COMMIT DROP AS
                  SELECT DISTINCT t.TradeId, lower(r.section) AS sec FROM needreq t JOIN impreq r ON r.code = t.CsiCode AND NOT r.isheading
                  UNION SELECT t.TradeId, g.sec FROM needreq t CROSS JOIN (SELECT DISTINCT lower(section) AS sec FROM impreq WHERE code IS NULL AND NOT isheading) g;
                INSERT INTO ScopeLine (TradeId, Section, RequirementLineId, SortKey, IsHeading, LineText, Desired, ChangedBy)
                SELECT ts.TradeId, 'baserequirement', r.rlid, r.sortkey, true, r.linetext, NULL, @u
                FROM tradesec ts JOIN impreq r ON r.isheading AND lower(r.section) = ts.sec;
                -- trade-specific lines: only the trade with that CSI code
                INSERT INTO ScopeLine (TradeId, Section, SortKey, IsHeading, LineText, Desired, ChangedBy)
                SELECT t.TradeId, 'baserequirement', r.sortkey, false, r.linetext, r.desired::DesiredKind, @u
                FROM impreq r JOIN needreq t ON t.CsiCode = r.code WHERE NOT r.isheading;
                """, p);
        await w.Exec("""
            INSERT INTO ScopeLine (TradeId, Section, RequirementLineId, SortKey, IsHeading, LineText, Desired, ChangedBy)
            SELECT t.TradeId, 'baserequirement', r.RequirementLineId, r.SortKey, r.IsHeading, r.LineText, r.Desired, @u
            FROM needreq t JOIN ProjectVersion v ON v.VersionId = @vid JOIN RequirementLine r ON r.RequirementSetId = v.RequirementSetId
            WHERE NOT EXISTS (SELECT 1 FROM impreq LIMIT 1);

            INSERT INTO ScopeLine (TradeId, Section, ZoneId, SortKey, LineText, ChangedBy)
            SELECT t.TradeId, 'zone', z.ZoneId, z.SortOrder, z.ZoneName, @u FROM needreq t JOIN Zone z ON z.VersionId = @vid;

            INSERT INTO ScopeLine (TradeId, Section, SortKey, IsHeading, LineText, Desired, ChangedBy)
            SELECT t.TradeId, 'tradeitem', row_number() OVER (PARTITION BY t.TradeId ORDER BY k.SortKey, k.KitLineId), k.IsHeading, k.LineText, k.Desired, @u
            FROM needreq t JOIN ScopeKitLine k ON k.KitId = t.KitId;
            """, p);
        Lap("scope lines");
        return (added, updated, bids, partners);
    }

    static async Task Crew(Work w, long projectId, ImportModel m)
    {
        if (m.Crew.Count == 0) return;
        var duration = (int)(m.PNum("Duration (months)") ?? 0);
        var preconMonths = m.Crew.Where(c => c.Phase == "precon").Select(c => c.End).DefaultIfEmpty(0).Max();
        var totalMonths = Math.Max(Math.Max(duration + preconMonths, m.Crew.Max(c => c.End)), 1);
        var buildStart = m.PDate("Construction start") ?? m.PDate("Bid due date") ?? DateOnly.FromDateTime(DateTime.Today);
        var first = new DateOnly(buildStart.Year, buildStart.Month, 1).AddMonths(-preconMonths);
        first = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));          // Monday
        var end = new DateOnly(first.Year, first.Month, 1).AddMonths(totalMonths);
        var weeks = new List<(long id, int month)>();
        var n = 0;
        for (var d = first; d < end; d = d.AddDays(7))
        {
            n++;
            var month = (d.Year - first.Year) * 12 + d.Month - first.Month + 1;
            var phase = month <= preconMonths ? "precon" : month > totalMonths - 1 && totalMonths > 2 ? "closeout" : "build";
            var id = await w.Scalar<long>("""
                INSERT INTO ProjectWeek (ProjectId, WeekNo, StartsOn, Phase, MonthNo) VALUES (@projectId, @n, @d, @phase::TeamPhase, @month)
                ON CONFLICT (ProjectId, WeekNo) DO UPDATE SET StartsOn = EXCLUDED.StartsOn, Phase = EXCLUDED.Phase, MonthNo = EXCLUDED.MonthNo RETURNING WeekId
                """, new { projectId, n, d, phase, month });
            weeks.Add((id, month));
        }
        var key = 1;
        foreach (var c in m.Crew)
        {
            var staff = c.Member is null or "TBA" ? default : await w.One<(long id, decimal? cost, decimal? bill)>("""
                SELECT StaffId, HourlyCost, BillRate FROM Staff WHERE lower(FullName) = lower(@n)
                   OR lower(left(FullName, 1) || '. ' || split_part(FullName, ' ', 2)) = lower(@n) LIMIT 1
                """, new { n = c.Member });
            var role = await w.One<(int id, decimal? rate)>("SELECT StaffRoleId, DefaultRate FROM StaffRole WHERE lower(RoleName) = lower(@r) LIMIT 1", new { r = c.Role });
            var bill = c.Rate ?? staff.bill ?? role.rate;
            var mid = await w.Scalar<long>("""
                INSERT INTO TeamMember (ProjectId, StaffId, StaffRoleId, RoleLabel, SortKey, HourlyCost, BillRate, Note, QuotedPct, TeamGroup)
                VALUES (@projectId, @sid, @rid, @role, @key, @cost, @bill, @note,
                        CASE WHEN @cost > 0 AND @bill > 0 THEN round(@bill / @cost * 100, 3) ELSE 100 END, coalesce((SELECT TeamGroup FROM Staff WHERE StaffId = @sid), 'operations')) RETURNING TeamMemberId
                """, new { projectId, sid = staff.id == 0 ? (long?)null : staff.id, rid = role.id == 0 ? (int?)null : role.id, role = c.Role, key = key++,
                           cost = staff.cost ?? (bill is decimal b ? Rules.R2(b * 0.55m) : null), bill,
                           note = c.Member is null or "TBA" ? (c.PerDiem ? "TBA · per diem" : "TBA") : (c.PerDiem ? "per diem" : null) });
            var hours = Math.Min(80, Rules.R2(c.Fte * 40));
            await w.Exec("INSERT INTO TeamMemberWeek (TeamMemberId, WeekId, Hours) SELECT @mid, WeekId, @hours FROM ProjectWeek WHERE ProjectId = @projectId AND MonthNo BETWEEN @s AND @e",
                new { mid, hours, projectId, s = c.Start, e = c.End });
        }
    }

    static Task W<T>(Npgsql.NpgsqlBinaryImporter c, T? v, NpgsqlDbType t) => v is null ? c.WriteNullAsync() : c.WriteAsync(v, t);
}
