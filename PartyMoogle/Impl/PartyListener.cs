using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using PartyMoogle.Notification;
using PartyMoogle.Util;

namespace PartyMoogle.Impl;

/// <summary>
/// Party membership.
///
/// Two problems observed in live data that this guards against:
///  1. A wholesale party swap (duty end, zone transition) makes the underlying poll
///     emit a join for every new member and a leave for every old member in the same
///     frame — 7-11 notifications in one second. We buffer changes and, if the batch
///     looks like a resync, collapse it instead of spamming.
///  2. "Party full" was level-triggered on <c>PartyCount == 8</c> per joining member,
///     so every member of an already-full party fired it. It is now edge-triggered on
///     the &lt;8 -&gt; 8 transition and fires at most once per fill.
/// </summary>
public static class PartyListener
{
    /// A batch with at least this many changes is treated as a resync, not real activity.
    private const int BulkThreshold = 4;

    /// Quiet period after the last change before a batch is flushed.
    private const long FlushDelayMs = 1500;

    private static readonly List<CrossWorldPartyListSystem.CrossWorldMember> pendingJoins = new();
    private static readonly List<CrossWorldPartyListSystem.CrossWorldMember> pendingLeaves = new();

    private static long lastChangeTick;
    private static int lastKnownCount = -1;

    public static void On()
    {
        Service.PluginLog.Debug("PartyListener On");
        CrossWorldPartyListSystem.OnJoin += OnJoin;
        CrossWorldPartyListSystem.OnLeave += OnLeave;
        Service.Framework.Update += Tick;
    }

    public static void Off()
    {
        Service.PluginLog.Debug("PartyListener Off");
        CrossWorldPartyListSystem.OnJoin -= OnJoin;
        CrossWorldPartyListSystem.OnLeave -= OnLeave;
        Service.Framework.Update -= Tick;
        pendingJoins.Clear();
        pendingLeaves.Clear();
        lastKnownCount = -1;
    }

    private static void OnJoin(CrossWorldPartyListSystem.CrossWorldMember m)
    {
        pendingJoins.Add(m);
        lastChangeTick = Environment.TickCount64;
    }

    private static void OnLeave(CrossWorldPartyListSystem.CrossWorldMember m)
    {
        pendingLeaves.Add(m);
        lastChangeTick = Environment.TickCount64;
    }

    private static void Tick(IFramework framework)
    {
        if (pendingJoins.Count == 0 && pendingLeaves.Count == 0)
            return;

        if (Environment.TickCount64 - lastChangeTick < FlushDelayMs)
            return;

        Flush();
    }

    private static void Flush()
    {
        var joins = new List<CrossWorldPartyListSystem.CrossWorldMember>(pendingJoins);
        var leaves = new List<CrossWorldPartyListSystem.CrossWorldMember>(pendingLeaves);
        pendingJoins.Clear();
        pendingLeaves.Clear();

        var total = joins.Count + leaves.Count;
        var current = CrossWorldPartyListSystem.CurrentCount;
        var previous = lastKnownCount;
        lastKnownCount = current;

        if (total >= BulkThreshold)
        {
            // Resync, not real activity — collapse it. The fill transition below still
            // gets its single notification if the party genuinely went from <8 to 8.
            Service.PluginLog.Debug(
                $"Collapsed party resync: {joins.Count} joins + {leaves.Count} leaves -> {current}/8");
        }
        else
        {
            foreach (var m in leaves)
                Notifier.Fire(EventKind.PartyLeave,
                              $"{current}/8: Party leave",
                              $"{Describe(m)} has left the party.");

            foreach (var m in joins)
                Notifier.Fire(EventKind.PartyJoin,
                              $"{current}/8: Party join",
                              $"{Describe(m)} joins the party.");
        }

        // Edge-triggered: fires once, only on an actual <8 -> 8 transition.
        if (previous >= 0 && previous < 8 && current == 8)
            Notifier.Fire(EventKind.PartyFull, "Party full", "All spots are filled.");
    }

    private static string Describe(CrossWorldPartyListSystem.CrossWorldMember m)
        => $"{m.Name} (Lv{m.Level} {LuminaDataUtil.GetJobAbbreviation(m.JobId)})";
}
