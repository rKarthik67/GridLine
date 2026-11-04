using System.Text.Json;
using Dapper;
using Gridline.Calc;
using Gridline.Infrastructure;
using Npgsql;

namespace Gridline.Endpoints;

public static class ScopeEndpoints
{
    public sealed class LineDto
    {
        public long LineId { get; set; } public string Section { get; set; } = ""; public decimal SortKey { get; set; } public bool IsHeading { get; set; }
        public string LineText { get; set; } = ""; public string? Desired { get; set; } public bool SkipInAdjustments { get; set; } public int Indent { get; set; }
        public string? LineNote { get; set; } public long? ZoneId { get; set; } public long? RequirementLineId { get; set; } public int RowRev { get; set; }
    }
    public sealed class AnswerDto
    {
        public long LineId { get; set; } public long BidId { get; set; } public string Kind { get; set; } = "blank"; public decimal? Amount { get; set; }
        public string? AnswerText { get; set; } public string? Formula { get; set; } public string? CellColour { get; set; } public string? Note { get; set; }
        public string Source { get; set; } = "manual"; public int RowRev { get; set; } public DateTime ChangedAt { get; set; } public string? ChangedByName { get; set; }
    }
    const string LineCols = "LineId, Section::text AS Section, SortKey, IsHeading, LineText, Desired::text AS Desired, SkipInAdjustments, Indent, LineNote, ZoneId, RequirementLineId, RowRev";
    const string AnswerCols = """
        a.LineId, a.BidId, a.Kind::text AS Kind, a.Amount, a.AnswerText, a.Formula, a.CellColour, a.Note, a.Source, a.RowRev, a.ChangedAt,
        (SELECT u.GivenName || ' ' || u.FamilyName FROM AppUser u WHERE u.UserId = a.ChangedBy) AS ChangedByName
        """;

    public sealed class AnswerPut
    {
        public long LineId { get; set; } public long BidId { get; set; }
        /// <summary>What the user typed: Included / Excluded / Price / 25,000 / (500) / =62700+25000 / text. Empty clears the cell.</summary>
        public string? Value { get; set; }
        public string? CellColour { get; set; } public bool? ClearColour { get; set; } public string? Note { get; set; }
        public int? RowRev { get; set; }
    }
    public sealed record AnswersIn(List<AnswerPut> Items, string? ClientHash, bool? Force);
    public sealed record LineIn(long? AfterLineId, long? BeforeLineId, int? Count, string? Section, bool? IsHeading, string? LineText, string? Desired, int? Indent);
    public sealed class LinePatch
    {
        public string? LineText { get; set; } public string? Desired { get; set; } public bool? ClearDesired { get; set; } public bool? IsHeading { get; set; }
        public int? Indent { get; set; } public bool? SkipInAdjustments { get; set; } public string? LineNote { get; set; } public long? MoveAfterLineId { get; set; } public long? MoveBeforeLineId { get; set; }
        public int? RowRev { get; set; }
    }
    public sealed record KitIn(long KitId);
    public sealed record EstimateRow(string? LineText, decimal? Qty, string? UomCode, decimal? UnitCost);

