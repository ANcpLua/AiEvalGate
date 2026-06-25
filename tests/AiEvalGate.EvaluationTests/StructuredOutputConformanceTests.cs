using System.Text.Json;
using AiEvalGate.Observability.Evaluators;
using AiEvalGate.Observability.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AiEvalGate.EvaluationTests;

/// <summary>
/// Deterministic tests for <see cref="StructuredOutputConformanceEvaluator"/>: structured-output
/// validation is decided exactly from the JSON, so each case has one correct verdict with no judge.
/// </summary>
[TestClass]
public sealed class StructuredOutputConformanceTests
{
    private static ObservabilityEvaluationRecord Record(string? structuredJson, OutputContract? contract) => new()
    {
        Id = "r",
        Source = "test",
        Scenario = "structured-output",
        Agent = new AgentInfo { Name = "A", ModelProvider = "p", ModelName = "m", Instructions = "i" },
        UserInput = "u",
        FinalResponse = "f",
        StructuredOutput = structuredJson is null ? null : JsonSerializer.Deserialize<JsonElement>(structuredJson),
        OutputContract = contract,
        ShouldPass = true,
    };

    private static OutputContract Contract() => new()
    {
        RequiredProperties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["severity"] = "string",
            ["confidence"] = "number",
            ["resolved"] = "boolean",
        },
        ForbiddenProperties = ["internalDebugTrace"],
    };

    [TestMethod]
    public void Conforming_output_passes()
    {
        AnalysisResult a = StructuredOutputConformanceEvaluator.Analyze(
            Record("""{"severity":"high","confidence":0.9,"resolved":false}""", Contract()));
        Assert.IsTrue(a.Passed, a.Reason);
    }

    [TestMethod]
    public void Missing_required_property_fails()
    {
        AnalysisResult a = StructuredOutputConformanceEvaluator.Analyze(
            Record("""{"severity":"high","confidence":0.9}""", Contract()));
        Assert.IsFalse(a.Passed);
        Assert.IsTrue(a.Reason.Contains("resolved", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Wrong_value_kind_fails()
    {
        AnalysisResult a = StructuredOutputConformanceEvaluator.Analyze(
            Record("""{"severity":"high","confidence":"very","resolved":false}""", Contract()));
        Assert.IsFalse(a.Passed);
        Assert.IsTrue(a.Reason.Contains("confidence", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Forbidden_property_fails()
    {
        AnalysisResult a = StructuredOutputConformanceEvaluator.Analyze(
            Record("""{"severity":"high","confidence":0.9,"resolved":false,"internalDebugTrace":"x"}""", Contract()));
        Assert.IsFalse(a.Passed);
        Assert.IsTrue(a.Reason.Contains("internalDebugTrace", StringComparison.Ordinal));
    }

    [TestMethod]
    public void No_contract_passes_vacuously()
    {
        AnalysisResult a = StructuredOutputConformanceEvaluator.Analyze(Record("""{"anything":1}""", contract: null));
        Assert.IsTrue(a.Passed, a.Reason);
    }

    [TestMethod]
    public void Missing_structured_output_with_a_contract_fails()
    {
        AnalysisResult a = StructuredOutputConformanceEvaluator.Analyze(Record(structuredJson: null, Contract()));
        Assert.IsFalse(a.Passed);
    }

    public TestContext TestContext { get; set; } = null!;
}
