using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using AiEvalGate.Observability.Models;

namespace AiEvalGate.Observability.Evaluators;

public sealed class ToolCallAccuracyEvaluator(ObservabilityEvaluationRecord record) : IEvaluator
{
    public IReadOnlyCollection<string> EvaluationMetricNames => [ObservabilityMetricNames.ToolCallAccuracy];

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
            if (!actual.TryGetValue(key, out JsonElement actualValue))
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

    private static bool JsonValuesEqual(JsonElement actual, JsonElement expected)
        => JsonElement.DeepEquals(actual, expected);
}
