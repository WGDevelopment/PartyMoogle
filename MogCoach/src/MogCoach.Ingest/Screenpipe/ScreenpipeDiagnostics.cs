using System.Globalization;
using Microsoft.Data.Sqlite;

namespace MogCoach.Ingest.Screenpipe;

/// <summary>Result of introspecting a Screenpipe DB against the schema MogCoach depends on.</summary>
public sealed record ScreenpipeCheckResult
{
    public bool FileExists { get; init; }
    /// <summary>Expected tables that are absent.</summary>
    public IReadOnlyList<string> MissingTables { get; init; } = [];
    /// <summary>Expected columns that are absent, as "table.column".</summary>
    public IReadOnlyList<string> MissingColumns { get; init; } = [];
    public long FrameCount { get; init; }
    public string? LatestTimestamp { get; init; }
    public string? Error { get; init; }

    public bool Ok => FileExists && Error is null && MissingTables.Count == 0 && MissingColumns.Count == 0;
}

/// <summary>
/// Verifies a Screenpipe SQLite DB has the tables/columns MogCoach's frame store queries, so schema
/// drift between Screenpipe versions surfaces as a clear diagnostic instead of a runtime SQL error.
/// The required set is exactly the columns used by <see cref="ScreenpipeSqliteFrameStore"/>.
/// </summary>
public static class ScreenpipeDiagnostics
{
    private static readonly IReadOnlyDictionary<string, string[]> Required = new Dictionary<string, string[]>
    {
        ["frames"] = ["id", "video_chunk_id", "offset_index", "timestamp"],
        ["video_chunks"] = ["id", "file_path", "device_name"],
        ["ocr_text"] = ["frame_id", "text"],
    };

    public static async Task<ScreenpipeCheckResult> RunAsync(ScreenpipeOptions opts, CancellationToken ct = default)
    {
        if (!File.Exists(opts.DbPath))
            return new ScreenpipeCheckResult { FileExists = false };

        try
        {
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = opts.DbPath,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString();

            await using var conn = new SqliteConnection(cs);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            var missingTables = new List<string>();
            var missingColumns = new List<string>();

            foreach (var (table, columns) in Required)
            {
                var present = await GetColumnsAsync(conn, table, ct).ConfigureAwait(false);
                if (present.Count == 0) { missingTables.Add(table); continue; }
                foreach (var col in columns)
                    if (!present.Contains(col, StringComparer.OrdinalIgnoreCase))
                        missingColumns.Add($"{table}.{col}");
            }

            long frameCount = 0;
            string? latest = null;
            if (!missingTables.Contains("frames"))
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*), MAX(timestamp) FROM frames;";
                await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await r.ReadAsync(ct).ConfigureAwait(false))
                {
                    frameCount = r.IsDBNull(0) ? 0 : r.GetInt64(0);
                    latest = r.IsDBNull(1) ? null : r.GetString(1);
                }
            }

            return new ScreenpipeCheckResult
            {
                FileExists = true,
                MissingTables = missingTables,
                MissingColumns = missingColumns,
                FrameCount = frameCount,
                LatestTimestamp = latest,
            };
        }
        catch (Exception ex)
        {
            return new ScreenpipeCheckResult { FileExists = true, Error = ex.Message };
        }
    }

    private static async Task<HashSet<string>> GetColumnsAsync(SqliteConnection conn, string table, CancellationToken ct)
    {
        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = conn.CreateCommand();
        // PRAGMA doesn't accept parameters; table names here are hardcoded constants, not user input.
        cmd.CommandText = $"PRAGMA table_info({table});";
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            cols.Add(r.GetString(1)); // column 1 = "name"
        return cols;
    }

    public static string FormatTimestamp(string? iso) =>
        DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
            ? t.ToString("u") : iso ?? "n/a";
}
