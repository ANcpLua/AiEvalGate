using AiEvalGate.Observability;
using AiEvalGate.Observability.Models;

string root = AppContext.BaseDirectory;
string[] scenarioPaths = args.Length > 0
    ? args
    : [Path.Combine(root, "Data", "incident-triage.jsonl")];

var records = new List<ObservabilityEvaluationRecord>();
foreach (string scenarioPath in scenarioPaths)
{
    records.AddRange(ScenarioLoader.LoadJsonl(Path.GetFullPath(scenarioPath)));
}

IReadOnlyList<ScenarioRunResult> results = await EvaluationRunner.RunAsync(records, CancellationToken.None);

foreach (ScenarioRunResult result in results)
{
    Console.WriteLine($"{result.Id}: {(result.Passed ? "PASS" : "FAIL")}");
    foreach (string mismatch in result.Mismatches)
    {
        Console.Error.WriteLine($"  {mismatch}");
    }
}

int failed = results.Count(static result => !result.Passed);
Console.WriteLine($"observability eval: {results.Count - failed}/{results.Count} scenarios passed");

return failed == 0 ? 0 : 1;
