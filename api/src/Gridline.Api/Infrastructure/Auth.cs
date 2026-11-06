using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Gridline.Infrastructure;

/// <summary>The signed-in person for this request. Everything they can see belongs to their company.</summary>
public sealed record CurrentUser(long UserId, string Name, string Initials, string Colour, string Email, string[] Roles, string[] Capabilities,
                                 long CompanyId = 0, string CompanyName = "", string CompanySlug = "", string CompanyPlan = "", DateOnly? TrialEndsOn = null)
{
    public bool Can(string cap) => Capabilities.Contains("*") || Capabilities.Contains(cap);
    public bool IsAdmin => Capabilities.Contains("*");
    public void Require(string cap) { if (!Can(cap)) throw ApiException.Forbidden($"You need the '{cap}' permission for this."); }
    public void RequireAdmin() { if (!IsAdmin) throw ApiException.Forbidden("Only a company administrator can do this."); }
    /// <summary>Background jobs and start-up work act as this user (no company: they see what the job tells them to).</summary>
    public static CurrentUser System => new(0, "System", "SY", "#888888", "", [], ["*"]);
}

public static class Passwords
{
    const int Iterations = 100_000;
    public const int MinLength = 10;
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }
    public static bool Verify(string password, string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        var p = stored.Split('$');
        if (p.Length != 4 || p[0] != "pbkdf2" || !int.TryParse(p[1], out var it)) return false;
        var salt = Convert.FromBase64String(p[2]); var want = Convert.FromBase64String(p[3]);
        var got = Rfc2898DeriveBytes.Pbkdf2(password, salt, it, HashAlgorithmName.SHA256, want.Length);
        return CryptographicOperations.FixedTimeEquals(got, want);
    }
    /// <summary>A password people can't guess: at least 10 characters, not all one kind of character.</summary>
    public static string? Weakness(string? password)
    {
        if (password is null || password.Length < MinLength) return $"Use at least {MinLength} characters.";
        if (password.Length > 200) return "That password is too long.";
        var kinds = (password.Any(char.IsLower) ? 1 : 0) + (password.Any(char.IsUpper) ? 1 : 0) + (password.Any(char.IsDigit) ? 1 : 0) + (password.Any(c => !char.IsLetterOrDigit(c)) ? 1 : 0);
        if (kinds < 2) return "Mix letters with numbers or symbols.";
        return null;
    }
}

/// <summary>Cached sign-ins are good for 30 s; revoking a person's sessions bumps their stamp so cached entries stop at once.</summary>
public static class Sessions
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<long, long> Stamps = new();
    public static long Stamp(long userId) => Stamps.GetValueOrDefault(userId);
    public static void Invalidate(long userId) => Stamps.AddOrUpdate(userId, 1, (_, v) => v + 1);
}

public static class Tokens
{
    public static string New() => Base64Url(RandomNumberGenerator.GetBytes(32));
    public static byte[] Hash(string token) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
    public static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// Opaque bearer tokens (hashed in LoginToken). The browser keeps one token per user, shared by all tabs.
/// SignalR sends it as ?access_token= on the websocket.
/// </summary>
public sealed class TokenAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e, Db db, IMemoryCache cache)
    : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
{
    public const string SchemeName = "Gridline";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? token = null;
        var h = Request.Headers.Authorization.ToString();
        if (h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = h[7..].Trim();
        else if (Request.Query.TryGetValue("access_token", out var q)) token = q.ToString();
        if (string.IsNullOrEmpty(token)) return AuthenticateResult.NoResult();

        var key = "tok:" + token;
        CurrentUser? user = null;
        if (cache.TryGetValue(key, out (CurrentUser u, long stamp) hit) && hit.stamp == Sessions.Stamp(hit.u.UserId)) user = hit.u;
        if (user is null)
        {
            await using var c = await db.Open();
            user = await Load(c, "t.TokenHash = @h AND t.RevokedAt IS NULL AND t.ExpiresAt > now()", new { h = Tokens.Hash(token) });
            if (user is null) return AuthenticateResult.Fail("Token is not valid");
            cache.Set(key, (user, Sessions.Stamp(user.UserId)), TimeSpan.FromSeconds(30));
            // at most once per cache miss (30 s): remember when the session was last used
            await c.ExecuteAsync("UPDATE LoginToken SET LastUsedAt = now() WHERE TokenHash = @h AND (LastUsedAt IS NULL OR LastUsedAt < now() - interval '5 minutes')", new { h = Tokens.Hash(token) });
        }
        var id = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user!.UserId.ToString()), new Claim(ClaimTypes.Name, user.Name)], SchemeName);
        Context.Items[typeof(CurrentUser)] = user;
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(id), SchemeName));
    }

    public static async Task<CurrentUser?> Load(Npgsql.NpgsqlConnection c, string where, object args, Npgsql.NpgsqlTransaction? tx = null)
    {
        var row = await c.QueryFirstOrDefaultAsync<UserRow>($"""
            SELECT u.UserId, u.GivenName || ' ' || u.FamilyName AS Name, u.Initials, u.Colour, u.LoginEmail::text AS Email,
                   coalesce((SELECT array_agg(r.RoleCode) FROM UserRole ur JOIN AccessRole r ON r.RoleId = ur.RoleId WHERE ur.UserId = u.UserId), ARRAY[]::text[]) AS Roles,
                   coalesce((SELECT array_agg(DISTINCT g.Capability) FROM UserRole ur JOIN RoleGrant g ON g.RoleId = ur.RoleId WHERE ur.UserId = u.UserId), ARRAY[]::text[]) AS Capabilities,
                   u.CompanyId, co.DisplayName AS CompanyName, co.Slug AS CompanySlug, co.Plan AS CompanyPlan, co.TrialEndsOn
            FROM AppUser u JOIN Company co ON co.CompanyId = u.CompanyId {(where.Contains("t.") ? "JOIN LoginToken t ON t.UserId = u.UserId" : "")}
            WHERE u.IsActive AND co.Status = 'active' AND NOT co.IsLibrary AND {where}
            """, args, tx);
        return row is null ? null : new CurrentUser(row.UserId, row.Name, row.Initials, row.Colour, row.Email, row.Roles, row.Capabilities, row.CompanyId, row.CompanyName, row.CompanySlug, row.CompanyPlan, row.TrialEndsOn);
    }
    sealed class UserRow
    {
        public long UserId { get; set; } public string Name { get; set; } = ""; public string Initials { get; set; } = ""; public string Colour { get; set; } = ""; public string Email { get; set; } = "";
        public string[] Roles { get; set; } = []; public string[] Capabilities { get; set; } = []; public long CompanyId { get; set; } public string CompanyName { get; set; } = ""; public string CompanySlug { get; set; } = ""; public string CompanyPlan { get; set; } = ""; public DateOnly? TrialEndsOn { get; set; }
    }
}

public static class UserExtensions
{
    public static CurrentUser Me(this HttpContext ctx) =>
        ctx.Items[typeof(CurrentUser)] as CurrentUser ?? throw new ApiException(401, "Please sign in.", "signin");
    public static string? TabId(this HttpContext ctx) => ctx.Request.Headers["X-Tab-Id"].FirstOrDefault();
    /// <summary>The caller's address for rate limiting and audit; honours X-Forwarded-For when behind a proxy (see UseForwardedHeaders).</summary>
    public static string ClientIp(this HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
