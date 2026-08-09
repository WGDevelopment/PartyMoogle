using System;
using System.IO;
using System.Text.Json;

namespace MogCoach.Recorder;

/// <summary>
/// Appends capture records as JSON Lines to a .mogcap file. All writes happen on the framework
/// thread (the sampler is the only caller), so no locking is needed. Buffered; flushed on stop.
/// </summary>
public sealed class CaptureWriter : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private StreamWriter? _writer;

    public string? CurrentPath { get; private set; }
    public bool IsOpen => _writer is not null;

    public void Open(string path)
    {
        Close();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _writer = new StreamWriter(path, append: false) { AutoFlush = false };
        CurrentPath = path;
    }

    /// <summary>Serializes one record (header/snapshot/event) as a single JSON line.</summary>
    public void Write(object record)
    {
        if (_writer is null) return;
        _writer.WriteLine(JsonSerializer.Serialize(record, record.GetType(), JsonOpts));
    }

    public void Flush() => _writer?.Flush();

    public void Close()
    {
        if (_writer is null) return;
        try { _writer.Flush(); _writer.Dispose(); }
        finally { _writer = null; CurrentPath = null; }
    }

    public void Dispose() => Close();
}
