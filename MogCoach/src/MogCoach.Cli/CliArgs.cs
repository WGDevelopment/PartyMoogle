namespace MogCoach.Cli;

/// <summary>Minimal argument parser: a leading command, then --key value / --key=value / --flag.</summary>
public sealed class CliArgs
{
    public string? Command { get; private init; }
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    public static CliArgs Parse(string[] args)
    {
        string? command = args.Length > 0 && !args[0].StartsWith('-') ? args[0] : null;
        var parsed = new CliArgs { Command = command };

        for (var i = command is null ? 0 : 1; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--")) continue;
            var key = a[2..];

            var eq = key.IndexOf('=');
            if (eq >= 0)
            {
                parsed._values[key[..eq]] = key[(eq + 1)..];
                continue;
            }

            // Value follows if the next token isn't another flag; otherwise it's a boolean flag.
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
            {
                parsed._values[key] = args[++i];
            }
            else
            {
                parsed._flags.Add(key);
            }
        }

        return parsed;
    }

    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;
    public string Get(string key, string fallback) => _values.TryGetValue(key, out var v) ? v : fallback;
    public bool Flag(string key) => _flags.Contains(key);
    public bool Has(string key) => _values.ContainsKey(key) || _flags.Contains(key);
}
