using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace AiEvalGate.Core.Infrastructure;

/// <summary>
/// Builds the Anthropic-backed <see cref="IChatClient"/> judge used to score evaluation runs,
/// reading its API key and model name from environment variables.
/// </summary>
/// <remarks>
/// Enforces the AI-only policy invariant: the single <see cref="IChatClient"/> produced here is a real
/// Claude judge that is consumed both by the Microsoft.Extensions.AI quality evaluators (wrapped in a
/// <see cref="ChatConfiguration"/>) and by the reviewer agents in <c>AiReviewerTeam</c>. There is no
/// heuristic or stubbed judge path; every metric and review is produced by this live model.
/// </remarks>
public static class AiClientFactory
{
    /// <summary>
    /// Creates the judge <see cref="IChatClient"/> from the <c>ANTHROPIC_API_KEY</c> and
    /// <c>AI_EVAL_REVIEW_MODEL</c> environment variables.
    /// </summary>
    /// <returns>
    /// An <see cref="IChatClient"/> wrapping an <see cref="AnthropicClient"/> for the configured model.
    /// </returns>
    /// <remarks>
    /// Configures the Anthropic seam: a default <c>maxOutputTokens</c> of 16000 (which an evaluator's own
    /// <see cref="ChatOptions"/> override when it sets <c>MaxOutputTokens</c>, since Anthropic requires
    /// <c>max_tokens</c> on every request), and a per-request adjustment that drops <c>TopP</c> whenever both
    /// <see cref="ChatOptions.Temperature"/> and <see cref="ChatOptions.TopP"/> are set, because Claude 4+
    /// rejects sending temperature and top_p together while the quality evaluators send both.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the <c>ANTHROPIC_API_KEY</c> or <c>AI_EVAL_REVIEW_MODEL</c> environment variable is missing
    /// or whitespace.
    /// </exception>
    public static IChatClient CreateJudgeChatClientFromEnvironment()
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
