using System.Text.RegularExpressions;
using Dapper;
using Gridline.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;

namespace Gridline.Endpoints;

/// <summary>
/// Getting into the product: create a company (sign-up), accept an invitation, forgotten passwords, change password.
/// Links carry a random token; only its hash is stored, and every token has an expiry and single use.
/// </summary>
public static class AccountEndpoints
{
    public sealed record SignupIn(string? CompanyName, string? GivenName, string? FamilyName, string? Email, string? Password, string? ProjectNoPrefix);
    public sealed record ForgotIn(string? Email);
    public sealed record ResetIn(string? Token, string? Password);
    public sealed record AcceptIn(string? Token, string? Password, string? GivenName, string? FamilyName);
    public sealed record ChangeIn(string? CurrentPassword, string? NewPassword);
    static readonly Regex EmailRx = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    public static void Map(RouteGroupBuilder api)
    {
        // ------------------------------------------------------------------ sign-up: a new company with its first administrator
        api.MapPost("/auth/signup", async (SignupIn body, HttpContext ctx, Db db, IConfiguration cfg) =>
        {
            if (!cfg.GetValue("Signup:Enabled", true)) throw ApiException.Forbidden("Sign-up is by invitation only on this server.");
            var company = body.CompanyName?.Trim(); var email = body.Email?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(company) || company.Length < 2) throw ApiException.Bad("Enter your company name.");
            if (string.IsNullOrWhiteSpace(body.GivenName) || string.IsNullOrWhiteSpace(body.FamilyName)) throw ApiException.Bad("Enter your first and last name.");
            if (email is null || !EmailRx.IsMatch(email)) throw ApiException.Bad("Enter a valid work email address.");
            if (Passwords.Weakness(body.Password) is string weak) throw ApiException.Bad(weak);
            var prefix = string.IsNullOrWhiteSpace(body.ProjectNoPrefix) ? "EST-" : body.ProjectNoPrefix.Trim().ToUpperInvariant();
            if (prefix.Length > 8) throw ApiException.Bad("The project number prefix can be at most 8 characters.");

            await using var c = await db.Open();
            await using var t = await c.BeginTransactionAsync();
            if (await c.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM AppUser WHERE LoginEmail = @e::citext)", new { e = email }, t))
                throw new ApiException(409, "There is already an account with that email. Sign in, or reset your password.", "duplicate");
            var slug = await FreeSlug(c, t, company);
            var cid = await c.ExecuteScalarAsync<long>("""
                INSERT INTO Company (Slug, DisplayName, LegalName, ProjectNoPrefix, Plan, TrialEndsOn)
                VALUES (@slug, @name, @name, @prefix, 'trial', current_date + 30) RETURNING CompanyId
                """, new { slug, name = company, prefix }, t);
            await c.ExecuteAsync("SELECT CopyCompanyLibrary((SELECT CompanyId FROM Company WHERE IsLibrary), @cid)", new { cid }, t);
            var uid = await c.ExecuteScalarAsync<long>("""
                INSERT INTO AppUser (CompanyId, GivenName, FamilyName, LoginEmail, PasswordHash, Colour, PasswordChangedAt, EmailVerifiedAt)
                VALUES (@cid, @gn, @fn, @e::citext, @h, @colour, now(), NULL) RETURNING UserId
                """, new { cid, gn = body.GivenName!.Trim(), fn = body.FamilyName!.Trim(), e = email, h = Passwords.Hash(body.Password!), colour = Colour(email) }, t);
            await c.ExecuteAsync("INSERT INTO UserRole (UserId, RoleId) SELECT @uid, RoleId FROM AccessRole WHERE RoleCode = 'admin'", new { uid }, t);
            var token = Tokens.New();
            await c.ExecuteAsync("INSERT INTO LoginToken (UserId, TokenHash, ExpiresAt) VALUES (@u, @h, now() + interval '12 hours')", new { u = uid, h = Tokens.Hash(token) }, t);
            await c.ExecuteAsync("INSERT INTO ActivityEvent (UserId, Verb, SubjectType, SubjectKey, AfterState) VALUES (@uid, 'company.create', 'company', @cid, @a::jsonb)", new { uid, cid = cid.ToString(), a = Jsonb.Text(new { company, email }) }, t);
            var me = await TokenAuthHandler.Load(c, "u.UserId = @id", new { id = uid }, t);
            await t.CommitAsync();
            return Results.Created("/api/me", new { accessToken = token, me = AuthEndpoints.MeDto(me!) });
        }).AllowAnonymous().RequireRateLimiting(Security.AuthPolicy);

