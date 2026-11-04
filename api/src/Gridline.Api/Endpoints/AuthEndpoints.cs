using Dapper;
using Gridline.Infrastructure;

namespace Gridline.Endpoints;

public static class AuthEndpoints
{
    public sealed record LoginIn(string Email, string Password);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/auth/login", async (LoginIn body, Db db) =>
        {
            await using var c = await db.Open();
            var row = await c.QueryFirstOrDefaultAsync<(long id, string? hash)>(
                "SELECT UserId, PasswordHash FROM AppUser WHERE LoginEmail = @e::citext AND IsActive", new { e = body.Email?.Trim() });
            if (row.id == 0 || !Passwords.Verify(body.Password ?? "", row.hash))
                throw new ApiException(401, "Email or password is not right.", "signin");
            var token = Tokens.New();
            await c.ExecuteAsync("INSERT INTO LoginToken (UserId, TokenHash, ExpiresAt) VALUES (@u, @h, now() + interval '12 hours')", new { u = row.id, h = Tokens.Hash(token) });
            await c.ExecuteAsync("UPDATE AppUser SET LastSeenAt = now() WHERE UserId = @u", new { u = row.id });
            var me = await TokenAuthHandler.Load(c, "u.UserId = @id", new { id = row.id });
            return Results.Ok(new { accessToken = token, me });
        }).AllowAnonymous();

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

        api.MapGet("/me", (HttpContext ctx) => ctx.Me());

        api.MapGet("/lookups", async (Db db) =>
        {
            await using var c = await db.Open();
            async Task<List<dynamic>> Q(string sql) => (await c.QueryAsync(sql)).ToList();
            return new
            {
                states = await Q("SELECT StateCode AS \"stateCode\", StateName AS \"stateName\", SalesTaxRate AS \"salesTaxRate\" FROM UsState ORDER BY StateName"),
                divisions = await Q("SELECT DivisionId AS \"divisionId\", ShortCode AS \"shortCode\", DivisionName AS \"divisionName\" FROM Division ORDER BY DivisionName"),
                branches = await Q("SELECT BranchId AS \"branchId\", BranchName AS \"branchName\" FROM Branch ORDER BY BranchName"),
                staffRoles = await Q("SELECT StaffRoleId AS \"staffRoleId\", RoleName AS \"roleName\", DefaultRate AS \"defaultRate\" FROM StaffRole ORDER BY Seniority DESC, RoleName"),
                staff = await Q("SELECT StaffId AS \"staffId\", FullName AS \"fullName\", StaffRoleId AS \"staffRoleId\", HourlyCost AS \"hourlyCost\", BillRate AS \"billRate\", TeamGroup AS \"teamGroup\" FROM Staff WHERE IsActive ORDER BY FullName"),
                users = await Q("SELECT UserId AS \"userId\", GivenName || ' ' || FamilyName AS \"name\", Initials AS \"initials\", Colour AS \"colour\", LoginEmail::text AS \"email\" FROM AppUser WHERE IsActive ORDER BY GivenName"),
                units = await Q("SELECT UomCode AS \"uomCode\", UomName AS \"uomName\" FROM UnitOfMeasure ORDER BY UomCode"),
                csiCodes = await Q("""
                    SELECT w.CodeId AS "codeId", w.CsiCode AS "csiCode", w.CodeTitle AS "codeTitle"
                    FROM WorkCode w JOIN CodeGroup g ON g.GroupId = w.GroupId JOIN CodeEdition e ON e.EditionId = g.EditionId AND e.IsCurrent
                    ORDER BY w.CsiCode
                    """),
                markupTypes = await Q("SELECT MarkupCode AS \"code\", MarkupName AS \"name\", AppliesTo AS \"appliesTo\", SortOrder AS \"sortOrder\", DefaultRatePct * 100 AS \"defaultRatePct\" FROM MarkupType WHERE MarkupCode NOT IN ('precon','genreq') ORDER BY SortOrder"),
                scopeKits = await Q("SELECT k.KitId AS \"kitId\", k.KitName AS \"kitName\", w.CsiCode AS \"csiCode\", (SELECT count(*) FROM ScopeKitLine l WHERE l.KitId = k.KitId) AS \"lines\" FROM ScopeKit k LEFT JOIN WorkCode w ON w.CodeId = k.CodeId WHERE k.IsActive ORDER BY k.KitName"),
                costKits = await Q("SELECT CostKitId AS \"costKitId\", KitName AS \"kitName\", Area::text AS \"area\", IsDefault AS \"isDefault\", (SELECT count(*) FROM CostKitItem i WHERE i.CostKitId = k.CostKitId) AS \"items\" FROM CostKit k ORDER BY Area, KitName"),
                activities = await Q("SELECT ActivityId AS \"activityId\", Phase::text AS \"phase\", ActivityName AS \"activityName\" FROM Activity ORDER BY Phase, SortOrder"),
                travelRates = await Q("SELECT TravelRateId AS \"travelRateId\", City AS \"city\", StateCode AS \"stateCode\", LodgingDaily + MealsDaily AS \"daily\" FROM TravelRate ORDER BY City"),
                stages = new[] { "lead", "pursuit", "bidding", "submitted", "awarded", "lost" },
                bidStates = new[] { "invited", "bidding", "posted", "late", "incomplete", "notplansspecs", "undecided", "disqualified", "declined" },
                projectTypes = await Q("SELECT DISTINCT ProjectType AS \"projectType\" FROM Project WHERE ProjectType IS NOT NULL ORDER BY 1"),
            };
        });

        api.MapGet("/partners", async (string? search, Db db) =>
        {
            await using var c = await db.Open();
            return await c.QueryAsync("""
                SELECT p.PartnerId AS "partnerId", p.CompanyName AS "companyName", p.IsPrequalified AS "isPrequalified", p.DiverseTags AS "diverseTags",
                       (SELECT pp.Email::text FROM PartnerPerson pp WHERE pp.PartnerId = p.PartnerId ORDER BY pp.IsPrimary DESC, pp.PersonId LIMIT 1) AS "email",
                       (SELECT pp.FullName FROM PartnerPerson pp WHERE pp.PartnerId = p.PartnerId ORDER BY pp.IsPrimary DESC, pp.PersonId LIMIT 1) AS "contact"
                FROM TradePartner p WHERE @s IS NULL OR p.CompanyName ILIKE '%' || @s || '%' ORDER BY p.CompanyName LIMIT 50
                """, new { s = string.IsNullOrWhiteSpace(search) ? null : search.Trim() });
        });
    }
}
