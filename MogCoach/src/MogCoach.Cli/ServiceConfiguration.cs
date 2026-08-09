using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MogCoach.Analysis;
using MogCoach.Analysis.Pipeline;
using MogCoach.Analysis.Report;
using MogCoach.Core.Abstractions;
using MogCoach.Ingest;
using MogCoach.Ingest.Iinact;
using MogCoach.Ingest.Sampler;
using MogCoach.Ingest.Screenpipe;
using MogCoach.Llm;
using MogCoach.References;
using MogCoach.References.Aoe;
using MogCoach.References.FfLogs;
using MogCoach.References.Rotations;

namespace MogCoach.Cli;

/// <summary>Wires the whole MogCoach graph into DI from configuration.</summary>
public static class ServiceConfiguration
{
    public static IServiceCollection AddMogCoach(this IServiceCollection services, IConfiguration config)
    {
        // Options
        services.Configure<OpenAiOptions>(config.GetSection(OpenAiOptions.SectionName));
        services.Configure<ScreenpipeOptions>(config.GetSection(ScreenpipeOptions.SectionName));
        services.PostConfigure<ScreenpipeOptions>(o => o.DbPath = PathUtil.Expand(o.DbPath));
        services.Configure<ReferenceOptions>(config.GetSection(ReferenceOptions.SectionName));
        services.Configure<FfLogsOptions>(config.GetSection(FfLogsOptions.SectionName));
        services.Configure<AnalysisOptions>(config.GetSection(AnalysisOptions.SectionName));
        services.Configure<SegmenterOptions>(config.GetSection("Segmenter"));

        // LLM (single Qwen2.5-VL endpoint serves text + vision)
        services.AddHttpClient<ILlmClient, OpenAiCompatibleClient>((sp, client) =>
        {
            var o = sp.GetRequiredService<IOptions<OpenAiOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        // Ingest — capture source picks IINACT .log vs sampler .mogcap by extension.
        services.AddSingleton<ITelemetrySourceFactory, CaptureSourceFactory>();
        services.AddSingleton<ISnapshotProvider, SamplerSnapshotProvider>();
        services.AddSingleton<IEncounterSegmenter>(sp =>
            new EncounterSegmenter(sp.GetRequiredService<IOptions<SegmenterOptions>>().Value));
        services.AddSingleton<IFrameStore, ScreenpipeSqliteFrameStore>();

        // References
        services.AddSingleton<IReferenceProvider, JsonRotationProvider>();
        services.AddSingleton<IAoeShapeProvider, JsonAoeShapeProvider>();
        services.AddHttpClient<IBenchmarkProvider, FfLogsClient>();

        // Analysis
        services.AddSingleton<IKeyframeSelector, KeyframeSelector>();
        services.AddTransient<TelemetryAnalyzer>();
        services.AddTransient<VisionAnalyzer>();
        services.AddTransient<PositionalAnalyzer>();
        services.AddSingleton<FindingFuser>();
        services.AddTransient<ICoachingPipeline, CoachingPipeline>();

        // Report renderers, keyed by extension
        services.AddSingleton<IReportRenderer, MarkdownReportRenderer>();
        services.AddSingleton<IReportRenderer, HtmlReportRenderer>();

        return services;
    }
}
