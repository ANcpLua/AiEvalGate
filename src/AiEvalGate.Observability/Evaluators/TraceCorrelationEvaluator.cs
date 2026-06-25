using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using AiEvalGate.Observability.Models;

namespace AiEvalGate.Observability.Evaluators;

public sealed class TraceCorrelationEvaluator(ObservabilityEvaluationRecord record) : IEvaluator
{
    public IReadOnlyCollection<string> EvaluationMetricNames => [ObservabilityMetricNames.TraceCorrelation];

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
