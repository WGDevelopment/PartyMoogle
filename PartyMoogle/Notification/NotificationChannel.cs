using System;

namespace PartyMoogle.Notification;

/// <summary>
/// Delivery channels a notification can be routed to. Flags so a single event
/// can target ntfy, Discord, both, or neither.
/// </summary>
[Flags]
public enum NotificationChannel
{
    None = 0,
    Ntfy = 1 << 0,
    Discord = 1 << 1,

    Both = Ntfy | Discord
}
