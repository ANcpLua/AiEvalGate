namespace AiEvalGate.Observability.Evaluators;

internal static class ObservabilityMetricNames
{
    /// <summary>
    /// Metric-name key for the telemetry-evidence check, emitted as a pass/fail <c>BooleanMetric</c> by
    /// <c>TelemetryEvidenceEvaluator</c> via <c>EvaluationMetricFactory.CreateBoolean</c>. The check passes only when
    /// every required evidence id is present in the captured telemetry, every required id is cited in the final
    /// response, and no forbidden claims appear in that response.
    /// </summary>
    public const string TelemetryEvidence = "observability.telemetry.evidence";
    /// <summary>
    /// Metric-name key for the tool-call-accuracy check, emitted as a pass/fail <c>BooleanMetric</c> by
    /// <c>ToolCallAccuracyEvaluator</c> via <c>EvaluationMetricFactory.CreateBoolean</c>. The check passes only when
    /// every expected tool call is matched by an actual call, comparing tool names case-insensitively and requiring
    /// each expected argument to appear (as a subset) with a deep-equal value on the actual call.
    /// </summary>
    public const string ToolCallAccuracy = "observability.tool.call.accuracy";
    /// <summary>
    /// Metric-name key for the trace-correlation check, emitted as a pass/fail <c>BooleanMetric</c> by
    /// <c>TraceCorrelationEvaluator</c> via <c>EvaluationMetricFactory.CreateBoolean</c>. The check inspects telemetry
    /// records whose signal type is <c>span</c> and passes only when every span carries a non-empty OpenTelemetry
    /// <c>TraceId</c> and <c>SpanId</c>, no two correlatable spans share the same trace/span key, and every
    /// <c>ParentSpanId</c> resolves to a span present within the same trace.
    /// </summary>
    public const string TraceCorrelation = "observability.trace.correlation";
    /// <summary>
    /// Metric-name key for the cardinality-safety check, emitted as a pass/fail <c>BooleanMetric</c> by
    /// <c>CardinalitySafetyEvaluator</c> via <c>EvaluationMetricFactory.CreateBoolean</c>. The check passes only when no
    /// telemetry attribute key contains a blocked fragment (such as <c>authorization</c>, <c>api_key</c>,
    /// <c>password</c>, <c>email</c>, or <c>*.raw</c> payloads) and no attribute value looks sensitive or unbounded
    /// (for example containing an <c>@</c>, a <c>Bearer </c> token, or an <c>sk-</c> key prefix).
    /// </summary>
    public const string CardinalitySafety = "observability.cardinality.safety";
}
