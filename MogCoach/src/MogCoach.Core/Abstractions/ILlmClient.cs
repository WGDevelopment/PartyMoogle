using MogCoach.Core.Model;

namespace MogCoach.Core.Abstractions;

/// <summary>
/// A chat completion client against an OpenAI-compatible endpoint (llama.cpp llama-server).
/// The same client serves text-only prompts (telemetry analysis) and multimodal prompts
/// (keyframe vision) — a single Qwen2.5-VL model backs both.
/// </summary>
public interface ILlmClient
{
    Task<LlmChatResponse> CompleteAsync(LlmChatRequest request, CancellationToken ct = default);
}
