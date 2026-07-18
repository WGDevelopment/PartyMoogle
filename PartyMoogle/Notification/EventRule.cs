using System.Collections.Generic;

namespace PartyMoogle.Notification;

/// <summary>
/// Per-event configuration: whether it fires, where it routes, and any
/// source-specific filters. Serialized as part of <see cref="Configuration"/>.
/// </summary>
public class EventRule
{
    public bool Enabled { get; set; } = false;

    public NotificationChannel Channels { get; set; } = NotificationChannel.Both;

    // Chat filter (FilterType.Chat): raw XivChatType values to notify on.
    // Empty = notify on any chat type.
    public HashSet<int> ChatTypes { get; set; } = new();

    // Optional case-insensitive regex; empty = match all. Applied to the message body.
    public string KeywordRegex { get; set; } = "";

    // Threshold filter (FilterType.Threshold): e.g. HP% or gil amount. Phase 2.
    public int Threshold { get; set; } = 0;
}
