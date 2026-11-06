using Dapper;
using Gridline.Infrastructure;

namespace Gridline.Endpoints;

/// <summary>Company administration: the company itself, its people and roles, the trade partner directory,
/// employees and employee roles, the template library and the audit trail. Administrators only.</summary>
public static class AdminEndpoints
{
    public sealed record CompanyPatch(string? DisplayName, string? LegalName, string? HelpEmail, string? TimeZone, string? ProjectNoPrefix, int? NextProjectNo, string? BrandPrimary, string? BrandAccent);
    public sealed record InviteIn(string? Email, string? GivenName, string? FamilyName, string? Role);
    public sealed record UserPatch(string? Role, bool? IsActive, string? GivenName, string? FamilyName, string? JobTitle, string? Colour);
    public sealed record PartnerIn(string? CompanyName, string? Website, bool? IsPrequalified, DateOnly? PrequalExpires, string[]? DiverseTags, string? ContactName, string? ContactEmail, string? ContactPhone, string? City, string? StateCode, string[]? CsiCodes);
    public sealed record StaffIn(string? FullName, string? WorkEmail, string? StaffNo, int? StaffRoleId, decimal? HourlyCost, decimal? BillRate, string? TeamGroup, bool? IsActive);
    public sealed record RoleIn(string? RoleName, string? ShortName, short? Seniority, decimal? DefaultRate);
    public sealed record TemplatePatch(string? Name, bool? IsActive, bool? IsDefault);
    static readonly string[] Roles = ["admin", "manager", "estimator", "viewer"];
    static readonly string[] Groups = ["operations", "siteops", "corporate"];

