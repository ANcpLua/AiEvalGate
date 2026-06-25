using AiEvalGate.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;

namespace AiEvalGate.Core.Evaluation;

public sealed class MicrosoftQualityEvaluator
{
    private const int JudgeSamples = 3;

    private readonly ChatConfiguration _chatConfiguration;
    private readonly IReadOnlyList<IEvaluator> _evaluators;

    public MicrosoftQualityEvaluator(ChatConfiguration chatConfiguration)
    {
        _chatConfiguration = chatConfiguration;
        _evaluators = new IEvaluator[]
        {
            new RelevanceEvaluator(),
            new CoherenceEvaluator(),
            new CompletenessEvaluator(),
            new GroundednessEvaluator()
        };
    }

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
                CompletenessEvaluator => new EvaluationContext[] { new CompletenessEvaluatorContext(BuildGroundTruth(scenario)) },
                GroundednessEvaluator => new EvaluationContext[] { new GroundednessEvaluatorContext(BuildGroundingContext(scenario, runResult)) },
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
