namespace MogCoach.Ingest.Screenpipe;

/// <summary>
/// Settings for reading Screenpipe's capture. The SQLite store reads frame metadata + OCR directly
/// from <see cref="DbPath"/> and extracts frame images from the recorded mp4 chunks with ffmpeg.
/// </summary>
public sealed class ScreenpipeOptions
{
    public const string SectionName = "Screenpipe";

    /// <summary>Path to Screenpipe's SQLite DB, e.g. "~/.screenpipe/db.sqlite" (expanded by the host).</summary>
    public string DbPath { get; set; } = "";

    /// <summary>HTTP base for the Screenpipe server (used by the HTTP store variant).</summary>
    public string BaseUrl { get; set; } = "http://localhost:3030";

    /// <summary>ffmpeg executable used to extract a single frame from a chunk. Must be on PATH or absolute.</summary>
    public string FfmpegPath { get; set; } = "ffmpeg";

    /// <summary>Directory for extracted frame images (cache). Created if missing.</summary>
    public string FrameCacheDir { get; set; } = "captures/frames";

    /// <summary>
    /// Clock offset (seconds) added to telemetry timestamps before matching frames, to correct any
    /// residual skew between the game log clock and Screenpipe's. Usually 0 (same machine).
    /// </summary>
    public double ClockOffsetSeconds { get; set; } = 0;
}
