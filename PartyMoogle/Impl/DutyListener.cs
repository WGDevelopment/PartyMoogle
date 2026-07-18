using Dalamud.Game.DutyState;
using Dalamud.Utility;
using Lumina.Excel.Sheets;
using PartyMoogle.Notification;

namespace PartyMoogle.Impl;

/// <summary>
/// Duty / content events. Queue-ready via IClientState.CfPop; the in-instance
/// lifecycle (start/wipe/recommence/complete) via IDutyState.
/// </summary>
public static class DutyListener
{
    public static void On()
    {
        Service.PluginLog.Debug("DutyListener On");
        Service.ClientState.CfPop += OnDutyPop;
        Service.DutyState.DutyStarted += OnDutyStarted;
        Service.DutyState.DutyWiped += OnDutyWiped;
        Service.DutyState.DutyRecommenced += OnDutyRecommenced;
        Service.DutyState.DutyCompleted += OnDutyCompleted;
    }

    public static void Off()
    {
        Service.PluginLog.Debug("DutyListener Off");
        Service.ClientState.CfPop -= OnDutyPop;
        Service.DutyState.DutyStarted -= OnDutyStarted;
        Service.DutyState.DutyWiped -= OnDutyWiped;
        Service.DutyState.DutyRecommenced -= OnDutyRecommenced;
        Service.DutyState.DutyCompleted -= OnDutyCompleted;
    }

    private static string DutyName(ContentFinderCondition e)
        => e.RowId == 0 ? "Duty Roulette" : e.Name.ToDalamudString().TextValue;

    private static void OnDutyPop(ContentFinderCondition e)
        => Notifier.Fire(EventKind.DutyPop, "Duty pop", $"Duty registered: '{DutyName(e)}'.");

    private static void OnDutyStarted(IDutyStateEventArgs args)
        => Notifier.Fire(EventKind.DutyStarted, "Duty commenced", "The duty has started.");

    private static void OnDutyWiped(IDutyStateEventArgs args)
        => Notifier.Fire(EventKind.DutyWiped, "Party wiped", "Everyone in the party has fallen.");

    private static void OnDutyRecommenced(IDutyStateEventArgs args)
        => Notifier.Fire(EventKind.DutyRecommenced, "Duty recommenced", "The duty has recommenced.");

    private static void OnDutyCompleted(IDutyStateEventArgs args)
        => Notifier.Fire(EventKind.DutyCompleted, "Duty completed", "The duty was completed.");
}
