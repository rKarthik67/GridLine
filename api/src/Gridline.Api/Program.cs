using System.Text.Json.Serialization;
using Dapper;
using Gridline.Docs;
using Gridline.Endpoints;
using Gridline.Export;
using Gridline.Infrastructure;
using Gridline.Live;
using Gridline.Proposal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.private.json", optional: true, reloadOnChange: true);
var cs = builder.Configuration.GetConnectionString("Gridline")
         ?? throw new InvalidOperationException("Set ConnectionStrings:Gridline (appsettings.Development.private.json, or the ConnectionStrings__Gridline environment variable when hosted).");

Db.Init();
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
builder.Services.AddSingleton(Db.Build(cs));
builder.Services.AddSingleton<Db>();
builder.Services.AddSingleton<Storage>();
builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddSingleton<ProposalQueue>();
builder.Services.AddSingleton<IEmailSender, EmailSender>();
builder.Services.AddHostedService<PresenceSweeper>();
builder.Services.AddHostedService<ProposalReader>();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddProblemDetails();
builder.Services.AddResponseCompression(o => o.EnableForHttps = true);
builder.Services.AddGridlineSecurity(builder.Configuration);
var swagger = Swagger.Enabled(builder);
if (swagger) Swagger.AddServices(builder);
builder.Services.AddAuthentication(TokenAuthHandler.SchemeName).AddScheme<AuthenticationSchemeOptions, TokenAuthHandler>(TokenAuthHandler.SchemeName, null);
builder.Services.AddAuthorization(o => o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 200L * 1024 * 1024);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 200L * 1024 * 1024);

var app = builder.Build();

// --migrate (or Db:AutoMigrate=true): bring the database up to the latest db/migrations file, then carry on (or exit with --migrate-only)
var migrateOnly = args.Contains("--migrate-only");
if (migrateOnly || args.Contains("--migrate") || app.Configuration.GetValue<bool>("Db:AutoMigrate"))
{
    var folder = Migrator.Folder(app.Configuration, app.Environment);
    var applied = await Migrator.Run(app.Services.GetRequiredService<Npgsql.NpgsqlDataSource>(), folder, app.Logger);
    if (applied.Count > 0) app.Logger.LogInformation("Applied migrations {List} from {Folder}", string.Join(", ", applied), folder);
    if (migrateOnly) return;
}

app.UseGridlineSecurity();
app.UseResponseCompression();
app.UseRequestLog();
app.UseApiErrors();

// Static web app (web/dist) when it has been built — one address for the API and the pages.
var dist = builder.Configuration["Web:Dist"] is { Length: > 0 } d ? d : Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "..", "web", "dist"));
var serveWeb = Directory.Exists(dist);
if (serveWeb)
{
    var files = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(dist);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = files,
        OnPrepareResponse = r =>
        {   // hashed assets can be cached for a year; index.html must always be re-checked
            var cache = r.Context.Request.Path.StartsWithSegments("/assets") ? "public,max-age=31536000,immutable" : "no-cache";
            r.Context.Response.Headers.CacheControl = cache;
        },
    });
}
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");
api.MapGet("/health", async (Db db, IHostEnvironment env) =>
{
    await using var c = await db.Open();
    var schema = await c.ExecuteScalarAsync<int>("SELECT max(ChangeNo) FROM SchemaChange");
    // the database name is an operational detail: only shown outside Production
    return new { ok = true, schema, db = env.IsProduction() ? null : await c.ExecuteScalarAsync<string>("SELECT current_database()"), time = DateTime.UtcNow, version = typeof(Program).Assembly.GetName().Version?.ToString(3) };
}).AllowAnonymous();
api.MapGet("/health/live", () => Results.Ok(new { ok = true })).AllowAnonymous();
// Each area is its own group so Swagger lists the endpoints by page.
AuthEndpoints.Map(api.MapGroup("").WithTags("Sign-in and lookups"));
AccountEndpoints.Map(api.MapGroup("").WithTags("Accounts: sign-up, invitations, passwords"));
AdminEndpoints.Map(api.MapGroup("").WithTags("Company administration"));
ProjectEndpoints.Map(api.MapGroup("").WithTags("Portfolio, versions and project profile"));
TradeEndpoints.Map(api.MapGroup("").WithTags("Bid board"));
ScopeEndpoints.Map(api.MapGroup("").WithTags("Scope match"));
ScopeExtrasEndpoints.Map(api.MapGroup("").WithTags("Scope match: files, qualifications, base requirements"));
SummaryEndpoints.Map(api.MapGroup("").WithTags("Price roll-up"));
TeamEndpoints.Map(api.MapGroup("").WithTags("Crew plan, precon services, project overheads"));
PreconEndpoints.Map(api.MapGroup("").WithTags("Precon services team and tasks"));
ImportEndpoints.Map(api.MapGroup("").WithTags("Excel import"));
ExportEndpoints.Map(api.MapGroup("").WithTags("Exports"));
PortalEndpoints.Map(api.MapGroup("").WithTags("Bid portal"));
DocumentEndpoints.Map(api.MapGroup("").WithTags("Documents and bid inbox (proposal reader)"));
api.MapFallback(() => Results.Problem(statusCode: 404, title: "No such API endpoint.")).AllowAnonymous();
app.MapHub<ProjectHub>("/hubs/project");
if (swagger) Swagger.Map(app);
if (serveWeb) app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(dist) }).AllowAnonymous();

// Local dev sign-in only: give seeded users a password the first time. Never in Staging or Production.
if (app.Environment.IsDevelopment() && builder.Configuration["Seed:DevPassword"] is { Length: > 0 } devPw)
{
    try
    {
        await using var c = await app.Services.GetRequiredService<Db>().Open();
        var ids = (await c.QueryAsync<long>("SELECT UserId FROM AppUser WHERE PasswordHash IS NULL")).ToList();
        foreach (var id in ids) await c.ExecuteAsync("UPDATE AppUser SET PasswordHash = @h WHERE UserId = @id", new { id, h = Passwords.Hash(devPw) });
        if (ids.Count > 0) app.Logger.LogInformation("Set the dev password on {Count} users", ids.Count);
    }
    catch (Exception ex) { app.Logger.LogWarning(ex, "Could not set dev passwords — is the database up?"); }
}
// Versions whose stored roll-up is missing (seed data, older imports) get one, so lists and exports show totals.
try
{
    await using var w = await Work.Begin(app.Services, CurrentUser.System);
    var missing = (await w.Query<long>("SELECT v.VersionId FROM ProjectVersion v WHERE NOT EXISTS (SELECT 1 FROM RollupLine r WHERE r.VersionId = v.VersionId) OR EXISTS (SELECT 1 FROM Trade t WHERE t.VersionId = v.VersionId AND t.NeedsRecalc)")).ToList();
    foreach (var vid in missing) await Gridline.Calc.Recalc.Version(w, vid, "startup");
    await w.Commit();
    if (missing.Count > 0) app.Logger.LogInformation("Recalculated {Count} versions at startup", missing.Count);
}
catch (Exception ex) { app.Logger.LogWarning(ex, "Startup recalculation skipped"); }
app.Run();

public partial class Program;
