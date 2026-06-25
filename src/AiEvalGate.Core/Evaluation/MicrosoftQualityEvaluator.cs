using AiEvalGate.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;

namespace AiEvalGate.Core.Evaluation;

/// <summary>
/// Runs the Microsoft.Extensions.AI.Evaluation quality evaluators (relevance, coherence,
/// completeness, and groundedness) over a scenario and its AI run result, producing the
/// numeric <see cref="MetricScore"/> values that <see cref="AiEvalGatekeeper"/> compares
/// against the configured metric thresholds.
/// </summary>
/// <remarks>
/// This is a purely AI-driven, LLM-as-judge stage: every metric is scored by an LLM judge
/// reached through the supplied <see cref="ChatConfiguration"/>, consistent with the
/// AI-only policy invariant enforced downstream (no human review or manual override). To
/// damp the run-to-run instability of a single judge call, each evaluator is sampled
/// <c>JudgeSamples</c> (3) times concurrently and the per-metric median is reported.
/// Completeness and groundedness only yield metrics when their evaluation contexts are
/// supplied, so this type synthesizes those contexts from the scenario's required and
/// forbidden claims and from the scenario plus retrieved context.
/// </remarks>
public sealed class MicrosoftQualityEvaluator
{
    private const int JudgeSamples = 3;

    private readonly ChatConfiguration _chatConfiguration;
    private readonly IReadOnlyList<IEvaluator> _evaluators;

    /// <summary>
    /// Initializes a new <see cref="MicrosoftQualityEvaluator"/> bound to the judge chat
    /// configuration and seeds the fixed set of quality evaluators it will run.
    /// </summary>
    /// <param name="chatConfiguration">
    /// The chat configuration whose underlying <c>IChatClient</c> backs the LLM judge used by
    /// every evaluator sample; typically produced by
    /// <see cref="AiClientFactory.CreateEvaluationChatConfigurationFromEnvironment"/>.
    /// </param>
    /// <remarks>
    /// The evaluator set is constructed once and reused for the lifetime of this instance:
    /// <see cref="RelevanceEvaluator"/>, <see cref="CoherenceEvaluator"/>,
    /// <see cref="CompletenessEvaluator"/>, and <see cref="GroundednessEvaluator"/>.
    /// </remarks>
    public MicrosoftQualityEvaluator(ChatConfiguration chatConfiguration)
    {
        _chatConfiguration = chatConfiguration;
        _evaluators =
        [
            new RelevanceEvaluator(),
            new CoherenceEvaluator(),
            new CompletenessEvaluator(),
            new GroundednessEvaluator()
        ];
    }

    /// <summary>
    /// Scores the AI run result for a scenario with each configured quality evaluator and
    /// returns the median sample for every numeric metric the judges emit.
    /// </summary>
    /// <param name="scenario">
    /// The scenario under test; supplies the system prompt, the required and forbidden claims
    /// used to build the completeness ground truth, and the provided context used to build the
    /// groundedness context.
    /// </param>
    /// <param name="runResult">
    /// The AI system's run result for the scenario; its <see cref="AiRunResult.FinalAnswer"/>
    /// is the assistant response being judged and its retrieved context contributes to the
    /// groundedness context.
    /// </param>
    /// <param name="cancellationToken">A token used to cancel the in-flight judge calls.</param>
    /// <returns>
    /// One <see cref="MetricScore"/> per numeric metric produced across all evaluators. Each
    /// score carries the median sample's value (defaulting to <c>0.0</c> when the judge
    /// returns no value), along with that sample's reason and interpretation.
    /// </returns>
    /// <remarks>
    /// Each evaluator is sampled three times concurrently via <see cref="Task.WhenAll"/>;
    /// samples are grouped by metric name and the median (the element at <c>count / 2</c> after
    /// ordering by value) is selected to stabilize otherwise borderline verdicts. Only
    /// <see cref="NumericMetric"/> results are collected; completeness and groundedness
    /// contribute metrics only because their evaluation contexts are supplied here.
    /// </remarks>
    public async Task<IReadOnlyList<MetricScore>> EvaluateAsync(
        AiScenario scenario,
        AiRunResult runResult,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, scenario.SystemPrompt ?? "You are a helpful assistant."),
            new(ChatRole.User, BuildUserPrompt(scenario, runResult))
        };

        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, runResult.FinalAnswer));
        var scores = new List<MetricScore>();

        foreach (IEvaluator evaluator in _evaluators)
        {
            // Completeness and Groundedness return null metrics unless their contexts are supplied.
            IEnumerable<EvaluationContext>? additionalContext = evaluator switch
            {
                CompletenessEvaluator => [new CompletenessEvaluatorContext(BuildGroundTruth(scenario))],
                GroundednessEvaluator => [new GroundednessEvaluatorContext(BuildGroundingContext(scenario, runResult))],
                _ => null
            };

            // A single judge sample flips borderline scores between runs; the median of three stabilizes the verdict.
            // Samples are independent judge calls, so they run concurrently.
            EvaluationResult[] sampleResults = await Task.WhenAll(
                Enumerable.Range(0, JudgeSamples).Select(_ => evaluator.EvaluateAsync(
                    messages,
                    response,
                    _chatConfiguration,
                    additionalContext,
                    cancellationToken: cancellationToken).AsTask()));

            var samplesByMetric = new Dictionary<string, List<NumericMetric>>();
            foreach (EvaluationResult result in sampleResults)
            {
                foreach (EvaluationMetric metric in result.Metrics.Values)
                {
                    if (metric is NumericMetric numeric)
                    {
                        if (!samplesByMetric.TryGetValue(metric.Name, out List<NumericMetric>? samples))
                        {
                            samplesByMetric[metric.Name] = samples = new List<NumericMetric>();
                        }

                        samples.Add(numeric);
                    }
                }
            }

            foreach ((string name, List<NumericMetric> samples) in samplesByMetric)
            {
                NumericMetric median = samples.OrderBy(m => m.Value ?? 0.0).ElementAt(samples.Count / 2);
                scores.Add(new MetricScore
                {
                    Name = name,
                    Value = median.Value ?? 0.0,
                    Reason = median.Reason,
                    Interpretation = median.Interpretation?.ToString()
                });
            }
        }

        return scores;
    }

    private static string BuildGroundTruth(AiScenario scenario)
    {
        return $$"""
        A complete answer makes each of these points:
        {{string.Join("\n", scenario.RequiredClaims.Select(c => "- " + c))}}

        And makes none of these claims:
        {{string.Join("\n", scenario.ForbiddenClaims.Select(c => "- " + c))}}
        """;
    }

    private static string BuildGroundingContext(AiScenario scenario, AiRunResult runResult)
    {
        return $$"""
        {{scenario.ContextBlock}}

        {{string.Join("\n", runResult.RetrievedContext)}}
        """;
    }

    private static string BuildUserPrompt(AiScenario scenario, AiRunResult runResult)
    {
        return $$"""
        User input:
        {{scenario.UserInput}}

        Expected behavior claims:
        {{string.Join("\n", scenario.RequiredClaims.Select(c => "- " + c))}}

        Forbidden claims:
        {{string.Join("\n", scenario.ForbiddenClaims.Select(c => "- " + c))}}

        Provided context:
        {{scenario.ContextBlock}}

        Retrieved context:
        {{string.Join("\n", runResult.RetrievedContext)}}

        Assistant answer:
        {{runResult.FinalAnswer}}
        """;
    }
}
