using PartyMoogle.Notification;
using PartyMoogle.Util;

namespace PartyMoogle.Impl;

/// <summary>
/// Party membership via the existing cross-world-aware poll. Join fires either
/// PartyJoin or PartyFull (at 8/8); leave fires PartyLeave.
/// </summary>
public static class PartyListener
{
    public static void On()
    {
        Service.PluginLog.Debug("PartyListener On");
        CrossWorldPartyListSystem.OnJoin += OnJoin;
        CrossWorldPartyListSystem.OnLeave += OnLeave;
    }

    public static void Off()
    {
        Service.PluginLog.Debug("PartyListener Off");
        CrossWorldPartyListSystem.OnJoin -= OnJoin;
        CrossWorldPartyListSystem.OnLeave -= OnLeave;
    }

    private static void OnJoin(CrossWorldPartyListSystem.CrossWorldMember m)
    {
        var jobAbbr = LuminaDataUtil.GetJobAbbreviation(m.JobId);

        if (m.PartyCount == 8)
        {
            Notifier.Fire(EventKind.PartyFull,
                          "Party full",
                          $"{m.Name} (Lv{m.Level} {jobAbbr}) joins the party.\nAll spots are filled.");
        }
        else
        {
            Notifier.Fire(EventKind.PartyJoin,
                          $"{m.PartyCount}/8: Party join",
                          $"{m.Name} (Lv{m.Level} {jobAbbr}) joins the party.");
        }
    }

    private static void OnLeave(CrossWorldPartyListSystem.CrossWorldMember m)
    {
        var jobAbbr = LuminaDataUtil.GetJobAbbreviation(m.JobId);

        Notifier.Fire(EventKind.PartyLeave,
                      $"{m.PartyCount - 1}/8: Party leave",
                      $"{m.Name} (Lv{m.Level} {jobAbbr}) has left the party.");
    }
}
