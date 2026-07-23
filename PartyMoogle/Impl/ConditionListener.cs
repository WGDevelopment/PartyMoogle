using Dalamud.Game.ClientState.Conditions;
using PartyMoogle.Notification;

namespace PartyMoogle.Impl;

/// <summary>
/// Event-driven vitals from ICondition flag transitions. Combat-end and
/// cutscene-end need no polling — the game raises ConditionChange(flag, value).
/// </summary>
public static class ConditionListener
{
    private static readonly ConditionFlag[] CutsceneFlags =
    [
        ConditionFlag.WatchingCutscene,
        ConditionFlag.WatchingCutscene78,
        ConditionFlag.OccupiedInCutSceneEvent
    ];

    public static void On()
    {
        Service.PluginLog.Debug("ConditionListener On");
        Service.Condition.ConditionChange += OnConditionChange;
    }

    public static void Off()
    {
        Service.PluginLog.Debug("ConditionListener Off");
        Service.Condition.ConditionChange -= OnConditionChange;
    }

    private static void OnConditionChange(ConditionFlag flag, bool value)
    {
        if (flag == ConditionFlag.InCombat && !value)
        {
            Notifier.Fire(EventKind.CombatEnd, "Combat ended", "You are no longer in combat.");
            return;
        }

        // A cutscene sets more than one flag; fire once, only when the last one clears.
        if (!value && System.Array.IndexOf(CutsceneFlags, flag) >= 0)
        {
            foreach (var f in CutsceneFlags)
                if (Service.Condition[f])
                    return; // another cutscene flag still set

            Notifier.Fire(EventKind.CutsceneEnd, "Cutscene ended", "The cutscene has ended.");
        }
    }
}
