namespace PartyMoogle.Util;

/// <summary>
/// The single global "away-only" gate. When <see cref="Configuration.AwayOnly"/> is
/// on, notifications fire only when the player is away — defined as AFK status
/// (/afk, camera mode) OR the game window being unfocused/minimized.
/// </summary>
public static class PresenceGate
{
    public static bool IsAway()
    {
        return CharacterUtil.IsClientAfk() || WindowUtil.IsGameUnfocused();
    }

    public static bool ShouldNotify()
    {
        return !Plugin.Configuration.AwayOnly || IsAway();
    }
}
