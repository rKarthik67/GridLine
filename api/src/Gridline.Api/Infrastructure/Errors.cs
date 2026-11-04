using System.Text.Json;
using Npgsql;

namespace Gridline.Infrastructure;

/// <summary>An error the user should see. Returned as problem JSON: { title, detail, code, errors }.</summary>
public class ApiException(int status, string message, string? code = null, object? errors = null) : Exception(message)
{
    public int Status { get; } = status;
    public string? Code { get; } = code;
    public object? Errors { get; } = errors;
    public static ApiException NotFound(string what) => new(404, $"{what} was not found.", "notfound");
    public static ApiException Forbidden(string why = "You don't have access to do that.") => new(403, why, "forbidden");
    public static ApiException Bad(string why, object? errors = null) => new(400, why, "invalid", errors);
    /// <summary>Someone else saved the same row first (RowRev moved on).</summary>
    public static ApiException Clash(string who, object? current = null) =>
        new(409, $"{who} changed this a moment ago. Reload to see their change, or save again to overwrite.", "clash", current);
}

public static class ErrorHandling
{
    public static void UseApiErrors(this WebApplication app) => app.Use(async (ctx, next) =>
    {
        try { await next(); }
        catch (Exception ex) when (!ctx.Response.HasStarted)
        {
            if (ex is OperationCanceledException && ctx.RequestAborted.IsCancellationRequested) return;   // the browser went away; nothing to answer
            var (status, msg, code, errors) = Map(ex);
            var traceId = ctx.TraceIdentifier;
            if (status >= 500) app.Logger.LogError(ex, "Unhandled error on {Method} {Path} (trace {TraceId})", ctx.Request.Method, ctx.Request.Path, traceId);
            else if (status is 409 or 503) app.Logger.LogWarning("{Status} on {Path}: {Message}", status, ctx.Request.Path, msg);
            if (status >= 500) msg = $"{msg} Reference: {traceId}";
            ctx.Response.Clear();
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/problem+json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { title = msg, detail = msg, status, code, errors, traceId }, JsonSerializerOptions.Web));
        }
    });

    static (int, string, string?, object?) Map(Exception ex) => ex switch
    {
        ApiException a => (a.Status, a.Message, a.Code, a.Errors),
        PostgresException { SqlState: "23505" } p => (409, Friendly(p, "That would create a duplicate"), "duplicate", null),
        PostgresException { SqlState: "23503" } p => (409, Friendly(p, "Another record still refers to this"), "inuse", null),
        PostgresException { SqlState: "23514" or "22P02" or "22003" or "23502" } p => (400, Friendly(p, "A value is not allowed"), "invalid", null),
        PostgresException { SqlState: "40001" or "40P01" } => (409, "Someone else was saving the same rows at the same moment. Please try again.", "busy", null),
        PostgresException { SqlState: "57014" } => (503, "The database took too long to answer. Please try again.", "timeout", null),
        PostgresException { SqlState: "53300" or "57P01" or "57P03" } => (503, "The database is busy or restarting. Please try again in a moment.", "dbdown", null),
        NpgsqlException { InnerException: System.Net.Sockets.SocketException or TimeoutException } or NpgsqlException { IsTransient: true } =>
            (503, "Can't reach the database right now. Check that PostgreSQL is running, then try again.", "dbdown", null),
        TimeoutException => (503, "The server took too long to answer. Please try again.", "timeout", null),
        BadHttpRequestException b => (b.StatusCode is >= 400 and < 500 ? b.StatusCode : 400, b.Message.Contains("JSON") ? "Some of the data sent was not in the right format." : b.Message, "invalid", null),
        JsonException j => (400, "The request body is not valid JSON: " + j.Message, "invalid", null),
        _ => (500, "Something went wrong on the server. It has been logged.", "server", null),
    };

    static string Friendly(PostgresException p, string lead) => p.ConstraintName switch
    {
        "uxonecarriedbid" => "Only one bid can be picked on a trade.",
        "uxtradecode" => "This version already has a trade with that CSI code.",
        "uxoneprimaryversion" => "A project can only have one primary version.",
        "uxpartnername" => "A trade partner with that name already exists.",
        null => $"{lead}: {p.MessageText}",
        var c => $"{lead} ({c}).",
    };
}
