using Dalamud.Utility;
using Lumina.Excel.Sheets;
using PartyMoogle.Notification;

namespace PartyMoogle.Impl;

/// <summary>
/// Named IClientState events: login/logout, level up, job change, zone change, PvP.
/// All cheap event subscriptions.
/// </summary>
public static class ClientStateListener
{
    public static void On()
    {
        Service.PluginLog.Debug("ClientStateListener On");
        Service.ClientState.Login += OnLogin;
        Service.ClientState.Logout += OnLogout;
        Service.ClientState.LevelChanged += OnLevelChanged;
        Service.ClientState.ClassJobChanged += OnClassJobChanged;
        Service.ClientState.TerritoryChanged += OnTerritoryChanged;
        Service.ClientState.EnterPvP += OnEnterPvP;
        Service.ClientState.LeavePvP += OnLeavePvP;
    }

    public static void Off()
    {
        Service.PluginLog.Debug("ClientStateListener Off");
        Service.ClientState.Login -= OnLogin;
        Service.ClientState.Logout -= OnLogout;
        Service.ClientState.LevelChanged -= OnLevelChanged;
        Service.ClientState.ClassJobChanged -= OnClassJobChanged;
        Service.ClientState.TerritoryChanged -= OnTerritoryChanged;
        Service.ClientState.EnterPvP -= OnEnterPvP;
        Service.ClientState.LeavePvP -= OnLeavePvP;
    }

    private static void OnLogin()
        => Notifier.Fire(EventKind.Login, "Login", "Character logged in.");

    // NOTE: Logout delegate carries (type, code) in recent SDKs — verify signature.
    private static void OnLogout(int type, int code)
        => Notifier.Fire(EventKind.Logout, "Logout", "Character logged out.");

    private static void OnLevelChanged(uint classJobId, uint level)
        => Notifier.Fire(EventKind.LevelUp, "Level up", $"Reached level {level}.");

    private static void OnClassJobChanged(uint classJobId)
        => Notifier.Fire(EventKind.JobChange, "Job change", $"Switched to class/job #{classJobId}.");

    private static void OnTerritoryChanged(uint territoryId)
    {
        var name = "";
        if (Service.DataManager.GetExcelSheet<TerritoryType>()!.TryGetRow(territoryId, out var row))
            name = row.PlaceName.Value.Name.ToDalamudString().TextValue;
        var body = string.IsNullOrEmpty(name) ? $"Entered territory #{territoryId}." : $"Entered {name}.";
        Notifier.Fire(EventKind.ZoneChange, "Zone change", body);
    }

    private static void OnEnterPvP()
        => Notifier.Fire(EventKind.EnterPvP, "Enter PvP", "Entered a PvP area.");

    private static void OnLeavePvP()
        => Notifier.Fire(EventKind.LeavePvP, "Leave PvP", "Left the PvP area.");
}
