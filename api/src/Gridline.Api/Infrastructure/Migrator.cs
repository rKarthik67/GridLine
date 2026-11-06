using Dapper;
using Npgsql;

namespace Gridline.Infrastructure;

/// <summary>
/// Applies db/migrations/NNN_name.sql files the database hasn't seen yet (number &gt; max SchemaChange.ChangeNo),
/// in order, each in its own transaction. Turned on with Db:AutoMigrate = true (the Docker image does this at
/// start-up) or by running the API with --migrate. Migration 001 creates the schema, so an empty database works too.
/// </summary>
public static class Migrator
{
    public static string Folder(IConfiguration cfg, IHostEnvironment env)
    {
        if (cfg["Db:MigrationsPath"] is { Length: > 0 } p) return Path.GetFullPath(p);
        foreach (var candidate in new[] { Path.Combine(env.ContentRootPath, "db", "migrations"), Path.Combine(env.ContentRootPath, "..", "..", "..", "db", "migrations"), Path.Combine(AppContext.BaseDirectory, "db", "migrations") })
            if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
        return Path.Combine(env.ContentRootPath, "db", "migrations");
    }

    /// <summary>Returns the numbers applied. Throws on the first failure (the rest are not run).</summary>
    public static async Task<List<int>> Run(NpgsqlDataSource source, string folder, ILogger log, CancellationToken ct = default)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"Migrations folder not found: {folder}");
        var files = Directory.GetFiles(folder, "*.sql").Select(f => (file: f, no: int.TryParse(Path.GetFileName(f).Split('_')[0], out var n) ? n : -1))
                             .Where(x => x.no > 0).OrderBy(x => x.no).ToList();
        await using var c = await source.OpenConnectionAsync(ct);
        // one app at a time runs migrations (several API replicas may start together)
        await c.ExecuteAsync("SELECT pg_advisory_lock(7461)");
        try
        {
            var hasSchema = await c.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'gridline' AND table_name = 'schemachange')");
            var current = hasSchema ? await c.ExecuteScalarAsync<int>("SELECT coalesce(max(ChangeNo), 0) FROM gridline.SchemaChange") : 0;
            var applied = new List<int>();
            foreach (var (file, no) in files.Where(x => x.no > current))
            {
                log.LogInformation("Applying migration {No}: {File}", no, Path.GetFileName(file));
                var sql = await File.ReadAllTextAsync(file, ct);
                await using var tx = await c.BeginTransactionAsync(ct);
                await using (var cmd = new NpgsqlCommand(sql, c, tx) { CommandTimeout = 1800 }) await cmd.ExecuteNonQueryAsync(ct);
                await c.ExecuteAsync("INSERT INTO gridline.SchemaChange (ChangeNo, ChangeName) VALUES (@no, @name) ON CONFLICT (ChangeNo) DO NOTHING", new { no, name = Path.GetFileNameWithoutExtension(file) }, tx);
                await tx.CommitAsync(ct);
                applied.Add(no);
            }
            if (applied.Count == 0) log.LogInformation("Database schema is current ({No})", current);
            return applied;
        }
        finally { await c.ExecuteAsync("SELECT pg_advisory_unlock(7461)"); }
    }
}
