using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

namespace Gridline.Infrastructure;

/// <summary>Headers and limits a hosted product needs: no framing, no MIME sniffing, a content security policy,
/// HSTS behind TLS, correct client addresses behind a proxy, and a brake on sign-in attempts.</summary>
public static class Security
{
    public const string AuthPolicy = "auth";
    public const string PublicPolicy = "public";

    public static void AddGridlineSecurity(this IServiceCollection services, IConfiguration cfg)
    {
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            o.KnownIPNetworks.Clear(); o.KnownProxies.Clear();   // trust the proxy in front of us (set Security:TrustProxies=false to turn this off)
        });
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
            {
                ctx.HttpContext.Response.ContentType = "application/problem+json";
                await ctx.HttpContext.Response.WriteAsync("""{"title":"Too many attempts. Please wait a minute and try again.","status":429,"code":"ratelimit"}""", ct);
            };
            // sign-in, sign-up, password reset: 10 per minute per address
            o.AddPolicy(AuthPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.ClientIp(), _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            // bid portal and other public pages: 60 per minute per address
            o.AddPolicy(PublicPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.ClientIp(), _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        if (!cfg.GetValue("Security:Hsts", true)) return;
        services.AddHsts(o => { o.MaxAge = TimeSpan.FromDays(365); o.IncludeSubDomains = true; });
    }

    public static void UseGridlineSecurity(this WebApplication app)
    {
        if (app.Configuration.GetValue("Security:TrustProxies", true)) app.UseForwardedHeaders();
        if (!app.Environment.IsDevelopment() && app.Configuration.GetValue("Security:Hsts", true)) app.UseHsts();
        app.Use(async (ctx, next) =>
        {
            var h = ctx.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            // the SPA, Google Fonts, and (only on /swagger) the Swagger UI bundle from jsDelivr
            var swagger = ctx.Request.Path.StartsWithSegments("/swagger");
            h["Content-Security-Policy"] = swagger
                ? "default-src 'self'; script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; img-src 'self' data:; connect-src 'self'"
                : "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src 'self' https://fonts.gstatic.com data:; img-src 'self' data: blob:; connect-src 'self' ws: wss:; worker-src 'self' blob:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
            await next();
        });
        app.UseRateLimiter();
    }
}
