namespace AiEvalGate.Core.Models;

/// <summary>
/// The captured output of a single AI scenario run, produced by an
/// <c>IAiSystemUnderTest</c> for the scenario identified by <see cref="ScenarioId"/>.
/// This is the evidence record the evaluation pipeline grades: it feeds the quality
/// evaluators, the AI reviewer team, the service-boundary validator, the AI-only
/// gatekeeper, and the report/artifact writers.
/// </summary>
/// <remarks>
/// The record carries no scoring or pass/fail state of its own; it only records what
/// the system under test produced (its answer, what it retrieved, which tools it called,
/// and which service operations it performed). Verdicts are derived downstream and are
/// rendered separately as a <c>GateResult</c>. Consistent with the AI-only policy
/// invariant enforced by the gatekeeper, this record reflects an automated run with no
/// human-in-the-loop step.
/// </remarks>
public sealed record AiRunResult
{
    /// <summary>
    /// Identifier of the scenario this run answers; mirrors the scenario's <c>Id</c> and
    /// is used to correlate the run with its gate result and report artifacts.
    /// </summary>
    public required string ScenarioId { get; init; }
    /// <summary>
    /// The final response the system under test returned to the user. This is the text
    /// graded for grounding and quality and echoed into the human-readable report.
    /// </summary>
    public required string FinalAnswer { get; init; }
    /// <summary>
    /// Identifiers of the knowledge sources the run actually retrieved (for example
    /// policy document ids). The service-boundary validator checks these against the
    /// scenario's required sources, comparing case-insensitively. Defaults to empty.
    /// </summary>
    public IReadOnlyList<string> RetrievedSources { get; init; } = Array.Empty<string>();
    /// <summary>
    /// The retrieved context passages supplied to the model as grounding for the answer.
    /// Evaluators join these into the grounding context used to judge groundedness of
    /// <see cref="FinalAnswer"/>. Defaults to empty.
    /// </summary>
    public IReadOnlyList<string> RetrievedContext { get; init; } = Array.Empty<string>();
    /// <summary>
    /// The ordered trace of tool/function calls the run made. The boundary validator
    /// asserts that every required tool appears and that no forbidden tool was invoked.
    /// Defaults to empty.
    /// </summary>
    public IReadOnlyList<ToolCallTrace> ToolCalls { get; init; } = Array.Empty<ToolCallTrace>();
    /// <summary>
    /// The trace of service operations performed during the run, modeling the AI
    /// pipeline's stages (intent resolution, retrieval, answer composition, safety, etc.).
    /// The boundary validator checks these against the architecture's required services
    /// and operations. Defaults to empty.
    /// </summary>
    public IReadOnlyList<ServiceTrace> ServiceTraces { get; init; } = Array.Empty<ServiceTrace>();
    /// <summary>
    /// Free-form, case-insensitive key/value metadata about the run (for example the
    /// architecture it exercised or a sample marker). Defaults to an empty,
    /// ordinal-ignore-case dictionary.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// A single tool/function invocation made during an <see cref="AiRunResult"/>, recording
/// which tool ran, the arguments it received, and a short summary of what it returned.
/// </summary>
/// <remarks>
/// The boundary validator matches <see cref="Name"/> case-insensitively against the
/// scenario's expected and forbidden tool lists, so the name is the load-bearing field.
/// </remarks>
public sealed record ToolCallTrace
{
    /// <summary>
    /// The name of the tool or function that was called (for example
    /// <c>retrieval.search</c> or <c>order.lookup</c>); matched case-insensitively when
    /// validating required and forbidden tools.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// The arguments passed to the tool, as case-insensitive name/value pairs. Defaults
    /// to an empty, ordinal-ignore-case dictionary.
    /// </summary>
    public IReadOnlyDictionary<string, string> Arguments { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// An optional human-readable summary of the tool's result; <see langword="null"/>
    /// when no summary was captured.
    /// </summary>
    public string? ResultSummary { get; init; }
}

/// <summary>
/// A trace of one service operation executed during an <see cref="AiRunResult"/>,
/// modeling a stage of the AI pipeline (such as intent resolution, retrieval, answer
/// composition, or a safety check) in span-like terms.
/// </summary>
/// <remarks>
/// <see cref="ServiceName"/> and <see cref="Operation"/> mirror the OpenTelemetry-style
/// service/operation pairing the observability layer uses for spans, and the boundary
/// validator matches both case-insensitively against the architecture's required services
/// and operations. When source traceability is required, retrieval operations are expected
/// to carry non-empty <see cref="SourceIds"/>.
/// </remarks>
public sealed record ServiceTrace
{
    /// <summary>
    /// The name of the service (or pipeline stage) that performed the operation, for
    /// example <c>RetrievalService</c> or <c>SafetyPolicyService</c>; matched
    /// case-insensitively when validating required services.
    /// </summary>
    public required string ServiceName { get; init; }
    /// <summary>
    /// The operation the service performed, for example <c>retrieval.search</c> or
    /// <c>answer.compose</c>; matched case-insensitively when validating required
    /// operations, and inspected for the substring <c>retrieval</c> to enforce source
    /// traceability.
    /// </summary>
    public required string Operation { get; init; }
    /// <summary>
    /// An optional short summary of the operation's input; <see langword="null"/> when
    /// not captured.
    /// </summary>
    public string? InputSummary { get; init; }
    /// <summary>
    /// An optional short summary of the operation's output; <see langword="null"/> when
    /// not captured.
    /// </summary>
    public string? OutputSummary { get; init; }
    /// <summary>
    /// The knowledge-source identifiers this operation touched. Retrieval operations are
    /// required to populate this list when the boundary contract demands source
    /// traceability; otherwise an empty list is allowed. Defaults to empty.
    /// </summary>
    public IReadOnlyList<string> SourceIds { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Free-form, case-insensitive key/value metadata about the operation. Defaults to an
    /// empty, ordinal-ignore-case dictionary.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
