using AiEvalGate.Core.Models;

namespace AiEvalGate.SampleApp;

/// <summary>
/// The system under test in the AI-only evaluation pipeline: an AI assistant (for example
/// <see cref="PolicyAssistantSystemUnderTest"/>) that answers a single <see cref="AiScenario"/>
/// and reports what it produced as an <see cref="AiRunResult"/>.
/// </summary>
/// <remarks>
/// The runner and the evaluation tests invoke an implementation once per scenario, then hand the
/// returned result to the quality evaluators, the AI reviewer team, the service-boundary validator,
/// and the AI-only gatekeeper for grading. Implementations only run the scenario and record their
/// behavior (final answer, retrieved sources/context, tool calls, and service traces); they carry no
/// scoring or pass/fail logic, and — consistent with the AI-only policy invariant the gatekeeper
/// enforces — the run is fully automated with no human-in-the-loop step.
/// </remarks>
public interface IAiSystemUnderTest
{
    /// <summary>
    /// Runs the assistant against a single scenario and captures its output as the evidence record
    /// the downstream evaluators, reviewers, boundary validator, and gatekeeper grade.
    /// </summary>
    /// <remarks>
    /// The implementation answers <paramref name="scenario"/>'s user input (grounded in its context
    /// and required sources), records the tool/function calls it made and the OpenTelemetry-style
    /// service/operation traces for the AI pipeline stages it executed (intent resolution, retrieval,
    /// answer composition, safety check), and returns them together. It produces no verdict of its
    /// own; pass/fail is derived downstream and rendered separately as a <c>GateResult</c>.
    /// </remarks>
    /// <param name="scenario">
    /// The evaluation case to exercise: its user input, optional system prompt, grounding context, and
    /// the expected/forbidden claims, required sources, and expected/forbidden tools the run is graded
    /// against; its <see cref="AiScenario.Id"/> and <see cref="AiScenario.Architecture"/> shape the
    /// result's scenario id and emitted service names.
    /// </param>
    /// <param name="cancellationToken">A token to observe while awaiting the run.</param>
    /// <returns>
    /// A task whose result is the captured <see cref="AiRunResult"/> for the scenario — its final
    /// answer, retrieved sources and context, tool-call trace, and service traces — with
    /// <see cref="AiRunResult.ScenarioId"/> set to the scenario's id.
    /// </returns>
    Task<AiRunResult> RunAsync(AiScenario scenario, CancellationToken cancellationToken = default);
}
