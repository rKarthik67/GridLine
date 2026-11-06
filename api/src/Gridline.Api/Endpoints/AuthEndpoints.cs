using Dapper;
using Gridline.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;

namespace Gridline.Endpoints;

/// <summary>Sign in and out, who am I, and the lookups every page needs (all scoped to the caller's company).</summary>
public static class AuthEndpoints
{
    public sealed record LoginIn(string Email, string Password, bool? RememberMe);
    const int MaxFailed = 8;

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/auth/login", async (LoginIn body, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            var email = body.Email?.Trim();
            var row = await c.QueryFirstOrDefaultAsync<(long id, string? hash, int failed, DateTime? locked)>(
                "SELECT u.UserId, u.PasswordHash, u.FailedLogins, u.LockedUntil FROM AppUser u JOIN Company co ON co.CompanyId = u.CompanyId WHERE u.LoginEmail = @e::citext AND u.IsActive AND co.Status = 'active'", new { e = email });
            if (row.id != 0 && row.locked is DateTime until && until > DateTime.UtcNow)
                throw new ApiException(423, $"Too many wrong passwords. Try again in {Math.Max(1, (int)(until - DateTime.UtcNow).TotalMinutes)} minutes, or reset your password.", "locked");
            if (row.id == 0 || !Passwords.Verify(body.Password ?? "", row.hash))
            {
                if (row.id != 0)
                    await c.ExecuteAsync("UPDATE AppUser SET FailedLogins = FailedLogins + 1, LockedUntil = CASE WHEN FailedLogins + 1 >= @max THEN now() + interval '15 minutes' END WHERE UserId = @u", new { u = row.id, max = MaxFailed });
                throw new ApiException(401, "Email or password is not right.", "signin");
            }
            var token = Tokens.New();
            var days = body.RememberMe == true ? 30 : 0;
            await c.ExecuteAsync("INSERT INTO LoginToken (UserId, TokenHash, ExpiresAt, UserAgent) VALUES (@u, @h, CASE WHEN @days > 0 THEN now() + make_interval(days => @days) ELSE now() + interval '12 hours' END, @ua)",
                new { u = row.id, h = Tokens.Hash(token), days, ua = ctx.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua[..Math.Min(ua.Length, 200)] : null });
            await c.ExecuteAsync("UPDATE AppUser SET LastSeenAt = now(), FailedLogins = 0, LockedUntil = NULL WHERE UserId = @u", new { u = row.id });
            var me = await TokenAuthHandler.Load(c, "u.UserId = @id", new { id = row.id });
            return Results.Ok(new { accessToken = token, me = MeDto(me!) });
        }).AllowAnonymous().RequireRateLimiting(Security.AuthPolicy);

        api.MapPost("/auth/logout", async (HttpContext ctx, Db db) =>
        {
            var h = ctx.Request.Headers.Authorization.ToString();
            if (h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                await using var c = await db.Open();
                await c.ExecuteAsync("UPDATE LoginToken SET RevokedAt = now() WHERE TokenHash = @h", new { h = Tokens.Hash(h[7..].Trim()) });
            }
            return Results.NoContent();
        }).AllowAnonymous();

        api.MapGet("/me", (HttpContext ctx) => MeDto(ctx.Me()));

        // what the sign-in pages need before anyone is signed in: is sign-up open, where to get help, terms and privacy links
        api.MapGet("/config", (IConfiguration cfg) => new
        {
            signupEnabled = cfg.GetValue("Signup:Enabled", true),
            supportEmail = cfg["Support:Email"],
            termsUrl = cfg["Legal:TermsUrl"], privacyUrl = cfg["Legal:PrivacyUrl"],
            version = typeof(Program).Assembly.GetName().Version?.ToString(3),
        }).AllowAnonymous();

        api.MapGet("/lookups", async (HttpContext ctx, Db db) =>
        {
            var me = ctx.Me(); var cid = me.CompanyId;
            await using var c = await db.Open();
            async Task<List<dynamic>> Q(string sql) => (await c.QueryAsync(sql, new { cid })).ToList();
            return new
            {
                states = await Q("SELECT StateCode AS \"stateCode\", StateName AS \"stateName\", SalesTaxRate AS \"salesTaxRate\" FROM UsState ORDER BY StateName"),
                divisions = await Q("SELECT DivisionId AS \"divisionId\", ShortCode AS \"shortCode\", DivisionName AS \"divisionName\" FROM Division WHERE CompanyId = @cid ORDER BY DivisionName"),
                branches = await Q("SELECT BranchId AS \"branchId\", BranchName AS \"branchName\" FROM Branch WHERE CompanyId = @cid ORDER BY BranchName"),
                staffRoles = await Q("SELECT StaffRoleId AS \"staffRoleId\", RoleName AS \"roleName\", DefaultRate AS \"defaultRate\" FROM StaffRole WHERE CompanyId = @cid ORDER BY Seniority DESC, RoleName"),
                staff = await Q("SELECT StaffId AS \"staffId\", FullName AS \"fullName\", StaffRoleId AS \"staffRoleId\", HourlyCost AS \"hourlyCost\", BillRate AS \"billRate\", TeamGroup AS \"teamGroup\" FROM Staff WHERE CompanyId = @cid AND IsActive ORDER BY FullName"),
                users = await Q("SELECT UserId AS \"userId\", GivenName || ' ' || FamilyName AS \"name\", Initials AS \"initials\", Colour AS \"colour\", LoginEmail::text AS \"email\" FROM AppUser WHERE CompanyId = @cid AND IsActive ORDER BY GivenName"),
                units = await Q("SELECT UomCode AS \"uomCode\", UomName AS \"uomName\" FROM UnitOfMeasure ORDER BY UomCode"),
                csiCodes = await Q("""
                    SELECT w.CodeId AS "codeId", w.CsiCode AS "csiCode", w.CodeTitle AS "codeTitle"
                    FROM WorkCode w JOIN CodeGroup g ON g.GroupId = w.GroupId JOIN CodeEdition e ON e.EditionId = g.EditionId AND e.IsCurrent
                    ORDER BY w.CsiCode
                    """),
                markupTypes = await Q("SELECT MarkupCode AS \"code\", MarkupName AS \"name\", AppliesTo AS \"appliesTo\", SortOrder AS \"sortOrder\", DefaultRatePct * 100 AS \"defaultRatePct\" FROM MarkupType WHERE MarkupCode NOT IN ('precon','genreq') ORDER BY SortOrder"),
                scopeKits = await Q("SELECT k.KitId AS \"kitId\", k.KitName AS \"kitName\", w.CsiCode AS \"csiCode\", (SELECT count(*) FROM ScopeKitLine l WHERE l.KitId = k.KitId) AS \"lines\" FROM ScopeKit k LEFT JOIN WorkCode w ON w.CodeId = k.CodeId WHERE k.CompanyId = @cid AND k.IsActive ORDER BY k.KitName"),
                costKits = await Q("SELECT CostKitId AS \"costKitId\", KitName AS \"kitName\", Area::text AS \"area\", IsDefault AS \"isDefault\", (SELECT count(*) FROM CostKitItem i WHERE i.CostKitId = k.CostKitId) AS \"items\" FROM CostKit k WHERE k.CompanyId = @cid ORDER BY Area, KitName"),
                activities = await Q("SELECT ActivityId AS \"activityId\", Phase::text AS \"phase\", ActivityName AS \"activityName\" FROM Activity WHERE CompanyId = @cid AND ProjectId IS NULL ORDER BY Phase, SortOrder"),
                travelRates = await Q("SELECT TravelRateId AS \"travelRateId\", City AS \"city\", StateCode AS \"stateCode\", LodgingDaily + MealsDaily AS \"daily\" FROM TravelRate ORDER BY City"),
                stages = new[] { "lead", "pursuit", "bidding", "submitted", "awarded", "lost" },
                bidStates = new[] { "invited", "bidding", "posted", "late", "incomplete", "notplansspecs", "undecided", "disqualified", "declined" },
                projectTypes = await Q("SELECT DISTINCT ProjectType AS \"projectType\" FROM Project WHERE CompanyId = @cid AND ProjectType IS NOT NULL ORDER BY 1"),
                company = new { me.CompanyId, name = me.CompanyName, slug = me.CompanySlug },
            };
        });

        api.MapGet("/partners", async (string? search, HttpContext ctx, Db db) =>
        {
            await using var c = await db.Open();
            return await c.QueryAsync("""
                SELECT p.PartnerId AS "partnerId", p.CompanyName AS "companyName", p.IsPrequalified AS "isPrequalified", p.DiverseTags AS "diverseTags",
                       (SELECT pp.Email::text FROM PartnerPerson pp WHERE pp.PartnerId = p.PartnerId ORDER BY pp.IsPrimary DESC, pp.PersonId LIMIT 1) AS "email",
                       (SELECT pp.FullName FROM PartnerPerson pp WHERE pp.PartnerId = p.PartnerId ORDER BY pp.IsPrimary DESC, pp.PersonId LIMIT 1) AS "contact"
                FROM TradePartner p WHERE p.CompanyId = @cid AND (@s IS NULL OR p.CompanyName ILIKE '%' || @s || '%') ORDER BY p.CompanyName LIMIT 50
                """, new { cid = ctx.Me().CompanyId, s = string.IsNullOrWhiteSpace(search) ? null : search.Trim() });
        });
    }

    /// <summary>What the browser keeps about the signed-in person.</summary>
    public static object MeDto(CurrentUser me) => new
    {
        me.UserId, me.Name, me.Initials, me.Colour, me.Email, me.Roles, me.Capabilities,
        company = new { id = me.CompanyId, name = me.CompanyName, slug = me.CompanySlug, plan = me.CompanyPlan, trialEndsOn = me.TrialEndsOn },
    };
}
