using System.Text.Json.Serialization;

namespace AiEvalGate.Core.Models;

/// <summary>
/// The declarative input specification for one AI evaluation case: the user input and
/// optional system prompt to exercise, the grounding context to supply, and the expected
/// behavior (required/forbidden claims, required sources, and expected/forbidden tools)
/// the run is graded against. Loaded one-per-line from JSONL by the scenario loader and
/// consumed by the quality evaluators, the AI reviewer team, the service-boundary
/// validator, and the AI-only gatekeeper.
/// </summary>
/// <remarks>
/// This record carries only the test definition; it holds no run output or verdict. It
/// drives a fully automated, AI-driven pipeline (the AI-only invariant — no human review
/// or manual override — is enforced downstream by the gatekeeper, not by this type).
/// String matching against scenario fields throughout the pipeline is case-insensitive.
/// </remarks>
public sealed record AiScenario
{
    /// <summary>
    /// Stable unique identifier for the scenario. The loader rejects any scenario with a
    /// blank id, and the id flows through to <see cref="AiRunResult.ScenarioId"/> and the
    /// resulting <c>GateResult</c> to correlate the run with its verdict and report artifacts.
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// Free-form classification of the topic or domain the scenario exercises (for example
    /// "refunds"). Carried through into the system under test (for example as a retrieval
    /// query argument) and available for grouping/reporting; not used by the gate decision.
    /// </summary>
    public required string Area { get; init; }
    /// <summary>
    /// The system architecture this scenario targets (for example "microservices" or
    /// "monolith"). The service-boundary validator uses it to select the single matching
    /// <see cref="Boundaries.ServiceBoundaryContract"/> (matched case-insensitively on its
    /// <c>Architecture</c>), and the system under test uses it to shape the service names it
    /// emits in its traces.
    /// </summary>
    public required string Architecture { get; init; }
    /// <summary>
    /// The user message handed to the system under test for this scenario, and the input
    /// against which the answer's relevance and completeness are judged. It is echoed into
    /// the evaluator's user prompt and drives the system under test's response.
    /// </summary>
    public required string UserInput { get; init; }
    /// <summary>
    /// Optional system prompt establishing the assistant's role and constraints for the run.
    /// When the quality evaluator builds its judge conversation it uses this as the system
    /// message, falling back to a generic "You are a helpful assistant." when it is null.
    /// </summary>
    public string? SystemPrompt { get; init; }
    /// <summary>
    /// The grounding context passages provided to the scenario (for example policy
    /// document excerpts). Joined newline-by-newline into <see cref="ContextBlock"/>, which
    /// the evaluator includes as provided context and folds into the grounding context used
    /// to judge groundedness of the answer. Defaults to empty.
    /// </summary>
    public IReadOnlyList<string> Context { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Identifiers of the knowledge sources the run is expected to retrieve (for example a
    /// policy document id). When the matched contract requires source traceability, the
    /// boundary validator flags any of these missing from <see cref="AiRunResult.RetrievedSources"/>,
    /// comparing case-insensitively. Defaults to empty.
    /// </summary>
    public IReadOnlyList<string> RequiredSources { get; init; } = Array.Empty<string>();
    /// <summary>
    /// The points a complete, correct answer must make. The evaluator lists them as the
    /// expected behavior claims in the judge prompt and as the ground truth that the
    /// completeness evaluator scores the answer against. Defaults to empty.
    /// </summary>
    public IReadOnlyList<string> RequiredClaims { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Claims the answer must not make (for example "all refunds are approved
    /// automatically"). The evaluator lists them as forbidden claims in the judge prompt and
    /// as claims the ground truth states a complete answer makes none of. Defaults to empty.
    /// </summary>
    public IReadOnlyList<string> ForbiddenClaims { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Tool/function names the run is expected to call. The boundary validator unions these
    /// with the contract's required tools and flags any that never appear in
    /// <see cref="AiRunResult.ToolCalls"/>; the sample system under test also keys some of its
    /// behavior off whether a tool (for example "order.lookup") is expected. Defaults to empty.
    /// </summary>
    public IReadOnlyList<string> ExpectedTools { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Tool/function names the run must not call (for example "refund.issue" or
    /// "payment.capture"). The boundary validator unions these with the contract's forbidden
    /// tools and flags any that were actually invoked in <see cref="AiRunResult.ToolCalls"/>.
    /// Defaults to empty.
    /// </summary>
    public IReadOnlyList<string> ForbiddenTools { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Free-form labels for organizing and filtering scenarios (for example "rag",
    /// "tool-use", "refund"). Purely descriptive metadata; not consulted by the evaluation
    /// or gate logic. Defaults to empty.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Per-scenario minimum-score overrides keyed by metric name (for example
    /// "Groundedness" or "Relevance"). For each metric the gatekeeper checks, a matching
    /// entry here replaces the gate policy's default <c>MinMetrics</c> threshold for this
    /// scenario only. Keyed case-insensitively; defaults to an empty,
    /// ordinal-ignore-case dictionary (use policy defaults for all metrics).
    /// </summary>
    public IReadOnlyDictionary<string, double> Thresholds { get; init; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Free-form, case-insensitive key/value annotations attached to the scenario. Available
    /// for callers and reporting; not consulted by the evaluation or gate logic. Defaults to
    /// an empty, ordinal-ignore-case dictionary.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The <see cref="Context"/> passages concatenated into a single newline-separated block.
    /// Computed on access and marked <see cref="JsonIgnoreAttribute"/> so it is not serialized;
    /// the evaluator uses it as the provided context and as part of the grounding context.
    /// </summary>
    [JsonIgnore]
    public string ContextBlock => string.Join("\n", Context);
}
