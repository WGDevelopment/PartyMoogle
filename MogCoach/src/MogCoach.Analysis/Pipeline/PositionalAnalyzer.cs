using Microsoft.Extensions.Logging;
using MogCoach.Core.Abstractions;
using MogCoach.Core.Geometry;
using MogCoach.Core.Model;

namespace MogCoach.Analysis.Pipeline;

/// <summary>
/// Derives findings from the sampler's continuous snapshots — the data IINACT can't give:
/// <list type="bullet">
/// <item>whether the player stood in a resolving AoE (geometry: caster origin+heading + shape DB),</item>
/// <item>movement tells (backpedaling / keyboard-turning: heading vs velocity).</item>
/// </list>
/// No LLM call; this is deterministic geometry over the pull's snapshots.
/// </summary>
public sealed class PositionalAnalyzer(IAoeShapeProvider shapes, ILogger<PositionalAnalyzer> log)
{
    private const float MovingSpeedYps = 1.0f;      // yalms/sec above which we consider the player moving
    private const float BackpedalDot = -0.5f;       // velocity·facing below this ⇒ moving backwards
    private const int MaxDangerFindings = 8;

    private readonly record struct Resolution(string CastId, float Ox, float Oz, float H, DateTimeOffset At, float Sx, float Sz, bool HasSelf);

    public async Task<IReadOnlyList<Finding>> AnalyzeAsync(
        Pull pull, IReadOnlyList<WorldSnapshot> snapshots, CancellationToken ct = default)
    {
        if (snapshots.Count == 0) return [];

        var findings = new List<Finding>();
        var resolutions = new List<Resolution>();

        double movingTime = 0, backpedalTime = 0;
        ActorSnapshot? prevSelf = null;
        DateTimeOffset prevTs = default;
        var casting = new Dictionary<string, (string castId, float ox, float oz, float h)>();

        foreach (var snap in snapshots)
        {
            ct.ThrowIfCancellationRequested();
            var self = snap.Self;

            // --- movement tells ---
            if (self is not null && prevSelf is not null)
            {
                var dt = (snap.Timestamp - prevTs).TotalSeconds;
                if (dt is > 0 and <= 1.0)
                {
                    var vx = (self.X - prevSelf.X) / (float)dt;
                    var vz = (self.Z - prevSelf.Z) / (float)dt;
                    var speed = MathF.Sqrt(vx * vx + vz * vz);
                    if (speed > MovingSpeedYps)
                    {
                        movingTime += dt;
                        var (fx, fz) = Geometry.ForwardVector(self.Heading);
                        var dot = (vx * fx + vz * fz) / speed;
                        if (dot < BackpedalDot) backpedalTime += dt;
                    }
                }
            }
            if (self is not null) { prevSelf = self; prevTs = snap.Timestamp; }

            // --- enemy cast tracking → resolution detection ---
            var castingNow = new HashSet<string>();
            foreach (var enemy in snap.Enemies)
            {
                if (enemy.Cast is null) continue;
                castingNow.Add(enemy.Id);
                casting[enemy.Id] = (enemy.Cast.AbilityId, enemy.X, enemy.Z, enemy.Heading);
            }
            foreach (var id in casting.Keys.Where(k => !castingNow.Contains(k)).ToList())
            {
                var c = casting[id];
                casting.Remove(id);
                resolutions.Add(new Resolution(
                    c.castId, c.ox, c.oz, c.h, snap.Timestamp,
                    self?.X ?? 0, self?.Z ?? 0, self is not null));
            }
        }

        // --- movement finding ---
        if (movingTime > 4 && backpedalTime / movingTime > 0.2)
        {
            findings.Add(new Finding
            {
                Severity = Severity.Minor,
                Category = FindingCategory.Awareness,
                Title = "Frequent backpedaling / keyboard-turning",
                Detail = $"You moved backwards for ~{backpedalTime:F0}s of ~{movingTime:F0}s spent moving "
                         + $"({backpedalTime / movingTime:P0}). Walking backwards is slower than your run speed and "
                         + "usually means you're turning with the keyboard.",
                Recommendation = "Turn with the mouse (hold right-click) and strafe/run forward instead of backpedaling.",
                Origin = "positional",
            });
        }

        // --- danger-zone findings (need the AoE shape DB) ---
        var emitted = 0;
        foreach (var r in resolutions)
        {
            if (emitted >= MaxDangerFindings) break;
            if (!r.HasSelf) continue;

            var shape = await shapes.GetShapeAsync(r.CastId, ct).ConfigureAwait(false);
            if (shape is null) continue; // unknown ability geometry ⇒ no claim (see SPEC: DB is the authoring cost)

            if (Geometry.Contains(shape, r.Ox, r.Oz, r.H, r.Sx, r.Sz))
            {
                emitted++;
                findings.Add(new Finding
                {
                    Severity = Severity.Major,
                    Category = FindingCategory.Positioning,
                    Title = $"Stood in AoE (ability {r.CastId})",
                    Detail = $"At {(r.At - pull.StartedAt).TotalSeconds:F0}s you were inside a {shape.Kind} AoE when it "
                             + "resolved (computed from your position vs the caster's position/facing).",
                    Recommendation = "Move out of the telegraph before the cast completes.",
                    At = r.At,
                    PullOffsetSeconds = (r.At - pull.StartedAt).TotalSeconds,
                    Origin = "positional",
                });
            }
        }

        log.LogInformation("{Pull}: positional pass — {Res} cast resolutions, {F} finding(s)",
            pull.Id, resolutions.Count, findings.Count);
        return findings;
    }
}