    public static async Task<object> Load(NpgsqlConnection c, NpgsqlTransaction? tx, long tradeId) => new
    {
        tradeId,
        trade = await TradeEndpoints.One(c, tx, tradeId),
        lines = await c.QueryAsync<LineDto>($"SELECT {LineCols} FROM ScopeLine WHERE TradeId = @tradeId AND ArchivedAt IS NULL ORDER BY CASE Section WHEN 'baserequirement' THEN 1 WHEN 'tradeitem' THEN 2 ELSE 3 END, SortKey, LineId", new { tradeId }, tx),
        answers = await c.QueryAsync<AnswerDto>($"SELECT {AnswerCols} FROM ScopeAnswer a JOIN Bid b ON b.BidId = a.BidId WHERE b.TradeId = @tradeId AND b.ArchivedAt IS NULL", new { tradeId }, tx),
        zones = await c.QueryAsync("""
            SELECT z.ZoneId AS "zoneId", z.ZoneCode AS "zoneCode", z.ZoneName AS "zoneName", z.FloorArea AS "floorArea", z.AreaUnit AS "areaUnit",
                   z.UnitCount AS "unitCount", z.UnitLabel AS "unitLabel", z.IsRemainder AS "isRemainder", z.Colour AS "colour"
            FROM Zone z JOIN Trade t ON t.VersionId = z.VersionId WHERE t.TradeId = @tradeId ORDER BY z.SortOrder
            """, new { tradeId }, tx),
        invites = await c.QueryAsync("""
            SELECT i.InviteId AS "inviteId", i.BidId AS "bidId", i.PartnerId AS "partnerId", tp.CompanyName AS "companyName", i.SentToEmail::text AS "email",
                   i.Status::text AS "status", i.SentAt AS "sentAt", i.OpenedAt AS "openedAt", i.SubmittedAt AS "submittedAt", i.ExpiresAt AS "expiresAt",
                   (SELECT s.SubmissionId FROM BidSubmission s WHERE s.InviteId = i.InviteId AND s.Decision = 'pending' ORDER BY s.SubmissionId DESC LIMIT 1) AS "pendingSubmissionId"
            FROM BidInvite i JOIN TradePartner tp ON tp.PartnerId = i.PartnerId WHERE i.TradeId = @tradeId ORDER BY i.InviteId
            """, new { tradeId }, tx),
        readings = await c.QueryAsync("""
            SELECT r.ReadingId AS "readingId", r.BidId AS "bidId", r.Status::text AS "status", r.FinishedAt AS "finishedAt",
                   (SELECT count(*) FROM ProposalSuggestion s WHERE s.ReadingId = r.ReadingId AND s.Decision = 'pending') AS "pending"
            FROM ProposalReading r JOIN Bid b ON b.BidId = r.BidId WHERE b.TradeId = @tradeId ORDER BY r.ReadingId DESC
            """, new { tradeId }, tx),
    };

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/trades/{id:long}/scope", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            await Access.Trade(c, ctx.Me(), id);
            return await Load(c, null, id);
        });

        // One cell or many (paste, fill down). All in one transaction; the trade and roll-up are recalculated once.
        api.MapPut("/answers", async (AnswersIn body, HttpContext ctx) =>
        {
            if (body.Items is null || body.Items.Count == 0) throw ApiException.Bad("Nothing to save.");
            if (body.Items.Count > 5000) throw ApiException.Bad("Paste at most 5,000 cells at a time.");
            await using var w = await Work.Begin(ctx);
            w.User.Require("scope.edit");
            var bidIds = body.Items.Select(i => i.BidId).Distinct().ToArray();
            var tradeIds = (await w.Query<long>("SELECT DISTINCT TradeId FROM Bid WHERE BidId = ANY(@bidIds) AND ArchivedAt IS NULL", new { bidIds })).ToList();
            if (tradeIds.Count != 1) throw ApiException.Bad("All cells must be on one trade.");
            var tradeId = tradeIds[0];
            var s = await Access.Trade(w, tradeId);
            var lines = (await w.Query<(long id, string section, bool heading, bool remainder)>("""
                SELECT l.LineId, l.Section::text, l.IsHeading, coalesce(z.IsRemainder, false) FROM ScopeLine l LEFT JOIN Zone z ON z.ZoneId = l.ZoneId
                WHERE l.TradeId = @tradeId AND l.ArchivedAt IS NULL
                """, new { tradeId })).ToDictionary(l => l.id);
            foreach (var it in body.Items)
            {
                if (!lines.TryGetValue(it.LineId, out var line)) throw ApiException.Bad("A scope line is not on this trade (it may have been deleted).");
                if (line.heading) continue;
                if (it.RowRev is int rr && body.Force != true)
                {
                    var cur = await w.One<(int rev, string? who)>("SELECT a.RowRev, (SELECT u.GivenName || ' ' || u.FamilyName FROM AppUser u WHERE u.UserId = a.ChangedBy) FROM ScopeAnswer a WHERE a.LineId = @l AND a.BidId = @b FOR UPDATE", new { l = it.LineId, b = it.BidId });
                    if (cur.rev != 0 && cur.rev != rr && rr != 0) throw ApiException.Clash(cur.who ?? "Someone", new { it.LineId, it.BidId });
                    if (cur.rev != 0 && rr == 0) throw ApiException.Clash(cur.who ?? "Someone", new { it.LineId, it.BidId });
                }
                if (it.CellColour is not null and not ("red" or "yellow" or "green")) throw ApiException.Bad("Cell colour must be red, yellow or green.");
                if (it.Value is null)
                {   // colour / note only
                    await w.Exec("""
                        INSERT INTO ScopeAnswer (LineId, BidId, CellColour, Note, ChangedBy) VALUES (@l, @b, @col, nullif(@note, ''), @u)
                        ON CONFLICT (LineId, BidId) DO UPDATE SET CellColour = CASE WHEN @clear THEN NULL ELSE coalesce(@col, ScopeAnswer.CellColour) END,
                          Note = CASE WHEN @note IS NULL THEN ScopeAnswer.Note ELSE nullif(@note, '') END, ChangedBy = @u
                        """, new { l = it.LineId, b = it.BidId, col = it.CellColour, clear = it.ClearColour ?? false, note = it.Note, u = w.User.UserId });
                    continue;
                }
                var a = Rules.ParseAnswer(it.Value);
                if (line.section == "zone")
                {   // zone lines hold the bidder's amount for that zone (they never count in scope adjustments)
                    if (line.remainder) throw ApiException.Bad("This zone is the remainder of the base bid; it's calculated from the bid total minus the other zones.");
                    if (a.Kind is not ("amount" or "blank")) throw ApiException.Bad("Zone rows take an amount (or nothing), e.g. 125000.");
                }
                await w.Exec("""
                    INSERT INTO ScopeAnswer (LineId, BidId, Kind, Amount, AnswerText, Formula, CellColour, Note, Source, ChangedBy)
                    VALUES (@l, @b, @kind::AnswerKind, @amount, @text, @formula, @col, nullif(@note, ''), 'manual', @u)
                    ON CONFLICT (LineId, BidId) DO UPDATE SET Kind = EXCLUDED.Kind, Amount = EXCLUDED.Amount, AnswerText = EXCLUDED.AnswerText, Formula = EXCLUDED.Formula,
                      CellColour = CASE WHEN @clear THEN NULL ELSE coalesce(@col, ScopeAnswer.CellColour) END,
                      Note = CASE WHEN @note IS NULL THEN ScopeAnswer.Note ELSE nullif(@note, '') END, Source = 'manual', ChangedBy = @u
                    """, new { l = it.LineId, b = it.BidId, kind = a.Kind, amount = a.Amount, text = a.Text, formula = a.Formula, col = it.CellColour,
                               clear = it.ClearColour ?? false, note = it.Note, u = w.User.UserId });
            }
            var (_, sum) = await Recalc.TradeAndSummary(w, tradeId, "answer.update", body.ClientHash);
            var first = body.Items[0];
            await w.Log(s.ProjectId, s.VersionId, "answer.update", "answer", body.Items.Count == 1 ? $"{first.LineId}:{first.BidId}" : $"{tradeId}:{body.Items.Count} cells", after: body.Items.Count <= 20 ? body.Items : null);
            await w.Commit();
            var keys = body.Items.Select(i => $"{i.LineId}:{i.BidId}").ToArray();
            return Results.Ok(new
            {
                answers = await w.C.QueryAsync<AnswerDto>($"SELECT {AnswerCols} FROM ScopeAnswer a WHERE (a.LineId::text || ':' || a.BidId::text) = ANY(@keys)", new { keys }),
                trade = await TradeEndpoints.One(w.C, null, tradeId), totalsHash = sum.Hash, matched = sum.Matched,
            });
        });

        // Insert 1, 10 or 50 rows (or a header row) before a row, after a row, or at the end of the trade items — like the original right-click menu.
        api.MapPost("/trades/{id:long}/lines", async (long id, LineIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            w.User.Require("scope.edit");
            var s = await Access.Trade(w, id);
            if ((body.Section ?? "tradeitem") != "tradeitem") throw ApiException.Bad("Only trade item lines can be added here. Base requirements come from the base requirements checklist; zone lines from the project's zones.");
            var n = Math.Clamp(body.Count ?? 1, 1, 50);
            var keys = await KeysFor(w, id, body.BeforeLineId, body.AfterLineId, n);
            var ids = new List<long>();
            foreach (var key in keys)
                ids.Add(await w.Scalar<long>("""
                    INSERT INTO ScopeLine (TradeId, Section, SortKey, IsHeading, LineText, Desired, Indent, ChangedBy)
                    VALUES (@id, 'tradeitem', @key, coalesce(@IsHeading, false), coalesce(trim(@LineText), ''), @Desired::DesiredKind, coalesce(@Indent, 0), @u) RETURNING LineId
                    """, new { id, key, body.IsHeading, body.LineText, body.Desired, body.Indent, u = w.User.UserId }));
            await w.Log(s.ProjectId, s.VersionId, "line.create", "line", ids[0], after: new { count = n, body.BeforeLineId, body.AfterLineId, body.IsHeading });
            await w.Commit();
            var lines = await w.C.QueryAsync<LineDto>($"SELECT {LineCols} FROM ScopeLine WHERE LineId = ANY(@ids) ORDER BY SortKey", new { ids = ids.ToArray() });
            return Results.Created("", new { lines, firstLineId = ids[0] });
        });

        api.MapPatch("/lines/{id:long}", async (long id, LinePatch body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            w.User.Require("scope.edit");
            var s = await Access.Line(w, id);
            await TradeEndpoints.CheckRev(w, "ScopeLine", "LineId", id, body.RowRev);
            var section = await w.Scalar<string>("SELECT Section::text FROM ScopeLine WHERE LineId = @id", new { id });
            if (section != "tradeitem" && (body.LineText is not null || body.IsHeading is not null || body.MoveAfterLineId is not null || body.MoveBeforeLineId is not null))
                throw ApiException.Bad(section == "zone" ? "Zone lines follow the project's zones and can't be edited here." : "Base requirement text is locked. Change it in the requirement set.");
            if (body.Indent is < 0 or > 6) throw ApiException.Bad("Indent must be between 0 and 6.");
            decimal? key = null;
            if (body.MoveAfterLineId is not null || body.MoveBeforeLineId is not null)
            {
                var tid = await w.Scalar<long>("SELECT TradeId FROM ScopeLine WHERE LineId = @id", new { id });
                await w.Exec("UPDATE ScopeLine SET SortKey = -SortKey - 1000000 WHERE LineId = @id", new { id });   // take it out of the order first
                key = (await KeysFor(w, tid, body.MoveBeforeLineId, body.MoveAfterLineId is 0 ? null : body.MoveAfterLineId, 1, atStart: body.MoveAfterLineId is 0))[0];
            }
            await w.Exec("""
                UPDATE ScopeLine SET LineText = coalesce(nullif(trim(@LineText), ''), LineText),
                  Desired = CASE WHEN @ClearDesired THEN NULL ELSE coalesce(@Desired::DesiredKind, Desired) END,
                  IsHeading = coalesce(@IsHeading, IsHeading), Indent = coalesce(@Indent, Indent), SkipInAdjustments = coalesce(@SkipInAdjustments, SkipInAdjustments),
                  LineNote = CASE WHEN @LineNote IS NULL THEN LineNote ELSE nullif(@LineNote, '') END, SortKey = coalesce(@key, SortKey), ChangedBy = @u
                WHERE LineId = @id
                """, new { body.LineText, body.Desired, ClearDesired = body.ClearDesired ?? false, body.IsHeading, body.Indent, body.SkipInAdjustments, body.LineNote, key, u = w.User.UserId, id });
            Recalc.SummaryResult? sum = null;
            if (body.SkipInAdjustments is not null || body.IsHeading is not null) (_, sum) = await Recalc.TradeAndSummary(w, s.TradeId!.Value, "line.update");
            await w.Log(s.ProjectId, s.VersionId, "line.update", "line", id, after: body);
            await w.Commit();
            return Results.Ok(new { line = await w.C.QueryFirstAsync<LineDto>($"SELECT {LineCols} FROM ScopeLine WHERE LineId = @id", new { id }), totalsHash = sum?.Hash });
        });

        api.MapDelete("/lines/{id:long}", async (long id, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            w.User.Require("scope.edit");
            var s = await Access.Line(w, id);
            if (await w.Scalar<string>("SELECT Section::text FROM ScopeLine WHERE LineId = @id", new { id }) != "tradeitem")
                throw ApiException.Bad("Only trade item lines can be deleted.");
            await w.Exec("UPDATE ScopeLine SET ArchivedAt = now(), ChangedBy = @u WHERE LineId = @id", new { id, u = w.User.UserId });
            var (_, sum) = await Recalc.TradeAndSummary(w, s.TradeId!.Value, "line.delete");
            await w.Log(s.ProjectId, s.VersionId, "line.delete", "line", id);
            await w.Commit();
            return Results.Ok(new { totalsHash = sum.Hash });
        });

        api.MapPost("/trades/{id:long}/apply-kit", async (long id, KitIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            w.User.Require("scope.edit");
            var s = await Access.Trade(w, id);
            var n = await ApplyKit(w, id, body.KitId);
            await w.Exec("UPDATE Trade SET KitId = @k WHERE TradeId = @id", new { k = body.KitId, id });
            await w.Log(s.ProjectId, s.VersionId, "trade.kit", "trade", id, after: new { body.KitId, lines = n });
            await w.Commit();
            return Results.Ok(new { added = n });
        });

        // ------------------------------------------------------------------ internal estimate tab
        api.MapGet("/trades/{id:long}/estimate", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            await Access.Trade(c, ctx.Me(), id);
            return await Estimate(c, null, id);
        });
        api.MapPut("/trades/{id:long}/estimate", async (long id, List<EstimateRow> rows, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Trade(w, id);
            await w.Exec("DELETE FROM EstimateLine WHERE TradeId = @id", new { id });
            var k = 1;
            foreach (var r in rows.Where(r => !string.IsNullOrWhiteSpace(r.LineText) || r.Qty is not null || r.UnitCost is not null))
                await w.Exec("INSERT INTO EstimateLine (TradeId, SortKey, LineText, Qty, UomCode, UnitCost) VALUES (@id, @k, coalesce(nullif(trim(@LineText), ''), '—'), @Qty, nullif(@UomCode, ''), @UnitCost)",
                    new { id, k = k++, r.LineText, r.Qty, UomCode = r.UomCode?.Trim().ToUpperInvariant(), r.UnitCost });
            await w.Log(s.ProjectId, s.VersionId, "estimate.update", "trade", id, after: new { rows = k - 1 });
            await w.Commit();
            return Results.Ok(await Estimate(w.C, null, id));
        });

        // ------------------------------------------------------------------ scratch pad
        api.MapGet("/trades/{id:long}/scratch", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            await Access.Trade(c, ctx.Me(), id);
            var sheet = await c.ExecuteScalarAsync<string?>("SELECT Sheet::text FROM ScratchSheet WHERE TradeId = @id", new { id });
            return Results.Text(sheet ?? "{}", "application/json");
        });
        api.MapPut("/trades/{id:long}/scratch", async (long id, JsonElement sheet, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var s = await Access.Trade(w, id);
            var text = sheet.GetRawText();
            if (text.Length > 1_000_000) throw ApiException.Bad("The scratch pad is too large (1 MB max).");
            await w.Exec("INSERT INTO ScratchSheet (TradeId, Sheet) VALUES (@id, @t::jsonb) ON CONFLICT (TradeId) DO UPDATE SET Sheet = EXCLUDED.Sheet, ChangedAt = now()", new { id, t = text });
            await w.Log(s.ProjectId, s.VersionId, "scratch.update", "trade", id);
            await w.Commit();
            return Results.NoContent();
        });
    }

    static async Task<object> Estimate(NpgsqlConnection c, NpgsqlTransaction? t, long id)
    {
        var rows = (await c.QueryAsync("SELECT EstimateLineId AS \"estimateLineId\", LineText AS \"lineText\", Qty AS \"qty\", UomCode AS \"uomCode\", UnitCost AS \"unitCost\", LineTotal AS \"lineTotal\" FROM EstimateLine WHERE TradeId = @id ORDER BY SortKey", new { id }, t)).ToList();
        var total = await c.ExecuteScalarAsync<decimal?>("SELECT sum(LineTotal) FROM EstimateLine WHERE TradeId = @id", new { id }, t) ?? 0;
        return new { rows, total };
    }

    /// <summary>Adds a scope kit's lines to the end of the trade items (skips lines whose text is already there).</summary>
    public static async Task<int> ApplyKit(Work w, long tradeId, long kitId)
    {
        if (!await w.Scalar<bool>("SELECT EXISTS (SELECT 1 FROM ScopeKit WHERE KitId = @kitId)", new { kitId })) throw ApiException.Bad("Unknown scope kit.");
        var start = await w.Scalar<decimal?>("SELECT max(SortKey) FROM ScopeLine WHERE TradeId = @tradeId AND Section = 'tradeitem'", new { tradeId }) ?? 0;
        return await w.Exec("""
            INSERT INTO ScopeLine (TradeId, Section, SortKey, IsHeading, LineText, Desired, ChangedBy)
            SELECT @tradeId, 'tradeitem', @start + row_number() OVER (ORDER BY k.SortKey, k.KitLineId), k.IsHeading, k.LineText, k.Desired, @u
            FROM ScopeKitLine k WHERE k.KitId = @kitId
              AND NOT EXISTS (SELECT 1 FROM ScopeLine l WHERE l.TradeId = @tradeId AND l.ArchivedAt IS NULL AND lower(l.LineText) = lower(k.LineText))
            """, new { tradeId, kitId, start, u = w.User.UserId });
    }

    /// <summary>
    /// n sort keys between two neighbouring trade item lines (fractional, so inserting never renumbers;
    /// only when the gap is used up the section is renumbered 10, 20, 30 … and the keys worked out again).
    /// </summary>
    static async Task<List<decimal>> KeysFor(Work w, long tradeId, long? beforeLineId, long? afterLineId, int n, bool atStart = false)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            decimal lo, hi;
            async Task<decimal> KeyOf(long lid) => await w.Scalar<decimal?>("SELECT SortKey FROM ScopeLine WHERE LineId = @lid AND TradeId = @tradeId AND Section = 'tradeitem' AND ArchivedAt IS NULL", new { lid, tradeId })
                                                  ?? throw ApiException.Bad("That row is not a trade item on this trade.");
            var min = await w.Scalar<decimal?>("SELECT min(SortKey) FROM ScopeLine WHERE TradeId = @tradeId AND Section = 'tradeitem' AND ArchivedAt IS NULL AND SortKey >= 0", new { tradeId });
            var max = await w.Scalar<decimal?>("SELECT max(SortKey) FROM ScopeLine WHERE TradeId = @tradeId AND Section = 'tradeitem' AND ArchivedAt IS NULL", new { tradeId });
            if (beforeLineId is long b)
            {
                hi = await KeyOf(b);
                lo = await w.Scalar<decimal?>("SELECT max(SortKey) FROM ScopeLine WHERE TradeId = @tradeId AND Section = 'tradeitem' AND ArchivedAt IS NULL AND SortKey < @hi AND SortKey >= 0", new { tradeId, hi }) ?? hi - n - 1;
            }
            else if (afterLineId is long a)
            {
                lo = await KeyOf(a);
                hi = await w.Scalar<decimal?>("SELECT min(SortKey) FROM ScopeLine WHERE TradeId = @tradeId AND Section = 'tradeitem' AND ArchivedAt IS NULL AND SortKey > @lo", new { tradeId, lo }) ?? lo + n + 1;
            }
            else if (atStart) { hi = min ?? n + 1; lo = hi - n - 1; }
            else { lo = max ?? 0; hi = lo + n + 1; }
            var step = Math.Round((hi - lo) / (n + 1), 4);
            if (step >= 0.0005m) return Enumerable.Range(1, n).Select(i => Math.Round(lo + step * i, 4)).ToList();
            await w.Exec("""
                UPDATE ScopeLine l SET SortKey = x.n * 10 FROM (SELECT LineId, row_number() OVER (ORDER BY SortKey, LineId) AS n FROM ScopeLine
                  WHERE TradeId = @tradeId AND Section = 'tradeitem' AND SortKey >= 0) x WHERE l.LineId = x.LineId
                """, new { tradeId });
        }
        throw new ApiException(500, "Could not place the rows.");
    }

}
