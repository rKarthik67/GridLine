using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Gridline.Infrastructure;

/// <summary>The signed-in person for this request.</summary>
public sealed record CurrentUser(long UserId, string Name, string Initials, string Colour, string Email, string[] Roles, string[] Capabilities)
{
    public bool Can(string cap) => Capabilities.Contains("*") || Capabilities.Contains(cap);
    public bool IsAdmin => Capabilities.Contains("*");
    public void Require(string cap) { if (!Can(cap)) throw ApiException.Forbidden($"You need the '{cap}' permission for this."); }
}

public static class Passwords
{
    const int Iterations = 100_000;
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
        if (!cache.TryGetValue(key, out CurrentUser? user))
        {
            await using var c = await db.Open();
            user = await Load(c, "t.TokenHash = @h AND t.RevokedAt IS NULL AND t.ExpiresAt > now()", new { h = Tokens.Hash(token) });
            if (user is null) return AuthenticateResult.Fail("Token is not valid");
            cache.Set(key, user, TimeSpan.FromSeconds(30));
        }
        var id = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user!.UserId.ToString()), new Claim(ClaimTypes.Name, user.Name)], SchemeName);
        Context.Items[typeof(CurrentUser)] = user;
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(id), SchemeName));
    }

    public static async Task<CurrentUser?> Load(Npgsql.NpgsqlConnection c, string where, object args)
    {
        var row = await c.QueryFirstOrDefaultAsync<UserRow>($"""
            SELECT u.UserId, u.GivenName || ' ' || u.FamilyName AS Name, u.Initials, u.Colour, u.LoginEmail::text AS Email,
                   coalesce((SELECT array_agg(r.RoleCode) FROM UserRole ur JOIN AccessRole r ON r.RoleId = ur.RoleId WHERE ur.UserId = u.UserId), ARRAY[]::text[]) AS Roles,
                   coalesce((SELECT array_agg(DISTINCT g.Capability) FROM UserRole ur JOIN RoleGrant g ON g.RoleId = ur.RoleId WHERE ur.UserId = u.UserId), ARRAY[]::text[]) AS Capabilities
            FROM AppUser u {(where.Contains("t.") ? "JOIN LoginToken t ON t.UserId = u.UserId" : "")}
            WHERE u.IsActive AND {where}
            """, args);
        return row is null ? null : new CurrentUser(row.UserId, row.Name, row.Initials, row.Colour, row.Email, row.Roles, row.Capabilities);
    }
    sealed class UserRow { public long UserId { get; set; } public string Name { get; set; } = ""; public string Initials { get; set; } = ""; public string Colour { get; set; } = ""; public string Email { get; set; } = ""; public string[] Roles { get; set; } = []; public string[] Capabilities { get; set; } = []; }
}

public static class UserExtensions
{
    public static CurrentUser Me(this HttpContext ctx) =>
        ctx.Items[typeof(CurrentUser)] as CurrentUser ?? throw new ApiException(401, "Please sign in.", "signin");
    public static string? TabId(this HttpContext ctx) => ctx.Request.Headers["X-Tab-Id"].FirstOrDefault();
}
