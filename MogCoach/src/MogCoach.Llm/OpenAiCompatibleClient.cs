using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MogCoach.Core.Abstractions;
using MogCoach.Core.Model;

namespace MogCoach.Llm;

/// <summary>
/// <see cref="ILlmClient"/> over the OpenAI Chat Completions schema, as implemented by
/// llama.cpp's llama-server. Text and image content parts are supported; images are inlined
/// as base64 data URIs (the endpoint runs a vision-capable model).
/// </summary>
public sealed class OpenAiCompatibleClient : ILlmClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;
    private readonly ILogger<OpenAiCompatibleClient> _log;

    public OpenAiCompatibleClient(
        HttpClient http,
        IOptions<OpenAiOptions> options,
        ILogger<OpenAiCompatibleClient> log)
    {
        _http = http;
        _options = options.Value;
        _log = log;
    }

    public async Task<LlmChatResponse> CompleteAsync(LlmChatRequest request, CancellationToken ct = default)
    {
        var payload = BuildPayload(request);
        var url = ChatCompletionsUrl(_options.BaseUrl);

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload.ToJsonString(JsonOpts), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        _log.LogDebug("LLM request -> {Url} ({Bytes} bytes)", url, payload.ToJsonString().Length);

        using var resp = await _http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"LLM endpoint returned {(int)resp.StatusCode}: {Truncate(body, 500)}");

        return Parse(body);
    }

    private JsonObject BuildPayload(LlmChatRequest request)
    {
        var messages = new JsonArray();
        foreach (var m in request.Messages)
            messages.Add(BuildMessage(m));

        var obj = new JsonObject
        {
            ["model"] = request.Model ?? _options.Model,
            ["messages"] = messages,
            ["temperature"] = request.Temperature,
            ["max_tokens"] = request.MaxTokens ?? _options.MaxTokens,
            ["stream"] = false,
        };
        if (request.JsonMode)
            obj["response_format"] = new JsonObject { ["type"] = "json_object" };

        return obj;
    }

    private static JsonObject BuildMessage(LlmMessage m)
    {
        var role = m.Role switch
        {
            LlmRole.System => "system",
            LlmRole.Assistant => "assistant",
            _ => "user",
        };

        // Plain-text single-part messages use the string form; anything with an image (or
        // multiple parts) uses the array/content-part form the vision API expects.
        var hasImage = m.Content.Any(p => p.IsImage);
        if (!hasImage && m.Content.Count <= 1)
        {
            return new JsonObject
            {
                ["role"] = role,
                ["content"] = m.Content.Count == 1 ? m.Content[0].Text ?? string.Empty : string.Empty,
            };
        }

        var parts = new JsonArray();
        foreach (var p in m.Content)
        {
            if (p.IsImage)
            {
                var b64 = Convert.ToBase64String(p.ImageBytes!);
                var mediaType = string.IsNullOrEmpty(p.ImageMediaType) ? "image/png" : p.ImageMediaType;
                parts.Add(new JsonObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JsonObject { ["url"] = $"data:{mediaType};base64,{b64}" },
                });
            }
            else
            {
                parts.Add(new JsonObject { ["type"] = "text", ["text"] = p.Text ?? string.Empty });
            }
        }

        return new JsonObject { ["role"] = role, ["content"] = parts };
    }

    private static LlmChatResponse Parse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var content = root
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;

        int? prompt = null, completion = null;
        string? model = root.TryGetProperty("model", out var mEl) ? mEl.GetString() : null;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt)) prompt = pt.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var ctk)) completion = ctk.GetInt32();
        }

        return new LlmChatResponse
        {
            Content = content,
            PromptTokens = prompt,
            CompletionTokens = completion,
            Model = model,
        };
    }

    internal static string ChatCompletionsUrl(string baseUrl)
    {
        var b = baseUrl.TrimEnd('/');
        if (!b.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            b += "/v1";
        return b + "/chat/completions";
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
