using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MogCoach.Cli;
using MogCoach.Core.Abstractions;
using MogCoach.Core.Model;
using MogCoach.Ingest.Iinact;

var parsed = CliArgs.Parse(args);

if (parsed.Command is null or "help" or "--help")
{
    PrintUsage();
    return 0;
}

// Content root = the exe directory so appsettings.json (copied to output) always loads, regardless
// of the working directory the command was launched from. Args are intentionally not passed to the
// builder so our CLI flags aren't consumed by the command-line configuration provider.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
builder.Services.AddMogCoach(builder.Configuration);
using var host = builder.Build();

var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("mogcoach");
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    return parsed.Command switch
    {
        "analyze" => await RunAnalyzeAsync(host, parsed, cts.Token),
        "record" => await RunRecordAsync(host, parsed, cts.Token),
        _ => Unknown(parsed.Command),
    };
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}
catch (Exception ex)
{
    log.LogError(ex, "Fatal error");
    return 1;
}

static async Task<int> RunAnalyzeAsync(IHost host, CliArgs a, CancellationToken ct)
{
    var capture = a.Get("capture");
    if (string.IsNullOrWhiteSpace(capture))
    {
        Console.Error.WriteLine("--capture <path> is required.");
        return 2;
    }

    var request = new CoachingRequest
    {
        CapturePath = capture,
        Mode = ParseEnum(a.Get("mode"), CoachingMode.Rotation),
        Job = ParseEnum(a.Get("job"), Job.Unknown),
        PlayerName = a.Get("player"),
        OnlyPullId = a.Get("pull"),
        EnableVision = !a.Flag("no-vision"),
    };

    var pipeline = host.Services.GetRequiredService<ICoachingPipeline>();
    var reports = await pipeline.RunAsync(request, ct);

    if (reports.Count == 0)
    {
        Console.WriteLine("No pulls found in capture.");
        return 0;
    }

    var format = a.Get("format", "md").ToLowerInvariant();
    var renderer = host.Services.GetServices<IReportRenderer>().FirstOrDefault(r => r.Extension == format)
                   ?? host.Services.GetServices<IReportRenderer>().First();

    var outPath = a.Get("out") ?? Path.Combine("reports", $"session-{DateTime.Now:yyyyMMdd-HHmmss}.{renderer.Extension}");
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
    await File.WriteAllTextAsync(outPath, renderer.RenderSession(reports), ct);

    Console.WriteLine($"Analyzed {reports.Count} pull(s). Report: {outPath}");
    foreach (var r in reports)
        Console.WriteLine($"  {r.PullId}: {r.EncounterName} — {r.Findings.Count} finding(s), {r.Metrics.DeathCount} death(s)");
    return 0;
}

static async Task<int> RunRecordAsync(IHost host, CliArgs a, CancellationToken ct)
{
    var cfg = host.Services.GetRequiredService<IConfiguration>();
    var iinact = cfg.GetSection(IinactOptions.SectionName).Get<IinactOptions>() ?? new IinactOptions();

    var outPath = a.Get("out") ?? Path.Combine("captures", $"session-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger<IinactWebSocketRecorder>();
    var recorder = new IinactWebSocketRecorder(new Uri(iinact.WebSocketUrl), logger);

    Console.WriteLine($"Recording from {iinact.WebSocketUrl} -> {outPath}");
    Console.WriteLine("Play FFXIV; press Ctrl+C to stop.");
    try { await recorder.RecordAsync(outPath, ct); }
    catch (OperationCanceledException) { /* expected on Ctrl+C */ }
    Console.WriteLine($"Saved capture: {outPath}");
    return 0;
}

static int Unknown(string cmd)
{
    Console.Error.WriteLine($"Unknown command: {cmd}");
    PrintUsage();
    return 2;
}

static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct, Enum =>
    Enum.TryParse<TEnum>(value, ignoreCase: true, out var v) ? v : fallback;

static void PrintUsage()
{
    Console.WriteLine("""
        MogCoach — FFXIV post-session coaching (record → transcribe → analyze → recommend)

        Usage:
          mogcoach record  [--out captures/session.log]
          mogcoach analyze --capture <path> [options]

        analyze options:
          --capture <path>     IINACT network-log capture (required)
          --mode <mode>        rotation | mechanics | awareness   (default: rotation)
          --job <job>          e.g. Warrior, WhiteMage, BlackMage  (default: infer/Unknown)
          --player "<name>"    local player name for self-attribution
          --pull <id>          analyze only one pull (e.g. pull-03)
          --no-vision          skip the vision pass (telemetry only)
          --format <fmt>       md | html                          (default: md)
          --out <path>         output report path

        Config: appsettings.json (+ appsettings.Local.json for secrets). See docs/SPEC.md.
        """);
}
