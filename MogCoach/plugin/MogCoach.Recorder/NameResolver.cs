using System.Collections.Generic;
using System;

namespace MogCoach.Recorder;

/// <summary>
/// Resolves action/status ids to display names via Lumina game sheets, cached so each id is looked up
/// once. Writing names into the capture means reports read "Fell Cleave", not "1CE8".
/// <para>
/// VERIFY against your Dalamud/Lumina: sheet types live in <c>Lumina.Excel.Sheets</c> and rows expose
/// <c>Name.ExtractText()</c> on current versions; older ones differ. Failures fall back to null (the
/// report then shows the id), so this never breaks capture — but a wrong member name is a compile error.
/// </para>
/// </summary>
internal static class NameResolver
{
    private static readonly Dictionary<uint, string?> Actions = new();
    private static readonly Dictionary<uint, string?> Statuses = new();
    private static readonly Dictionary<uint, string?> Zones = new();

    /// <summary>Resolves a TerritoryType id to its place name (the duty/area name).</summary>
    public static string? Zone(uint territoryTypeId)
    {
        if (territoryTypeId == 0) return null;
        if (Zones.TryGetValue(territoryTypeId, out var cached)) return cached;
        var name = Resolve(() =>
            Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()
                ?.GetRowOrDefault(territoryTypeId)?.PlaceName.Value.Name.ExtractText());
        Zones[territoryTypeId] = name;
        return name;
    }

    public static string? Action(uint id)
    {
        if (id == 0) return null;
        if (Actions.TryGetValue(id, out var cached)) return cached;
        var name = Resolve(() =>
            Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>()?.GetRowOrDefault(id)?.Name.ExtractText());
        Actions[id] = name;
        return name;
    }

    public static string? Status(uint id)
    {
        if (id == 0) return null;
        if (Statuses.TryGetValue(id, out var cached)) return cached;
        var name = Resolve(() =>
            Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>()?.GetRowOrDefault(id)?.Name.ExtractText());
        Statuses[id] = name;
        return name;
    }

    private static string? Resolve(Func<string?> f)
    {
        try { var s = f(); return string.IsNullOrWhiteSpace(s) ? null : s; }
        catch { return null; }
    }
}
