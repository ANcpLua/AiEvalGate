using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using AiEvalGate.Observability.Models;

namespace AiEvalGate.Observability.Evaluators;

/// <summary>
/// Deterministic observability evaluator that checks OpenTelemetry trace correlation: every span in
/// the record must carry a non-empty <c>traceId</c> and <c>spanId</c>, those (trace, span) keys must be
/// unique, and any declared <c>parentSpanId</c> must resolve to another span within the same trace.
/// </summary>
/// <remarks>
/// Implements <see cref="IEvaluator"/> and reports the single <c>observability.trace.correlation</c>
/// metric. As a deterministic validator it inspects the captured telemetry only and makes no model
/// call, so the same record always yields the same verdict — matching the gate's AI-only, fail-closed,
/// fully reproducible policy. Only telemetry whose <c>signalType</c> equals <c>"span"</c> (case-insensitive)
/// participates; other signals are ignored. A passing analysis is surfaced as a <c>Good</c> boolean
/// metric and a failing one as an <c>Unacceptable</c> failed metric via
/// <c>EvaluationMetricFactory.CreateBoolean</c>.
/// </remarks>
/// <param name="record">
/// The captured agent run, including the <c>Telemetry</c> spans whose trace/span identifiers and parent
/// references are validated.
/// </param>
public sealed class TraceCorrelationEvaluator(ObservabilityEvaluationRecord record) : IEvaluator
{
    /// <summary>
    /// The metric names produced by this evaluator: the single <c>observability.trace.correlation</c> metric.
    /// </summary>
    public IReadOnlyCollection<string> EvaluationMetricNames => [ObservabilityMetricNames.TraceCorrelation];

    /// <summary>
    /// Evaluates trace correlation for the record supplied to the constructor and returns a single boolean
    /// <c>observability.trace.correlation</c> metric.
    /// </summary>
    /// <remarks>
    /// The chat-oriented parameters are part of the <see cref="IEvaluator"/> contract but are not used:
    /// the verdict is computed purely from the constructor's record by <see cref="Analyze"/>, so the call
    /// completes synchronously inside the returned <see cref="ValueTask{TResult}"/>.
    /// </remarks>
    /// <param name="messages">The conversation history; ignored by this deterministic evaluator.</param>
    /// <param name="modelResponse">The model's response under evaluation; ignored by this deterministic evaluator.</param>
    /// <param name="chatConfiguration">Optional chat client configuration; ignored by this deterministic evaluator.</param>
    /// <param name="additionalContext">Optional additional evaluation context; ignored by this deterministic evaluator.</param>
    /// <param name="cancellationToken">A token to cancel the operation; unused because no asynchronous work is performed.</param>
    /// <returns>
    /// A completed <see cref="ValueTask{TResult}"/> whose <see cref="EvaluationResult"/> holds the boolean
    /// metric rated <c>Good</c> when correlation holds, or <c>Unacceptable</c> and failed when it does not,
    /// with the analysis reason attached.
    /// </returns>
    public ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        AnalysisResult analysis = Analyze(record);
        BooleanMetric metric = EvaluationMetricFactory.CreateBoolean(ObservabilityMetricNames.TraceCorrelation, analysis.Passed, analysis.Reason);
        return new ValueTask<EvaluationResult>(new EvaluationResult(metric));
    }

    /// <summary>
    /// Runs the deterministic trace-correlation analysis over the record's span telemetry, independent of any
    /// evaluator instance.
    /// </summary>
    /// <remarks>
    /// Considers only telemetry whose <c>signalType</c> is <c>"span"</c> (case-insensitive). A span is flagged
    /// when its <c>traceId</c> or <c>spanId</c> is missing; spans with both identifiers are then checked for
    /// duplicate <c>traceId/spanId</c> keys and for <c>parentSpanId</c> values that reference a parent absent
    /// from the same trace. The result passes only when no invalid spans, no duplicate keys and no missing
    /// parents are detected; otherwise the failure reason lists the offending span identifiers grouped by
    /// category.
    /// </remarks>
    /// <param name="record">The captured agent run whose <c>Telemetry</c> spans are validated.</param>
    /// <returns>
    /// An <see cref="AnalysisResult"/> that passes when all span identifiers are complete, unique and have
    /// resolvable parents, or fails with a semicolon-joined summary of the invalid spans, duplicate span keys
    /// and missing parent spans.
    /// </returns>
    public static AnalysisResult Analyze(ObservabilityEvaluationRecord record)
    {
        List<TelemetryEvidenceRecord> spans = [.. record.Telemetry.Where(static telemetry => IsSpan(telemetry))];
        List<TelemetryEvidenceRecord> correlatableSpans = [];
        List<string> invalidSpans = [];

        foreach (TelemetryEvidenceRecord span in spans)
        {
            if (string.IsNullOrWhiteSpace(span.TraceId))
            {
                invalidSpans.Add($"{span.Id}:traceId");
            }

            if (string.IsNullOrWhiteSpace(span.SpanId))
            {
                invalidSpans.Add($"{span.Id}:spanId");
            }

            if (!string.IsNullOrWhiteSpace(span.TraceId) && !string.IsNullOrWhiteSpace(span.SpanId))
            {
                correlatableSpans.Add(span);
            }
        }

        string[] duplicateSpanKeys = [.. correlatableSpans
            .GroupBy(static span => BuildSpanKey(span.TraceId!, span.SpanId!))
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)];

        HashSet<string> spanKeys = [.. correlatableSpans.Select(static span => BuildSpanKey(span.TraceId!, span.SpanId!))];
        List<string> missingParents = [];

        foreach (TelemetryEvidenceRecord span in correlatableSpans)
        {
            if (string.IsNullOrWhiteSpace(span.ParentSpanId))
            {
                continue;
            }

            string parentKey = BuildSpanKey(span.TraceId!, span.ParentSpanId);
            if (!spanKeys.Contains(parentKey))
            {
                missingParents.Add($"{span.Id}:{span.ParentSpanId}");
            }
        }

        if (invalidSpans.Count == 0 && duplicateSpanKeys.Length == 0 && missingParents.Count == 0)
        {
            return AnalysisResult.Pass("All span identifiers are complete and parent references resolve within their trace.");
        }

        List<string> reasons = [];
        if (invalidSpans.Count > 0)
        {
            reasons.Add($"invalid spans: {string.Join(", ", invalidSpans)}");
        }

        if (duplicateSpanKeys.Length > 0)
        {
            reasons.Add($"duplicate span keys: {string.Join(", ", duplicateSpanKeys)}");
        }

        if (missingParents.Count > 0)
        {
            reasons.Add($"missing parent spans: {string.Join(", ", missingParents)}");
        }

        return AnalysisResult.Fail(string.Join("; ", reasons));
    }

    private static bool IsSpan(TelemetryEvidenceRecord telemetry)
        => telemetry.SignalType.Equals("span", StringComparison.OrdinalIgnoreCase);

    private static string BuildSpanKey(string traceId, string spanId)
        => string.Concat(traceId, "/", spanId);
}
