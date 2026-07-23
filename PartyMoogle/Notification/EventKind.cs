using System.Collections.Generic;

namespace PartyMoogle.Notification;

/// <summary>
/// Every notifiable event. Grows per phase; only kinds with a wired listener
/// appear here. Config UI and rules are keyed on this.
/// </summary>
public enum EventKind
{
    // Duty / content
    DutyPop,
    DutyStarted,
    DutyWiped,
    DutyRecommenced,
    DutyCompleted,

    // Party
    PartyJoin,
    PartyLeave,
    PartyFull,

    // Chat (sub-filtered by XivChatType + optional keyword)
    Chat,

    // Character state
    Login,
    Logout,
    LevelUp,
    JobChange,
    ZoneChange,
    EnterPvP,
    LeavePvP,

    // Vitals / combat (Phase 2: condition events + HP polling)
    CombatEnd,
    CutsceneEnd,
    LowHp,
    Death
}

/// <summary>Which extra filter widgets the config UI draws for an event.</summary>
public enum FilterType
{
    None,
    Chat,
    Threshold
}

public enum EventCategory
{
    Duty,
    Party,
    Chat,
    Character,
    Vitals
}

public readonly record struct EventMeta(string Display, EventCategory Category, FilterType Filter);

public static class EventCatalog
{
    public static readonly IReadOnlyDictionary<EventKind, EventMeta> Meta = new Dictionary<EventKind, EventMeta>
    {
        [EventKind.DutyPop]         = new("Duty pop / queue ready", EventCategory.Duty, FilterType.None),
        [EventKind.DutyStarted]     = new("Duty commenced",         EventCategory.Duty, FilterType.None),
        [EventKind.DutyWiped]       = new("Party wiped",            EventCategory.Duty, FilterType.None),
        [EventKind.DutyRecommenced] = new("Duty recommenced",       EventCategory.Duty, FilterType.None),
        [EventKind.DutyCompleted]   = new("Duty completed",         EventCategory.Duty, FilterType.None),

        [EventKind.PartyJoin]  = new("Party member joins", EventCategory.Party, FilterType.None),
        [EventKind.PartyLeave] = new("Party member leaves", EventCategory.Party, FilterType.None),
        [EventKind.PartyFull]  = new("Party full (8/8)",   EventCategory.Party, FilterType.None),

        [EventKind.Chat] = new("Chat message", EventCategory.Chat, FilterType.Chat),

        [EventKind.Login]      = new("Login",           EventCategory.Character, FilterType.None),
        [EventKind.Logout]     = new("Logout",          EventCategory.Character, FilterType.None),
        [EventKind.LevelUp]    = new("Level up",        EventCategory.Character, FilterType.None),
        [EventKind.JobChange]  = new("Job / class change", EventCategory.Character, FilterType.None),
        [EventKind.ZoneChange] = new("Zone change",     EventCategory.Character, FilterType.None),
        [EventKind.EnterPvP]   = new("Enter PvP",       EventCategory.Character, FilterType.None),
        [EventKind.LeavePvP]   = new("Leave PvP",       EventCategory.Character, FilterType.None),

        [EventKind.CombatEnd]   = new("Combat ended",   EventCategory.Vitals, FilterType.None),
        [EventKind.CutsceneEnd] = new("Cutscene ended", EventCategory.Vitals, FilterType.None),
        [EventKind.LowHp]       = new("Low HP",         EventCategory.Vitals, FilterType.Threshold),
        [EventKind.Death]       = new("Death (KO)",     EventCategory.Vitals, FilterType.None)
    };
}
