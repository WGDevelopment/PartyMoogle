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

        MasterDelivery.Deliver(title, body, rule.Channels);
    }
}
