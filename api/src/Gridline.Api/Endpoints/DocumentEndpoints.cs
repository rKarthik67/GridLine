using Dapper;
using Gridline.Calc;
using Gridline.Infrastructure;
using Gridline.Proposal;

namespace Gridline.Endpoints;

/// <summary>Project documents and the AI proposal reader (upload a proposal on a bid lane → suggestions → accept/reject).</summary>
public static class DocumentEndpoints
{
    public sealed record DecideItem(long SuggestionId, string Decision);
    public sealed record ReadingDecideIn(List<DecideItem>? Items, string? All, bool? ApplyBase);
    const long MaxUpload = 50L * 1024 * 1024;

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/projects/{id:long}/documents", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            await Access.Project(c, null, ctx.Me(), id, "viewer");
            return await c.QueryAsync("""
                SELECT d.DocumentId AS "documentId", d.DocKind AS "docKind", d.FileName AS "fileName", d.ContentType AS "contentType", d.ByteSize AS "byteSize",
                       d.TradeId AS "tradeId", d.BidId AS "bidId", d.UploadedAt AS "uploadedAt", u.GivenName || ' ' || u.FamilyName AS "uploadedBy"
                FROM Document d LEFT JOIN AppUser u ON u.UserId = d.UploadedBy WHERE d.ProjectId = @id AND d.DocKind <> 'import' ORDER BY d.UploadedAt DESC
                """, new { id });
        });

        api.MapPost("/projects/{id:long}/documents", async (long id, HttpContext ctx, Storage st) =>
        {
            await using var w = await Work.Begin(ctx);
            await Access.Project(w, id);
            var form = await ctx.Request.ReadFormAsync();
            var kind = form["kind"].FirstOrDefault() is { Length: > 0 } k ? k : "other";
            if (kind is not ("drawing" or "other" or "proposal")) throw ApiException.Bad("Unknown document kind.");
            var added = new List<long>();
            foreach (var f in form.Files)
            {
                if (f.Length > MaxUpload) throw ApiException.Bad($"{f.FileName} is larger than 50 MB.");
                var key = Guid.NewGuid();
                var (rel, size) = await st.Save(f, $"documents/{id}", key + Path.GetExtension(f.FileName));
                added.Add(await w.Scalar<long>("INSERT INTO Document (PublicKey, ProjectId, DocKind, FileName, ContentType, ByteSize, StoragePath, UploadedBy) VALUES (@key, @id, @kind, @fn, @ct, @size, @rel, @u) RETURNING DocumentId",
                    new { key, id, kind, fn = Path.GetFileName(f.FileName), ct = f.ContentType, size, rel, u = w.User.UserId }));
            }
            if (added.Count == 0) throw ApiException.Bad("Choose at least one file.");
            await w.Log(id, null, "document.upload", "document", string.Join(",", added));
            await w.Commit();
            return Results.Ok(new { added });
        }).DisableAntiforgery();

        api.MapGet("/documents/{id:long}/file", async (long id, HttpContext ctx, Db db, Storage st) =>
        {
            await using var c = await db.Open();
            var d = await c.QueryFirstOrDefaultAsync<(long pid, string path, string name, string? type)>("SELECT ProjectId, StoragePath, FileName, ContentType FROM Document WHERE DocumentId = @id", new { id });
            if (d.path is null) throw ApiException.NotFound("Document");
            await Access.Project(c, null, ctx.Me(), d.pid, "viewer");
            return Results.File(st.Open(d.path), d.type ?? "application/octet-stream", d.name);
        });

        api.MapDelete("/documents/{id:long}", async (long id, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var pid = await w.Scalar<long?>("SELECT ProjectId FROM Document WHERE DocumentId = @id", new { id }) ?? throw ApiException.NotFound("Document");
            await Access.Project(w, pid);
            await w.Exec("DELETE FROM Document WHERE DocumentId = @id", new { id });     // file stays on disk (audit); the record is gone
            await w.Log(pid, null, "document.delete", "document", id);
            await w.Commit();
            return Results.NoContent();
        });

        // ------------------------------------------------------------------ proposal reader
        api.MapPost("/bids/{id:long}/proposals", async (long id, HttpContext ctx, Storage st, ProposalQueue queue) =>
        {
            await using var w = await Work.Begin(ctx);
            w.User.Require("proposal.read");
            var s = await Access.Bid(w, id);
            var form = await ctx.Request.ReadFormAsync();
            var f = form.Files.GetFile("file") ?? form.Files.FirstOrDefault() ?? throw ApiException.Bad("Choose the proposal PDF.");
            if (!f.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) throw ApiException.Bad("The proposal must be a PDF.");
            if (f.Length > MaxUpload) throw ApiException.Bad("The PDF is larger than 50 MB.");
            var key = Guid.NewGuid();
            var (rel, size) = await st.Save(f, $"proposals/{s.ProjectId}", key + ".pdf");
            var docId = await w.Scalar<long>("INSERT INTO Document (PublicKey, ProjectId, TradeId, BidId, DocKind, FileName, ContentType, ByteSize, StoragePath, UploadedBy) VALUES (@key, @p, @t, @id, 'proposal', @fn, 'application/pdf', @size, @rel, @u) RETURNING DocumentId",
                new { key, p = s.ProjectId, t = s.TradeId, id, fn = Path.GetFileName(f.FileName), size, rel, u = w.User.UserId });
            var rid = await w.Scalar<long>("INSERT INTO ProposalReading (BidId, DocumentId, Status, StartedBy) VALUES (@id, @docId, 'queued', @u) RETURNING ReadingId", new { id, docId, u = w.User.UserId });
            await w.Log(s.ProjectId, s.VersionId, "proposal.upload", "reading", rid, after: new { bidId = id, f.FileName });
            await w.Commit();
            await queue.Add(rid);
            return Results.Accepted($"/api/readings/{rid}", new { readingId = rid, status = "queued" });
        }).DisableAntiforgery();

        api.MapGet("/readings/{id:long}", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            var bidId = await c.ExecuteScalarAsync<long?>("SELECT BidId FROM ProposalReading WHERE ReadingId = @id", new { id }) ?? throw ApiException.NotFound("Proposal reading");
            await Access.Bid(c, ctx.Me(), bidId);
            var reading = await c.QueryFirstAsync("""
                SELECT r.ReadingId AS "readingId", r.BidId AS "bidId", r.Status::text AS "status", r.ModelName AS "modelName", r.BaseAmountFound AS "baseAmountFound",
                       r.Summary AS "summary", r.ErrorText AS "errorText", r.StartedAt AS "startedAt", r.FinishedAt AS "finishedAt", d.FileName AS "fileName", d.DocumentId AS "documentId",
                       b.BaseAmount AS "currentBase", b.BidderName AS "bidderName"
                FROM ProposalReading r JOIN Document d ON d.DocumentId = r.DocumentId JOIN Bid b ON b.BidId = r.BidId WHERE r.ReadingId = @id
                """, new { id });
            var suggestions = await c.QueryAsync("""
                SELECT s.SuggestionId AS "suggestionId", s.LineId AS "lineId", l.LineText AS "lineText", l.Section::text AS "section", l.Desired::text AS "desired",
                       s.Kind::text AS "kind", s.Amount AS "amount", s.AnswerText AS "answerText", s.Evidence AS "evidence", s.PageNo AS "pageNo", s.Confidence AS "confidence",
                       s.Decision::text AS "decision", a.Kind::text AS "currentKind", a.Amount AS "currentAmount"
                FROM ProposalSuggestion s LEFT JOIN ScopeLine l ON l.LineId = s.LineId
                LEFT JOIN ProposalReading r ON r.ReadingId = s.ReadingId LEFT JOIN ScopeAnswer a ON a.LineId = s.LineId AND a.BidId = r.BidId
                WHERE s.ReadingId = @id ORDER BY s.LineId IS NULL, l.Section, l.SortKey, s.SuggestionId
                """, new { id });
            return new { reading, suggestions };
        });

        api.MapPost("/readings/{id:long}/decide", async (long id, ReadingDecideIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            w.User.Require("scope.edit");
            var r = await w.One<(long bidId, decimal? baseFound, string status)>("SELECT BidId, BaseAmountFound, Status::text FROM ProposalReading WHERE ReadingId = @id", new { id });
            if (r.bidId == 0) throw ApiException.NotFound("Proposal reading");
            if (r.status != "done") throw ApiException.Bad("The proposal is still being read.");
            var s = await Access.Bid(w, r.bidId);
            if (body.All is "accepted" or "rejected")
                await w.Exec("UPDATE ProposalSuggestion SET Decision = @d::ReviewDecision, DecidedBy = @u, DecidedAt = now() WHERE ReadingId = @id AND Decision = 'pending'", new { id, d = body.All, u = w.User.UserId });
            foreach (var it in body.Items ?? [])
            {
                if (it.Decision is not ("accepted" or "rejected" or "pending")) throw ApiException.Bad("Decision must be accepted, rejected or pending.");
                await w.Exec("UPDATE ProposalSuggestion SET Decision = @d::ReviewDecision, DecidedBy = @u, DecidedAt = now() WHERE ReadingId = @id AND SuggestionId = @sid", new { id, sid = it.SuggestionId, d = it.Decision, u = w.User.UserId });
            }
            // accepted suggestions that point at a line become answers (source 'proposal'); newest decision wins per line
            var applied = await w.Exec("""
                INSERT INTO ScopeAnswer (LineId, BidId, Kind, Amount, AnswerText, Source, ChangedBy)
                SELECT DISTINCT ON (s.LineId) s.LineId, @bid, s.Kind, s.Amount, s.AnswerText, 'proposal', @u
                FROM ProposalSuggestion s WHERE s.ReadingId = @id AND s.Decision = 'accepted' AND s.LineId IS NOT NULL ORDER BY s.LineId, s.DecidedAt DESC
                ON CONFLICT (LineId, BidId) DO UPDATE SET Kind = EXCLUDED.Kind, Amount = EXCLUDED.Amount, AnswerText = EXCLUDED.AnswerText, Source = 'proposal', ChangedBy = @u
                """, new { id, bid = r.bidId, u = w.User.UserId });
            if (body.ApplyBase == true && r.baseFound is decimal b)
                await w.Exec("UPDATE Bid SET BaseAmount = @b, BaseSource = 'proposal', State = CASE WHEN State IN ('invited','bidding') THEN 'posted' ELSE State END, IsPosted = true, ChangedBy = @u WHERE BidId = @bid",
                    new { b, bid = r.bidId, u = w.User.UserId });
            var (_, sum) = await Recalc.TradeAndSummary(w, s.TradeId!.Value, "proposal.decide");
            await w.Log(s.ProjectId, s.VersionId, "proposal.decide", "reading", id, after: new { applied, applyBase = body.ApplyBase });
            await w.Commit();
            return Results.Ok(new { applied, trade = await TradeEndpoints.One(w.C, null, s.TradeId.Value), totalsHash = sum.Hash });
        });
    }
}
