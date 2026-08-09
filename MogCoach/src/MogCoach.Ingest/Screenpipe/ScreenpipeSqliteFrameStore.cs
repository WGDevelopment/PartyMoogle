using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MogCoach.Core.Abstractions;
using MogCoach.Core.Model;

namespace MogCoach.Ingest.Screenpipe;

/// <summary>
/// <see cref="IFrameStore"/> backed by Screenpipe's SQLite DB. Frame metadata + OCR come from the
/// DB; the frame image is extracted on demand from the recorded mp4 chunk with ffmpeg and cached.
/// <para>
/// The SQL below targets Screenpipe's <c>frames</c> / <c>video_chunks</c> / <c>ocr_text</c> schema.
/// Schema drifts between Screenpipe versions — VERIFY against your install (see docs/SPEC.md) and
/// adjust the column names here if needed. Image materialisation degrades gracefully: if ffmpeg is
/// missing or extraction fails, the frame is returned with OCR text but null image bytes, so
/// telemetry+OCR analysis still works and only the vision pass for that frame is skipped.
/// </para>
/// </summary>
public sealed class ScreenpipeSqliteFrameStore : IFrameStore
{
    private readonly ScreenpipeOptions _opts;
    private readonly ILogger<ScreenpipeSqliteFrameStore> _log;
    private readonly string _connectionString;

    public ScreenpipeSqliteFrameStore(IOptions<ScreenpipeOptions> opts, ILogger<ScreenpipeSqliteFrameStore> log)
    {
        _opts = opts.Value;
        _log = log;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _opts.DbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
        Directory.CreateDirectory(_opts.FrameCacheDir);
    }

    public async Task<Frame?> GetNearestAsync(DateTimeOffset timestamp, CancellationToken ct = default)
    {
        var frame = await QueryNearestAsync(Adjust(timestamp), ct).ConfigureAwait(false);
        if (frame is null) return null;
        return frame with { ImageBytes = await MaterialiseAsync(frame, ct).ConfigureAwait(false) };
    }

    public async Task<IReadOnlyList<Frame>> GetWindowAsync(
        DateTimeOffset timestamp, TimeSpan before, TimeSpan after, CancellationToken ct = default)
    {
        var center = Adjust(timestamp);
        var metas = await QueryRangeAsync(center - before, center + after, ct).ConfigureAwait(false);
        var result = new List<Frame>(metas.Count);
        foreach (var m in metas)
        {
            ct.ThrowIfCancellationRequested();
            result.Add(m with { ImageBytes = await MaterialiseAsync(m, ct).ConfigureAwait(false) });
        }
        return result;
    }

    private DateTimeOffset Adjust(DateTimeOffset ts) => ts.AddSeconds(_opts.ClockOffsetSeconds);

    private async Task<Frame?> QueryNearestAsync(DateTimeOffset ts, CancellationToken ct)
    {
        const string sql = """
            SELECT f.id, f.timestamp, f.offset_index, vc.file_path, o.text
            FROM frames f
            JOIN video_chunks vc ON vc.id = f.video_chunk_id
            LEFT JOIN ocr_text o ON o.frame_id = f.id
            ORDER BY ABS(strftime('%s', f.timestamp) - strftime('%s', $ts))
            LIMIT 1;
            """;
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$ts", ts.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false) ? Read(r) : null;
    }

    private async Task<IReadOnlyList<Frame>> QueryRangeAsync(
        DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        const string sql = """
            SELECT f.id, f.timestamp, f.offset_index, vc.file_path, o.text
            FROM frames f
            JOIN video_chunks vc ON vc.id = f.video_chunk_id
            LEFT JOIN ocr_text o ON o.frame_id = f.id
            WHERE f.timestamp BETWEEN $start AND $end
            ORDER BY f.timestamp ASC;
            """;
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$start", start.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$end", end.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var list = new List<Frame>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(Read(r));
        return list;
    }

    private static Frame Read(SqliteDataReader r)
    {
        var id = r.GetInt64(0);
        var ts = DateTimeOffset.Parse(r.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        var offset = r.IsDBNull(2) ? (long?)null : r.GetInt64(2);
        var path = r.IsDBNull(3) ? null : r.GetString(3);
        var ocr = r.IsDBNull(4) ? null : r.GetString(4);
        return new Frame
        {
            Id = id,
            Timestamp = ts,
            OffsetIndex = offset,
            VideoChunkPath = path,
            OcrText = ocr,
        };
    }

    /// <summary>Extracts (and caches) the frame image via ffmpeg. Returns null on any failure.</summary>
    private async Task<byte[]?> MaterialiseAsync(Frame frame, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(frame.VideoChunkPath) || frame.OffsetIndex is null)
            return null;

        var cachePath = Path.Combine(_opts.FrameCacheDir, $"frame-{frame.Id}.png");
        if (File.Exists(cachePath))
            return await File.ReadAllBytesAsync(cachePath, ct).ConfigureAwait(false);

        if (!File.Exists(frame.VideoChunkPath))
        {
            _log.LogWarning("Video chunk missing for frame {Id}: {Path}", frame.Id, frame.VideoChunkPath);
            return null;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _opts.FfmpegPath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(frame.VideoChunkPath);
            psi.ArgumentList.Add("-vf");
            psi.ArgumentList.Add($"select=eq(n\\,{frame.OffsetIndex})");
            psi.ArgumentList.Add("-vframes");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add(cachePath);

            using var proc = Process.Start(psi);
            if (proc is null) return null;
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);

            if (proc.ExitCode == 0 && File.Exists(cachePath))
                return await File.ReadAllBytesAsync(cachePath, ct).ConfigureAwait(false);

            _log.LogWarning("ffmpeg exit {Code} extracting frame {Id}", proc.ExitCode, frame.Id);
            return null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Frame extraction failed for {Id} (is ffmpeg installed?)", frame.Id);
            return null;
        }
    }
}
