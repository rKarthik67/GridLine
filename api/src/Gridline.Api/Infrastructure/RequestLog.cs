using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Gridline.Infrastructure;

/// <summary>
/// One log line per API call: method, path, user, tab, request body (passwords/tokens masked), status and time.
/// Turn off or change with Logging:LogLevel:Gridline.Requests (Information = on, Warning = only failures).
/// </summary>
public static class RequestLog
{
    static readonly Regex Secret = new("(\"(?:password|accessToken|token|key|apiKey)\"\\s*:\\s*)\"[^\"]*\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    const int MaxBody = 4000;

    public static void UseRequestLog(this WebApplication app)
    {
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Gridline.Requests");
        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path.Value ?? "";
            if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase) && !path.StartsWith("/hubs", StringComparison.OrdinalIgnoreCase)) { await next(); return; }
            var sw = Stopwatch.StartNew();
            string body = "";
            if (log.IsEnabled(LogLevel.Information) && ctx.Request.ContentLength is > 0 or null && !HttpMethods.IsGet(ctx.Request.Method))
                body = await ReadBody(ctx.Request);
            Exception? fail = null;
            try { await next(); }
            catch (Exception ex) { fail = ex; throw; }
            finally
            {
                var me = ctx.Items[typeof(CurrentUser)] as CurrentUser;
                var status = fail is not null && !ctx.Response.HasStarted ? 500 : ctx.Response.StatusCode;
                var level = status >= 500 ? LogLevel.Error : status >= 400 ? LogLevel.Warning : LogLevel.Information;
                if (path.StartsWith("/hubs") && status < 400) level = LogLevel.Debug;
                log.Log(level, "{Method} {Path}{Query} → {Status} in {Ms} ms · user {User} · tab {Tab}{Body}",
                    ctx.Request.Method, path, ctx.Request.QueryString.Value, status, sw.ElapsedMilliseconds,
                    me is null ? "anonymous" : $"{me.UserId} {me.Name}", ctx.Request.Headers["X-Tab-Id"].FirstOrDefault() ?? "-",
                    body.Length > 0 ? "\n      body: " + body : "");
            }
        });
    }

    static async Task<string> ReadBody(HttpRequest req)
    {
        var type = req.ContentType ?? "";
        if (type.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var form = await req.ReadFormAsync();
                var parts = form.Select(f => $"{f.Key}={(Secret.IsMatch($"\"{f.Key}\":\"x\"") ? "•••" : f.Value.ToString())}")
                    .Concat(form.Files.Select(f => $"[file {f.Name}] {f.FileName} · {f.Length:N0} bytes · {f.ContentType}"));
                return "{ " + string.Join(", ", parts) + " }";
            }
            catch { return "(multipart form)"; }
        }
        req.EnableBuffering();
        using var reader = new StreamReader(req.Body, Encoding.UTF8, leaveOpen: true);
        var text = await reader.ReadToEndAsync();
        req.Body.Position = 0;
        text = Secret.Replace(text, "$1\"•••\"");
        return text.Length > MaxBody ? text[..MaxBody] + $"… ({text.Length:N0} chars)" : text;
    }
}
