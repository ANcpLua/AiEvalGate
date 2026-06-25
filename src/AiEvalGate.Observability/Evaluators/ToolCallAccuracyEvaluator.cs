using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using AiEvalGate.Observability.Models;

namespace AiEvalGate.Observability.Evaluators;

/// <summary>
/// Deterministic <see cref="IEvaluator"/> that checks whether an agent run made the tool calls a
/// scenario required, emitting the <c>observability.tool.call.accuracy</c> metric.
/// </summary>
/// <remarks>
/// Every expected tool call must be matched, one-to-one, by a distinct actual tool call: the names
/// must be equal (case-insensitively) and the actual call's arguments must contain the expected
/// arguments as a subset, where argument keys are compared case-insensitively and argument values
/// are compared with <see cref="System.Text.Json.JsonElement.DeepEquals(System.Text.Json.JsonElement,System.Text.Json.JsonElement)"/>.
/// The check passes only when every expected call is matched and fails listing the names of the
/// expected calls left unmatched. This is a non-LLM check that completes synchronously; its result
/// is one deterministic input feeding the overall AI-only gate decision.
/// </remarks>
/// <param name="record">The captured agent-run record whose <c>ExpectedToolCalls</c> are matched against its actual <c>ToolCalls</c>.</param>
public sealed class ToolCallAccuracyEvaluator(ObservabilityEvaluationRecord record) : IEvaluator
{
    /// <summary>
    /// The single metric this evaluator produces: <c>observability.tool.call.accuracy</c>.
    /// </summary>
    public IReadOnlyCollection<string> EvaluationMetricNames => [ObservabilityMetricNames.ToolCallAccuracy];

    /// <summary>
    /// Evaluates tool-call accuracy and returns the result as a single <c>observability.tool.call.accuracy</c> metric.
    /// </summary>
    /// <remarks>
    /// The verdict is computed by <see cref="Analyze"/> over the constructor-supplied record; the
    /// <see cref="IEvaluator"/> parameters below are part of the interface contract but are not
    /// consulted by this implementation. The boolean outcome is wrapped by
    /// <c>EvaluationMetricFactory.CreateBoolean</c> into a <see cref="BooleanMetric"/> whose
    /// interpretation rates a pass as <see cref="EvaluationRating.Good"/> and a failure as
    /// <see cref="EvaluationRating.Unacceptable"/> (marked failed). The work is synchronous, so the
    /// returned <see cref="ValueTask{TResult}"/> is already completed.
    /// </remarks>
    /// <param name="messages">Conversation history from the <see cref="IEvaluator"/> contract; not used by this evaluator.</param>
    /// <param name="modelResponse">The model response from the <see cref="IEvaluator"/> contract; not used by this evaluator.</param>
    /// <param name="chatConfiguration">Optional chat configuration from the <see cref="IEvaluator"/> contract; not used by this evaluator.</param>
    /// <param name="additionalContext">Optional additional evaluation context from the <see cref="IEvaluator"/> contract; not used by this evaluator.</param>
    /// <param name="cancellationToken">Cancellation token from the <see cref="IEvaluator"/> contract; not observed because the work completes synchronously.</param>
    /// <returns>A completed <see cref="ValueTask{TResult}"/> whose <see cref="EvaluationResult"/> carries the single tool-call-accuracy <see cref="BooleanMetric"/>.</returns>
    public ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        AnalysisResult analysis = Analyze(record);
        BooleanMetric metric = EvaluationMetricFactory.CreateBoolean(ObservabilityMetricNames.ToolCallAccuracy, analysis.Passed, analysis.Reason);
        return new ValueTask<EvaluationResult>(new EvaluationResult(metric));
    }

    /// <summary>
    /// Matches a record's expected tool calls against its actual tool calls and reports whether all were satisfied.
    /// </summary>
    /// <remarks>
    /// Each expected call is greedily matched to the first not-yet-used actual call with an equal name
    /// (case-insensitive) whose arguments contain the expected arguments as a subset (keys compared
    /// case-insensitively, values by <see cref="System.Text.Json.JsonElement.DeepEquals(System.Text.Json.JsonElement,System.Text.Json.JsonElement)"/>);
    /// a matched actual call is consumed and cannot satisfy another expected call. The names of any
    /// expected calls that find no match are collected into the failure reason.
    /// </remarks>
    /// <param name="record">The agent-run record whose <c>ExpectedToolCalls</c> are checked against its actual <c>ToolCalls</c>.</param>
    /// <returns>
    /// A passing <see cref="AnalysisResult"/> when every expected tool call is matched; otherwise a
    /// failing result whose reason lists the names of the missing or mismatched expected tool calls.
    /// </returns>
    public static AnalysisResult Analyze(ObservabilityEvaluationRecord record)
    {
        var unmatched = new List<string>();
        var usedIndexes = new HashSet<int>();

        foreach (ExpectedToolCallRecord expected in record.ExpectedToolCalls)
        {
            int matchIndex = FindMatchingCall(record.ToolCalls, expected, usedIndexes);
            if (matchIndex < 0)
            {
                unmatched.Add(expected.Name);
                continue;
            }

            usedIndexes.Add(matchIndex);
        }

        return unmatched.Count == 0
            ? AnalysisResult.Pass("All required tool calls were present with matching argument subsets.")
            : AnalysisResult.Fail($"missing or mismatched tool calls: {string.Join(", ", unmatched)}");
    }

    private static int FindMatchingCall(IReadOnlyList<ToolCallRecord> actualCalls, ExpectedToolCallRecord expected, HashSet<int> usedIndexes)
    {
        for (int i = 0; i < actualCalls.Count; i++)
        {
            if (usedIndexes.Contains(i))
            {
                continue;
            }

            ToolCallRecord actual = actualCalls[i];
            if (!actual.Name.Equals(expected.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ArgumentsContainExpectedSubset(actual.Arguments, expected.Arguments))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool ArgumentsContainExpectedSubset(
        IReadOnlyDictionary<string, JsonElement> actual,
        IReadOnlyDictionary<string, JsonElement> expected)
    {
        foreach ((string key, JsonElement expectedValue) in expected)
        {
            // Tool names are matched case-insensitively (see FindMatchingCall); match
            // argument keys the same way so expected "Query" still matches actual "query".
            if (!TryGetArgument(actual, key, out JsonElement actualValue))
            {
                return false;
            }

            if (!JsonValuesEqual(actualValue, expectedValue))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetArgument(IReadOnlyDictionary<string, JsonElement> arguments, string key, out JsonElement value)
    {
        if (arguments.TryGetValue(key, out value))
        {
            return true;
        }

        foreach ((string actualKey, JsonElement actualValue) in arguments)
        {
            if (string.Equals(actualKey, key, StringComparison.OrdinalIgnoreCase))
            {
                value = actualValue;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool JsonValuesEqual(JsonElement actual, JsonElement expected)
        => JsonElement.DeepEquals(actual, expected);
}
