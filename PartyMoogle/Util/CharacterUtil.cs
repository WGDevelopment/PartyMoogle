namespace PartyMoogle.Util;

public static class CharacterUtil
{
    // Raw AFK/idle status. The "always notify" decision now lives in the presence
    // gate (Configuration.AwayOnly), not here.
    public static bool IsClientAfk()
    {
        if (!Service.ClientState.IsLoggedIn ||
            Service.ObjectTable.LocalPlayer == null)
            return false;

        // 17 = AFK, 18 = Camera Mode (should catch idle camera. also has the effect of catching gpose!)
        // update 26/11/2024 (patch 7.11): the above IDs are still correct - not sure how long it'll persist
        // considering the game likes to change these every once in a while
        return Service.ObjectTable.LocalPlayer.OnlineStatus.RowId is 17 or 18;
    }
}
