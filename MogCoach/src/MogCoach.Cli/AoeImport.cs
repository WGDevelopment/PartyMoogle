using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MogCoach.Cli;

/// <summary>
/// `mogcoach import-aoe --bossmod &lt;path&gt; [--out reference-data/aoe/shapes.json]` — best-effort
/// scraper that reads AOE shapes from a local BossMod source tree and drafts a shapes.json for the
/// positional pass.
/// <para>
/// Honest about yield: BossMod binds shapes to abilities in component code, not a table, so only shapes
/// with an <c>AID.Name</c> reference within a few lines (resolvable via the file's <c>enum AID</c>) are
/// auto-bound. Everything else is written to a sibling <c>shapes.unbound.json</c> catalog (file, line,
/// shape, context) for manual curation. Existing entries in the output are preserved (hand-curated wins).
/// </para>
/// BossMod shape → MogCoach mapping: Circle→Circle, Donut→Donut, Cone→Cone(halfAngle°),
/// Rect(front,halfW,back)→Line(length=front, halfWidth) [back is dropped — noted in the catalog],
/// Cross→Cross. Only literal-number constructions are captured (named-variable radii can't be resolved).
/// </summary>
public static class AoeImport
{
    private static readonly JsonSerializerOptions JsonOut = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Regex CtorRegex =
        new(@"new\s+AOEShape(Circle|Donut|Cone|Rect|Cross)\s*\(", RegexOptions.Compiled);
    private static readonly Regex NumberRegex = new(@"-?\d+(?:\.\d+)?", RegexOptions.Compiled);
    private static readonly Regex AidRefRegex = new(@"AID\.(\w+)", RegexOptions.Compiled);
    private static readonly Regex AidDefRegex =
        new(@"(\w+)\s*=\s*(0x[0-9A-Fa-f]+|\d+)", RegexOptions.Compiled);

