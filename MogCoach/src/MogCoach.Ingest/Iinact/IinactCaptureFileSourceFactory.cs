using MogCoach.Core.Abstractions;

namespace MogCoach.Ingest.Iinact;

/// <inheritdoc />
public sealed class IinactCaptureFileSourceFactory : ITelemetrySourceFactory
{
    public ITelemetrySource Create(string capturePath, string? playerNameOverride = null) =>
        new IinactCaptureFileSource(capturePath, playerNameOverride);
}
