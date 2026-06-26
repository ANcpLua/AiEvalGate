using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace AiEvalGate.Core.Infrastructure;

/// <summary>
/// Builds the <see cref="IChatClient"/> judge used to score evaluation runs, selecting the provider
/// from the <c>AI_EVAL_JUDGE</c> environment variable and reading model/endpoint settings from the
/// environment.
/// </summary>
/// <remarks>
/// <para><c>AI_EVAL_JUDGE</c> selects the live judge provider (default <c>anthropic</c>):</para>
/// <list type="bullet">
///   <item><c>anthropic</c> — a Claude judge from <c>ANTHROPIC_API_KEY</c> + <c>AI_EVAL_REVIEW_MODEL</c>.</item>
///   <item><c>openai</c> — any OpenAI-compatible endpoint (e.g. a local bitnet.cpp / llama-server) from
///   <c>AI_EVAL_OPENAI_BASE_URL</c> (or <c>BITNET_URL</c>) + <c>AI_EVAL_REVIEW_MODEL</c> (or <c>BITNET_MODEL</c>).</item>
/// </list>
/// <para>The single produced client is consumed both by the Microsoft.Extensions.AI quality evaluators
/// (wrapped in a <see cref="ChatConfiguration"/>) and by the reviewer agents in <c>AiReviewerTeam</c>.
/// A deterministic stub judge (used by default in CI) lives in the test project, not here.</para>
/// </remarks>
public static class AiClientFactory
{
    /// <summary>
    /// Creates the judge <see cref="IChatClient"/> for the provider named by <c>AI_EVAL_JUDGE</c>
    /// (<c>anthropic</c> by default, or <c>openai</c> for an OpenAI-compatible endpoint).
    /// </summary>
    /// <returns>The configured live judge client.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>AI_EVAL_JUDGE</c> names an unknown provider, or a required environment variable for the
    /// selected provider is missing or whitespace.
    /// </exception>
    public static IChatClient CreateJudgeChatClientFromEnvironment()
    {
        string provider = (Environment.GetEnvironmentVariable("AI_EVAL_JUDGE") ?? "anthropic").Trim().ToLowerInvariant();
        return provider switch
        {
            "" or "anthropic" => CreateAnthropicJudge(),
            "openai" or "openai-compatible" or "bitnet" or "local" => CreateOpenAiCompatibleJudge(),
            _ => throw new InvalidOperationException($"Unknown AI_EVAL_JUDGE '{provider}'. Use 'anthropic' or 'openai'."),
        };
    }

    /// <remarks>
    /// Configures the Anthropic seam: a default <c>maxOutputTokens</c> of 16000 (which an evaluator's own
    /// <see cref="ChatOptions"/> override when it sets <c>MaxOutputTokens</c>, since Anthropic requires
    /// <c>max_tokens</c> on every request), and a per-request adjustment that drops <c>TopP</c> whenever both
    /// <see cref="ChatOptions.Temperature"/> and <see cref="ChatOptions.TopP"/> are set, because Claude 4+
    /// rejects sending temperature and top_p together while the quality evaluators send both.
    /// </remarks>
    private static IChatClient CreateAnthropicJudge()
    {
        string apiKey = RequiredEnvironment("ANTHROPIC_API_KEY");
        string model = RequiredEnvironment("AI_EVAL_REVIEW_MODEL");
        // Anthropic requires max_tokens on every request; evaluator ChatOptions that set it win over this default.
        // Claude 4+ rejects temperature and top_p together, but MEAI quality evaluators send both —
        // keep temperature (their determinism knob) and drop top_p at the seam.
        return new AnthropicClient { ApiKey = apiKey }
            .AsIChatClient(model, defaultMaxOutputTokens: 16000)
            .AsBuilder()
            .ConfigureOptions(options =>
            {
                if (options.Temperature is not null && options.TopP is not null)
                {
                    options.TopP = null;
                }
            })
            .Build();
    }

    /// <remarks>
    /// Targets any OpenAI-compatible <c>/v1/chat/completions</c> endpoint (for example a local
    /// bitnet.cpp / llama-server) via <see cref="OpenAiCompatibleChatClient"/>. The endpoint comes from
    /// <c>AI_EVAL_OPENAI_BASE_URL</c> or <c>BITNET_URL</c>; the model from <c>AI_EVAL_REVIEW_MODEL</c> or
    /// <c>BITNET_MODEL</c> (default <c>bitnet-b1.58-2B-4T</c>); an optional bearer token from
    /// <c>AI_EVAL_OPENAI_API_KEY</c>.
    /// </remarks>
    private static IChatClient CreateOpenAiCompatibleJudge()
    {
        string baseUrl = FirstEnvironment("AI_EVAL_OPENAI_BASE_URL", "BITNET_URL")
            ?? throw new InvalidOperationException("Set AI_EVAL_OPENAI_BASE_URL or BITNET_URL for the 'openai' judge provider.");
        string model = FirstEnvironment("AI_EVAL_REVIEW_MODEL", "BITNET_MODEL") ?? "bitnet-b1.58-2B-4T";
        string? apiKey = Environment.GetEnvironmentVariable("AI_EVAL_OPENAI_API_KEY");
        var endpoint = new Uri(baseUrl.TrimEnd('/') + "/v1/");
        return new OpenAiCompatibleChatClient(endpoint, model, apiKey);
    }

    private static string? FirstEnvironment(params string[] names)
    {
        foreach (string name in names)
        {
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Creates a <see cref="ChatConfiguration"/> wrapping the judge <see cref="IChatClient"/> from environment
    /// variables, ready to pass to the Microsoft.Extensions.AI quality evaluators.
    /// </summary>
    /// <returns>
    /// A <see cref="ChatConfiguration"/> backed by the judge client from
    /// <see cref="CreateJudgeChatClientFromEnvironment"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown transitively (via <see cref="CreateJudgeChatClientFromEnvironment"/>) when the
    /// <c>ANTHROPIC_API_KEY</c> or <c>AI_EVAL_REVIEW_MODEL</c> environment variable is missing or whitespace.
    /// </exception>
    public static ChatConfiguration CreateEvaluationChatConfigurationFromEnvironment()
    {
        return new ChatConfiguration(CreateJudgeChatClientFromEnvironment());
    }

    private static string RequiredEnvironment(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required environment variable: {name}");
        }

        return value;
    }
}
