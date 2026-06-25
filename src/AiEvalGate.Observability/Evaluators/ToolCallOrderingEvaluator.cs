using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using AiEvalGate.Observability.Models;

namespace AiEvalGate.Observability.Evaluators;

/// <summary>
/// Deterministic <see cref="IEvaluator"/> that enforces happens-before ordering constraints between
/// an agent's tool calls, emitting the <c>observability.tool.order</c> metric.
/// </summary>
/// <remarks>
/// Where <c>ToolCallAccuracyEvaluator</c> checks <em>which</em> tools were called with which
/// arguments, this checks the <em>order</em> they were called in — the safety-relevant property
/// behind requirements like "authorize before charging" or "retrieve before answering". Each
/// constraint requires every call to its <c>After</c> tool to be preceded by at least one call to its
/// <c>Before</c> tool. Ordering is decided exactly from the recorded sequence, so the verdict is fully
/// reproducible with no model judgement; it passes vacuously when no constraints are declared.
/// </remarks>
/// <param name="record">The captured agent-run record whose <c>ToolCalls</c> are checked against its <c>ExpectedToolOrder</c>.</param>
public sealed class ToolCallOrderingEvaluator(ObservabilityEvaluationRecord record) : IEvaluator
{
    /// <summary>
    /// The single metric this evaluator produces: <c>observability.tool.order</c>.
    /// </summary>
    public IReadOnlyCollection<string> EvaluationMetricNames => [ObservabilityMetricNames.ToolOrder];

    /// <summary>
    /// Evaluates tool-call ordering and returns it as a single <c>observability.tool.order</c> metric.
    /// </summary>
    /// <param name="messages">Conversation history from the <see cref="IEvaluator"/> contract; not used, as the verdict derives solely from the injected record.</param>
    /// <param name="modelResponse">The model response from the <see cref="IEvaluator"/> contract; not used by this evaluator.</param>
    /// <param name="chatConfiguration">Optional chat configuration from the <see cref="IEvaluator"/> contract; not used by this evaluator.</param>
    /// <param name="additionalContext">Optional additional evaluation context from the <see cref="IEvaluator"/> contract; not used by this evaluator.</param>
    /// <param name="cancellationToken">Cancellation token from the <see cref="IEvaluator"/> contract; not observed because the work completes synchronously.</param>
    /// <returns>A completed <see cref="ValueTask{TResult}"/> whose <see cref="EvaluationResult"/> carries the tool-order <see cref="BooleanMetric"/>.</returns>
    public ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        AnalysisResult analysis = Analyze(record);
        BooleanMetric metric = EvaluationMetricFactory.CreateBoolean(ObservabilityMetricNames.ToolOrder, analysis.Passed, analysis.Reason);
        return new ValueTask<EvaluationResult>(new EvaluationResult(metric));
    }

    /// <summary>
    /// Checks that the record's tool calls satisfy every declared happens-before ordering constraint.
    /// </summary>
    /// <remarks>
    /// Passes when no constraints are declared. Otherwise, for each constraint, it scans the tool calls
    /// in order and flags a violation when its <c>After</c> tool is seen before any occurrence of its
    /// <c>Before</c> tool (which also covers the case where <c>Before</c> never occurs at all). Tool
    /// names are compared case-insensitively, consistent with <c>ToolCallAccuracyEvaluator</c>.
    /// </remarks>
    /// <param name="record">The agent-run record whose <c>ToolCalls</c> are checked against its <c>ExpectedToolOrder</c>.</param>
    /// <returns>A passing <see cref="AnalysisResult"/> when every constraint holds (or none are declared); otherwise a failing result listing each ordering violation.</returns>
    public static AnalysisResult Analyze(ObservabilityEvaluationRecord record)
    {
        if (record.ExpectedToolOrder.Count == 0)
        {
            return AnalysisResult.Pass("No tool-order constraints were declared.");
        }

        List<string> violations = [];

        foreach (ToolOrderConstraint constraint in record.ExpectedToolOrder)
        {
            bool seenBefore = false;

            foreach (ToolCallRecord call in record.ToolCalls)
            {
                if (string.Equals(call.Name, constraint.Before, StringComparison.OrdinalIgnoreCase))
                {
                    seenBefore = true;
                    continue;
                }

                if (string.Equals(call.Name, constraint.After, StringComparison.OrdinalIgnoreCase) && !seenBefore)
                {
                    violations.Add($"'{constraint.After}' occurred without a preceding '{constraint.Before}'");
                    break;
                }
            }
        }

        return violations.Count == 0
            ? AnalysisResult.Pass("All tool-order constraints were satisfied.")
            : AnalysisResult.Fail($"tool-order violations: {string.Join(", ", violations)}");
    }
}
