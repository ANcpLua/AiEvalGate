using System.Text.Json;
using AiEvalGate.Core;

namespace AiEvalGate.Core.Boundaries;

/// <summary>
/// Declares the service-boundary expectations for a single architecture style (for example
/// "microservices" or "monolith"): which services and operations must appear in a run's traces,
/// which tools must or must not be called, and whether retrieved sources must be traceable.
/// </summary>
/// <remarks>
/// Contracts are loaded from JSON via <see cref="LoadMany"/> and consumed by
/// <c>ServiceBoundaryValidator</c>, which selects the contract whose <see cref="Architecture"/>
/// matches the scenario's architecture (case-insensitively) and then verifies the recorded
/// service traces and tool calls against it. The forbidden-tool set encodes the AI-only
/// invariant: the system under test may retrieve and compose answers but must not invoke
/// mutating or financial actions such as <c>refund.issue</c>, <c>payment.capture</c>, or
/// <c>account.delete</c>.
/// </remarks>
public sealed record ServiceBoundaryContract
{
    /// <summary>
    /// Human-readable identifier for this contract (for example "microservice-refund-ai-pipeline").
    /// Used for diagnostics and authoring; matching is performed on <see cref="Architecture"/>, not this value.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// Architecture style this contract applies to (for example "microservices" or "monolith").
    /// The validator selects a contract by comparing this value to the scenario's architecture
    /// case-insensitively, so exactly one contract should be defined per architecture.
    /// </summary>
    public required string Architecture { get; init; }
    /// <summary>
    /// Service names that must appear in the run's service traces. The validator reports a failure
    /// for each entry that has no matching <c>ServiceTrace.ServiceName</c> (compared case-insensitively).
    /// Defaults to empty, meaning no required services.
    /// </summary>
    public IReadOnlyList<string> RequiredServices { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Operation names (for example "retrieval.search", "answer.compose") that must appear in the
    /// run's service traces. The validator reports a failure for each entry that has no matching
    /// <c>ServiceTrace.Operation</c> (compared case-insensitively). Defaults to empty.
    /// </summary>
    public IReadOnlyList<string> RequiredOperations { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Tool names that must be called during the run. The validator unions these with the scenario's
    /// expected tools (de-duplicated case-insensitively) and reports a failure for each tool that has
    /// no matching tool-call trace. Defaults to empty.
    /// </summary>
    public IReadOnlyList<string> RequiredTools { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Tool names that must never be called during the run. The validator unions these with the
    /// scenario's forbidden tools (de-duplicated case-insensitively) and reports a failure if any
    /// matching tool-call trace is present. This is where the AI-only invariant is enforced: mutating
    /// or financial tools (for example <c>refund.issue</c>, <c>payment.capture</c>, <c>account.delete</c>)
    /// are listed here so the assistant stays read-only. Defaults to empty.
    /// </summary>
    public IReadOnlyList<string> ForbiddenTools { get; init; } = Array.Empty<string>();
    /// <summary>
    /// When <see langword="true"/> (the default), the validator additionally enforces source
    /// traceability: every source the scenario requires must appear in the run's retrieved sources,
    /// and every retrieval operation trace must carry at least one source id. When
    /// <see langword="false"/>, these source checks are skipped.
    /// </summary>
    public bool RequireSourceTraceability { get; init; } = true;

    /// <summary>
    /// Loads and deserializes a JSON array of service-boundary contracts from the given file using the
    /// shared <c>JsonOptions.Default</c> settings (camelCase property names, case-insensitive matching,
    /// comments and trailing commas allowed).
    /// </summary>
    /// <param name="path">Absolute or relative path to the JSON file containing the contract array.</param>
    /// <returns>The read-only list of contracts parsed from the file.</returns>
    /// <exception cref="FileNotFoundException">Thrown when no file exists at <paramref name="path"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the JSON deserializes to <see langword="null"/>, or when the file cannot be parsed
    /// (the underlying JSON error is caught and wrapped as the inner exception).
    /// </exception>
    public static IReadOnlyList<ServiceBoundaryContract> LoadMany(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Service-boundary contract file not found.", path);
        }

        string json = File.ReadAllText(path);
        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<ServiceBoundaryContract>>(json, JsonOptions.Default)
                   ?? throw new InvalidOperationException($"Service-boundary contracts deserialized to null: {path}");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Unable to parse service-boundary contracts: {path}", ex);
        }
    }
}
