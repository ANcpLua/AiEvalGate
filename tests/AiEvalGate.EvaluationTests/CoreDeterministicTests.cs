using AiEvalGate.Core.Boundaries;
using AiEvalGate.Core.Evaluation;
using AiEvalGate.Core.Models;
using AiEvalGate.Core.Scenarios;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AiEvalGate.EvaluationTests;

/// <summary>
/// Deterministic gate, validator, and loader tests. None of these touch a model judge,
/// so they pin the AI-only gate's decision logic in CI without any API key.
/// </summary>
[TestClass]
public sealed class CoreDeterministicTests
{
    private static AiScenario Scenario(
        string architecture = "monolith",
        IReadOnlyList<string>? forbiddenTools = null) => new()
    {
        Id = "s1",
        Area = "refunds",
        Architecture = architecture,
        UserInput = "hi",
        ForbiddenTools = forbiddenTools ?? [],
    };

    private static AiRunResult Run(
        IReadOnlyList<ToolCallTrace>? toolCalls = null,
        IReadOnlyList<ServiceTrace>? serviceTraces = null) => new()
    {
        ScenarioId = "s1",
        FinalAnswer = "ok",
        ToolCalls = toolCalls ?? [],
        ServiceTraces = serviceTraces ?? [],
    };

    private static GatePolicy Policy(
        IReadOnlyList<string>? requiredReviewers = null,
        IReadOnlyDictionary<string, double>? minMetrics = null,
        IReadOnlyList<string>? blockSeverities = null,
        bool serviceBoundaryStrict = false,
        AiOnlyPolicy? aiOnly = null) => new()
    {
        RequiredReviewers = requiredReviewers ?? [],
        MinMetrics = minMetrics ?? new Dictionary<string, double>(),
        MinReviewerScore = 0.0,
        BlockSeverities = blockSeverities ?? ["P0", "P1"],
        RequireAllReviewerPasses = false,
        ServiceBoundaryStrict = serviceBoundaryStrict,
        AiOnlyPolicy = aiOnly ?? new AiOnlyPolicy
        {
            HumanReviewRequired = false,
            ManualOverrideAllowed = false,
            ManualApprovalSteps = 0,
        },
    };

    // ---- AiEvalGatekeeper ----

    [TestMethod]
    public void Evaluate_NothingBlocks_Passes()
    {
        GateResult gate = AiEvalGatekeeper.Evaluate(Scenario(), Run(), [], [], [], Policy());

        Assert.IsTrue(gate.Passed);
        Assert.AreEqual(0, gate.Blockers.Count);
    }

