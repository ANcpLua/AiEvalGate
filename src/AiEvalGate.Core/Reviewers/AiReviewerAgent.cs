using System.Text.Json;
using AiEvalGate.Core.Models;
using Microsoft.Extensions.AI;
using AiEvalGate.Core;

namespace AiEvalGate.Core.Reviewers;

/// <summary>
/// An automated AI reviewer agent that judges a single evaluation case by prompting an
/// <see cref="IChatClient"/> and returning the structured <see cref="AgentReview"/> verdict
/// it produces.
/// </summary>
/// <remarks>
/// Each instance wraps one chat client (the LLM judge) and a fixed system prompt built from a
/// reviewer name and focus via <c>ReviewerPromptLibrary.SystemPrompt</c>. The agent enforces the
/// pipeline's AI-only invariant: it stamps an <c>aiOnlyPolicy</c> block
/// (<c>humanReviewRequired=false</c>, <c>manualOverrideAllowed=false</c>,
/// <c>manualApprovalSteps=0</c>) into every judging payload, and the prompt forbids the model from
/// deferring to a human, so the reviewer must always return a definitive pass/fail decision.
/// A team of these agents is typically run together (see <c>AiReviewerTeam</c>), and each verdict
/// feeds <c>AiEvalGatekeeper.Evaluate</c>.
/// </remarks>
public sealed class AiReviewerAgent : IReviewerAgent
{
    private readonly IChatClient _chatClient;
    private readonly string _systemPrompt;

    /// <summary>
    /// Creates a reviewer agent with the given identity and focus, backed by the supplied chat client.
    /// </summary>
    /// <param name="name">
    /// The canonical reviewer name (for example <c>SafetyReviewer</c>). It is exposed as
    /// <see cref="Name"/>, embedded in the generated system prompt, and stamped back onto the returned
    /// review so the reported reviewer matches the gatekeeper's required-reviewer list.
    /// </param>
    /// <param name="focus">
    /// The natural-language description of what this reviewer should scrutinize; it is woven into the
    /// system prompt to steer the LLM judge's focus.
    /// </param>
    /// <param name="chatClient">The chat client used as the LLM judge that produces each review.</param>
    public AiReviewerAgent(string name, string focus, IChatClient chatClient)
    {
        Name = name;
        _chatClient = chatClient;
        _systemPrompt = ReviewerPromptLibrary.SystemPrompt(name, focus);
    }

    /// <summary>
    /// The canonical name identifying this reviewer. The gatekeeper matches it case-insensitively
    /// against the policy's required reviewers, and <see cref="ReviewAsync"/> overwrites the model's
    /// self-reported reviewer with this value to keep the returned review authoritative.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Judges a single evaluation case and returns this reviewer's structured verdict.
    /// </summary>
    /// <remarks>
    /// The scenario, run result, evaluator scores, and the fixed AI-only policy block are serialized
    /// into one JSON payload and sent to the chat client as a user message under this reviewer's system
    /// prompt. Judging is deterministic and structured (<c>Temperature</c> 0.0 with a JSON response
    /// format). The model's reply is reduced to its first complete <c>{...}</c> object and deserialized
    /// into an <see cref="AgentReview"/>; if the model reported a different reviewer name, it is replaced
    /// case-insensitively with <see cref="Name"/> so the returned review stays authoritative.
    /// </remarks>
    /// <param name="scenario">The evaluation case definition (user input, context, and expected behavior) being reviewed.</param>
    /// <param name="runResult">The captured system-under-test output (answer, retrieved sources/context, tool calls, and service traces) being graded.</param>
    /// <param name="evaluatorScores">The deterministic evaluator metric scores for the run, supplied to the judge as additional evidence.</param>
    /// <param name="cancellationToken">A token to cancel the underlying chat request.</param>
    /// <returns>
    /// The reviewer's <see cref="AgentReview"/>, with its <see cref="AgentReview.Reviewer"/> guaranteed
    /// to equal <see cref="Name"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the model output contains no JSON object, or when the extracted JSON deserializes to
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="System.Text.Json.JsonException">
    /// Thrown when the extracted JSON is malformed or omits a required <see cref="AgentReview"/> member.
    /// </exception>
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
