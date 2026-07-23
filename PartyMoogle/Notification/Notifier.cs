using System;
using System.Collections.Generic;
using System.Linq;
using PartyMoogle.Delivery;
using PartyMoogle.Util;

namespace PartyMoogle.Notification;

/// <summary>
/// Central dispatch. Listeners call <see cref="Fire"/> with an event kind and a
/// rendered title/body; Notifier applies the per-event rule and global presence
/// gate, then routes to the configured channels.
/// </summary>
public static class Notifier
{
    public static EventRule GetRule(EventKind kind)
    {
        if (!Plugin.Configuration.EventRules.TryGetValue(kind, out var rule))
        {
            rule = new EventRule();
            Plugin.Configuration.EventRules[kind] = rule;
        }

        return rule;
    }

    /// <summary>
    /// Fire a notification for <paramref name="kind"/>. No-op if the event is
    /// disabled, the presence gate blocks it, or no channels are selected.
    /// Source-specific filtering (chat type/keyword) is the caller's job.
    /// </summary>
    public static void Fire(EventKind kind, string title, string body)
    {
        var rule = GetRule(kind);

        if (!rule.Enabled)
            return;

        if (!PresenceGate.ShouldNotify())
            return;

        if (rule.Channels == NotificationChannel.None)
            return;

        if (IsThrottled(kind, title, body))
            return;

        MasterDelivery.Deliver(title, body, rule.Channels);
    }

    // --- Dedup / throttle -------------------------------------------------
    // Collapses identical notifications repeated inside a short window, so a burst
    // of the same event doesn't turn into a stack of identical pushes.

    private static readonly Dictionary<string, long> lastSent = new();

    private static bool IsThrottled(EventKind kind, string title, string body)
    {
        var window = Plugin.Configuration.ThrottleSeconds * 1000L;
        if (window <= 0)
            return false;

        var key = $"{kind}|{title}|{body}";
        var now = Environment.TickCount64;

        if (lastSent.TryGetValue(key, out var previous) && now - previous < window)
        {
            Service.PluginLog.Debug($"Throttled duplicate notification: {kind}");
            return true;
        }

        lastSent[key] = now;

        // Keep the map from growing without bound during a long session.
        if (lastSent.Count > 256)
            foreach (var stale in lastSent.Where(kv => now - kv.Value > window * 4).Select(kv => kv.Key).ToList())
                lastSent.Remove(stale);

        return false;
    }
}
