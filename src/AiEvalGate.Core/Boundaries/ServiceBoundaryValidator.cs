using AiEvalGate.Core.Models;

namespace AiEvalGate.Core.Boundaries;

/// <summary>
/// Deterministic, non-AI checker that verifies an <see cref="AiRunResult"/> respected the
/// service-boundary contract declared for its scenario's architecture. It confirms that the
/// expected OpenTelemetry-style service and operation traces were emitted, that required tools
/// were called and forbidden tools were not, and (when the contract demands it) that retrieval
/// was traceable back to declared sources. The validator only inspects recorded traces; it makes
/// no model calls, keeping it on the deterministic side of the AI-only gate.
/// </summary>
/// <remarks>
/// The validator selects the single contract whose <see cref="ServiceBoundaryContract.Architecture"/>
/// matches the scenario's architecture (case-insensitive). All comparisons throughout are performed
/// with <see cref="StringComparison.OrdinalIgnoreCase"/>. The returned failure messages are consumed
/// by the gatekeeper as one input to the overall AI-only pass/fail decision.
/// </remarks>
public static class ServiceBoundaryValidator
{
    /// <summary>
    /// Validates a single agent run against the service-boundary contract that matches the scenario's
    /// architecture, returning every contract violation found.
    /// </summary>
    /// <remarks>
    /// Resolves the contract by case-insensitive match on <see cref="ServiceBoundaryContract.Architecture"/>
    /// against <see cref="AiScenario.Architecture"/>; if none matches, a single "no contract found" failure
    /// is returned and no further checks run. Otherwise the method accumulates a failure for each of:
    /// a required service absent from <see cref="AiRunResult.ServiceTraces"/> (matched on
    /// <see cref="ServiceTrace.ServiceName"/>); a required operation absent from the service traces
    /// (matched on <see cref="ServiceTrace.Operation"/>); a required tool — the union of the scenario's
    /// <see cref="AiScenario.ExpectedTools"/> and the contract's <see cref="ServiceBoundaryContract.RequiredTools"/> —
    /// never called in <see cref="AiRunResult.ToolCalls"/> (matched on <see cref="ToolCallTrace.Name"/>);
    /// and a forbidden tool — the union of <see cref="AiScenario.ForbiddenTools"/> and
    /// <see cref="ServiceBoundaryContract.ForbiddenTools"/> — that was called. When
    /// <see cref="ServiceBoundaryContract.RequireSourceTraceability"/> is set, it additionally flags any
    /// <see cref="AiScenario.RequiredSources"/> entry missing from <see cref="AiRunResult.RetrievedSources"/>,
    /// and any retrieval-style service trace (one whose <see cref="ServiceTrace.Operation"/> contains
    /// "retrieval") that carries no <see cref="ServiceTrace.SourceIds"/>. All string comparisons use
    /// <see cref="StringComparison.OrdinalIgnoreCase"/>.
    /// </remarks>
    /// <param name="scenario">
    /// The scenario under test, supplying the architecture used to select the contract along with the
    /// scenario-level expected/forbidden tools and required sources folded into the checks.
    /// </param>
    /// <param name="runResult">
    /// The recorded outcome of executing the scenario, whose service traces, tool calls, and retrieved
    /// sources are inspected against the contract.
    /// </param>
    /// <param name="contracts">
    /// The available service-boundary contracts; the first whose architecture matches the scenario is used.
    /// </param>
    /// <returns>
    /// A read-only list of human-readable failure messages, one per violation. An empty list means the run
    /// satisfied the matching contract (the boundary check passed).
    /// </returns>
    public static IReadOnlyList<string> Validate(
        AiScenario scenario,
        AiRunResult runResult,
        IReadOnlyList<ServiceBoundaryContract> contracts)
    {
        var failures = new List<string>();
        ServiceBoundaryContract? contract = contracts.FirstOrDefault(c =>
            string.Equals(c.Architecture, scenario.Architecture, StringComparison.OrdinalIgnoreCase));

        if (contract is null)
        {
            failures.Add($"No service-boundary contract found for architecture '{scenario.Architecture}'.");
            return failures;
        }

        foreach (string requiredService in contract.RequiredServices)
        {
            if (!runResult.ServiceTraces.Any(t => string.Equals(t.ServiceName, requiredService, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"Required service trace missing: {requiredService}");
            }
        }

        foreach (string requiredOperation in contract.RequiredOperations)
        {
            if (!runResult.ServiceTraces.Any(t => string.Equals(t.Operation, requiredOperation, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"Required operation trace missing: {requiredOperation}");
            }
        }

        foreach (string requiredTool in scenario.ExpectedTools.Concat(contract.RequiredTools).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!runResult.ToolCalls.Any(t => string.Equals(t.Name, requiredTool, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"Required tool call missing: {requiredTool}");
            }
        }

        foreach (string forbiddenTool in scenario.ForbiddenTools.Concat(contract.ForbiddenTools).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (runResult.ToolCalls.Any(t => string.Equals(t.Name, forbiddenTool, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"Forbidden tool call was used: {forbiddenTool}");
            }
        }

        if (contract.RequireSourceTraceability)
        {
            foreach (string source in scenario.RequiredSources)
            {
                if (!runResult.RetrievedSources.Contains(source, StringComparer.OrdinalIgnoreCase))
                {
                    failures.Add($"Required source not retrieved: {source}");
                }
            }

            foreach (ServiceTrace trace in runResult.ServiceTraces)
            {
                if (trace.Operation.Contains("retrieval", StringComparison.OrdinalIgnoreCase) && trace.SourceIds.Count == 0)
                {
                    failures.Add($"Retrieval trace '{trace.ServiceName}.{trace.Operation}' has no source ids.");
                }
            }
        }

        return failures;
    }
}
