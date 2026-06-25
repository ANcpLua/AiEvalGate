using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using AiEvalGate.Observability.Models;

namespace AiEvalGate.Observability.Evaluators;

/// <summary>
/// Deterministic <see cref="IEvaluator"/> that checks an agent's structured JSON output against a
/// declared <see cref="OutputContract"/>, emitting the <c>observability.output.schema</c> metric.
/// </summary>
/// <remarks>
/// Structured (function-calling) outputs are increasingly how agents return machine-consumable
/// results, and whether one conforms to a contract is decidable exactly from the JSON — no model
/// judgement, so the verdict never varies for a given output. The check passes when the output is a
/// JSON object that contains every required property with the required value kind and none of the
/// forbidden properties; it passes vacuously when no contract is declared. This keeps it on the
/// deterministic, AI-only side of the gate.
/// </remarks>
/// <param name="record">The captured agent-run record whose <c>StructuredOutput</c> is validated against its <c>OutputContract</c>.</param>
public sealed class StructuredOutputConformanceEvaluator(ObservabilityEvaluationRecord record) : IEvaluator
{
    /// <summary>
    /// The single metric this evaluator produces: <c>observability.output.schema</c>.
    /// </summary>
    public IReadOnlyCollection<string> EvaluationMetricNames => [ObservabilityMetricNames.OutputSchema];

    /// <summary>
    /// Evaluates structured-output conformance and returns it as a single <c>observability.output.schema</c> metric.
    /// </summary>
    /// <param name="messages">Conversation history from the <see cref="IEvaluator"/> contract; not used, as the verdict derives solely from the injected record.</param>
    /// <param name="modelResponse">The model response from the <see cref="IEvaluator"/> contract; not used by this evaluator.</param>
    /// <param name="chatConfiguration">Optional chat configuration from the <see cref="IEvaluator"/> contract; not used by this evaluator.</param>
    /// <param name="additionalContext">Optional additional evaluation context from the <see cref="IEvaluator"/> contract; not used by this evaluator.</param>
    /// <param name="cancellationToken">Cancellation token from the <see cref="IEvaluator"/> contract; not observed because the work completes synchronously.</param>
    /// <returns>A completed <see cref="ValueTask{TResult}"/> whose <see cref="EvaluationResult"/> carries the structured-output <see cref="BooleanMetric"/>.</returns>
    public ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        AnalysisResult analysis = Analyze(record);
        BooleanMetric metric = EvaluationMetricFactory.CreateBoolean(ObservabilityMetricNames.OutputSchema, analysis.Passed, analysis.Reason);
        return new ValueTask<EvaluationResult>(new EvaluationResult(metric));
    }

    /// <summary>
    /// Validates the record's structured output against its declared output contract.
    /// </summary>
    /// <remarks>
    /// Passes when no contract is declared (or the contract is empty). Otherwise the structured output
    /// must be a JSON object; each required property must be present with the required value kind, and
    /// no forbidden property may appear. Required-property names are matched case-sensitively (per the
    /// JSON specification) and value kinds case-insensitively. Every violation is collected into the
    /// failure reason.
    /// </remarks>
    /// <param name="record">The agent-run record whose <c>StructuredOutput</c> is checked against its <c>OutputContract</c>.</param>
    /// <returns>A passing <see cref="AnalysisResult"/> when the output conforms (or no contract is declared); otherwise a failing result listing each contract violation.</returns>
    public static AnalysisResult Analyze(ObservabilityEvaluationRecord record)
    {
        OutputContract? contract = record.OutputContract;
        if (contract is null || (contract.RequiredProperties.Count == 0 && contract.ForbiddenProperties.Count == 0))
        {
            return AnalysisResult.Pass("No output contract was declared.");
        }

        if (record.StructuredOutput is not { ValueKind: JsonValueKind.Object } output)
        {
            return AnalysisResult.Fail("structured output is missing or is not a JSON object.");
        }

        List<string> violations = [];

        foreach ((string name, string expectedKind) in contract.RequiredProperties)
        {
            if (!output.TryGetProperty(name, out JsonElement property))
            {
                violations.Add($"missing required property '{name}'");
                continue;
            }

            if (!KindMatches(expectedKind, property.ValueKind))
            {
                violations.Add($"property '{name}' expected {expectedKind} but was {property.ValueKind.ToString().ToLowerInvariant()}");
            }
        }

        foreach (string forbidden in contract.ForbiddenProperties)
        {
            if (output.TryGetProperty(forbidden, out _))
            {
                violations.Add($"forbidden property present: '{forbidden}'");
            }
        }

        return violations.Count == 0
            ? AnalysisResult.Pass("Structured output conforms to the declared contract.")
            : AnalysisResult.Fail($"output contract violations: {string.Join(", ", violations)}");
    }

    private static bool KindMatches(string expectedKind, JsonValueKind actual) => expectedKind.ToLowerInvariant() switch
    {
        "string" => actual == JsonValueKind.String,
        "number" => actual == JsonValueKind.Number,
        "boolean" or "bool" => actual is JsonValueKind.True or JsonValueKind.False,
        "object" => actual == JsonValueKind.Object,
        "array" => actual == JsonValueKind.Array,
        "null" => actual == JsonValueKind.Null,
        _ => false,
    };
}
