using System.Globalization;
using MogCoach.Core.Model;

namespace MogCoach.Ingest.Iinact;

/// <summary>
/// Parses FFXIV network log lines (the pipe-delimited ACT format that IINACT emits as
/// <c>rawLine</c> and that ACT writes to its Network*.log files) into <see cref="CombatEvent"/>s.
/// <para>
/// Only the fields coaching needs are lifted. Damage amount decoding from the ability flags is
/// intentionally NOT done here — it is fiddly and error-prone; DPS/throughput metrics are meant
/// to come from IINACT's aggregated encounter data instead (see docs/SPEC.md, "Metrics source").
/// This parser owns the reliable, structural signals: casts, deaths, statuses, zone/director.
/// </para>
/// Field layouts reference the FFXIV ACT log line specification. Director command codes (type 33)
/// are the least-stable part and are centralised in <see cref="DirectorCommand"/> for easy fixup.
/// </summary>
public static class IinactLogLineParser
{
    // FFXIV network log line message types (field 0), decimal.
    private const int ChatLog = 0;
    private const int ChangeZone = 1;
    private const int ChangePrimaryPlayer = 2;
    private const int StartsCasting = 20;
    private const int Ability = 21;
    private const int AoeAbility = 22;
    private const int DoTHoT = 24;
    private const int Death = 25;
    private const int StatusAdd = 26;
    private const int StatusRemove = 30;
    private const int DirectorUpdate = 33;

    /// <summary>Director (type 33) command codes. VERIFY against a live capture before relying on these.</summary>
    public static class DirectorCommand
    {
        public const string Commence = "40000001";   // duty commenced
        public const string Recommence = "40000005";  // recommence after wipe
        public const string Wipe = "40000010";        // party wipe
        public const string Complete = "40000003";    // duty complete / clear
        public const string Fade = "40000006";        // fade out
    }

    /// <summary>
    /// Attempts to parse a single raw log line. Returns false for lines we don't model
    /// (they're skipped, not errors). <paramref name="playerId"/>/<paramref name="playerName"/>
    /// are used only to set <see cref="CombatEvent.IsSelf"/>.
    /// </summary>
    public static bool TryParse(
        string line,
        string? playerId,
        string? playerName,
        out CombatEvent ev)
    {
        ev = null!;
        if (string.IsNullOrWhiteSpace(line)) return false;

        var f = line.Split('|');
        if (f.Length < 2) return false;
        if (!int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var type)) return false;
        if (!DateTimeOffset.TryParse(f[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts))
            return false;

        switch (type)
        {
            case ChangeZone when f.Length >= 4:
                ev = Base(ts, CombatEventType.ZoneChange, line) with { TargetName = f[3], AbilityId = f[2] };
                return true;

            case ChangePrimaryPlayer when f.Length >= 4:
                ev = Base(ts, CombatEventType.Unknown, line) with { SourceId = f[2], SourceName = f[3] };
                return true;

            case StartsCasting when f.Length >= 8:
                ev = Base(ts, CombatEventType.Cast, line) with
                {
                    SourceId = f[2], SourceName = f[3],
                    AbilityId = f[4], AbilityName = f[5],
                    TargetId = f[6], TargetName = f[7],
                    IsSelf = IsSelf(f[2], f[3], playerId, playerName),
                };
                return true;

            case Ability or AoeAbility when f.Length >= 8:
                ev = Base(ts, CombatEventType.Ability, line) with
                {
                    SourceId = f[2], SourceName = f[3],
                    AbilityId = f[4], AbilityName = f[5],
                    TargetId = f[6], TargetName = f[7],
                    IsSelf = IsSelf(f[2], f[3], playerId, playerName),
                    // Amount deliberately left null — see class remarks (metrics come from aggregated data).
                };
                return true;

            case DoTHoT when f.Length >= 2:
                ev = Base(ts, CombatEventType.DotTick, line);
                return true;

            case Death when f.Length >= 4:
                ev = Base(ts, CombatEventType.Death, line) with
                {
                    TargetId = f[2], TargetName = f[3],
                    SourceId = f.Length > 4 ? f[4] : null,
                    SourceName = f.Length > 5 ? f[5] : null,
                    IsSelf = IsSelf(f[2], f[3], playerId, playerName),
                };
                return true;

            case StatusAdd when f.Length >= 9:
                ev = Base(ts, CombatEventType.StatusApply, line) with
                {
                    AbilityId = f[2], AbilityName = f[3],
                    SourceId = f[5], SourceName = f[6],
                    TargetId = f[7], TargetName = f[8],
                    IsSelf = IsSelf(f[7], f.Length > 8 ? f[8] : null, playerId, playerName),
                };
                return true;

            case StatusRemove when f.Length >= 9:
                ev = Base(ts, CombatEventType.StatusRemove, line) with
                {
                    AbilityId = f[2], AbilityName = f[3],
                    SourceId = f[5], SourceName = f[6],
                    TargetId = f[7], TargetName = f[8],
                    IsSelf = IsSelf(f[7], f.Length > 8 ? f[8] : null, playerId, playerName),
                };
                return true;

            case DirectorUpdate when f.Length >= 4:
                ev = ParseDirector(ts, f, line);
                return true;

            case ChatLog when f.Length >= 5:
                ev = Base(ts, CombatEventType.Chat, line) with { SourceName = f[3], AbilityName = f[4] };
                return true;

            default:
                return false;
        }
    }

    private static CombatEvent ParseDirector(DateTimeOffset ts, string[] f, string line)
    {
        // 33|time|instanceId|command|data0..3|checksum — command is the 4th field.
        var command = f.Length > 3 ? f[3] : string.Empty;
        var mapped = command switch
        {
            DirectorCommand.Commence or DirectorCommand.Recommence => CombatEventType.EncounterStart,
            DirectorCommand.Wipe => CombatEventType.Wipe,
            DirectorCommand.Complete => CombatEventType.EncounterEnd,
            _ => CombatEventType.Unknown,
        };
        return Base(ts, mapped, line) with { AbilityId = command };
    }

    private static CombatEvent Base(DateTimeOffset ts, CombatEventType type, string raw) =>
        new() { Timestamp = ts, Type = type, Raw = raw };

    private static bool IsSelf(string? id, string? name, string? playerId, string? playerName)
    {
        if (!string.IsNullOrEmpty(playerId) && id == playerId) return true;
        if (!string.IsNullOrEmpty(playerName) && !string.IsNullOrEmpty(name) &&
            string.Equals(name, playerName, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
