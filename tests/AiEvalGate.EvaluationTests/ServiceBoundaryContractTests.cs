using AiEvalGate.Core.Boundaries;
using AiEvalGate.Core.Models;
using AiEvalGate.Core.Scenarios;
using AiEvalGate.SampleApp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AiEvalGate.EvaluationTests;

[TestClass]
public sealed class ServiceBoundaryContractTests
{
    [TestMethod]
    public async Task SampleSystemEmitsRequiredServiceBoundaryTraces()
    {
        string root = TestPaths.FindRepositoryRoot();
        var contracts = ServiceBoundaryContract.LoadMany(Path.Combine(root, "evals", "service-boundaries", "refund-services.json"));
        var scenarios = ScenarioLoader.LoadJsonl(Path.Combine(root, "evals", "scenarios", "refund-policy.jsonl"))
            .Concat(ScenarioLoader.LoadJsonl(Path.Combine(root, "evals", "scenarios", "security.jsonl")))
            .ToArray();

        var system = new PolicyAssistantSystemUnderTest();
        var failures = new List<string>();

        foreach (AiScenario scenario in scenarios)
        {
            AiRunResult run = await system.RunAsync(scenario, CancellationToken.None);
            failures.AddRange(ServiceBoundaryValidator.Validate(scenario, run, contracts).Select(f => $"{scenario.Id}: {f}"));
        }

        Assert.IsFalse(failures.Count > 0, string.Join("\n", failures));
    }

    public TestContext TestContext { get; set; } = null!;
}