        // ------------------------------------------------------------------ invitations
        api.MapGet("/auth/invite/{token}", async (string token, Db db) =>
        {
            await using var c = await db.Open();
            var inv = await c.QueryFirstOrDefaultAsync("""
                SELECT i.Email::text AS "email", i.GivenName AS "givenName", i.FamilyName AS "familyName", i.RoleCode AS "roleCode", co.DisplayName AS "companyName",
                       (SELECT u.GivenName || ' ' || u.FamilyName FROM AppUser u WHERE u.UserId = i.InvitedBy) AS "invitedBy", i.ExpiresAt AS "expiresAt"
                FROM UserInvite i JOIN Company co ON co.CompanyId = i.CompanyId WHERE i.TokenHash = @h AND i.AcceptedAt IS NULL AND i.ExpiresAt > now()
                """, new { h = Tokens.Hash(token) }) ?? throw ApiException.NotFound("This invitation");
            return inv;
        }).AllowAnonymous().RequireRateLimiting(Security.PublicPolicy);

        api.MapPost("/auth/invite/accept", async (AcceptIn body, Db db) =>
        {
            if (string.IsNullOrWhiteSpace(body.Token)) throw ApiException.Bad("The invitation link is incomplete.");
            if (Passwords.Weakness(body.Password) is string weak) throw ApiException.Bad(weak);
            await using var c = await db.Open();
            await using var t = await c.BeginTransactionAsync();
            var inv = await c.QueryFirstOrDefaultAsync<(long id, long cid, string email, string? gn, string? fn, string role)>(
                "SELECT InviteId, CompanyId, Email::text, GivenName, FamilyName, RoleCode FROM UserInvite WHERE TokenHash = @h AND AcceptedAt IS NULL AND ExpiresAt > now() FOR UPDATE", new { h = Tokens.Hash(body.Token) }, t);
            if (inv.id == 0) throw ApiException.NotFound("This invitation");
            var gn = body.GivenName?.Trim() is { Length: > 0 } g ? g : inv.gn; var fn = body.FamilyName?.Trim() is { Length: > 0 } f ? f : inv.fn;
            if (string.IsNullOrWhiteSpace(gn) || string.IsNullOrWhiteSpace(fn)) throw ApiException.Bad("Enter your first and last name.");
            var uid = await c.ExecuteScalarAsync<long?>("SELECT UserId FROM AppUser WHERE LoginEmail = @e::citext", new { e = inv.email }, t);
            if (uid is long existing)
            {
                var sameCompany = await c.ExecuteScalarAsync<bool>("SELECT CompanyId = @cid FROM AppUser WHERE UserId = @u", new { cid = inv.cid, u = existing }, t);
                if (!sameCompany) throw new ApiException(409, "That email already belongs to an account in another company.", "duplicate");
                await c.ExecuteAsync("UPDATE AppUser SET PasswordHash = @h, IsActive = true, GivenName = @gn, FamilyName = @fn, PasswordChangedAt = now(), EmailVerifiedAt = coalesce(EmailVerifiedAt, now()), FailedLogins = 0, LockedUntil = NULL WHERE UserId = @u",
                    new { h = Passwords.Hash(body.Password!), gn, fn, u = existing }, t);
            }
            else
            {
                uid = await c.ExecuteScalarAsync<long>("""
                    INSERT INTO AppUser (CompanyId, GivenName, FamilyName, LoginEmail, PasswordHash, Colour, PasswordChangedAt, EmailVerifiedAt)
                    VALUES (@cid, @gn, @fn, @e::citext, @h, @colour, now(), now()) RETURNING UserId
                    """, new { cid = inv.cid, gn, fn, e = inv.email, h = Passwords.Hash(body.Password!), colour = Colour(inv.email) }, t);
            }
            await c.ExecuteAsync("DELETE FROM UserRole WHERE UserId = @u", new { u = uid }, t);
            await c.ExecuteAsync("INSERT INTO UserRole (UserId, RoleId) SELECT @u, RoleId FROM AccessRole WHERE RoleCode = @role", new { u = uid, role = inv.role }, t);
            await c.ExecuteAsync("UPDATE UserInvite SET AcceptedAt = now(), UserId = @u WHERE InviteId = @id", new { u = uid, id = inv.id }, t);
            var token = Tokens.New();
            await c.ExecuteAsync("INSERT INTO LoginToken (UserId, TokenHash, ExpiresAt) VALUES (@u, @h, now() + interval '12 hours')", new { u = uid, h = Tokens.Hash(token) }, t);
            var me = await TokenAuthHandler.Load(c, "u.UserId = @id", new { id = uid }, t);
            await t.CommitAsync();
            return Results.Ok(new { accessToken = token, me = AuthEndpoints.MeDto(me!) });
        }).AllowAnonymous().RequireRateLimiting(Security.AuthPolicy);

