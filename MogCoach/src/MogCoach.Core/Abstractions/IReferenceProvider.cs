using MogCoach.Core.Model;

namespace MogCoach.Core.Abstractions;

/// <summary>Supplies trusted rotation references for a job / encounter.</summary>
public interface IReferenceProvider
{
    Task<RotationReference?> GetRotationAsync(
        Job job,
        string? encounter = null,
        CancellationToken ct = default);
}

/// <summary>Supplies FFLogs-style performance benchmarks for an encounter / job.</summary>
public interface IBenchmarkProvider
{
    Task<FightBenchmark?> GetBenchmarkAsync(
        string encounter,
        Job job,
        CancellationToken ct = default);
}
