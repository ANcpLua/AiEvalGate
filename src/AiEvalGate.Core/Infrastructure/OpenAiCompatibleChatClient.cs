using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace AiEvalGate.Core.Infrastructure;

/// <summary>
/// Minimal, dependency-free <see cref="IChatClient"/> for any OpenAI-compatible
/// <c>/v1/chat/completions</c> endpoint — for example a local <c>bitnet.cpp</c> / <c>llama.cpp</c>
/// <c>llama-server</c>, Ollama, or vLLM — so the evaluation judge can be a local model instead of a
/// hosted API.
/// </summary>
/// <remarks>
/// Sends the legacy <c>max_tokens</c> field (not <c>max_completion_tokens</c>) so older
/// <c>llama-server</c> builds honor the output cap instead of generating until the context fills.
/// Only non-streaming <see cref="GetResponseAsync"/> is implemented — the quality evaluators and the
/// reviewer agents use it; <see cref="GetStreamingResponseAsync"/> throws.
/// </remarks>
public sealed class OpenAiCompatibleChatClient : IChatClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly Uri _completionsUri;
    private readonly string _model;
    private readonly int _defaultMaxOutputTokens;
    private readonly ChatClientMetadata _metadata;

    /// <summary>
    /// Creates a client targeting <paramref name="endpoint"/> (the base of the API, e.g.
    /// <c>http://localhost:11434/v1/</c>).
    /// </summary>
    /// <param name="endpoint">The OpenAI-compatible API base URI; <c>chat/completions</c> is resolved against it.</param>
    /// <param name="model">The model identifier sent in each request.</param>
    /// <param name="apiKey">Optional bearer token; local servers usually need none.</param>
    /// <param name="defaultMaxOutputTokens">The <c>max_tokens</c> sent when a request's <see cref="ChatOptions.MaxOutputTokens"/> is unset.</param>
    /// <param name="httpClient">Optional injected <see cref="HttpClient"/> (used by tests); when null the client owns and disposes its own.</param>
    public OpenAiCompatibleChatClient(Uri endpoint, string model, string? apiKey = null, int defaultMaxOutputTokens = 2048, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        _completionsUri = new Uri(endpoint, "chat/completions");
        _model = model;
        _defaultMaxOutputTokens = defaultMaxOutputTokens;
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }

        _metadata = new ChatClientMetadata("openai-compatible", endpoint, model);
    }

    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var request = new ChatCompletionRequest
        {
            Model = _model,
            Temperature = options?.Temperature,
            MaxTokens = options?.MaxOutputTokens ?? _defaultMaxOutputTokens,
            Messages = [.. messages.Select(static message => new ChatCompletionMessage
            {
                Role = RoleOf(message),
                Content = message.Text ?? string.Empty,
            })],
        };

        using HttpResponseMessage response = await _http
            .PostAsJsonAsync(_completionsUri, request, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        ChatCompletionResponse? completion = await response.Content
            .ReadFromJsonAsync<ChatCompletionResponse>(SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        string text = completion?.Choices is { Count: > 0 } choices
            ? choices[0].Message?.Content ?? string.Empty
            : string.Empty;

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("OpenAiCompatibleChatClient supports only non-streaming GetResponseAsync.");

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(_metadata) ? _metadata : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    private static string RoleOf(ChatMessage message) =>
        message.Role == ChatRole.System ? "system"
        : message.Role == ChatRole.Assistant ? "assistant"
        : message.Role == ChatRole.Tool ? "tool"
        : "user";

    private sealed class ChatCompletionRequest
    {
        [JsonPropertyName("model")] public required string Model { get; init; }
        [JsonPropertyName("messages")] public required IReadOnlyList<ChatCompletionMessage> Messages { get; init; }
        [JsonPropertyName("temperature")] public float? Temperature { get; init; }

        // Legacy field on purpose: older llama-server honors max_tokens, not max_completion_tokens.
        [JsonPropertyName("max_tokens")] public int? MaxTokens { get; init; }
    }

    private sealed class ChatCompletionMessage
    {
        [JsonPropertyName("role")] public string? Role { get; init; }
        [JsonPropertyName("content")] public string? Content { get; init; }
    }

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")] public IReadOnlyList<ChatCompletionChoice>? Choices { get; init; }
    }

    private sealed class ChatCompletionChoice
    {
        [JsonPropertyName("message")] public ChatCompletionMessage? Message { get; init; }
    }
}