        // ------------------------------------------------------------------ forgotten password
        api.MapPost("/auth/forgot", async (ForgotIn body, HttpContext ctx, Db db, IEmailSender mail, IConfiguration cfg, IHostEnvironment env) =>
        {
            var email = body.Email?.Trim();
            if (string.IsNullOrWhiteSpace(email)) throw ApiException.Bad("Enter your email address.");
            await using var c = await db.Open();
            var u = await c.QueryFirstOrDefaultAsync<(long id, string name)>("SELECT UserId, GivenName FROM AppUser WHERE LoginEmail = @e::citext AND IsActive", new { e = email });
            string? devLink = null;
            if (u.id != 0)
            {
                var token = Tokens.New();
                await c.ExecuteAsync("INSERT INTO PasswordReset (UserId, TokenHash, ExpiresAt) VALUES (@u, @h, now() + interval '2 hours')", new { u = u.id, h = Tokens.Hash(token) });
                var link = PublicUrl.Of(cfg, ctx.Request, $"/reset/{token}");
                await mail.Send(email, "Reset your Gridline password", $"Hi {u.name},\n\nSomeone asked to reset the password for this Gridline account. Open this link within 2 hours to choose a new one:\n\n{link}\n\nIf that wasn't you, ignore this email; your password stays the same.");
                if (env.IsDevelopment() && !mail.Configured) devLink = link;
            }
            // the same answer whether or not the address exists
            return Results.Ok(new { sent = true, message = "If that email has an account, a reset link is on its way.", devLink });
        }).AllowAnonymous().RequireRateLimiting(Security.AuthPolicy);

        api.MapPost("/auth/reset", async (ResetIn body, Db db) =>
        {
            if (string.IsNullOrWhiteSpace(body.Token)) throw ApiException.Bad("The reset link is incomplete.");
            if (Passwords.Weakness(body.Password) is string weak) throw ApiException.Bad(weak);
            await using var c = await db.Open();
            await using var t = await c.BeginTransactionAsync();
            var r = await c.QueryFirstOrDefaultAsync<(long id, long uid)>("SELECT ResetId, UserId FROM PasswordReset WHERE TokenHash = @h AND UsedAt IS NULL AND ExpiresAt > now() FOR UPDATE", new { h = Tokens.Hash(body.Token) }, t);
            if (r.id == 0) throw ApiException.NotFound("This reset link");
            await c.ExecuteAsync("UPDATE AppUser SET PasswordHash = @h, PasswordChangedAt = now(), FailedLogins = 0, LockedUntil = NULL WHERE UserId = @u", new { h = Passwords.Hash(body.Password!), u = r.uid }, t);
            await c.ExecuteAsync("UPDATE PasswordReset SET UsedAt = now() WHERE ResetId = @id", new { id = r.id }, t);
            await c.ExecuteAsync("UPDATE LoginToken SET RevokedAt = now() WHERE UserId = @u AND RevokedAt IS NULL", new { u = r.uid }, t);   // every other session signs out
            await t.CommitAsync();
            Sessions.Invalidate(r.uid);
            return Results.Ok(new { ok = true });
        }).AllowAnonymous().RequireRateLimiting(Security.AuthPolicy);