    public static void Map(RouteGroupBuilder api)
    {
        // ------------------------------------------------------------------ company
        api.MapGet("/admin/company", async (HttpContext ctx, Db db) =>
        {
            var me = ctx.Me();
            await using var c = await db.Open();
            return await Company(c, me.CompanyId);
        });
        api.MapPatch("/admin/company", async (CompanyPatch body, HttpContext ctx, Db db) =>
        {
            var me = ctx.Me(); me.RequireAdmin();
            if (body.DisplayName is not null && body.DisplayName.Trim().Length < 2) throw ApiException.Bad("The company name is too short.");
            if (body.ProjectNoPrefix is { Length: > 8 }) throw ApiException.Bad("The project number prefix can be at most 8 characters.");
            foreach (var col in new[] { body.BrandPrimary, body.BrandAccent })
                if (col is not null && !System.Text.RegularExpressions.Regex.IsMatch(col, "^#[0-9a-fA-F]{6}$")) throw ApiException.Bad("Colours must look like #27624d.");
            await using var c = await db.Open();
            await c.ExecuteAsync("""
                UPDATE Company SET DisplayName = coalesce(nullif(trim(@DisplayName), ''), DisplayName), LegalName = coalesce(@LegalName, LegalName), HelpEmail = coalesce(@HelpEmail::citext, HelpEmail),
                  TimeZone = coalesce(@TimeZone, TimeZone), ProjectNoPrefix = coalesce(upper(trim(@ProjectNoPrefix)), ProjectNoPrefix), NextProjectNo = coalesce(@NextProjectNo, NextProjectNo),
                  BrandPrimary = coalesce(lower(@BrandPrimary), BrandPrimary), BrandAccent = coalesce(lower(@BrandAccent), BrandAccent)
                WHERE CompanyId = @cid
                """, new { body.DisplayName, body.LegalName, body.HelpEmail, body.TimeZone, body.ProjectNoPrefix, body.NextProjectNo, body.BrandPrimary, body.BrandAccent, cid = me.CompanyId });
            await Audit(c, me, "company.update", "company", me.CompanyId, body);
            return await Company(c, me.CompanyId);
        });

        // ------------------------------------------------------------------ people
        api.MapGet("/admin/users", async (HttpContext ctx, Db db) =>
        {
            var me = ctx.Me();
            await using var c = await db.Open();
            return new
            {
                users = await c.QueryAsync("""
                    SELECT u.UserId AS "userId", u.GivenName AS "givenName", u.FamilyName AS "familyName", u.GivenName || ' ' || u.FamilyName AS "name", u.Initials AS "initials", u.Colour AS "colour",
                           u.LoginEmail::text AS "email", u.JobTitle AS "jobTitle", u.IsActive AS "isActive", u.LastSeenAt AS "lastSeenAt", u.CreatedAt AS "createdAt", u.LockedUntil AS "lockedUntil",
                           coalesce((SELECT r.RoleCode FROM UserRole ur JOIN AccessRole r ON r.RoleId = ur.RoleId WHERE ur.UserId = u.UserId ORDER BY r.RoleId LIMIT 1), 'viewer') AS "role",
                           u.PasswordHash IS NOT NULL AS "hasPassword",
                           (SELECT count(*) FROM ProjectMember m WHERE m.UserId = u.UserId) AS "projects"
                    FROM AppUser u WHERE u.CompanyId = @cid ORDER BY u.IsActive DESC, u.GivenName, u.FamilyName
                    """, new { cid = me.CompanyId }),
                invites = await c.QueryAsync("""
                    SELECT i.InviteId AS "inviteId", i.Email::text AS "email", i.GivenName AS "givenName", i.FamilyName AS "familyName", i.RoleCode AS "role", i.CreatedAt AS "createdAt", i.ExpiresAt AS "expiresAt",
                           (SELECT u.GivenName || ' ' || u.FamilyName FROM AppUser u WHERE u.UserId = i.InvitedBy) AS "invitedBy"
                    FROM UserInvite i WHERE i.CompanyId = @cid AND i.AcceptedAt IS NULL AND i.ExpiresAt > now() ORDER BY i.CreatedAt DESC
                    """, new { cid = me.CompanyId }),
                roles = await c.QueryAsync("SELECT RoleCode AS \"code\", RoleName AS \"name\", Summary AS \"summary\" FROM AccessRole ORDER BY RoleId"),
            };
        });

        api.MapPost("/admin/users/invite", async (InviteIn body, HttpContext ctx, Db db, IEmailSender mail, IConfiguration cfg, IHostEnvironment env) =>
        {
            var me = ctx.Me(); me.RequireAdmin();
            var email = body.Email?.Trim().ToLowerInvariant();
            if (email is null || !email.Contains('@') || !email.Contains('.')) throw ApiException.Bad("Enter a valid email address.");
            var role = body.Role ?? "estimator";
            if (!Roles.Contains(role)) throw ApiException.Bad("Role must be admin, manager, estimator or viewer.");
            await using var c = await db.Open();
            var other = await c.ExecuteScalarAsync<long?>("SELECT CompanyId FROM AppUser WHERE LoginEmail = @e::citext", new { e = email });
            if (other is long oc && oc != me.CompanyId) throw new ApiException(409, "That email already belongs to an account in another company.", "duplicate");
            if (other == me.CompanyId && await c.ExecuteScalarAsync<bool>("SELECT IsActive AND PasswordHash IS NOT NULL FROM AppUser WHERE LoginEmail = @e::citext", new { e = email }))
                throw new ApiException(409, "That person already has an account here.", "duplicate");
            var token = Tokens.New();
            await c.ExecuteAsync("UPDATE UserInvite SET ExpiresAt = now() WHERE CompanyId = @cid AND Email = @e::citext AND AcceptedAt IS NULL", new { cid = me.CompanyId, e = email });   // one live invitation per address
            var id = await c.ExecuteScalarAsync<long>("""
                INSERT INTO UserInvite (CompanyId, Email, GivenName, FamilyName, RoleCode, TokenHash, InvitedBy, ExpiresAt)
                VALUES (@cid, @e::citext, @gn, @fn, @role, @h, @by, now() + interval '7 days') RETURNING InviteId
                """, new { cid = me.CompanyId, e = email, gn = body.GivenName?.Trim(), fn = body.FamilyName?.Trim(), role, h = Tokens.Hash(token), by = me.UserId });
            var link = PublicUrl.Of(cfg, ctx.Request, $"/invite/{token}");
            await mail.Send(email, $"{me.Name} invited you to {me.CompanyName} on Gridline",
                $"Hi{(body.GivenName is { Length: > 0 } g ? " " + g.Trim() : "")},\n\n{me.Name} has invited you to join {me.CompanyName} on Gridline, the preconstruction estimating workspace.\n\nAccept the invitation and choose a password here (the link works for 7 days):\n\n{link}\n");
            await Audit(c, me, "user.invite", "invite", id, new { email, role });
            return Results.Ok(new { inviteId = id, email, role, link, emailSent = mail.Configured, showLink = !mail.Configured || env.IsDevelopment() });
        });

        api.MapDelete("/admin/users/invite/{id:long}", async (long id, HttpContext ctx, Db db) =>
        {
            var me = ctx.Me(); me.RequireAdmin();
            await using var c = await db.Open();
            var n = await c.ExecuteAsync("UPDATE UserInvite SET ExpiresAt = now() WHERE InviteId = @id AND CompanyId = @cid AND AcceptedAt IS NULL", new { id, cid = me.CompanyId });
            if (n == 0) throw ApiException.NotFound("Invitation");
            return Results.NoContent();
        });

        api.MapPatch("/admin/users/{id:long}", async (long id, UserPatch body, HttpContext ctx, Db db) =>
        {
            var me = ctx.Me(); me.RequireAdmin();
            await using var c = await db.Open();
            if (!await c.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM AppUser WHERE UserId = @id AND CompanyId = @cid)", new { id, cid = me.CompanyId })) throw ApiException.NotFound("User");
            if (body.Role is not null && !Roles.Contains(body.Role)) throw ApiException.Bad("Role must be admin, manager, estimator or viewer.");
            if (id == me.UserId && (body.IsActive == false || (body.Role is not null && body.Role != "admin"))) throw ApiException.Bad("You can't remove your own administrator access. Ask another administrator.");
            if ((body.IsActive == false || (body.Role is not null && body.Role != "admin")) &&
                await c.ExecuteScalarAsync<int>("SELECT count(*) FROM AppUser u JOIN UserRole ur ON ur.UserId = u.UserId JOIN AccessRole r ON r.RoleId = ur.RoleId AND r.RoleCode = 'admin' WHERE u.CompanyId = @cid AND u.IsActive AND u.UserId <> @id", new { cid = me.CompanyId, id }) == 0)
                throw ApiException.Bad("A company needs at least one active administrator.");
            await c.ExecuteAsync("""
                UPDATE AppUser SET GivenName = coalesce(nullif(trim(@GivenName), ''), GivenName), FamilyName = coalesce(nullif(trim(@FamilyName), ''), FamilyName),
                  JobTitle = CASE WHEN @JobTitle IS NULL THEN JobTitle ELSE nullif(trim(@JobTitle), '') END, Colour = coalesce(@Colour, Colour), IsActive = coalesce(@IsActive, IsActive)
                WHERE UserId = @id
                """, new { body.GivenName, body.FamilyName, body.JobTitle, body.Colour, body.IsActive, id });
            if (body.Role is string role)
            {
                await c.ExecuteAsync("DELETE FROM UserRole WHERE UserId = @id", new { id });
                await c.ExecuteAsync("INSERT INTO UserRole (UserId, RoleId, GrantedBy) SELECT @id, RoleId, @by FROM AccessRole WHERE RoleCode = @role", new { id, role, by = me.UserId });
            }
            if (body.IsActive == false) await c.ExecuteAsync("UPDATE LoginToken SET RevokedAt = now() WHERE UserId = @id AND RevokedAt IS NULL", new { id });
            Sessions.Invalidate(id);   // role or status changes take effect on the next request
            await Audit(c, me, "user.update", "user", id, body);
            return Results.NoContent();
        });

        // ------------------------------------------------------------------ trade partner directory
        api.MapGet("/admin/partners", async (string? search, int? page, HttpContext ctx, Db db) =>
        {
            var me = ctx.Me();
            await using var c = await db.Open();
            var q = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
            const int size = 50; var off = Math.Max(0, (page ?? 1) - 1) * size;
            var rows = await c.QueryAsync("""
                SELECT p.PartnerId AS "partnerId", p.CompanyName AS "companyName", p.Website AS "website", p.IsPrequalified AS "isPrequalified", p.PrequalExpires AS "prequalExpires", p.DiverseTags AS "diverseTags", p.AddedByUser AS "addedByUser",
                       pp.FullName AS "contactName", pp.Email::text AS "contactEmail", pp.Phone AS "contactPhone", br.City AS "city", br.StateCode AS "stateCode",
                       (SELECT count(*) FROM Bid b WHERE b.PartnerId = p.PartnerId AND b.ArchivedAt IS NULL) AS "bids",
                       (SELECT count(*) FROM Bid b WHERE b.PartnerId = p.PartnerId AND b.ArchivedAt IS NULL AND b.IsCarried) AS "picked",
                       (SELECT string_agg(w.CsiCode, ', ' ORDER BY w.CsiCode) FROM PartnerCode pc JOIN WorkCode w ON w.CodeId = pc.CodeId WHERE pc.PartnerId = p.PartnerId) AS "csiCodes"
                FROM TradePartner p
                LEFT JOIN LATERAL (SELECT * FROM PartnerPerson x WHERE x.PartnerId = p.PartnerId ORDER BY x.IsPrimary DESC, x.PersonId LIMIT 1) pp ON true
                LEFT JOIN LATERAL (SELECT * FROM PartnerBranch x WHERE x.PartnerId = p.PartnerId ORDER BY x.IsHeadOffice DESC, x.PartnerBranchId LIMIT 1) br ON true
                WHERE p.CompanyId = @cid AND (@q IS NULL OR p.CompanyName ILIKE '%' || @q || '%' OR pp.FullName ILIKE '%' || @q || '%' OR pp.Email ILIKE '%' || @q || '%')
                ORDER BY p.CompanyName LIMIT @size OFFSET @off
                """, new { cid = me.CompanyId, q, size, off });
            var total = await c.ExecuteScalarAsync<int>("SELECT count(*) FROM TradePartner p WHERE p.CompanyId = @cid AND (@q IS NULL OR p.CompanyName ILIKE '%' || @q || '%')", new { cid = me.CompanyId, q });
            return new { rows, total, page = page ?? 1, size };
        });
        api.MapPost("/admin/partners", async (PartnerIn body, HttpContext ctx, Db db) =>
        {
            var me = ctx.Me(); me.Require("project.edit");
            var name = body.CompanyName?.Trim();
            if (string.IsNullOrWhiteSpace(name)) throw ApiException.Bad("Enter the company name.");
            await using var c = await db.Open();
            await using var t = await c.BeginTransactionAsync();
            if (await c.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM TradePartner WHERE CompanyId = @cid AND lower(CompanyName) = lower(@name))", new { cid = me.CompanyId, name }, t))
                throw new ApiException(409, "A trade partner with that name already exists.", "duplicate");
            var id = await c.ExecuteScalarAsync<long>("INSERT INTO TradePartner (CompanyId, CompanyName, Website, IsPrequalified, PrequalExpires, DiverseTags, AddedByUser) VALUES (@cid, @name, @Website, coalesce(@IsPrequalified, false), @PrequalExpires, coalesce(@DiverseTags, '{}'), false) RETURNING PartnerId",
                new { cid = me.CompanyId, name, body.Website, body.IsPrequalified, body.PrequalExpires, body.DiverseTags }, t);
            await SavePartnerDetails(c, t, id, body);
            await Audit(c, me, "partner.create", "partner", id, new { name }, t);
            await t.CommitAsync();
            return Results.Created($"/api/admin/partners/{id}", new { partnerId = id });
        });
        api.MapPatch("/admin/partners/{id:long}", async (long id, PartnerIn body, HttpContext ctx, Db db) =>
        {
            var me = ctx.Me(); me.Require("project.edit");
            await using var c = await db.Open();
            await using var t = await c.BeginTransactionAsync();
            if (!await c.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM TradePartner WHERE PartnerId = @id AND CompanyId = @cid)", new { id, cid = me.CompanyId }, t)) throw ApiException.NotFound("Trade partner");
            if (body.CompanyName is not null && string.IsNullOrWhiteSpace(body.CompanyName)) throw ApiException.Bad("The company name can't be empty.");
            await c.ExecuteAsync("""
                UPDATE TradePartner SET CompanyName = coalesce(trim(@CompanyName), CompanyName), Website = coalesce(@Website, Website), IsPrequalified = coalesce(@IsPrequalified, IsPrequalified),
                  PrequalExpires = coalesce(@PrequalExpires, PrequalExpires), DiverseTags = coalesce(@DiverseTags, DiverseTags) WHERE PartnerId = @id
                """, new { body.CompanyName, body.Website, body.IsPrequalified, body.PrequalExpires, body.DiverseTags, id }, t);
            if (body.CompanyName is string n) await c.ExecuteAsync("UPDATE Bid SET BidderName = @n WHERE PartnerId = @id AND ArchivedAt IS NULL", new { n = n.Trim(), id }, t);
            await SavePartnerDetails(c, t, id, body);
            await Audit(c, me, "partner.update", "partner", id, body, t);
            await t.CommitAsync();
            return Results.NoContent();
        });
        api.MapDelete("/admin/partners/{id:long}", async (long id, HttpContext ctx, Db db) =>
        {
            var me = ctx.Me(); me.RequireAdmin();
            await using var c = await db.Open();
            if (await c.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM Bid WHERE PartnerId = @id AND ArchivedAt IS NULL)", new { id }))
                throw new ApiException(409, "This trade partner has bids on projects. Remove those bids first, or keep the partner.", "inuse");
            var n = await c.ExecuteAsync("DELETE FROM TradePartner WHERE PartnerId = @id AND CompanyId = @cid", new { id, cid = me.CompanyId });
            if (n == 0) throw ApiException.NotFound("Trade partner");
            await Audit(c, me, "partner.delete", "partner", id);
            return Results.NoContent();
        });

        // ------------------------------------------------------------------ employees and employee roles
        api.MapGet("/admin/staff", async (HttpContext ctx, Db db) =>
        {
            var me = ctx.Me();
            await using var c = await db.Open();
            return new
            {
                staff = await c.QueryAsync("""
                    SELECT s.StaffId AS "staffId", s.FullName AS "fullName", s.WorkEmail::text AS "workEmail", s.StaffNo AS "staffNo", s.StaffRoleId AS "staffRoleId", r.RoleName AS "roleName",
                           s.HourlyCost AS "hourlyCost", s.BillRate AS "billRate", s.TeamGroup AS "teamGroup", s.IsActive AS "isActive",
                           (SELECT count(DISTINCT m.ProjectId) FROM TeamMember m WHERE m.StaffId = s.StaffId) AS "projects"
                    FROM Staff s LEFT JOIN StaffRole r ON r.StaffRoleId = s.StaffRoleId WHERE s.CompanyId = @cid ORDER BY s.IsActive DESC, s.FullName
                    """, new { cid = me.CompanyId }),
                roles = await c.QueryAsync("""
                    SELECT r.StaffRoleId AS "staffRoleId", r.RoleName AS "roleName", r.ShortName AS "shortName", r.Seniority AS "seniority", r.DefaultRate AS "defaultRate",
                           (SELECT count(*) FROM Staff s WHERE s.StaffRoleId = r.StaffRoleId AND s.IsActive) AS "people"
                    FROM StaffRole r WHERE r.CompanyId = @cid ORDER BY r.Seniority DESC NULLS LAST, r.RoleName
                    """, new { cid = me.CompanyId }),
            };
        });
        api.MapPost("/admin/staff", async (StaffIn body, HttpContext ctx, Db db) =>
        {
            var me = ctx.Me(); me.RequireAdmin();
            if (string.IsNullOrWhiteSpace(body.FullName)) throw ApiException.Bad("Enter the person's name.");
            if (body.TeamGroup is not null && !Groups.Contains(body.TeamGroup)) throw ApiException.Bad("Team must be operations, siteops or corporate.");
            await using var c = await db.Open();
            if (body.StaffRoleId is int rid && !await c.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM StaffRole WHERE StaffRoleId = @rid AND CompanyId = @cid)", new { rid, cid = me.CompanyId })) throw ApiException.Bad("Unknown employee role.");
            var id = await c.ExecuteScalarAsync<long>("""
                INSERT INTO Staff (CompanyId, FullName, WorkEmail, StaffNo, StaffRoleId, HourlyCost, BillRate, TeamGroup, IsActive)
                VALUES (@cid, trim(@FullName), nullif(trim(@WorkEmail), '')::citext, nullif(trim(@StaffNo), ''), @StaffRoleId, @HourlyCost, @BillRate, coalesce(@TeamGroup, 'operations'), coalesce(@IsActive, true)) RETURNING StaffId
                """, new { cid = me.CompanyId, body.FullName, body.WorkEmail, body.StaffNo, body.StaffRoleId, body.HourlyCost, body.BillRate, body.TeamGroup, body.IsActive });
            await Audit(c, me, "staff.create", "staff", id, new { body.FullName });
            return Results.Created($"/api/admin/staff/{id}", new { staffId = id });
        });
        api.MapPatch("/admin/staff/{id:long}", async (long id, StaffIn body, HttpContext ctx, Db db) =>
        {
            var me = ctx.Me(); me.RequireAdmin();
            if (body.TeamGroup is not null && !Groups.Contains(body.TeamGroup)) throw ApiException.Bad("Team must be operations, siteops or corporate.");
            await using var c = await db.Open();
            if (body.StaffRoleId is int rid && !await c.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM StaffRole WHERE StaffRoleId = @rid AND CompanyId = @cid)", new { rid, cid = me.CompanyId })) throw ApiException.Bad("Unknown employee role.");
            var n = await c.ExecuteAsync("""
                UPDATE Staff SET FullName = coalesce(nullif(trim(@FullName), ''), FullName), WorkEmail = CASE WHEN @WorkEmail IS NULL THEN WorkEmail ELSE nullif(trim(@WorkEmail), '')::citext END,
                  StaffNo = CASE WHEN @StaffNo IS NULL THEN StaffNo ELSE nullif(trim(@StaffNo), '') END, StaffRoleId = coalesce(@StaffRoleId, StaffRoleId),
                  HourlyCost = coalesce(@HourlyCost, HourlyCost), BillRate = coalesce(@BillRate, BillRate), TeamGroup = coalesce(@TeamGroup, TeamGroup), IsActive = coalesce(@IsActive, IsActive)
                WHERE StaffId = @id AND CompanyId = @cid
                """, new { body.FullName, body.WorkEmail, body.StaffNo, body.StaffRoleId, body.HourlyCost, body.BillRate, body.TeamGroup, body.IsActive, id, cid = me.CompanyId });
            if (n == 0) throw ApiException.NotFound("Staff member");
            await Audit(c, me, "staff.update", "staff", id, body);
            return Results.NoContent();
        });
        api.MapPost("/admin/staff-roles", async (RoleIn body, HttpContext ctx, Db db) =>
        {
            var me = ctx.Me(); me.RequireAdmin();
            if (string.IsNullOrWhiteSpace(body.RoleName)) throw ApiException.Bad("Enter the role name.");
            await using var c = await db.Open();
            var id = await c.ExecuteScalarAsync<int>("INSERT INTO StaffRole (CompanyId, RoleName, ShortName, Seniority, DefaultRate) VALUES (@cid, trim(@RoleName), @ShortName, @Seniority, @DefaultRate) RETURNING StaffRoleId",
                new { cid = me.CompanyId, body.RoleName, body.ShortName, body.Seniority, body.DefaultRate });
            await Audit(c, me, "staffrole.create", "staffrole", id, body);
            return Results.Created($"/api/admin/staff-roles/{id}", new { staffRoleId = id });
        });
        api.MapPatch("/admin/staff-roles/{id:int}", async (int id, RoleIn body, HttpContext ctx, Db db) =>
        {
            var me = ctx.Me(); me.RequireAdmin();
            await using var c = await db.Open();
            var n = await c.ExecuteAsync("UPDATE StaffRole SET RoleName = coalesce(nullif(trim(@RoleName), ''), RoleName), ShortName = coalesce(@ShortName, ShortName), Seniority = coalesce(@Seniority, Seniority), DefaultRate = coalesce(@DefaultRate, DefaultRate) WHERE StaffRoleId = @id AND CompanyId = @cid",
                new { body.RoleName, body.ShortName, body.Seniority, body.DefaultRate, id, cid = me.CompanyId });
            if (n == 0) throw ApiException.NotFound("Employee role");
            await Audit(c, me, "staffrole.update", "staffrole", id, body);
            return Results.NoContent();
        });
        api.MapDelete("/admin/staff-roles/{id:int}", async (int id, HttpContext ctx, Db db) =>
        {
            var me = ctx.Me(); me.RequireAdmin();
            await using var c = await db.Open();
            if (await c.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM Staff WHERE StaffRoleId = @id) OR EXISTS (SELECT 1 FROM TeamMember WHERE StaffRoleId = @id) OR EXISTS (SELECT 1 FROM PreconMember WHERE StaffRoleId = @id)", new { id }))
                throw new ApiException(409, "People or crew plans still use this role.", "inuse");
            var n = await c.ExecuteAsync("DELETE FROM StaffRole WHERE StaffRoleId = @id AND CompanyId = @cid", new { id, cid = me.CompanyId });
            if (n == 0) throw ApiException.NotFound("Employee role");
            return Results.NoContent();
        });

        // ------------------------------------------------------------------ template library
        api.MapGet("/admin/templates", async (HttpContext ctx, Db db) =>
        {
            var me = ctx.Me();
            await using var c = await db.Open();
            var p = new { cid = me.CompanyId };
            return new
            {
                scopeKits = await c.QueryAsync("SELECT k.KitId AS \"id\", k.KitName AS \"name\", k.IsActive AS \"isActive\", w.CsiCode AS \"csiCode\", (SELECT count(*) FROM ScopeKitLine l WHERE l.KitId = k.KitId) AS \"lines\", (SELECT count(*) FROM Trade t WHERE t.KitId = k.KitId) AS \"used\" FROM ScopeKit k LEFT JOIN WorkCode w ON w.CodeId = k.CodeId WHERE k.CompanyId = @cid ORDER BY k.KitName", p),
                requirementSets = await c.QueryAsync("SELECT s.RequirementSetId AS \"id\", s.SetName AS \"name\", s.IsTemplate AS \"isTemplate\", (SELECT count(*) FROM RequirementLine l WHERE l.RequirementSetId = s.RequirementSetId) AS \"lines\", (SELECT count(*) FROM ProjectVersion v WHERE v.RequirementSetId = s.RequirementSetId) AS \"used\" FROM RequirementSet s WHERE s.CompanyId = @cid AND s.IsTemplate ORDER BY s.SetName", p),
                costKits = await c.QueryAsync("SELECT k.CostKitId AS \"id\", k.KitName AS \"name\", k.Area::text AS \"area\", k.IsDefault AS \"isDefault\", (SELECT count(*) FROM CostKitItem i WHERE i.CostKitId = k.CostKitId) AS \"lines\" FROM CostKit k WHERE k.CompanyId = @cid ORDER BY k.Area, k.KitName", p),
                qualificationSets = await c.QueryAsync("SELECT s.QualificationSetId AS \"id\", s.SetName AS \"name\", s.IsActive AS \"isActive\", (SELECT count(*) FROM QualificationSetLine l WHERE l.QualificationSetId = s.QualificationSetId) AS \"lines\" FROM QualificationSet s WHERE s.CompanyId = @cid ORDER BY s.SetName", p),
                preconTasks = await c.QueryAsync("SELECT a.ActivityId AS \"id\", a.ActivityName AS \"name\", a.IsDefault AS \"isDefault\", a.IsActive AS \"isActive\", a.SortOrder AS \"sortOrder\" FROM Activity a WHERE a.CompanyId = @cid AND a.ProjectId IS NULL AND a.Phase = 'precon' ORDER BY a.SortOrder, a.ActivityId", p),
            };
        });
        api.MapGet("/admin/templates/{kind}/{id:long}/lines", async (string kind, long id, HttpContext ctx, Db db) =>
        {
            var me = ctx.Me();
            await using var c = await db.Open();
            var p = new { id, cid = me.CompanyId };
            return kind switch
            {
                "scope-kit" => await c.QueryAsync("SELECT l.KitLineId AS \"lineId\", l.SortKey AS \"sortKey\", l.IsHeading AS \"isHeading\", l.LineText AS \"text\", l.Desired::text AS \"desired\" FROM ScopeKitLine l JOIN ScopeKit k ON k.KitId = l.KitId WHERE l.KitId = @id AND k.CompanyId = @cid ORDER BY l.SortKey, l.KitLineId", p),
                "requirement-set" => await c.QueryAsync("SELECT l.RequirementLineId AS \"lineId\", l.SortKey AS \"sortKey\", l.SectionName AS \"section\", l.IsHeading AS \"isHeading\", l.LineText AS \"text\", l.Desired::text AS \"desired\" FROM RequirementLine l JOIN RequirementSet s ON s.RequirementSetId = l.RequirementSetId WHERE l.RequirementSetId = @id AND s.CompanyId = @cid ORDER BY l.SortKey, l.RequirementLineId", p),
                "cost-kit" => await c.QueryAsync("SELECT i.CostKitItemId AS \"lineId\", i.SortOrder AS \"sortKey\", i.CategoryName AS \"section\", i.ItemName AS \"text\", i.DefaultQty AS \"qty\", i.UomCode AS \"uom\", i.DefaultRate AS \"rate\" FROM CostKitItem i JOIN CostKit k ON k.CostKitId = i.CostKitId WHERE i.CostKitId = @id AND k.CompanyId = @cid ORDER BY i.SortOrder, i.CostKitItemId", p),
                "qualification-set" => await c.QueryAsync("SELECT l.QualificationSetLineId AS \"lineId\", l.SortOrder AS \"sortKey\", l.Kind AS \"section\", l.QualText AS \"text\" FROM QualificationSetLine l JOIN QualificationSet s ON s.QualificationSetId = l.QualificationSetId WHERE l.QualificationSetId = @id AND s.CompanyId = @cid ORDER BY l.SortOrder, l.QualificationSetLineId", p),
                _ => throw ApiException.NotFound("Template kind"),
            };
        });
        api.MapPatch("/admin/templates/{kind}/{id:long}", async (string kind, long id, TemplatePatch body, HttpContext ctx, Db db) =>
        {
            var me = ctx.Me(); me.RequireAdmin();
            await using var c = await db.Open();
            var p = new { id, cid = me.CompanyId, name = body.Name?.Trim(), body.IsActive, body.IsDefault };
            var n = kind switch
            {
                "scope-kit" => await c.ExecuteAsync("UPDATE ScopeKit SET KitName = coalesce(nullif(@name, ''), KitName), IsActive = coalesce(@IsActive, IsActive) WHERE KitId = @id AND CompanyId = @cid", p),
                "requirement-set" => await c.ExecuteAsync("UPDATE RequirementSet SET SetName = coalesce(nullif(@name, ''), SetName) WHERE RequirementSetId = @id AND CompanyId = @cid", p),
                "cost-kit" => await c.ExecuteAsync("UPDATE CostKit SET KitName = coalesce(nullif(@name, ''), KitName), IsDefault = coalesce(@IsDefault, IsDefault) WHERE CostKitId = @id AND CompanyId = @cid", p),
                "qualification-set" => await c.ExecuteAsync("UPDATE QualificationSet SET SetName = coalesce(nullif(@name, ''), SetName), IsActive = coalesce(@IsActive, IsActive) WHERE QualificationSetId = @id AND CompanyId = @cid", p),
                "precon-task" => await c.ExecuteAsync("UPDATE Activity SET ActivityName = coalesce(nullif(@name, ''), ActivityName), IsActive = coalesce(@IsActive, IsActive), IsDefault = coalesce(@IsDefault, IsDefault) WHERE ActivityId = @id AND CompanyId = @cid AND ProjectId IS NULL", p),
                _ => throw ApiException.NotFound("Template kind"),
            };
            if (n == 0) throw ApiException.NotFound("Template");
            await Audit(c, me, "template.update", kind, id, body);
            return Results.NoContent();
        });

        // ------------------------------------------------------------------ data export (portability: everything the company owns, as JSON)
        api.MapGet("/admin/export.json", async (HttpContext ctx, Db db) =>
        {
            var me = ctx.Me(); me.RequireAdmin();
            await using var c = await db.Open();
            var p = new { cid = me.CompanyId };
            var export = new
            {
                exportedAt = DateTime.UtcNow, exportedBy = me.Email, format = "gridline-company-export/1",
                company = await Company(c, me.CompanyId),
                users = await c.QueryAsync("SELECT UserId AS \"userId\", GivenName AS \"givenName\", FamilyName AS \"familyName\", LoginEmail::text AS \"email\", JobTitle AS \"jobTitle\", IsActive AS \"isActive\", CreatedAt AS \"createdAt\", (SELECT r.RoleCode FROM UserRole ur JOIN AccessRole r ON r.RoleId = ur.RoleId WHERE ur.UserId = u.UserId LIMIT 1) AS \"role\" FROM AppUser u WHERE CompanyId = @cid ORDER BY UserId", p),
                staffRoles = await c.QueryAsync("SELECT StaffRoleId AS \"staffRoleId\", RoleName AS \"roleName\", ShortName AS \"shortName\", Seniority AS \"seniority\", DefaultRate AS \"defaultRate\" FROM StaffRole WHERE CompanyId = @cid ORDER BY StaffRoleId", p),
                staff = await c.QueryAsync("SELECT StaffId AS \"staffId\", FullName AS \"fullName\", WorkEmail::text AS \"workEmail\", StaffNo AS \"staffNo\", StaffRoleId AS \"staffRoleId\", HourlyCost AS \"hourlyCost\", BillRate AS \"billRate\", TeamGroup AS \"teamGroup\", IsActive AS \"isActive\" FROM Staff WHERE CompanyId = @cid ORDER BY StaffId", p),
                tradePartners = await c.QueryAsync("""
                    SELECT p.PartnerId AS "partnerId", p.CompanyName AS "companyName", p.Website AS "website", p.IsPrequalified AS "isPrequalified", p.PrequalExpires AS "prequalExpires", p.DiverseTags AS "diverseTags",
                           (SELECT json_agg(json_build_object('name', x.FullName, 'email', x.Email, 'phone', x.Phone, 'isPrimary', x.IsPrimary)) FROM PartnerPerson x WHERE x.PartnerId = p.PartnerId) AS "contacts",
                           (SELECT json_agg(json_build_object('city', b.City, 'state', b.StateCode, 'street', b.Street, 'postalCode', b.PostalCode)) FROM PartnerBranch b WHERE b.PartnerId = p.PartnerId) AS "offices",
                           (SELECT array_agg(w.CsiCode ORDER BY w.CsiCode) FROM PartnerCode pc JOIN WorkCode w ON w.CodeId = pc.CodeId WHERE pc.PartnerId = p.PartnerId) AS "csiCodes"
                    FROM TradePartner p WHERE p.CompanyId = @cid ORDER BY p.CompanyName
                    """, p),
                projects = await c.QueryAsync("""
                    SELECT p.ProjectId AS "projectId", p.ProjectNo AS "projectNo", p.ProjectName AS "projectName", p.Stage::text AS "stage", p.ClientName AS "clientName", p.ProjectType AS "projectType", p.ContractType AS "contractType",
                           p.City AS "city", p.StateCode AS "stateCode", p.BidDueOn AS "bidDueOn", p.CreatedAt AS "createdAt", p.ArchivedAt AS "archivedAt",
                           (SELECT json_agg(json_build_object('versionNo', v.VersionNo, 'label', v.VersionLabel, 'isPrimary', v.IsPrimary, 'status', v.Status, 'grossArea', v.GrossArea, 'feePct', v.FeePct,
                                'rollUp', (SELECT json_agg(json_build_object('line', r.LineName, 'kind', r.Kind, 'ratePct', r.RatePct, 'amount', r.Amount) ORDER BY r.SortOrder) FROM RollupLine r WHERE r.VersionId = v.VersionId),
                                'trades', (SELECT json_agg(json_build_object('csiCode', t.CsiCode, 'tradeName', t.TradeName, 'budget', t.BudgetValue, 'lowest', t.LowestValue, 'carried', t.CarriedValue, 'plug', t.PlugAmount, 'adjustment', t.Adjustment,
                                     'bids', (SELECT json_agg(json_build_object('bidder', b.BidderName, 'state', b.State, 'base', b.BaseAmount, 'scopeAdjustments', b.ScopeAdjustments, 'total', b.TotalAmount, 'picked', b.IsCarried) ORDER BY b.LaneNo) FROM Bid b WHERE b.TradeId = t.TradeId AND b.ArchivedAt IS NULL)) ORDER BY t.CsiCode)
                                     FROM Trade t WHERE t.VersionId = v.VersionId AND t.ArchivedAt IS NULL)) ORDER BY v.VersionNo) FROM ProjectVersion v WHERE v.ProjectId = p.ProjectId) AS "versions"
                    FROM Project p WHERE p.CompanyId = @cid ORDER BY p.ProjectNo
                    """, p),
                templates = new
                {
                    scopeKits = await c.QueryAsync("SELECT k.KitName AS \"name\", k.IsActive AS \"isActive\", (SELECT json_agg(json_build_object('text', l.LineText, 'isHeading', l.IsHeading, 'desired', l.Desired) ORDER BY l.SortKey) FROM ScopeKitLine l WHERE l.KitId = k.KitId) AS \"lines\" FROM ScopeKit k WHERE k.CompanyId = @cid ORDER BY k.KitName", p),
                    requirementSets = await c.QueryAsync("SELECT s.SetName AS \"name\", (SELECT json_agg(json_build_object('section', l.SectionName, 'text', l.LineText, 'isHeading', l.IsHeading, 'desired', l.Desired) ORDER BY l.SortKey) FROM RequirementLine l WHERE l.RequirementSetId = s.RequirementSetId) AS \"lines\" FROM RequirementSet s WHERE s.CompanyId = @cid AND s.IsTemplate ORDER BY s.SetName", p),
                    costKits = await c.QueryAsync("SELECT k.KitName AS \"name\", k.Area::text AS \"area\", k.IsDefault AS \"isDefault\", (SELECT json_agg(json_build_object('category', i.CategoryName, 'item', i.ItemName, 'qty', i.DefaultQty, 'uom', i.UomCode, 'rate', i.DefaultRate) ORDER BY i.SortOrder) FROM CostKitItem i WHERE i.CostKitId = k.CostKitId) AS \"lines\" FROM CostKit k WHERE k.CompanyId = @cid ORDER BY k.KitName", p),
                    qualificationSets = await c.QueryAsync("SELECT s.SetName AS \"name\", (SELECT json_agg(json_build_object('kind', l.Kind, 'text', l.QualText) ORDER BY l.SortOrder) FROM QualificationSetLine l WHERE l.QualificationSetId = s.QualificationSetId) AS \"lines\" FROM QualificationSet s WHERE s.CompanyId = @cid ORDER BY s.SetName", p),
                },
            };
            await Audit(c, me, "company.export", "company", me.CompanyId);
            ctx.Response.Headers.ContentDisposition = $"attachment; filename=\"{me.CompanySlug}-export-{DateTime.UtcNow:yyyy-MM-dd}.json\"";
            return Results.Json(export, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true });
        });