    [TestMethod]
    public void Evaluate_HumanReviewReEnabled_FailsClosed()
    {
        GatePolicy broken = Policy(aiOnly: new AiOnlyPolicy
        {
            HumanReviewRequired = true,
            ManualOverrideAllowed = false,
            ManualApprovalSteps = 0,
        });

        GateResult gate = AiEvalGatekeeper.Evaluate(Scenario(), Run(), [], [], [], broken);

        Assert.IsFalse(gate.Passed);
        Assert.IsTrue(gate.Blockers.Any(b => b.Contains("AI-only policy", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Evaluate_RequiredReviewerMissing_Fails()
    {
        GateResult gate = AiEvalGatekeeper.Evaluate(
            Scenario(), Run(), [], [], [], Policy(requiredReviewers: ["SafetyReviewer"]));

        Assert.IsFalse(gate.Passed);
        Assert.IsTrue(gate.Blockers.Any(b => b.Contains("SafetyReviewer", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Evaluate_BlockingReviewerSeverity_Fails()
    {
        AgentReview review = new()
        {
            Reviewer = "SafetyReviewer",
            Passed = true,
            Score = 1.0,
            Severity = "P0",
            Findings = ["unsafe output"],
        };

        GateResult gate = AiEvalGatekeeper.Evaluate(
            Scenario(), Run(), [], [review], [], Policy(blockSeverities: ["P0"]));

        Assert.IsFalse(gate.Passed);
        Assert.IsTrue(gate.Blockers.Any(b => b.Contains("P0", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Evaluate_MetricBelowThreshold_Fails()
    {
        MetricScore score = new() { Name = "relevance", Value = 0.5, Reason = "weak" };

        GateResult gate = AiEvalGatekeeper.Evaluate(
            Scenario(), Run(), [score], [], [],
            Policy(minMetrics: new Dictionary<string, double> { ["relevance"] = 0.9 }));

        Assert.IsFalse(gate.Passed);
        Assert.IsTrue(gate.Blockers.Any(b => b.Contains("relevance", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Evaluate_BoundaryFailures_BlockOnlyWhenStrict()
    {
        GateResult strict = AiEvalGatekeeper.Evaluate(
            Scenario(), Run(), [], [], ["missing OrchestratorService"], Policy(serviceBoundaryStrict: true));
        Assert.IsFalse(strict.Passed);

        GateResult lenient = AiEvalGatekeeper.Evaluate(
            Scenario(), Run(), [], [], ["missing OrchestratorService"], Policy(serviceBoundaryStrict: false));
        Assert.IsTrue(lenient.Passed);
        Assert.AreEqual(1, lenient.Warnings.Count);
    }

    // ---- ServiceBoundaryValidator ----

    [TestMethod]
    public void Validate_UnknownArchitecture_ReportsMissingContract()
    {
        ServiceBoundaryContract contract = new() { Name = "monolith", Architecture = "monolith" };

        IReadOnlyList<string> failures = ServiceBoundaryValidator.Validate(
            Scenario(architecture: "microservices"), Run(), [contract]);

        Assert.IsTrue(failures.Any(f => f.Contains("No service-boundary contract", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validate_MissingRequiredService_ReportsFailure()
    {
        ServiceBoundaryContract contract = new()
        {
            Name = "monolith",
            Architecture = "monolith",
            RequiredServices = ["OrchestratorService"],
            RequireSourceTraceability = false,
        };

        IReadOnlyList<string> failures = ServiceBoundaryValidator.Validate(Scenario(), Run(), [contract]);

        Assert.IsTrue(failures.Any(f => f.Contains("OrchestratorService", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validate_ForbiddenToolUse_ReportsFailure()
    {
        ServiceBoundaryContract contract = new()
        {
            Name = "monolith",
            Architecture = "monolith",
            RequireSourceTraceability = false,
        };
        AiRunResult run = Run(toolCalls: [new ToolCallTrace { Name = "payment.capture" }]);

        IReadOnlyList<string> failures = ServiceBoundaryValidator.Validate(
            Scenario(forbiddenTools: ["payment.capture"]), run, [contract]);

        Assert.IsTrue(failures.Any(f => f.Contains("Forbidden tool", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validate_SatisfiedContract_Passes()
    {
        ServiceBoundaryContract contract = new()
        {
            Name = "monolith",
            Architecture = "monolith",
            RequiredServices = ["AnswerService"],
            RequireSourceTraceability = false,
        };
        AiRunResult run = Run(serviceTraces:
        [
            new ServiceTrace { ServiceName = "AnswerService", Operation = "answer.compose" },
        ]);

        IReadOnlyList<string> failures = ServiceBoundaryValidator.Validate(Scenario(), run, [contract]);

        Assert.AreEqual(0, failures.Count);
    }

    // ---- ScenarioLoader ----

    [TestMethod]
    public void LoadJsonl_MissingFile_Throws()
    {
        Assert.ThrowsExactly<FileNotFoundException>(
            () => ScenarioLoader.LoadJsonl(Path.Combine(Path.GetTempPath(), "does-not-exist.jsonl")));
    }

    [TestMethod]
    public void LoadJsonl_BlankAndCommentLines_AreSkipped()
    {
        string path = WriteTemp(
            "# a comment\n" +
            "\n" +
            "{\"id\":\"only\",\"area\":\"a\",\"architecture\":\"monolith\",\"userInput\":\"hi\"}\n");
        try
        {
            IReadOnlyList<AiScenario> scenarios = ScenarioLoader.LoadJsonl(path);
            Assert.AreEqual(1, scenarios.Count);
            Assert.AreEqual("only", scenarios[0].Id);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LoadJsonl_BlankId_Throws()
    {
        string path = WriteTemp("{\"id\":\"\",\"area\":\"a\",\"architecture\":\"monolith\",\"userInput\":\"hi\"}\n");
        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => ScenarioLoader.LoadJsonl(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---- GatePolicy.Load ----

    [TestMethod]
    public void Load_MissingFile_Throws()
    {
        Assert.ThrowsExactly<FileNotFoundException>(
            () => GatePolicy.Load(Path.Combine(Path.GetTempPath(), "no-policy.json")));
    }

    [TestMethod]
    public void Load_MalformedJson_WrapsWithContext()
    {
        string path = WriteTemp("{ this is not valid json ");
        try
        {
            InvalidOperationException ex = Assert.ThrowsExactly<InvalidOperationException>(
                () => GatePolicy.Load(path));
            Assert.IsTrue(ex.Message.Contains(path, StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Load_RepoDefault_EnforcesAiOnlyInvariant()
    {
        string root = TestPaths.FindRepositoryRoot();
        GatePolicy policy = GatePolicy.Load(Path.Combine(root, "evals", "thresholds", "default-gates.json"));

        Assert.IsFalse(policy.AiOnlyPolicy.HumanReviewRequired);
        Assert.IsFalse(policy.AiOnlyPolicy.ManualOverrideAllowed);
        Assert.AreEqual(0, policy.AiOnlyPolicy.ManualApprovalSteps);
    }

    // ---- GateResult.ToFailureMessage ----

    [TestMethod]
    public void ToFailureMessage_Failed_ListsEveryBlocker()
    {
        GateResult gate = new()
        {
            ScenarioId = "s1",
            Passed = false,
            Blockers = ["first blocker", "second blocker"],
        };

        string message = gate.ToFailureMessage();

        Assert.IsTrue(message.Contains("failed", StringComparison.Ordinal));
        Assert.IsTrue(message.Contains("first blocker", StringComparison.Ordinal));
        Assert.IsTrue(message.Contains("second blocker", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ToFailureMessage_Passed_IsPassNote()
    {
        GateResult gate = new() { ScenarioId = "s1", Passed = true };

        Assert.IsTrue(gate.ToFailureMessage().Contains("passed", StringComparison.Ordinal));
    }

    private static string WriteTemp(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"aievalgate-test-{Path.GetRandomFileName()}.jsonl");
        File.WriteAllText(path, content);
        return path;
    }
}
