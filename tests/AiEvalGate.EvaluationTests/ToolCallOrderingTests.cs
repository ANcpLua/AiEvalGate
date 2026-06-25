using AiEvalGate.Observability.Evaluators;
using AiEvalGate.Observability.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AiEvalGate.EvaluationTests;

/// <summary>
/// Deterministic tests for <see cref="ToolCallOrderingEvaluator"/>: happens-before ordering is decided
/// exactly from the recorded call sequence, so each case has one correct verdict with no judge.
/// </summary>
[TestClass]
public sealed class ToolCallOrderingTests
{
    private static ObservabilityEvaluationRecord Record(IEnumerable<string> toolCallNames, params ToolOrderConstraint[] constraints) => new()
    {
        Id = "r",
        Source = "test",
        Scenario = "tool-order",
        Agent = new AgentInfo { Name = "A", ModelProvider = "p", ModelName = "m", Instructions = "i" },
        UserInput = "u",
        FinalResponse = "f",
        ToolCalls = [.. toolCallNames.Select(n => new ToolCallRecord { Name = n })],
        ExpectedToolOrder = constraints,
        ShouldPass = true,
    };

    private static ToolOrderConstraint Order(string before, string after) => new() { Before = before, After = after };

    [TestMethod]
    public void Analyze_CorrectOrder_Passes()
    {
        AnalysisResult a = ToolCallOrderingEvaluator.Analyze(
            Record(["auth.check", "payment.capture"], Order("auth.check", "payment.capture")));
        Assert.IsTrue(a.Passed, a.Reason);
    }

    [TestMethod]
    public void Analyze_AfterBeforeBefore_Fails()
    {
        AnalysisResult a = ToolCallOrderingEvaluator.Analyze(
            Record(["payment.capture", "auth.check"], Order("auth.check", "payment.capture")));
        Assert.IsFalse(a.Passed);
        Assert.IsTrue(a.Reason.Contains("payment.capture", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Analyze_AfterWithoutAnyBefore_Fails()
    {
        AnalysisResult a = ToolCallOrderingEvaluator.Analyze(
            Record(["payment.capture"], Order("auth.check", "payment.capture")));
        Assert.IsFalse(a.Passed);
    }

    [TestMethod]
    public void Analyze_AfterNeverOccurs_PassesVacuously()
    {
        AnalysisResult a = ToolCallOrderingEvaluator.Analyze(
            Record(["auth.check"], Order("auth.check", "payment.capture")));
        Assert.IsTrue(a.Passed, a.Reason);
    }

    [TestMethod]
    public void Analyze_NoConstraints_PassesVacuously()
    {
        AnalysisResult a = ToolCallOrderingEvaluator.Analyze(Record(["payment.capture", "auth.check"]));
        Assert.IsTrue(a.Passed, a.Reason);
    }

    [TestMethod]
    public void Analyze_CaseInsensitiveToolNames_Passes()
    {
        AnalysisResult a = ToolCallOrderingEvaluator.Analyze(
            Record(["Auth.Check", "Payment.Capture"], Order("auth.check", "payment.capture")));
        Assert.IsTrue(a.Passed, a.Reason);
    }

    public TestContext TestContext { get; set; } = null!;
}
