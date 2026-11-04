using System.Data;
using System.Text.Json;
using Dapper;
using Npgsql;

namespace Gridline.Infrastructure;

/// <summary>Opens connections to the gridline schema. Every name is unquoted, so queries work in any letter case.</summary>
public sealed class Db(NpgsqlDataSource source)
{
    public NpgsqlDataSource Source => source;
    public async Task<NpgsqlConnection> Open(CancellationToken ct = default) => await source.OpenConnectionAsync(ct);

    public static NpgsqlDataSource Build(string connectionString)
    {
        var csb = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrEmpty(csb.SearchPath)) csb.SearchPath = "gridline,public";
        if (!connectionString.Contains("Command Timeout", StringComparison.OrdinalIgnoreCase)) csb.CommandTimeout = 300;   // big imports
        var b = new NpgsqlDataSourceBuilder(csb.ConnectionString);
        b.EnableUnmappedTypes();          // PostgreSQL enums read as text
        return b.Build();
    }

    static Db()
    {
        SqlMapper.AddTypeHandler(new JsonHandler());
        SqlMapper.AddTypeHandler(new DateOnlyHandler());
        SqlMapper.AddTypeHandler(new NullableDateOnlyHandler());
    }
    public static void Init() { /* triggers the static constructor */ }

    sealed class JsonHandler : SqlMapper.TypeHandler<JsonElement>
    {
        public override void SetValue(IDbDataParameter p, JsonElement v) { p.Value = v.GetRawText(); if (p is NpgsqlParameter np) np.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb; }
        public override JsonElement Parse(object v) => JsonDocument.Parse(v.ToString() ?? "null").RootElement.Clone();
    }
    sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override void SetValue(IDbDataParameter p, DateOnly v) { p.Value = v; if (p is NpgsqlParameter np) np.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Date; }
        public override DateOnly Parse(object v) => v switch { DateOnly d => d, DateTime dt => DateOnly.FromDateTime(dt), _ => DateOnly.Parse(v.ToString()!) };
    }
    sealed class NullableDateOnlyHandler : SqlMapper.TypeHandler<DateOnly?>
    {
        public override void SetValue(IDbDataParameter p, DateOnly? v) { p.Value = v.HasValue ? v.Value : DBNull.Value; if (p is NpgsqlParameter np) np.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Date; }
        public override DateOnly? Parse(object v) => v switch { null or DBNull => null, DateOnly d => d, DateTime dt => DateOnly.FromDateTime(dt), _ => DateOnly.Parse(v.ToString()!) };
    }
}

/// <summary>Serialises an object to a jsonb parameter.</summary>
public static class Jsonb
{
    static readonly JsonSerializerOptions Opts = new(JsonSerializerDefaults.Web);
    public static NpgsqlParameter Param(string name, object? value) =>
        new(name, NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = value is null ? DBNull.Value : JsonSerializer.Serialize(value, Opts) };
    public static string Text(object? value) => JsonSerializer.Serialize(value, Opts);
}
