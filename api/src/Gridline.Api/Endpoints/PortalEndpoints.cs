using Dapper;
using Gridline.Calc;
using Gridline.Infrastructure;
using Gridline.Live;
using Microsoft.AspNetCore.SignalR;

namespace Gridline.Endpoints;

/// <summary>Bid portal: send a sub a private link; they answer the scope online; the estimator reviews and accepts it into the lane.</summary>
public static class PortalEndpoints
{
    public sealed record InviteIn(long? BidId, long? PartnerId, string? BidderName, string? Email, string? Message, int? ExpiresDays);
    public sealed record PortalAnswer(long LineId, string? Value);
    public sealed record PortalIn(decimal? BaseAmount, string? Notes, List<PortalAnswer>? Answers);
    public sealed record DecideIn(string Decision);

    static async Task<object> Invites(Npgsql.NpgsqlConnection c, Npgsql.NpgsqlTransaction? t, long tradeId) => await c.QueryAsync("""
        SELECT i.InviteId AS "inviteId", i.BidId AS "bidId", i.PartnerId AS "partnerId", tp.CompanyName AS "companyName", i.SentToEmail::text AS "email",
               i.Status::text AS "status", i.Message AS "message", i.SentAt AS "sentAt", i.OpenedAt AS "openedAt", i.SubmittedAt AS "submittedAt", i.ExpiresAt AS "expiresAt",
               (SELECT s.SubmissionId FROM BidSubmission s WHERE s.InviteId = i.InviteId ORDER BY s.SubmissionId DESC LIMIT 1) AS "submissionId",
               (SELECT s.Decision::text FROM BidSubmission s WHERE s.InviteId = i.InviteId ORDER BY s.SubmissionId DESC LIMIT 1) AS "decision"
        FROM BidInvite i JOIN TradePartner tp ON tp.PartnerId = i.PartnerId WHERE i.TradeId = @tradeId ORDER BY i.InviteId
        """, new { tradeId }, t);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/trades/{id:long}/invites", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            await Access.Trade(c, ctx.Me(), id);
            return await Invites(c, null, id);
        });

        api.MapPost("/trades/{id:long}/invites", async (long id, InviteIn body, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            w.User.Require("invite.send");
            var s = await Access.Trade(w, id);
            var bidId = body.BidId ?? await TradeEndpoints.AddBid(w, id, new TradeEndpoints.BidIn(body.BidderName, body.PartnerId, null, "invited", body.Email, null), "portal");
            var bid = await w.One<(long partnerId, string? email)>("""
                SELECT b.PartnerId, coalesce(pp.Email::text, (SELECT x.Email::text FROM PartnerPerson x WHERE x.PartnerId = b.PartnerId AND x.Email IS NOT NULL ORDER BY x.IsPrimary DESC LIMIT 1))
                FROM Bid b LEFT JOIN PartnerPerson pp ON pp.PersonId = b.PersonId WHERE b.BidId = @bidId AND b.TradeId = @id AND b.ArchivedAt IS NULL
                """, new { bidId, id });
            if (bid.partnerId == 0) throw ApiException.Bad("That bid is not on this trade.");
            var email = body.Email?.Trim() ?? bid.email;
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) throw ApiException.Bad("Enter the sub's email address.");
            var token = Tokens.New();
            var inviteId = await w.Scalar<long>("""
                INSERT INTO BidInvite (TradeId, BidId, PartnerId, SentToEmail, TokenHash, Status, Message, SentAt, ExpiresAt, SentBy)
                VALUES (@id, @bidId, @p, @email::citext, @h, 'sent', @msg, now(), now() + make_interval(days => @days), @u) RETURNING InviteId
                """, new { id, bidId, p = bid.partnerId, email, h = Tokens.Hash(token), msg = body.Message, days = Math.Clamp(body.ExpiresDays ?? 21, 1, 120), u = w.User.UserId });
            await w.Exec("UPDATE Bid SET State = 'bidding' WHERE BidId = @bidId AND State = 'invited'", new { bidId });
            await w.Log(s.ProjectId, s.VersionId, "invite.send", "invite", inviteId, after: new { email, bidId });
            await w.Commit();
            return Results.Ok(new { inviteId, bidId, token, link = $"/bid/{token}", email, invites = await Invites(w.C, null, id) });
        });

        api.MapPost("/invites/{id:long}/resend", async (long id, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            w.User.Require("invite.send");
            var tradeId = await w.Scalar<long?>("SELECT TradeId FROM BidInvite WHERE InviteId = @id", new { id }) ?? throw ApiException.NotFound("Invite");
            var s = await Access.Trade(w, tradeId);
            var token = Tokens.New();
            await w.Exec("UPDATE BidInvite SET TokenHash = @h, Status = 'sent', SentAt = now(), ExpiresAt = greatest(ExpiresAt, now() + interval '14 days'), SentBy = @u WHERE InviteId = @id",
                new { id, h = Tokens.Hash(token), u = w.User.UserId });
            await w.Log(s.ProjectId, s.VersionId, "invite.resend", "invite", id);
            await w.Commit();
            return Results.Ok(new { inviteId = id, token, link = $"/bid/{token}" });
        });

        api.MapDelete("/invites/{id:long}", async (long id, HttpContext ctx) =>
        {
            await using var w = await Work.Begin(ctx);
            var tradeId = await w.Scalar<long?>("SELECT TradeId FROM BidInvite WHERE InviteId = @id", new { id }) ?? throw ApiException.NotFound("Invite");
            var s = await Access.Trade(w, tradeId);
            await w.Exec("UPDATE BidInvite SET Status = 'withdrawn' WHERE InviteId = @id", new { id });
            await w.Log(s.ProjectId, s.VersionId, "invite.withdraw", "invite", id);
            await w.Commit();
            return Results.NoContent();
        });

        // ------------------------------------------------------------------ public: what the sub sees (no sign-in; the token is the key)
        api.MapGet("/portal/{token}", async (string token, Db db) =>
        {
            await using var c = await db.Open();
            var inv = await Find(c, token);
            if (inv.Status == "sent") await c.ExecuteAsync("UPDATE BidInvite SET Status = 'opened', OpenedAt = now() WHERE InviteId = @id", new { id = inv.InviteId });
            var lines = await c.QueryAsync("""
                SELECT LineId AS "lineId", Section::text AS "section", IsHeading AS "isHeading", LineText AS "lineText", Desired::text AS "desired", Indent AS "indent"
                FROM ScopeLine WHERE TradeId = @t AND ArchivedAt IS NULL AND Section <> 'zone'
                ORDER BY CASE Section WHEN 'baserequirement' THEN 1 ELSE 2 END, SortKey
                """, new { t = inv.TradeId });
            return new { inv.ProjectName, inv.TradeName, inv.CsiCode, inv.CompanyName, dueOn = inv.BidDueOn, inv.Message, status = inv.Status == "sent" ? "opened" : inv.Status, lines, company = inv.Company };
        }).AllowAnonymous();

        api.MapPost("/portal/{token}", async (string token, PortalIn body, Db db, IHubContext<ProjectHub> hub) =>
        {
            await using var c = await db.Open();
            await using var t = await c.BeginTransactionAsync();
            var inv = await Find(c, token, t);
            if (inv.Status is "submitted") throw new ApiException(409, "This bid was already submitted. Contact the estimator to change it.", "duplicate");
            if (body.BaseAmount is null or <= 0) throw ApiException.Bad("Enter your base bid.");
            var lineIds = (await c.QueryAsync<long>("SELECT LineId FROM ScopeLine WHERE TradeId = @t AND ArchivedAt IS NULL AND NOT IsHeading", new { t = inv.TradeId }, t)).ToHashSet();
            var answers = (body.Answers ?? []).Where(a => lineIds.Contains(a.LineId) && !string.IsNullOrWhiteSpace(a.Value))
                .Select(a => { var p = Rules.ParseAnswer(a.Value); return new { lineId = a.LineId, kind = p.Kind, amount = p.Amount, text = p.Text ?? (p.Kind == "text" ? a.Value : null) }; }).ToList();
            var sid = await c.ExecuteScalarAsync<long>("INSERT INTO BidSubmission (InviteId, BaseAmount, Answers, Notes) VALUES (@i, @b, @a::jsonb, @n) RETURNING SubmissionId",
                new { i = inv.InviteId, b = body.BaseAmount, a = Jsonb.Text(answers), n = body.Notes }, t);
            await c.ExecuteAsync("UPDATE BidInvite SET Status = 'submitted', SubmittedAt = now() WHERE InviteId = @id", new { id = inv.InviteId }, t);
            var ev = await c.ExecuteScalarAsync<long>("INSERT INTO ActivityEvent (ProjectId, VersionId, Verb, SubjectType, SubjectKey, AfterState) VALUES (@p, @v, 'portal.submit', 'submission', @sid, @a::jsonb) RETURNING EventId",
                new { p = inv.ProjectId, v = inv.VersionId, sid = sid.ToString(), a = Jsonb.Text(new { inv.CompanyName, body.BaseAmount, answers = answers.Count }) }, t);
            await t.CommitAsync();
            await hub.Clients.Group(ProjectHub.Group(inv.ProjectId)).SendAsync("changed", new ChangeEvent(ev, "portal.submit", "submission", sid.ToString(), 0, inv.CompanyName, inv.VersionId, inv.ProjectId));
            return Results.Ok(new { submissionId = sid, received = DateTime.UtcNow });
        }).AllowAnonymous();

        // ------------------------------------------------------------------ review a submission
        api.MapGet("/submissions/{id:long}", async (long id, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            var tradeId = await c.ExecuteScalarAsync<long?>("SELECT i.TradeId FROM BidSubmission s JOIN BidInvite i ON i.InviteId = s.InviteId WHERE s.SubmissionId = @id", new { id }) ?? throw ApiException.NotFound("Submission");
            await Access.Trade(c, ctx.Me(), tradeId);
            return await c.QueryFirstAsync("""
                SELECT s.SubmissionId AS "submissionId", s.BaseAmount AS "baseAmount", s.Answers::text AS "answers", s.Notes AS "notes", s.SubmittedAt AS "submittedAt",
                       s.Decision::text AS "decision", i.BidId AS "bidId", tp.CompanyName AS "companyName"
                FROM BidSubmission s JOIN BidInvite i ON i.InviteId = s.InviteId JOIN TradePartner tp ON tp.PartnerId = i.PartnerId WHERE s.SubmissionId = @id
                """, new { id });
        });

        api.MapPost("/submissions/{id:long}/decide", async (long id, DecideIn body, HttpContext ctx) =>
        {
            if (body.Decision is not ("accepted" or "rejected")) throw ApiException.Bad("Decision must be accepted or rejected.");
            await using var w = await Work.Begin(ctx);
            var sub = await w.One<(long tradeId, long? bidId, decimal? baseAmount, string answers, string decision)>("""
                SELECT i.TradeId, i.BidId, s.BaseAmount, s.Answers::text, s.Decision::text FROM BidSubmission s JOIN BidInvite i ON i.InviteId = s.InviteId WHERE s.SubmissionId = @id FOR UPDATE OF s
                """, new { id });
            if (sub.tradeId == 0) throw ApiException.NotFound("Submission");
            var s = await Access.Trade(w, sub.tradeId);
            if (sub.decision != "pending") throw new ApiException(409, $"This submission was already {sub.decision}.", "duplicate");
            await w.Exec("UPDATE BidSubmission SET Decision = @d::ReviewDecision, DecidedBy = @u, DecidedAt = now() WHERE SubmissionId = @id", new { id, d = body.Decision, u = w.User.UserId });
            if (body.Decision == "accepted" && sub.bidId is long bidId)
            {
                await w.Exec("UPDATE Bid SET BaseAmount = @b, BaseSource = 'portal', State = 'posted', IsPosted = true, ChangedBy = @u WHERE BidId = @bidId", new { b = sub.baseAmount, bidId, u = w.User.UserId });
                await w.Exec("""
                    INSERT INTO ScopeAnswer (LineId, BidId, Kind, Amount, AnswerText, Source, ChangedBy)
                    SELECT (a->>'lineId')::bigint, @bidId, (a->>'kind')::AnswerKind, (a->>'amount')::numeric, a->>'text', 'portal', @u
                    FROM jsonb_array_elements(@answers::jsonb) a JOIN ScopeLine l ON l.LineId = (a->>'lineId')::bigint AND l.TradeId = @t
                    ON CONFLICT (LineId, BidId) DO UPDATE SET Kind = EXCLUDED.Kind, Amount = EXCLUDED.Amount, AnswerText = EXCLUDED.AnswerText, Source = 'portal', ChangedBy = @u
                    """, new { bidId, answers = sub.answers, t = sub.tradeId, u = w.User.UserId });
            }
            var (_, sum) = await Recalc.TradeAndSummary(w, sub.tradeId, "portal.decide");
            await w.Log(s.ProjectId, s.VersionId, $"portal.{body.Decision}", "submission", id);
            await w.Commit();
            return Results.Ok(new { trade = await TradeEndpoints.One(w.C, null, sub.tradeId), totalsHash = sum.Hash });
        });
    }

    sealed class InviteRow
    {
        public long InviteId { get; set; } public long TradeId { get; set; } public long ProjectId { get; set; } public long VersionId { get; set; } public string Status { get; set; } = "";
        public string ProjectName { get; set; } = ""; public string TradeName { get; set; } = ""; public string CsiCode { get; set; } = ""; public string CompanyName { get; set; } = "";
        public DateOnly? BidDueOn { get; set; } public string? Message { get; set; } public string Company { get; set; } = "";
    }
    static async Task<InviteRow> Find(Npgsql.NpgsqlConnection c, string token, Npgsql.NpgsqlTransaction? t = null)
    {
        var inv = await c.QueryFirstOrDefaultAsync<InviteRow>("""
            SELECT i.InviteId, i.TradeId, tr.ProjectId, tr.VersionId, i.Status::text AS Status, p.ProjectName, tr.TradeName, tr.CsiCode, tp.CompanyName,
                   coalesce(tr.BidDueOn, p.BidDueOn) AS BidDueOn, i.Message, (SELECT DisplayName FROM CompanyProfile LIMIT 1) AS Company
            FROM BidInvite i JOIN Trade tr ON tr.TradeId = i.TradeId JOIN Project p ON p.ProjectId = tr.ProjectId JOIN TradePartner tp ON tp.PartnerId = i.PartnerId
            WHERE i.TokenHash = @h AND i.ExpiresAt > now() AND i.Status NOT IN ('withdrawn','expired','declined') AND tr.ArchivedAt IS NULL
            """, new { h = Tokens.Hash(token) }, t);
        return inv ?? throw ApiException.NotFound("This bid link");
    }
}
