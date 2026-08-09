namespace MogCoach.Core.Model;

/// <summary>
/// A single captured screen frame from Screenpipe, addressed by wall-clock time so it can
/// be aligned to <see cref="CombatEvent"/>s. Image bytes are loaded lazily — <see cref="ImageBytes"/>
/// may be null until a frame store is asked to materialise it.
/// </summary>
public sealed record Frame
{
    /// <summary>Screenpipe frame id (primary key in db.sqlite).</summary>
    public required long Id { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>PNG/JPEG bytes for the frame, once materialised.</summary>
    public byte[]? ImageBytes { get; init; }

    /// <summary>OCR text Screenpipe extracted for this frame (chat, cast bars, UI numerals).</summary>
    public string? OcrText { get; init; }

    /// <summary>Source video chunk file (mp4) and frame offset, for on-demand extraction.</summary>
    public string? VideoChunkPath { get; init; }
    public long? OffsetIndex { get; init; }
}
