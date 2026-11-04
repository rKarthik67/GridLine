using Dapper;
using Gridline.Live;
using Microsoft.AspNetCore.SignalR;
using Npgsql;

namespace Gridline.Infrastructure;

public sealed record ChangeEvent(long EventId, string Verb, string SubjectType, string SubjectKey, long UserId, string PersonName, long? VersionId, long ProjectId);

/// <summary>
/// One unit of work: a connection + transaction. Every write logs an ActivityEvent; after Commit the
/// events are broadcast to everyone on the project (except the tab that made the change).
/// </summary>
public sealed class Work : IAsyncDisposable
{
    readonly List<ChangeEvent> _events = [];
    readonly IHubContext<ProjectHub> _hub;
    readonly PresenceTracker _tracker;
    readonly string? _tab;
    public NpgsqlConnection C { get; }
    public NpgsqlTransaction T { get; }
    public CurrentUser User { get; }
    bool _done;
    /// <summary>The open transaction, or null once committed (so reads after Commit still work).</summary>
    public NpgsqlTransaction? Tx => _done ? null : T;

    Work(NpgsqlConnection c, NpgsqlTransaction t, CurrentUser u, IHubContext<ProjectHub> hub, PresenceTracker tracker, string? tab)
    { C = c; T = t; User = u; _hub = hub; _tracker = tracker; _tab = tab; }

    public static async Task<Work> Begin(HttpContext ctx)
    {
        var sp = ctx.RequestServices;
        var c = await sp.GetRequiredService<Db>().Open(ctx.RequestAborted);
        var t = await c.BeginTransactionAsync(ctx.RequestAborted);
        return new Work(c, t, ctx.Me(), sp.GetRequiredService<IHubContext<ProjectHub>>(), sp.GetRequiredService<PresenceTracker>(), ctx.TabId());
    }

    /// <summary>Outside a request (startup jobs, background work), acting as the given user.</summary>
    public static async Task<Work> Begin(IServiceProvider sp, CurrentUser user)
    {
        var c = await sp.GetRequiredService<Db>().Open();
        var t = await c.BeginTransactionAsync();
        return new Work(c, t, user, sp.GetRequiredService<IHubContext<ProjectHub>>(), sp.GetRequiredService<PresenceTracker>(), null);
    }

    public Task<int> Exec(string sql, object? p = null) => C.ExecuteAsync(sql, p, Tx);
    public Task<IEnumerable<X>> Query<X>(string sql, object? p = null) => C.QueryAsync<X>(sql, p, Tx);
    public Task<X?> One<X>(string sql, object? p = null) => C.QueryFirstOrDefaultAsync<X>(sql, p, Tx);
    public Task<IEnumerable<dynamic>> Query(string sql, object? p = null) => C.QueryAsync(sql, p, Tx);
    public Task<X> Scalar<X>(string sql, object? p = null) => C.ExecuteScalarAsync<X>(sql, p, Tx)!;

    /// <summary>Records who did what. Broadcast happens after Commit.</summary>
    public async Task Log(long projectId, long? versionId, string verb, string subjectType, object subjectKey, object? before = null, object? after = null)
    {
        var id = await C.ExecuteScalarAsync<long>("""
            INSERT INTO ActivityEvent (UserId, ProjectId, VersionId, Verb, SubjectType, SubjectKey, BeforeState, AfterState)
            VALUES (@u, @p, @v, @verb, @st, @sk, @b::jsonb, @a::jsonb) RETURNING EventId
            """, new { u = User.UserId, p = projectId, v = versionId, verb, st = subjectType, sk = subjectKey.ToString(),
                       b = before is null ? null : Jsonb.Text(before), a = after is null ? null : Jsonb.Text(after) }, T);
        _events.Add(new ChangeEvent(id, verb, subjectType, subjectKey.ToString()!, User.UserId, User.Name, versionId, projectId));
    }

    public async Task Commit()
    {
        await T.CommitAsync(); _done = true;
        foreach (var g in _events.GroupBy(e => e.ProjectId))
        {
            var except = _tab is null ? [] : _tracker.ConnectionsForTab(_tab);
            foreach (var e in g) await _hub.Clients.GroupExcept(ProjectHub.Group(g.Key), except).SendAsync("changed", e);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_done) { try { await T.RollbackAsync(); } catch { /* connection may be broken */ } }
        await T.DisposeAsync(); await C.DisposeAsync();
    }
}