    public static int Run(CliArgs a)
    {
        var bossmod = a.Get("bossmod");
        if (string.IsNullOrWhiteSpace(bossmod) || !Directory.Exists(bossmod))
        {
            Console.Error.WriteLine("--bossmod <path-to-bossmod-source> is required and must exist.");
            return 2;
        }
        var outPath = a.Get("out") ?? Path.Combine("reference-data", "aoe", "shapes.json");
        var unboundPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outPath))!, "shapes.unbound.json");

        var bound = new Dictionary<string, ShapeDto>(StringComparer.OrdinalIgnoreCase);
        var unbound = new List<UnboundEntry>();
        var files = Directory.EnumerateFiles(bossmod, "*.cs", SearchOption.AllDirectories);
        var fileCount = 0;

        foreach (var file in files)
        {
            fileCount++;
            string[] lines;
            try { lines = File.ReadAllLines(file); }
            catch { continue; }

            var aid = BuildAidMap(lines);
            var rel = Path.GetRelativePath(bossmod, file);

            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match m in CtorRegex.Matches(lines[i]))
                {
                    var kind = m.Groups[1].Value;
                    var args = BalancedArgs(lines[i], m.Index + m.Length - 1);
                    var nums = NumberRegex.Matches(args)
                        .Select(x => double.Parse(x.Value, CultureInfo.InvariantCulture)).ToList();
                    var (shape, ok) = MapShape(kind, nums);
                    if (!ok) continue;

                    var id = FindNearbyAid(lines, i, aid);
                    if (id is not null)
                        bound.TryAdd(id, shape); // first binding wins; don't clobber
                    else
                        unbound.Add(new UnboundEntry(rel, i + 1, kind, shape, lines[i].Trim()));
                }
            }
        }

        MergeAndWrite(outPath, bound);
        File.WriteAllText(unboundPath, JsonSerializer.Serialize(unbound, JsonOut));

        Console.WriteLine($"Scanned {fileCount} file(s).");
        Console.WriteLine($"Auto-bound {bound.Count} shape(s) → {outPath}");
        Console.WriteLine($"{unbound.Count} unbound shape(s) for review → {unboundPath}");
        Console.WriteLine("Review both before trusting positional findings — this is a draft, not authoritative.");
        return 0;
    }

    private static (ShapeDto shape, bool ok) MapShape(string kind, List<double> n) => kind switch
    {
        "Circle" when n.Count >= 1 => (new ShapeDto { Kind = "Circle", Radius = (float)n[0] }, true),
        "Donut" when n.Count >= 2 => (new ShapeDto { Kind = "Donut", InnerRadius = (float)n[0], Radius = (float)n[1] }, true),
        "Cone" when n.Count >= 2 => (new ShapeDto { Kind = "Cone", Radius = (float)n[0], HalfAngleDegrees = (float)n[1] }, true),
        "Rect" when n.Count >= 2 => (new ShapeDto { Kind = "Line", Length = (float)n[0], HalfWidth = (float)n[1] }, true),
        "Cross" when n.Count >= 2 => (new ShapeDto { Kind = "Cross", Length = (float)n[0], HalfWidth = (float)n[1] }, true),
        _ => (new ShapeDto(), false),
    };

    /// <summary>Returns the substring inside the balanced parens beginning at <paramref name="openParenIndex"/>.</summary>
    private static string BalancedArgs(string line, int openParenIndex)
    {
        if (openParenIndex < 0 || openParenIndex >= line.Length || line[openParenIndex] != '(') return "";
        var depth = 0;
        var start = openParenIndex + 1;
        for (var i = openParenIndex; i < line.Length; i++)
        {
            if (line[i] == '(') depth++;
            else if (line[i] == ')')
            {
                depth--;
                if (depth == 0) return line[start..i];
            }
        }
        return line[start..]; // unterminated on this line (multi-line ctor) — best effort
    }

    private static string? FindNearbyAid(string[] lines, int i, Dictionary<string, string> aid)
    {
        for (var d = 0; d <= 3; d++)
        {
            foreach (var j in new[] { i - d, i + d })
            {
                if (j < 0 || j >= lines.Length) continue;
                var mm = AidRefRegex.Match(lines[j]);
                if (mm.Success && aid.TryGetValue(mm.Groups[1].Value, out var id)) return id;
            }
        }
        return null;
    }

    private static Dictionary<string, string> BuildAidMap(string[] lines)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var inEnum = false;
        var depth = 0;
        foreach (var raw in lines)
        {
            var line = raw;
            if (!inEnum)
            {
                if (line.Contains("enum AID")) { inEnum = true; depth += Count(line, '{') - Count(line, '}'); }
                continue;
            }
            depth += Count(line, '{') - Count(line, '}');
            foreach (Match m in AidDefRegex.Matches(line))
            {
                var name = m.Groups[1].Value;
                var val = m.Groups[2].Value;
                uint num;
                if (val.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    uint.TryParse(val.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out num);
                else
                    uint.TryParse(val, out num);
                if (num != 0) map[name] = num.ToString("X");
            }
            if (depth <= 0) break; // left the enum block
        }
        return map;
    }

    private static int Count(string s, char c)
    {
        var n = 0;
        foreach (var ch in s) if (ch == c) n++;
        return n;
    }

    private static void MergeAndWrite(string outPath, Dictionary<string, ShapeDto> bound)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);

        // Preserve any existing (hand-curated) entries; only add ids we don't already have.
        JsonObject root;
        if (File.Exists(outPath))
        {
            try { root = JsonNode.Parse(File.ReadAllText(outPath))?.AsObject() ?? new JsonObject(); }
            catch { root = new JsonObject(); }
        }
        else
        {
            root = new JsonObject { ["//"] = "Generated draft — review before trusting. Hand-edits are preserved on re-import." };
        }

        foreach (var (id, shape) in bound)
            if (!root.ContainsKey(id))
                root[id] = JsonSerializer.SerializeToNode(shape, JsonOut);

        File.WriteAllText(outPath, root.ToJsonString(JsonOut));
    }

    private sealed record ShapeDto
    {
        [JsonPropertyName("kind")] public string Kind { get; init; } = "Circle";
        [JsonPropertyName("radius")] public float Radius { get; init; }
        [JsonPropertyName("innerRadius")] public float InnerRadius { get; init; }
        [JsonPropertyName("halfAngleDegrees")] public float HalfAngleDegrees { get; init; }
        [JsonPropertyName("length")] public float Length { get; init; }
        [JsonPropertyName("halfWidth")] public float HalfWidth { get; init; }
    }

    private sealed record UnboundEntry(string File, int Line, string Kind, ShapeDto Shape, string Context);
}
