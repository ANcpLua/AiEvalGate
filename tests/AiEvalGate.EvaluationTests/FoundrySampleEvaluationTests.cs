using AiEvalGate.Core.Samples;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AiEvalGate.EvaluationTests;

[TestClass]
public sealed class FoundrySampleEvaluationTests
{
    [TestMethod]
    public async Task FoundrySamplesPassDeterministicEvaluationPipeline()
    {
        if (!FoundrySampleTestsEnabled())
        {
            TestContext.WriteLine("Set AI_EVAL_ENABLE_FOUNDRY_SAMPLE_TESTS=true or AI_EVAL_FOUNDRY_SAMPLE_REPO_ROOT to run the private Foundry sample evaluation test.");
            return;
        }

        string root = TestPaths.FindRepositoryRoot();
        string artifactRoot = Environment.GetEnvironmentVariable("AI_EVAL_FOUNDRY_SAMPLE_ARTIFACT_DIR")
            ?? Path.Combine(root, "artifacts", "foundry-sample-eval-tests");

        FoundrySampleEvaluationReport report = await FoundrySampleEvaluationRunner.RunAsync(
            new FoundrySampleEvaluationOptions
            {
                RepositoryRoot = Environment.GetEnvironmentVariable("AI_EVAL_FOUNDRY_SAMPLE_REPO_ROOT"),
                RepositoryUrl = Environment.GetEnvironmentVariable("AI_EVAL_FOUNDRY_SAMPLE_REPO_URL")
                    ?? "https://github.com/ANcpLua/agent-framework-codex-pr.git",
                Branch = Environment.GetEnvironmentVariable("AI_EVAL_FOUNDRY_SAMPLE_BRANCH") ?? "main",
                ArtifactRoot = artifactRoot
            },
            CancellationToken.None);

        Assert.IsTrue(report.Passed, string.Join("\n", report.FailureMessages));
    }

    public TestContext TestContext { get; set; } = null!;

    private static bool FoundrySampleTestsEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable("AI_EVAL_ENABLE_FOUNDRY_SAMPLE_TESTS"), "true", StringComparison.OrdinalIgnoreCase)
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AI_EVAL_FOUNDRY_SAMPLE_REPO_ROOT"));
}
