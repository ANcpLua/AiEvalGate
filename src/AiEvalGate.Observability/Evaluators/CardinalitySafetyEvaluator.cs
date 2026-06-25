using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using AiEvalGate.Observability.Models;

namespace AiEvalGate.Observability.Evaluators;

public sealed class CardinalitySafetyEvaluator(ObservabilityEvaluationRecord record) : IEvaluator
{
    private static readonly string[] BlockedAttributeFragments =
    [
        "authorization",
        "api_key",
        "apikey",
        "password",
        "prompt.raw",
        "message.raw",
        "tool.arguments.raw",
        "email"
    ];

    public IReadOnlyCollection<string> EvaluationMetricNames => [ObservabilityMetricNames.CardinalitySafety];

    public ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        AnalysisResult analysis = Analyze(record);
        BooleanMetric metric = EvaluationMetricFactory.CreateBoolean(ObservabilityMetricNames.CardinalitySafety, analysis.Passed, analysis.Reason);
        return new ValueTask<EvaluationResult>(new EvaluationResult(metric));
    }

    public static AnalysisResult Analyze(ObservabilityEvaluationRecord record)
    {
        List<string> violations = [];

        foreach (TelemetryEvidenceRecord telemetry in record.Telemetry)
        {
            foreach ((string key, JsonElement value) in telemetry.Attributes)
            {
                if (BlockedAttributeFragments.Any(fragment => key.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                {
                    violations.Add($"{telemetry.Id}:{key}");
                    continue;
                }

                if (LooksSensitiveOrUnbounded(value.ToString()))
                {
                    violations.Add($"{telemetry.Id}:{key}");
                }
            }
        }

        return violations.Count == 0
            ? AnalysisResult.Pass("No high-cardinality or sensitive telemetry attributes were found.")
            : AnalysisResult.Fail($"unsafe telemetry attributes: {string.Join(", ", violations)}");
    }

    private static bool LooksSensitiveOrUnbounded(string value)
        => value.Contains('@', StringComparison.Ordinal) ||
           value.Contains("Bearer ", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("sk-", StringComparison.OrdinalIgnoreCase);
}
