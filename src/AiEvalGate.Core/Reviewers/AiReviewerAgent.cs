using System.Text.Json;
using AiEvalGate.Core.Models;
using Microsoft.Extensions.AI;
using AiEvalGate.Core;

namespace AiEvalGate.Core.Reviewers;

public sealed class AiReviewerAgent : IReviewerAgent
{
    private readonly IChatClient _chatClient;
    private readonly string _systemPrompt;

    public AiReviewerAgent(string name, string focus, IChatClient chatClient)
    {
        Name = name;
        _chatClient = chatClient;
        _systemPrompt = ReviewerPromptLibrary.SystemPrompt(name, focus);
    }

    public string Name { get; }

    public async Task<AgentReview> ReviewAsync(
        AiScenario scenario,
        AiRunResult runResult,
        IReadOnlyList<MetricScore> evaluatorScores,
        CancellationToken cancellationToken = default)
    {
        string payload = JsonSerializer.Serialize(new
        {
            scenario,
            runResult,
            evaluatorScores,
            aiOnlyPolicy = new
            {
                humanReviewRequired = false,
                manualOverrideAllowed = false,
                manualApprovalSteps = 0
            }
        }, JsonOptions.Default);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, _systemPrompt),
            new(ChatRole.User, payload)
        };

        var options = new ChatOptions
        {
            Temperature = 0.0f,
            ResponseFormat = ChatResponseFormat.Json
        };

        ChatResponse response = await _chatClient.GetResponseAsync(messages, options, cancellationToken);
        string text = response.Text ?? string.Empty;
        string json = ExtractJsonObject(text);

        AgentReview? review = JsonSerializer.Deserialize<AgentReview>(json, JsonOptions.Default);
        if (review is null)
        {
            throw new InvalidOperationException($"{Name} returned invalid review JSON: {text}");
        }

        if (!string.Equals(review.Reviewer, Name, StringComparison.OrdinalIgnoreCase))
        {
            review = review with { Reviewer = Name };
        }

        return review;
    }

    private static string ExtractJsonObject(string text)
    {
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start < 0 || end < start)
        {
            throw new InvalidOperationException($"No JSON object found in reviewer output: {text}");
        }

        return text[start..(end + 1)];
    }
}
