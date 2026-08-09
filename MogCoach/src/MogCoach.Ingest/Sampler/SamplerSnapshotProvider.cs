using System.Text.Json;
using MogCoach.Core.Abstractions;
using MogCoach.Core.Model;

namespace MogCoach.Ingest.Sampler;

/// <summary>Reads the world snapshots from a .mogcap capture. Non-.mogcap captures yield an empty list.</summary>
public sealed class SamplerSnapshotProvider : ISnapshotProvider
{
    public async Task<IReadOnlyList<WorldSnapshot>> ReadSnapshotsAsync(string capturePath, CancellationToken ct = default)
    {
        if (!IsSamplerCapture(capturePath) || !File.Exists(capturePath))
            return [];

        var snapshots = new List<WorldSnapshot>();
        using var reader = new StreamReader(capturePath);
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                if (SamplerCaptureParser.Kind(doc.RootElement) == "snap")
                    snapshots.Add(SamplerCaptureParser.ParseSnapshot(doc.RootElement));
            }
            catch (JsonException) { /* skip malformed line */ }
        }
        return snapshots;
    }

    public static bool IsSamplerCapture(string path) =>
        path.EndsWith(".mogcap", StringComparison.OrdinalIgnoreCase);
}
