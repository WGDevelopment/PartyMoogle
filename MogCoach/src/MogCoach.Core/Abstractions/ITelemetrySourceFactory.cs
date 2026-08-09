namespace MogCoach.Core.Abstractions;

/// <summary>Creates a telemetry source for a specific capture. Lets the pipeline stay decoupled
/// from the concrete IINACT file reader.</summary>
public interface ITelemetrySourceFactory
{
    ITelemetrySource Create(string capturePath, string? playerNameOverride = null);
}
