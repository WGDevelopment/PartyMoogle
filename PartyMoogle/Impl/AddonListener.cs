using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;

namespace PartyMoogle.Impl;

/// <summary>
/// Phase 3 (hooks/addons) foundation. Addon names are runtime game strings that
/// can't be verified from the Dalamud assemblies, so this ships in DISCOVERY mode:
/// a global PostSetup listener that logs every addon name as it opens. Trigger a
/// ready check / trade request / party invite with discovery on, read the names
/// from the Dalamud log, then wire the specific events in <see cref="OnPostSetup"/>.
///
/// Toggle with:  /partymoogle addonspy
/// </summary>
public static class AddonListener
{
    public static bool Discovery;

    public static void On()
    {
        Service.PluginLog.Debug("AddonListener On");
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, OnPostSetup);
    }

    public static void Off()
    {
        Service.PluginLog.Debug("AddonListener Off");
        Service.AddonLifecycle.UnregisterListener(OnPostSetup);
    }

    private static void OnPostSetup(AddonEvent type, AddonArgs args)
    {
        if (Discovery)
            Service.PluginLog.Information($"[AddonSpy] PostSetup: {args.AddonName}");

        // Phase 3 events get wired here once names are confirmed from discovery, e.g.:
        //   switch (args.AddonName)
        //   {
        //       case "ReadyCheck": Notifier.Fire(EventKind.ReadyCheck, "Ready check", "..."); break;
        //       case "Trade":      Notifier.Fire(EventKind.TradeRequest, "Trade request", "..."); break;
        //   }
    }
}
