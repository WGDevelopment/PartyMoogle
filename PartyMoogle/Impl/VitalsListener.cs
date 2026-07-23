using System;
using Dalamud.Plugin.Services;
using PartyMoogle.Notification;

namespace PartyMoogle.Impl;

/// <summary>
/// The one genuinely poll-based source: HP has no event, so we sample the local
/// player on a throttled Framework tick. Low-HP is edge-triggered with hysteresis
/// (won't re-fire until HP recovers past a buffer) so a fight hovering near the
/// threshold doesn't spam. Death is edge-triggered on alive -> dead.
/// </summary>
public static class VitalsListener
{
    private const long PollIntervalMs = 1000;

    // Re-arm only after HP climbs this many points above the threshold, to avoid
    // flapping when HP oscillates around the line.
    private const int RearmBufferPercent = 5;

    private static long lastPoll;
    private static bool lowHpArmed = true;
    private static bool wasDead;

    public static void On()
    {
        Service.PluginLog.Debug("VitalsListener On");
        Service.Framework.Update += Tick;
    }

    public static void Off()
    {
        Service.PluginLog.Debug("VitalsListener Off");
        Service.Framework.Update -= Tick;
        lowHpArmed = true;
        wasDead = false;
    }

    private static void Tick(IFramework framework)
    {
        var now = Environment.TickCount64;
        if (now - lastPoll < PollIntervalMs)
            return;
        lastPoll = now;

        var player = Service.ObjectTable.LocalPlayer;
        if (player == null || player.MaxHp == 0)
            return;

        // Death (edge: alive -> dead)
        if (player.IsDead)
        {
            if (!wasDead)
            {
                wasDead = true;
                Notifier.Fire(EventKind.Death, "You were KO'd", "You have been defeated.");
            }
            return; // don't also fire low-HP for a dead player
        }
        wasDead = false;

        // Low HP (edge with hysteresis)
        var rule = Notifier.GetRule(EventKind.LowHp);
        if (!rule.Enabled || rule.Threshold <= 0)
            return;

        var pct = (int)(player.CurrentHp * 100 / player.MaxHp);

        if (lowHpArmed && pct <= rule.Threshold)
        {
            lowHpArmed = false;
            Notifier.Fire(EventKind.LowHp, "Low HP", $"HP at {pct}% ({player.CurrentHp}/{player.MaxHp}).");
        }
        else if (!lowHpArmed && pct >= rule.Threshold + RearmBufferPercent)
        {
            lowHpArmed = true;
        }
    }
}