        api.MapPost("/auth/change-password", async (ChangeIn body, HttpContext ctx, Db db) =>
        {
            var me = ctx.Me();
            if (Passwords.Weakness(body.NewPassword) is string weak) throw ApiException.Bad(weak);
            await using var c = await db.Open();
            var hash = await c.ExecuteScalarAsync<string?>("SELECT PasswordHash FROM AppUser WHERE UserId = @u", new { u = me.UserId });
            if (!Passwords.Verify(body.CurrentPassword ?? "", hash)) throw ApiException.Bad("Your current password is not right.");
            await c.ExecuteAsync("UPDATE AppUser SET PasswordHash = @h, PasswordChangedAt = now() WHERE UserId = @u", new { h = Passwords.Hash(body.NewPassword!), u = me.UserId });
            var h = ctx.Request.Headers.Authorization.ToString();
            await c.ExecuteAsync("UPDATE LoginToken SET RevokedAt = now() WHERE UserId = @u AND RevokedAt IS NULL AND TokenHash <> @keep", new { u = me.UserId, keep = Tokens.Hash(h.StartsWith("Bearer ") ? h[7..].Trim() : "") });
            Sessions.Invalidate(me.UserId);
            return Results.Ok(new { ok = true });
        });

        api.MapGet("/auth/sessions", async (HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            return await c.QueryAsync("SELECT TokenId AS \"tokenId\", IssuedAt AS \"issuedAt\", ExpiresAt AS \"expiresAt\", LastUsedAt AS \"lastUsedAt\", UserAgent AS \"userAgent\" FROM LoginToken WHERE UserId = @u AND RevokedAt IS NULL AND ExpiresAt > now() ORDER BY IssuedAt DESC", new { u = ctx.Me().UserId });
        });
        api.MapPost("/auth/sessions/revoke-others", async (HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            var h = ctx.Request.Headers.Authorization.ToString();
            var n = await c.ExecuteAsync("UPDATE LoginToken SET RevokedAt = now() WHERE UserId = @u AND RevokedAt IS NULL AND TokenHash <> @keep", new { u = ctx.Me().UserId, keep = Tokens.Hash(h.StartsWith("Bearer ") ? h[7..].Trim() : "") });
            Sessions.Invalidate(ctx.Me().UserId);
            return Results.Ok(new { revoked = n });
        });
    }

    /// <summary>A URL-safe handle from the company name, made unique with a number if needed.</summary>
    public static async Task<string> FreeSlug(Npgsql.NpgsqlConnection c, Npgsql.NpgsqlTransaction? t, string name)
    {
        var baseSlug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (baseSlug.Length < 3) baseSlug = "company-" + baseSlug;
        baseSlug = baseSlug[..Math.Min(baseSlug.Length, 40)];
        var slug = baseSlug;
        for (var i = 2; await c.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM Company WHERE Slug = @slug)", new { slug }, t); i++) slug = $"{baseSlug}-{i}";
        return slug;
    }
    static readonly string[] Colours = ["#27624d", "#315c91", "#946211", "#b33632", "#586719", "#6a32d4", "#0e7490", "#be185d", "#7c2d12", "#4d7c0f"];
    public static string Colour(string seed) => Colours[Math.Abs(seed.GetHashCode()) % Colours.Length];
}
