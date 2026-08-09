using MogCoach.Core.Abstractions;
using MogCoach.Ingest.Iinact;
using MogCoach.Ingest.Sampler;

namespace MogCoach.Ingest;

/// <summary>
/// Picks the telemetry reader by capture type: a sampler <c>.mogcap</c> uses
/// <see cref="SamplerCaptureSource"/>; anything else is treated as an IINACT network log.
/// </summary>
public sealed class CaptureSourceFactory : ITelemetrySourceFactory
{
    public ITelemetrySource Create(string capturePath, string? playerNameOverride = null) =>
        SamplerSnapshotProvider.IsSamplerCapture(capturePath)
            ? new SamplerCaptureSource(capturePath)
            : new IinactCaptureFileSource(capturePath, playerNameOverride);
}