        // ------------------------------------------------------------------ audit trail (company-wide)
        api.MapGet("/admin/audit", async (long? before, string? verb, HttpContext ctx, Db db) =>
        {
            var me = ctx.Me(); me.RequireAdmin();
            await using var c = await db.Open();
            return await c.QueryAsync("""
                SELECT e.EventId AS "eventId", e.HappenedAt AS "happenedAt", e.Verb AS "verb", e.SubjectType AS "subjectType", e.SubjectKey AS "subjectKey", e.ProjectId AS "projectId",
                       p.ProjectNo AS "projectNo", p.ProjectName AS "projectName", u.GivenName || ' ' || u.FamilyName AS "personName", u.Initials AS "initials", u.Colour AS "colour"
                FROM ActivityEvent e LEFT JOIN AppUser u ON u.UserId = e.UserId LEFT JOIN Project p ON p.ProjectId = e.ProjectId
                WHERE (p.CompanyId = @cid OR (p.ProjectId IS NULL AND u.CompanyId = @cid)) AND (@before IS NULL OR e.EventId < @before) AND (@verb IS NULL OR e.Verb LIKE @verb || '%')
                ORDER BY e.EventId DESC LIMIT 100
                """, new { cid = me.CompanyId, before, verb });
        });
    }

    static Task<dynamic> Company(Npgsql.NpgsqlConnection c, long cid) => c.QueryFirstAsync("""
        SELECT CompanyId AS "companyId", Slug AS "slug", DisplayName AS "displayName", LegalName AS "legalName", TimeZone AS "timeZone", Currency AS "currency",
               BrandPrimary AS "brandPrimary", BrandAccent AS "brandAccent", HelpEmail::text AS "helpEmail", ProjectNoPrefix AS "projectNoPrefix", NextProjectNo AS "nextProjectNo",
               Plan AS "plan", Status AS "status", TrialEndsOn AS "trialEndsOn", CreatedAt AS "createdAt",
               (SELECT count(*) FROM AppUser u WHERE u.CompanyId = co.CompanyId AND u.IsActive) AS "users",
               (SELECT count(*) FROM Project p WHERE p.CompanyId = co.CompanyId AND p.ArchivedAt IS NULL) AS "projects",
               (SELECT count(*) FROM TradePartner p WHERE p.CompanyId = co.CompanyId) AS "partners"
        FROM Company co WHERE CompanyId = @cid
        """, new { cid });

    static async Task SavePartnerDetails(Npgsql.NpgsqlConnection c, Npgsql.NpgsqlTransaction t, long id, PartnerIn body)
    {
        if (body.ContactName is not null || body.ContactEmail is not null || body.ContactPhone is not null)
        {
            var pid = await c.ExecuteScalarAsync<long?>("SELECT PersonId FROM PartnerPerson WHERE PartnerId = @id ORDER BY IsPrimary DESC, PersonId LIMIT 1", new { id }, t);
            if (pid is null && (body.ContactName is { Length: > 0 } || body.ContactEmail is { Length: > 0 }))
                await c.ExecuteAsync("INSERT INTO PartnerPerson (PartnerId, FullName, Email, Phone, IsPrimary) VALUES (@id, coalesce(nullif(trim(@n), ''), @e), nullif(trim(@e), '')::citext, @p, true)", new { id, n = body.ContactName, e = body.ContactEmail, p = body.ContactPhone }, t);
            else if (pid is long x)
                await c.ExecuteAsync("UPDATE PartnerPerson SET FullName = coalesce(nullif(trim(@n), ''), FullName), Email = CASE WHEN @e IS NULL THEN Email ELSE nullif(trim(@e), '')::citext END, Phone = coalesce(@p, Phone) WHERE PersonId = @x", new { x, n = body.ContactName, e = body.ContactEmail, p = body.ContactPhone }, t);
        }
        if (body.City is not null || body.StateCode is not null)
        {
            var bid = await c.ExecuteScalarAsync<long?>("SELECT PartnerBranchId FROM PartnerBranch WHERE PartnerId = @id ORDER BY IsHeadOffice DESC, PartnerBranchId LIMIT 1", new { id }, t);
            if (bid is null) await c.ExecuteAsync("INSERT INTO PartnerBranch (PartnerId, City, StateCode, IsHeadOffice) VALUES (@id, @c, upper(@s), true)", new { id, c = body.City, s = body.StateCode }, t);
            else await c.ExecuteAsync("UPDATE PartnerBranch SET City = coalesce(@c, City), StateCode = coalesce(upper(@s), StateCode) WHERE PartnerBranchId = @b", new { b = bid, c = body.City, s = body.StateCode }, t);
        }
        if (body.CsiCodes is not null)
        {
            await c.ExecuteAsync("DELETE FROM PartnerCode WHERE PartnerId = @id", new { id }, t);
            await c.ExecuteAsync("INSERT INTO PartnerCode (PartnerId, CodeId) SELECT DISTINCT @id, w.CodeId FROM WorkCode w JOIN CodeGroup g ON g.GroupId = w.GroupId JOIN CodeEdition e ON e.EditionId = g.EditionId AND e.IsCurrent WHERE w.CsiCode = ANY(@codes) ON CONFLICT DO NOTHING", new { id, codes = body.CsiCodes }, t);
        }
    }

    static Task Audit(Npgsql.NpgsqlConnection c, CurrentUser me, string verb, string subjectType, object key, object? after = null, Npgsql.NpgsqlTransaction? t = null) =>
        c.ExecuteAsync("INSERT INTO ActivityEvent (UserId, Verb, SubjectType, SubjectKey, AfterState) VALUES (@u, @verb, @st, @sk, @a::jsonb)",
            new { u = me.UserId, verb, st = subjectType, sk = key.ToString(), a = after is null ? null : Jsonb.Text(after) }, t);
}
