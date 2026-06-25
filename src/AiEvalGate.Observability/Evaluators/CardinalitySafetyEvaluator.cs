using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using AiEvalGate.Observability.Models;

namespace AiEvalGate.Observability.Evaluators;

/// <summary>
/// Deterministic <see cref="IEvaluator"/> that inspects the telemetry attributes captured in an
/// <see cref="ObservabilityEvaluationRecord"/> and fails when any attribute key or value looks
/// high-cardinality or sensitive (for example secrets, raw prompts, or personal data such as email addresses).
/// </summary>
/// <remarks>
/// Part of the deterministic observability harness: the verdict depends only on the supplied record and the
/// fixed blocked-fragment and pattern rules, so the same record always yields the same pass/fail outcome.
/// </remarks>
/// <param name="record">The observability evaluation record whose telemetry attributes are scanned for cardinality and safety violations.</param>
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

    /// <summary>
    /// Gets the single metric name produced by this evaluator, the cardinality-safety metric
    /// (<c>observability.cardinality.safety</c>).
    /// </summary>
    public IReadOnlyCollection<string> EvaluationMetricNames => [ObservabilityMetricNames.CardinalitySafety];

    /// <summary>
    /// Evaluates the injected record by running <see cref="Analyze"/> and wrapping its outcome in a
    /// <see cref="BooleanMetric"/> for the cardinality-safety metric.
    /// </summary>
    /// <param name="messages">The chat messages for the turn under evaluation; part of the <see cref="IEvaluator"/> contract and not used here, since the verdict derives solely from the injected record.</param>
    /// <param name="modelResponse">The model response under evaluation; part of the <see cref="IEvaluator"/> contract and not used here.</param>
    /// <param name="chatConfiguration">Optional chat configuration; part of the <see cref="IEvaluator"/> contract and not used here.</param>
    /// <param name="additionalContext">Optional additional evaluation context; part of the <see cref="IEvaluator"/> contract and not used here.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>A completed <see cref="ValueTask{TResult}"/> holding an <see cref="EvaluationResult"/> with the cardinality-safety <see cref="BooleanMetric"/>.</returns>
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

    /// <summary>
    /// Scans every telemetry attribute in the record, flagging any whose key contains a blocked fragment
    /// (such as <c>authorization</c>, <c>api_key</c>, <c>password</c>, raw prompt/message/tool-argument fields,
    /// or <c>email</c>) or whose value looks sensitive or unbounded (contains <c>@</c>, a <c>Bearer </c> token,
    /// or an <c>sk-</c> prefix). Each violation is reported as <c>{telemetryId}:{attributeKey}</c>.
    /// </summary>
    /// <param name="record">The observability evaluation record whose <see cref="TelemetryEvidenceRecord.Attributes"/> are inspected.</param>
    /// <returns>A passing <see cref="AnalysisResult"/> when no unsafe attributes are found; otherwise a failing result listing the offending attributes.</returns>
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
