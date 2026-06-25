using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace AiEvalGate.Core.Infrastructure;

public static class AiClientFactory
{
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
