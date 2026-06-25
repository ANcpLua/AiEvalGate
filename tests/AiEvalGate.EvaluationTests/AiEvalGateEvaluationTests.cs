using AiEvalGate.Core.Boundaries;
using AiEvalGate.Core.Evaluation;
using AiEvalGate.Core.Infrastructure;
using AiEvalGate.Core.Models;
using AiEvalGate.Core.Reporting;
using AiEvalGate.Core.Reviewers;
using AiEvalGate.Core.Scenarios;
using AiEvalGate.SampleApp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AiEvalGate.EvaluationTests;

[TestClass]
public sealed class AiEvalGateEvaluationTests
{
    [TestMethod]
    [TestCategory("Integration")]
    [DataRow("evals/scenarios/refund-policy.jsonl")]
    [DataRow("evals/scenarios/security.jsonl")]
    public async Task ScenarioPackPassesAiEvalGateEvaluationGates(string scenarioPack)
    {
        string root = TestPaths.FindRepositoryRoot();
        string artifactRoot = Environment.GetEnvironmentVariable("AI_EVAL_ARTIFACT_DIR")
            ?? Path.Combine(root, "artifacts", "ai-eval");

        IReadOnlyList<AiScenario> scenarios = ScenarioLoader.LoadJsonl(Path.Combine(root, scenarioPack));
        GatePolicy policy = GatePolicy.Load(Path.Combine(root, "evals", "thresholds", "default-gates.json"));
        IReadOnlyList<ServiceBoundaryContract> serviceContracts = ServiceBoundaryContract.LoadMany(Path.Combine(root, "evals", "service-boundaries", "refund-services.json"));

        // The gate wiring is exercised end-to-end on every run with a deterministic stub judge (no API
        // call); the real LLM judge runs only when AI_EVAL_RUN_LIVE_EVALS=true, to verify model behavior.
        IQualityEvaluator qualityEvaluator;
        AiReviewerTeam reviewerTeam;
        if (LiveLlmJudgeTestsEnabled())
        {
            IChatClient judgeClient = AiClientFactory.CreateJudgeChatClientFromEnvironment();
            qualityEvaluator = new MicrosoftQualityEvaluator(new ChatConfiguration(judgeClient));
            reviewerTeam = AiReviewerTeam.CreateDefault(judgeClient);
        }
        else
        {
            TestContext.WriteLine("Using the deterministic stub judge (no API call). Set AI_EVAL_RUN_LIVE_EVALS=true to run against the real Anthropic model.");
            qualityEvaluator = new StubQualityEvaluator();
            reviewerTeam = StubReviewerAgent.DefaultRoster();
        }

        var systemUnderTest = new PolicyAssistantSystemUnderTest();
        var writer = new EvaluationArtifactWriter(artifactRoot);

        var failures = new List<string>();
        var gates = new List<GateResult>();

        foreach (AiScenario scenario in scenarios)
        {
            AiRunResult run = await systemUnderTest.RunAsync(scenario, CancellationToken.None);
            IReadOnlyList<MetricScore> scores = await qualityEvaluator.EvaluateAsync(scenario, run, CancellationToken.None);
            IReadOnlyList<AgentReview> reviews = await reviewerTeam.ReviewAsync(scenario, run, scores, CancellationToken.None);
            IReadOnlyList<string> boundaryFailures = ServiceBoundaryValidator.Validate(scenario, run, serviceContracts);

            GateResult gate = AiEvalGatekeeper.Evaluate(scenario, run, scores, reviews, boundaryFailures, policy);
            gates.Add(gate);
            await writer.WriteScenarioAsync(scenario, run, scores, reviews, boundaryFailures, gate, CancellationToken.None);

            if (!gate.Passed)
            {
                failures.Add(gate.ToFailureMessage());
            }
        }

        await writer.WriteSummaryAsync(gates, CancellationToken.None);
        await writer.WriteJUnitAsync(gates, CancellationToken.None);

        Assert.IsFalse(failures.Count > 0, string.Join("\n", failures));
    }

    public TestContext TestContext { get; set; } = null!;

    private static bool LiveLlmJudgeTestsEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable("AI_EVAL_RUN_LIVE_EVALS"), "true", StringComparison.OrdinalIgnoreCase);
}
