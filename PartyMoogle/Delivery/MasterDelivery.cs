using System.Collections.Generic;
using PartyMoogle.Notification;

namespace PartyMoogle.Delivery;

internal interface IDelivery
{
    /// <summary>Which channel this delivery represents, for routing.</summary>
    public NotificationChannel Channel { get; }

    /// <summary>True when this delivery is configured enough to send.</summary>
    public bool IsActive { get; }

    public void Deliver(string title, string text);
}

public static class MasterDelivery
{
    private static readonly IReadOnlyList<IDelivery> Deliveries =
    [
        new NtfyDelivery(),
        new DiscordDelivery()
    ];

    /// <summary>Deliver to every active delivery whose channel is in <paramref name="channels"/>.</summary>
    public static void Deliver(string title, string text, NotificationChannel channels)
    {
        foreach (var delivery in Deliveries)
            if (channels.HasFlag(delivery.Channel) && delivery.IsActive)
                delivery.Deliver(title, text);
    }

    /// <summary>Convenience for the test button: send to all configured channels.</summary>
    public static void DeliverToAll(string title, string text)
        => Deliver(title, text, NotificationChannel.Both);
}
