using System.Collections.Generic;
using System.Linq;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace PartyMoogle.Util;

public static class CrossWorldPartyListSystem
{
    public delegate void CrossWorldJoinDelegate(CrossWorldMember m);

    public delegate void CrossWorldLeaveDelegate(CrossWorldMember m);

    private static readonly List<CrossWorldMember> members = new();
    private static List<CrossWorldMember> oldMembers = new();

    public static event CrossWorldJoinDelegate? OnJoin;
    public static event CrossWorldLeaveDelegate? OnLeave;

    /// <summary>
    /// Authoritative current member count. Per-event <see cref="CrossWorldMember.PartyCount"/>
    /// is unreliable during a bulk resync (joiners carry the new count, leavers the old),
    /// so consumers should read this instead.
    /// </summary>
    public static int CurrentCount { get; private set; }

    public static void Start()
    {
        Service.Framework.Update += Update;
    }

    public static void Stop()
    {
        Service.Framework.Update -= Update;
    }

    private static bool ListContainsMember(List<CrossWorldMember> l, CrossWorldMember m)
        => l.Any(a => a.Name == m.Name);

    private static unsafe void Update(IFramework framework)
    {
        if (!Service.ClientState.IsLoggedIn)
            return;

        if (!InfoProxyCrossRealm.IsCrossRealmParty())
            return;

        members.Clear();
        var partyCount = InfoProxyCrossRealm.GetPartyMemberCount();
        for (var i = 0u; i < partyCount; i++)
        {
            var addr = InfoProxyCrossRealm.GetGroupMember(i);
            var name = addr->NameString;
            var mObj = new CrossWorldMember
            {
                Name = name,
                PartyCount = partyCount,
                Level = addr->Level,
                JobId = addr->ClassJobId
            };
            members.Add(mObj);
        }

        CurrentCount = members.Count;

        // Always diff by identity, not just when the count changes: a simultaneous
        // join+leave keeps the count equal but is still a real membership change.
        foreach (var i in members)
            if (!ListContainsMember(oldMembers, i))
                OnJoin?.Invoke(i);

        foreach (var i in oldMembers)
            if (!ListContainsMember(members, i))
                OnLeave?.Invoke(i);

        // Fix potential broken references caused by memory semantics
        oldMembers = members.ToList();
    }

    public struct CrossWorldMember
    {
        public string Name;
        public int PartyCount;
        public uint Level;
        public uint JobId;
    }
}
