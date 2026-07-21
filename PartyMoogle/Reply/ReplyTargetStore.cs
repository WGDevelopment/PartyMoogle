using System;
using System.Collections.Generic;

namespace PartyMoogle.Reply;

/// <summary>A tell sender that can be replied to, keyed by a session-unique index.</summary>
public readonly record struct ReplyTarget(int Index, string Name, string World, DateTime SeenUtc);

/// <summary>
/// The reply allowlist. Incoming tells are recorded here (on the framework thread by
/// <see cref="Impl.ChatListener"/>) and resolved by the inbound reply path (on the
/// listener task thread). A reply can therefore only ever be addressed to someone who
/// recently sent the player a tell.
///
/// The index is <b>monotonic and never reused within a session</b>: a stale "17" reply
/// resolves to the original sender or is rejected as expired — it can never silently
/// remap onto a different player.
/// </summary>
public static class ReplyTargetStore
{
    private const int MaxEntries = 64;

    private static readonly object Gate = new();
    private static readonly Dictionary<int, ReplyTarget> ByIndex = new();
    private static int nextIndex = 1;

    /// <summary>
    /// Record an incoming tell sender and return the index to surface in the outbound
    /// notification. The same player (name@world, case-insensitive) keeps their existing
    /// index with a refreshed timestamp; a new player gets a fresh, never-reused index.
    /// </summary>
    public static int Record(string name, string world)
    {
        var now = DateTime.UtcNow;
        lock (Gate)
        {
            foreach (var kv in ByIndex)
            {
                if (string.Equals(kv.Value.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(kv.Value.World, world, StringComparison.OrdinalIgnoreCase))
                {
                    ByIndex[kv.Key] = kv.Value with { SeenUtc = now };
                    return kv.Key;
                }
            }

            var index = nextIndex++;
            ByIndex[index] = new ReplyTarget(index, name, world, now);
            TrimLocked();
            return index;
        }
    }

    /// <summary>
    /// Resolve an index to its target, but only if it is known AND was seen within
    /// <paramref name="windowMinutes"/>. Returns null otherwise (fail-closed).
    /// </summary>
    public static ReplyTarget? Resolve(int index, int windowMinutes)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-Math.Max(1, windowMinutes));
        lock (Gate)
        {
            if (ByIndex.TryGetValue(index, out var t) && t.SeenUtc >= cutoff)
                return t;
            return null;
        }
    }

    /// <summary>Drop the oldest entries when the store exceeds its cap. Caller holds the lock.</summary>
    private static void TrimLocked()
    {
        if (ByIndex.Count <= MaxEntries)
            return;

        var ordered = new List<ReplyTarget>(ByIndex.Values);
        ordered.Sort(static (a, b) => a.SeenUtc.CompareTo(b.SeenUtc));

        var toRemove = ByIndex.Count - MaxEntries;
        for (var i = 0; i < toRemove; i++)
            ByIndex.Remove(ordered[i].Index);
    }
}
