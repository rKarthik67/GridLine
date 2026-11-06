using Dapper;
using Gridline.Infrastructure;

namespace Gridline.Endpoints;

/// <summary>
/// Scope match extras: files per trade, qualifications (was: clarifications) with reusable sets,
/// the base requirements checklist (was: general scope) and the feature switches.
/// </summary>
public static class ScopeExtrasEndpoints
{
    public sealed record QualIn(string? Kind, string? QualText, bool? ShowToClient, bool? ProjectWide, long? MoveAfterId);
    public sealed record SetIn(long SetId);
    public sealed record ReqLineIn(long? AfterId, string? SectionName, string? LineText, string? Desired, bool? IsHeading, int? Count);
    public sealed record ReqLinePatch(string? SectionName, string? LineText, string? Desired, bool? ClearDesired, bool? IsHeading, long? MoveAfterId);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/features", async (Db db) =>
        {
            await using var c = await db.Open();
            return (await c.QueryAsync<(string k, string v)>("SELECT SettingKey, SettingValue::text FROM Setting WHERE SettingKey LIKE 'feature.%'"))
                .ToDictionary(x => x.k["feature.".Length..], x => x.v == "true");
        });

        // ------------------------------------------------------------------ files on a trade (scope match → Files)
        api.MapGet("/trades/{id:long}/documents", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            await Access.Trade(c, ctx.Me(), id);
            return await c.QueryAsync("""
                SELECT d.DocumentId AS "documentId", d.DocKind AS "docKind", d.FileName AS "fileName", d.ByteSize AS "byteSize", d.BidId AS "bidId",
                       d.UploadedAt AS "uploadedAt", u.GivenName || ' ' || u.FamilyName AS "uploadedBy"
                FROM Document d LEFT JOIN AppUser u ON u.UserId = d.UploadedBy WHERE d.TradeId = @id ORDER BY d.UploadedAt DESC
                """, new { id });
        });
        api.MapPost("/trades/{id:long}/documents", async (long id, HttpContext ctx, Storage st) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Trade(w, id);
            var form = await ctx.Request.ReadFormAsync();
            long? bidId = long.TryParse(form["bidId"].FirstOrDefault(), out var b) ? b : null;
            var added = new List<long>();
            foreach (var f in form.Files)
            {
                if (f.Length > 50L * 1024 * 1024) throw ApiException.Bad($"{f.FileName} is larger than 50 MB.");
                var key = Guid.NewGuid();
                var (rel, size) = await st.Save(f, $"documents/{s.ProjectId}", key + Path.GetExtension(f.FileName));
                added.Add(await w.Scalar<long>("""
                    INSERT INTO Document (PublicKey, ProjectId, TradeId, BidId, DocKind, FileName, ContentType, ByteSize, StoragePath, UploadedBy)
                    VALUES (@key, @p, @id, @bidId, CASE WHEN lower(@fn) LIKE '%.pdf' AND @bidId IS NOT NULL THEN 'proposal' ELSE 'other' END, @fn, @ct, @size, @rel, @u) RETURNING DocumentId
                    """, new { key, p = s.ProjectId, id, bidId, fn = Path.GetFileName(f.FileName), ct = f.ContentType, size, rel, u = w.User.UserId }));
            }
            if (added.Count == 0) throw ApiException.Bad("Choose at least one file.");
            await w.Log(s.ProjectId, s.VersionId, "document.upload", "trade", id, after: new { files = added.Count });
            await w.Commit();
            return Results.Ok(new { added });
        }).DisableAntiforgery();

        // ------------------------------------------------------------------ qualifications (was: clarifications)
        api.MapGet("/qualification-sets", async (HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            return await c.QueryAsync("""
                SELECT s.QualificationSetId AS "setId", s.SetName AS "setName", (SELECT count(*) FROM QualificationSetLine l WHERE l.QualificationSetId = s.QualificationSetId) AS "lines"
                FROM QualificationSet s WHERE s.CompanyId = @cid AND s.IsActive ORDER BY s.SetName
                """, new { cid = ctx.Me().CompanyId });
        });
        api.MapGet("/trades/{id:long}/qualifications", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            var s = await Access.Trade(c, ctx.Me(), id);
            return await Quals(c, s.VersionId, id);
        });
        api.MapPost("/trades/{id:long}/qualifications", async (long id, QualIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Trade(w, id);
            var kind = body.Kind ?? "qualification";
            if (kind is not ("assumption" or "exclusion" or "qualification")) throw ApiException.Bad("Kind must be assumption, exclusion or qualification.");
            var tradeId = body.ProjectWide == true ? (long?)null : id;
            var key = (await w.Scalar<decimal?>("SELECT max(SortKey) FROM Qualification WHERE VersionId = @v AND TradeId IS NOT DISTINCT FROM @tradeId", new { v = s.VersionId, tradeId }) ?? 0) + 1;
            var qid = await w.Scalar<long>("""
                INSERT INTO Qualification (VersionId, TradeId, Kind, SortKey, QualText, ShowToClient, ChangedBy)
                VALUES (@v, @tradeId, @kind, @key, coalesce(nullif(trim(@t), ''), 'New qualification'), coalesce(@show, true), @u) RETURNING QualificationId
                """, new { v = s.VersionId, tradeId, kind, key, t = body.QualText, show = body.ShowToClient, u = w.User.UserId });
            await w.Log(s.ProjectId, s.VersionId, "qualification.create", "qualification", qid);
            await w.Commit();
            return Results.Ok(await Quals(w.C, s.VersionId, id));
        });
        api.MapPatch("/qualifications/{id:long}", async (long id, QualIn body, long tradeId, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var vid = await w.Scalar<long?>("SELECT VersionId FROM Qualification WHERE QualificationId = @id", new { id }) ?? throw ApiException.NotFound("Qualification");
            var s = await Access.Version(w, vid);
            if (body.Kind is not null and not ("assumption" or "exclusion" or "qualification")) throw ApiException.Bad("Kind must be assumption, exclusion or qualification.");
            decimal? key = null;
            if (body.MoveAfterId is long after)
            {
                var a = after == 0 ? 0 : await w.Scalar<decimal>("SELECT SortKey FROM Qualification WHERE QualificationId = @after", new { after });
                var b = await w.Scalar<decimal?>("SELECT min(SortKey) FROM Qualification q WHERE q.VersionId = @vid AND q.TradeId IS NOT DISTINCT FROM (SELECT TradeId FROM Qualification WHERE QualificationId = @id) AND SortKey > @a AND QualificationId <> @id", new { vid, id, a });
                key = b is null ? a + 1 : Math.Round((a + b.Value) / 2, 4);
            }
            await w.Exec("""
                UPDATE Qualification SET Kind = coalesce(@Kind, Kind), QualText = coalesce(nullif(trim(@QualText), ''), QualText), ShowToClient = coalesce(@ShowToClient, ShowToClient),
                  TradeId = CASE WHEN @ProjectWide IS NULL THEN TradeId WHEN @ProjectWide THEN NULL ELSE @tradeId END, SortKey = coalesce(@key, SortKey), ChangedAt = now(), ChangedBy = @u
                WHERE QualificationId = @id
                """, new { body.Kind, body.QualText, body.ShowToClient, body.ProjectWide, tradeId, key, id, u = w.User.UserId });
            await w.Log(s.ProjectId, vid, "qualification.update", "qualification", id);
            await w.Commit();
            return Results.Ok(await Quals(w.C, vid, tradeId));
        });
        api.MapDelete("/qualifications/{id:long}", async (long id, long tradeId, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var vid = await w.Scalar<long?>("SELECT VersionId FROM Qualification WHERE QualificationId = @id", new { id }) ?? throw ApiException.NotFound("Qualification");
            var s = await Access.Version(w, vid);
            await w.Exec("DELETE FROM Qualification WHERE QualificationId = @id", new { id });
            await w.Log(s.ProjectId, vid, "qualification.delete", "qualification", id);
            await w.Commit();
            return Results.Ok(await Quals(w.C, vid, tradeId));
        });
        api.MapPost("/trades/{id:long}/qualifications/apply-set", async (long id, SetIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Trade(w, id);
            await Access.QualificationSet(w, body.SetId);
            var start = await w.Scalar<decimal?>("SELECT max(SortKey) FROM Qualification WHERE VersionId = @v AND TradeId = @id", new { v = s.VersionId, id }) ?? 0;
            var n = await w.Exec("""
                INSERT INTO Qualification (VersionId, TradeId, Kind, SortKey, QualText, ChangedBy)
                SELECT @v, @id, l.Kind, @start + row_number() OVER (ORDER BY l.SortOrder, l.QualificationSetLineId), l.QualText, @u
                FROM QualificationSetLine l WHERE l.QualificationSetId = @set
                  AND NOT EXISTS (SELECT 1 FROM Qualification q WHERE q.VersionId = @v AND q.TradeId = @id AND lower(q.QualText) = lower(l.QualText))
                """, new { v = s.VersionId, id, start, set = body.SetId, u = w.User.UserId });
            await w.Log(s.ProjectId, s.VersionId, "qualification.set", "trade", id, after: new { body.SetId, added = n });
            await w.Commit();
            return Results.Ok(await Quals(w.C, s.VersionId, id));
        });

        // ------------------------------------------------------------------ base requirements checklist (was: general scope)
        api.MapGet("/versions/{id:long}/requirements", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            await Access.Version(c, ctx.Me(), id);
            return await Requirements(c, null, id);
        });
        api.MapPost("/versions/{id:long}/requirements/lines", async (long id, ReqLineIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Version(w, id);
            var set = await OwnSet(w, id);
            var n = Math.Clamp(body.Count ?? 1, 1, 50);
            var lo = body.AfterId is long a ? await w.Scalar<decimal>("SELECT SortKey FROM RequirementLine WHERE RequirementLineId = @a AND RequirementSetId = @set", new { a, set })
                                            : await w.Scalar<decimal?>("SELECT max(SortKey) FROM RequirementLine WHERE RequirementSetId = @set", new { set }) ?? 0;
            var hi = await w.Scalar<decimal?>("SELECT min(SortKey) FROM RequirementLine WHERE RequirementSetId = @set AND SortKey > @lo", new { set, lo }) ?? lo + n + 1;
            if ((hi - lo) / (n + 1) < 0.0005m)
            {
                await Renumber(w, set);
                lo = body.AfterId is long a2 ? await w.Scalar<decimal>("SELECT SortKey FROM RequirementLine WHERE RequirementLineId = @a2", new { a2 }) : await w.Scalar<decimal?>("SELECT max(SortKey) FROM RequirementLine WHERE RequirementSetId = @set", new { set }) ?? 0;
                hi = await w.Scalar<decimal?>("SELECT min(SortKey) FROM RequirementLine WHERE RequirementSetId = @set AND SortKey > @lo", new { set, lo }) ?? lo + n + 1;
            }
            var section = body.SectionName ?? (body.AfterId is long a3 ? await w.Scalar<string?>("SELECT SectionName FROM RequirementLine WHERE RequirementLineId = @a3", new { a3 }) : null) ?? "General";
            for (var i = 1; i <= n; i++)
            {
                var key = Math.Round(lo + (hi - lo) / (n + 1) * i, 4);
                var rid = await w.Scalar<long>("""
                    INSERT INTO RequirementLine (RequirementSetId, SortKey, SectionName, IsHeading, LineText, Desired)
                    VALUES (@set, @key, @section, coalesce(@h, false), coalesce(nullif(trim(@t), ''), CASE WHEN @h THEN 'New section' ELSE 'New requirement' END), coalesce(@d, CASE WHEN @h THEN NULL ELSE 'included' END)::DesiredKind)
                    RETURNING RequirementLineId
                    """, new { set, key, section, h = body.IsHeading, t = body.LineText, d = body.Desired });
                // every trade of this version gets the new line
                await w.Exec("""
                    INSERT INTO ScopeLine (TradeId, Section, RequirementLineId, SortKey, IsHeading, LineText, Desired, ChangedBy)
                    SELECT t.TradeId, 'baserequirement', r.RequirementLineId, r.SortKey, r.IsHeading, r.LineText, r.Desired, @u
                    FROM Trade t JOIN RequirementLine r ON r.RequirementLineId = @rid WHERE t.VersionId = @id AND t.ArchivedAt IS NULL
                    """, new { rid, id, u = w.User.UserId });
            }
            await w.Exec("UPDATE ScopeLine l SET SortKey = r.SortKey FROM RequirementLine r, Trade t WHERE l.RequirementLineId = r.RequirementLineId AND t.TradeId = l.TradeId AND t.VersionId = @id AND l.SortKey <> r.SortKey", new { id });
            await w.Log(s.ProjectId, id, "requirement.create", "version", id, after: new { count = n });
            await w.Commit();
            return Results.Ok(await Requirements(w.C, null, id));
        });
        api.MapPatch("/requirements/lines/{rid:long}", async (long rid, long versionId, ReqLinePatch body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Version(w, versionId);
            var set = await OwnSet(w, versionId);
            rid = await MapLine(w, set, rid);
            decimal? key = null;
            if (body.MoveAfterId is long after)
            {
                var a = after == 0 ? 0 : await w.Scalar<decimal>("SELECT SortKey FROM RequirementLine WHERE RequirementLineId = @x", new { x = await MapLine(w, set, after) });
                var b = await w.Scalar<decimal?>("SELECT min(SortKey) FROM RequirementLine WHERE RequirementSetId = @set AND SortKey > @a AND RequirementLineId <> @rid", new { set, a, rid });
                key = b is null ? a + 1 : Math.Round((a + b.Value) / 2, 4);
            }
            await w.Exec("""
                UPDATE RequirementLine SET SectionName = coalesce(@SectionName, SectionName), LineText = coalesce(nullif(trim(@LineText), ''), LineText),
                  Desired = CASE WHEN @ClearDesired THEN NULL ELSE coalesce(@Desired::DesiredKind, Desired) END, IsHeading = coalesce(@IsHeading, IsHeading), SortKey = coalesce(@key, SortKey)
                WHERE RequirementLineId = @rid
                """, new { body.SectionName, body.LineText, body.Desired, ClearDesired = body.ClearDesired ?? false, body.IsHeading, key, rid });
            // push the change to every trade's copy of the line (answers stay)
            await w.Exec("""
                UPDATE ScopeLine l SET LineText = r.LineText, Desired = r.Desired, IsHeading = r.IsHeading, SortKey = r.SortKey, ChangedBy = @u
                FROM RequirementLine r, Trade t WHERE r.RequirementLineId = @rid AND l.RequirementLineId = r.RequirementLineId AND t.TradeId = l.TradeId AND t.VersionId = @versionId
                """, new { rid, versionId, u = w.User.UserId });
            await Calc.Recalc.Version(w, versionId, "requirement.update");
            await w.Log(s.ProjectId, versionId, "requirement.update", "requirement", rid, after: body);
            await w.Commit();
            return Results.Ok(await Requirements(w.C, null, versionId));
        });
        api.MapDelete("/requirements/lines/{rid:long}", async (long rid, long versionId, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Version(w, versionId);
            var set = await OwnSet(w, versionId);
            rid = await MapLine(w, set, rid);
            await w.Exec("UPDATE ScopeLine l SET ArchivedAt = now(), RequirementLineId = NULL FROM Trade t WHERE l.RequirementLineId = @rid AND t.TradeId = l.TradeId AND t.VersionId = @versionId", new { rid, versionId });
            await w.Exec("DELETE FROM RequirementLine WHERE RequirementLineId = @rid", new { rid });
            await Calc.Recalc.Version(w, versionId, "requirement.delete");
            await w.Log(s.ProjectId, versionId, "requirement.delete", "requirement", rid);
            await w.Commit();
            return Results.Ok(await Requirements(w.C, null, versionId));
        });
        api.MapPost("/versions/{id:long}/requirements/apply-template", async (long id, SetIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Version(w, id);
            await Access.RequirementSet(w, body.SetId);
            var set = await OwnSet(w, id);
            var start = await w.Scalar<decimal?>("SELECT max(SortKey) FROM RequirementLine WHERE RequirementSetId = @set", new { set }) ?? 0;
            var n = await w.Exec("""
                INSERT INTO RequirementLine (RequirementSetId, SortKey, SectionName, IsHeading, LineText, Desired)
                SELECT @set, @start + row_number() OVER (ORDER BY l.SortKey), l.SectionName, l.IsHeading, l.LineText, l.Desired
                FROM RequirementLine l WHERE l.RequirementSetId = @tpl AND NOT EXISTS (SELECT 1 FROM RequirementLine x WHERE x.RequirementSetId = @set AND lower(x.LineText) = lower(l.LineText))
                """, new { set, start, tpl = body.SetId });
            await w.Exec("""
                INSERT INTO ScopeLine (TradeId, Section, RequirementLineId, SortKey, IsHeading, LineText, Desired, ChangedBy)
                SELECT t.TradeId, 'baserequirement', r.RequirementLineId, r.SortKey, r.IsHeading, r.LineText, r.Desired, @u
                FROM Trade t JOIN RequirementLine r ON r.RequirementSetId = @set
                WHERE t.VersionId = @id AND t.ArchivedAt IS NULL AND NOT EXISTS (SELECT 1 FROM ScopeLine l WHERE l.TradeId = t.TradeId AND l.RequirementLineId = r.RequirementLineId)
                """, new { set, id, u = w.User.UserId });
            await w.Log(s.ProjectId, id, "requirement.template", "version", id, after: new { body.SetId, added = n });
            await w.Commit();
            return Results.Ok(await Requirements(w.C, null, id));
        });
    }

    static async Task<object> Quals(Npgsql.NpgsqlConnection c, long versionId, long tradeId) => new
    {
        trade = await c.QueryAsync(QSql + " AND q.TradeId = @tradeId ORDER BY q.SortKey", new { versionId, tradeId }),
        project = await c.QueryAsync(QSql + " AND q.TradeId IS NULL ORDER BY q.SortKey", new { versionId }),
    };
    const string QSql = """
        SELECT q.QualificationId AS "qualificationId", q.TradeId AS "tradeId", q.Kind AS "kind", q.QualText AS "qualText", q.ShowToClient AS "showToClient",
               q.ChangedAt AS "changedAt", (SELECT u.GivenName || ' ' || u.FamilyName FROM AppUser u WHERE u.UserId = q.ChangedBy) AS "changedByName"
        FROM Qualification q WHERE q.VersionId = @versionId
        """;

    static async Task<object> Requirements(Npgsql.NpgsqlConnection c, Npgsql.NpgsqlTransaction? t, long versionId)
    {
        var set = await c.QueryFirstOrDefaultAsync("""
            SELECT r.RequirementSetId AS "setId", r.SetName AS "setName", r.IsTemplate AS "isTemplate" FROM ProjectVersion v JOIN RequirementSet r ON r.RequirementSetId = v.RequirementSetId WHERE v.VersionId = @versionId
            """, new { versionId }, t);
        var lines = await c.QueryAsync("""
            SELECT r.RequirementLineId AS "requirementLineId", r.SortKey AS "sortKey", r.SectionName AS "sectionName", r.IsHeading AS "isHeading", r.LineText AS "lineText", r.Desired::text AS "desired",
                   (SELECT count(*) FROM ScopeLine l JOIN Trade tr ON tr.TradeId = l.TradeId AND tr.VersionId = @versionId WHERE l.RequirementLineId = r.RequirementLineId AND l.ArchivedAt IS NULL) AS "trades"
            FROM ProjectVersion v JOIN RequirementLine r ON r.RequirementSetId = v.RequirementSetId WHERE v.VersionId = @versionId ORDER BY r.SortKey, r.RequirementLineId
            """, new { versionId }, t);
        var templates = await c.QueryAsync("""
            SELECT RequirementSetId AS "setId", SetName AS "setName", (SELECT count(*) FROM RequirementLine l WHERE l.RequirementSetId = s.RequirementSetId) AS "lines"
            FROM RequirementSet s WHERE s.IsTemplate AND s.CompanyId = (SELECT p.CompanyId FROM ProjectVersion v JOIN Project p ON p.ProjectId = v.ProjectId WHERE v.VersionId = @versionId) ORDER BY SetName
            """, new { versionId }, t);
        return new { set, lines, templates };
    }

    /// <summary>
    /// Copy-on-write: the version gets its own checklist the first time it's edited, so the company template and
    /// other versions are never changed. Its trades' base requirement lines are pointed at the copy.
    /// </summary>
    static async Task<long> OwnSet(Work w, long versionId)
    {
        var cur = await w.One<(long? set, bool tpl, string? name, int users)>("""
            SELECT v.RequirementSetId, coalesce(r.IsTemplate, false), r.SetName,
                   (SELECT count(*)::int FROM ProjectVersion x WHERE x.RequirementSetId = v.RequirementSetId)
            FROM ProjectVersion v LEFT JOIN RequirementSet r ON r.RequirementSetId = v.RequirementSetId WHERE v.VersionId = @versionId
            """, new { versionId });
        if (cur.set is long s0 && !cur.tpl && cur.users == 1) return s0;
        var pname = await w.Scalar<string>("SELECT p.ProjectName || ' v' || v.VersionNo FROM ProjectVersion v JOIN Project p ON p.ProjectId = v.ProjectId WHERE v.VersionId = @versionId", new { versionId });
        var set = await w.Scalar<long>("""
            INSERT INTO RequirementSet (CompanyId, SetName, IsTemplate)
            SELECT p.CompanyId, @n, false FROM ProjectVersion v JOIN Project p ON p.ProjectId = v.ProjectId WHERE v.VersionId = @versionId RETURNING RequirementSetId
            """, new { n = $"{pname} – base requirements", versionId });
        if (cur.set is long old)
        {
            await w.Exec("""
                DROP TABLE IF EXISTS mreq;
                CREATE TEMP TABLE mreq ON COMMIT DROP AS SELECT RequirementLineId AS old, nextval(pg_get_serial_sequence('gridline.requirementline','requirementlineid')) AS new FROM RequirementLine WHERE RequirementSetId = @old;
                INSERT INTO RequirementLine (RequirementLineId, RequirementSetId, SortKey, SectionName, IsHeading, LineText, Desired) OVERRIDING SYSTEM VALUE
                SELECT m.new, @set, r.SortKey, r.SectionName, r.IsHeading, r.LineText, r.Desired FROM RequirementLine r JOIN mreq m ON m.old = r.RequirementLineId;
                UPDATE ScopeLine l SET RequirementLineId = m.new FROM mreq m, Trade t WHERE l.RequirementLineId = m.old AND t.TradeId = l.TradeId AND t.VersionId = @versionId;
                """, new { old, set, versionId });
        }
        await w.Exec("UPDATE ProjectVersion SET RequirementSetId = @set WHERE VersionId = @versionId", new { set, versionId });
        return set;
    }
    /// <summary>After a copy-on-write the client may still send the template's line id; find the copy by text and position.</summary>
    static async Task<long> MapLine(Work w, long set, long rid) =>
        await w.Scalar<long?>("SELECT RequirementLineId FROM RequirementLine WHERE RequirementLineId = @rid AND RequirementSetId = @set", new { rid, set })
        ?? await w.Scalar<long?>("""
            SELECT c.RequirementLineId FROM RequirementLine o JOIN RequirementLine c ON c.RequirementSetId = @set AND c.SortKey = o.SortKey AND c.LineText = o.LineText
            WHERE o.RequirementLineId = @rid LIMIT 1
            """, new { rid, set }) ?? throw ApiException.NotFound("Requirement line");
    static Task Renumber(Work w, long set) => w.Exec("""
        UPDATE RequirementLine r SET SortKey = x.n * 10 FROM (SELECT RequirementLineId, row_number() OVER (ORDER BY SortKey, RequirementLineId) AS n FROM RequirementLine WHERE RequirementSetId = @set) x
        WHERE r.RequirementLineId = x.RequirementLineId
        """, new { set });
}
