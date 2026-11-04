using System.Collections.Concurrent;
using Dapper;
using Gridline.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Gridline.Live;

public sealed class PresenceRow
{
    public string ConnectionId { get; set; } = "";
    public long UserId { get; set; }
    public string PersonName { get; set; } = "";
    public string Initials { get; set; } = "";
    public string Colour { get; set; } = "";
    public long? ProjectId { get; set; }
    public long? VersionId { get; set; }
    public string? PageKey { get; set; }
    public string? FocusKind { get; set; }
    public string? FocusKey { get; set; }
    public string Activity { get; set; } = "viewing";
}

/// <summary>Connections on this server: SignalR id → (presence key, tab, project).</summary>
public sealed class PresenceTracker
{
    public sealed record Entry(string PresenceKey, string Tab, long UserId) { public long? ProjectId { get; set; } }
    readonly ConcurrentDictionary<string, Entry> _byConn = new();
    public void Add(string connId, Entry e) => _byConn[connId] = e;
    public Entry? Get(string connId) => _byConn.TryGetValue(connId, out var e) ? e : null;
    public Entry? Remove(string connId) => _byConn.TryRemove(connId, out var e) ? e : null;
    public IReadOnlyList<string> ConnectionsForTab(string tab) => _byConn.Where(kv => kv.Value.Tab == tab).Select(kv => kv.Key).ToList();
    public IReadOnlyList<string> PresenceKeys => _byConn.Values.Select(v => v.PresenceKey).ToList();
}

/// <summary>
/// One connection per browser tab. Join puts the tab in its project's group and writes a Presence row,
/// so two tabs on two projects show up separately and each only hears its own project's changes.
/// </summary>
[Authorize]
public sealed class ProjectHub(Db db, PresenceTracker tracker) : Hub
{
    public static string Group(long projectId) => $"p{projectId}";

    public override Task OnConnectedAsync()
    {
        var tab = Context.GetHttpContext()?.Request.Query["tab"].ToString();
        if (string.IsNullOrWhiteSpace(tab)) tab = Context.ConnectionId;
        var me = Context.GetHttpContext()!.Me();
        tracker.Add(Context.ConnectionId, new PresenceTracker.Entry($"{Context.ConnectionId}:{tab}", tab, me.UserId));
        return base.OnConnectedAsync();
    }

    public async Task Join(long projectId, long versionId, string page)
    {
        var me = Context.GetHttpContext()!.Me();
        var e = tracker.Get(Context.ConnectionId) ?? throw new HubException("Not connected");
        await using var c = await db.Open();
        await Access.Project(c, null, me, projectId, "viewer");
        var old = e.ProjectId;
        if (old is long o && o != projectId) await Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(o));
        e.ProjectId = projectId;
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(projectId));
        await c.ExecuteAsync("""
            INSERT INTO Presence (ConnectionId, UserId, ProjectId, VersionId, PageKey, Activity)
            VALUES (@k, @u, @p, @v, @page, 'viewing')
            ON CONFLICT (ConnectionId) DO UPDATE SET ProjectId = EXCLUDED.ProjectId, VersionId = EXCLUDED.VersionId, PageKey = EXCLUDED.PageKey,
                FocusKind = NULL, FocusKey = NULL, Activity = 'viewing', HeartbeatAt = now()
            """, new { k = e.PresenceKey, u = me.UserId, p = projectId, v = versionId, page });
        await c.ExecuteAsync("""
            INSERT INTO RecentProject (UserId, ProjectId) VALUES (@u, @p)
            ON CONFLICT (UserId, ProjectId) DO UPDATE SET OpenedAt = now()
            """, new { u = me.UserId, p = projectId });
        await c.ExecuteAsync("UPDATE AppUser SET LastSeenAt = now() WHERE UserId = @u", new { u = me.UserId });
        if (old is long o2 && o2 != projectId) await Broadcast(c, o2);
        await Broadcast(c, projectId);
    }

    public async Task Focus(string? kind, string? key, string activity)
    {
        var e = tracker.Get(Context.ConnectionId);
        if (e?.ProjectId is not long pid) return;
        if (activity is not ("viewing" or "editing" or "typing" or "idle")) activity = "viewing";
        await using var c = await db.Open();
        await c.ExecuteAsync("UPDATE Presence SET FocusKind = @kind, FocusKey = @key, Activity = @activity, HeartbeatAt = now() WHERE ConnectionId = @k",
            new { kind, key, activity, k = e.PresenceKey });
        await Broadcast(c, pid);
    }

    public async Task Leave()
    {
        var e = tracker.Get(Context.ConnectionId);
        if (e?.ProjectId is not long pid) return;
        e.ProjectId = null;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(pid));
        await using var c = await db.Open();
        await c.ExecuteAsync("DELETE FROM Presence WHERE ConnectionId = @k", new { k = e.PresenceKey });
        await Broadcast(c, pid);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var e = tracker.Remove(Context.ConnectionId);
        if (e is not null)
        {
            await using var c = await db.Open();
            await c.ExecuteAsync("DELETE FROM Presence WHERE ConnectionId = @k", new { k = e.PresenceKey });
            if (e.ProjectId is long pid) await Broadcast(c, pid);
        }
        await base.OnDisconnectedAsync(exception);
    }

    async Task Broadcast(Npgsql.NpgsqlConnection c, long projectId) =>
        await Clients.Group(Group(projectId)).SendAsync("presence", await Rows(c, projectId));

    public static async Task<List<PresenceRow>> Rows(Npgsql.NpgsqlConnection c, long projectId) =>
        (await c.QueryAsync<PresenceRow>("""
            SELECT ConnectionId, UserId, PersonName, Initials, Colour, ProjectId, VersionId, PageKey, FocusKind, FocusKey, Activity
            FROM WhoIsHere WHERE ProjectId = @projectId ORDER BY PersonName, ConnectionId
            """, new { projectId })).ToList();
}

/// <summary>Keeps Presence rows alive for open tabs and removes rows left behind by tabs that vanished.</summary>
public sealed class PresenceSweeper(Db db, PresenceTracker tracker, ILogger<PresenceSweeper> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try { await using var c = await db.Open(stop); await c.ExecuteAsync("DELETE FROM Presence"); }   // fresh start: nobody is connected yet
        catch (Exception ex) { log.LogWarning(ex, "Could not clear presence at startup"); }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stop))
        {
            try
            {
                await using var c = await db.Open(stop);
                var keys = tracker.PresenceKeys.ToArray();
                if (keys.Length > 0) await c.ExecuteAsync("UPDATE Presence SET HeartbeatAt = now() WHERE ConnectionId = ANY(@keys)", new { keys });
                await c.ExecuteAsync("DELETE FROM Presence WHERE HeartbeatAt < now() - interval '2 minutes'");
                await c.ExecuteAsync("DELETE FROM EditLock WHERE ExpiresAt < now()");
            }
            catch (Exception ex) when (!stop.IsCancellationRequested) { log.LogWarning(ex, "Presence sweep failed"); }
        }
    }
}
