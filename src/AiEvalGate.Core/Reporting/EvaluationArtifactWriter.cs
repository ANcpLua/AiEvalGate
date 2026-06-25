using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using AiEvalGate.Core.Models;
using AiEvalGate.Core;

namespace AiEvalGate.Core.Reporting;

public sealed class EvaluationArtifactWriter
{
    private readonly string _root;

    public EvaluationArtifactWriter(string root)
    {
        _root = root;
        Directory.CreateDirectory(Path.Combine(_root, "runs"));
    }

    public async Task WriteScenarioAsync(
        AiScenario scenario,
        AiRunResult runResult,
        IReadOnlyList<MetricScore> scores,
        IReadOnlyList<AgentReview> reviews,
        IReadOnlyList<string> serviceBoundaryFailures,
        GateResult gate,
        CancellationToken cancellationToken = default)
    {
        var artifact = new
        {
            scenario,
            runResult,
            scores,
            reviews,
            serviceBoundaryFailures,
            gate,
            generatedAtUtc = DateTimeOffset.UtcNow
        };

        string jsonPath = Path.Combine(_root, "runs", $"{SafeFileName(scenario.Id)}.json");
        string mdPath = Path.Combine(_root, "runs", $"{SafeFileName(scenario.Id)}.md");

        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(artifact, JsonOptions.Default), cancellationToken);
        await File.WriteAllTextAsync(mdPath, ToMarkdown(scenario, runResult, scores, reviews, serviceBoundaryFailures, gate), cancellationToken);
        await WriteIndexAsync(cancellationToken);
    }

    public async Task WriteSummaryAsync(IReadOnlyList<GateResult> gates, CancellationToken cancellationToken = default)
    {
        var summary = new
        {
            total = gates.Count,
            passed = gates.Count(g => g.Passed),
            failed = gates.Count(g => !g.Passed),
            generatedAtUtc = DateTimeOffset.UtcNow,
            gates
        };

        await File.WriteAllTextAsync(
            Path.Combine(_root, "summary.json"),
            JsonSerializer.Serialize(summary, JsonOptions.Default),
            cancellationToken);
    }

    public async Task WriteJUnitAsync(IReadOnlyList<GateResult> gates, CancellationToken cancellationToken = default)
    {
        var suite = new XElement("testsuite",
            new XAttribute("name", "AiEvalGateEvaluation"),
            new XAttribute("tests", gates.Count),
            new XAttribute("failures", gates.Count(g => !g.Passed)));

        foreach (GateResult gate in gates)
        {
            var test = new XElement("testcase",
                new XAttribute("classname", "AiEvalGateEvaluation"),
                new XAttribute("name", gate.ScenarioId));

            if (!gate.Passed)
            {
                test.Add(new XElement("failure",
                    new XAttribute("message", $"{gate.Blockers.Count} blocker(s)"),
                    string.Join(Environment.NewLine, gate.Blockers)));
            }

            suite.Add(test);
        }

        var document = new XDocument(new XElement("testsuites", suite));
        await File.WriteAllTextAsync(Path.Combine(_root, "junit-ai-eval.xml"), document.ToString(), cancellationToken);
    }

    private async Task WriteIndexAsync(CancellationToken cancellationToken)
    {
        string[] files = Directory.GetFiles(Path.Combine(_root, "runs"), "*.json")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html><head><meta charset='utf-8'><title>AI Evaluation Report</title>");
        sb.AppendLine("<style>body{font-family:system-ui,Segoe UI,sans-serif;margin:2rem} table{border-collapse:collapse;width:100%} td,th{border:1px solid #ddd;padding:.5rem} .pass{color:green}.fail{color:#b00020}</style>");
        sb.AppendLine("</head><body><h1>AI-only evaluation report</h1><table><thead><tr><th>Scenario</th><th>Gate</th><th>Artifact</th></tr></thead><tbody>");

        foreach (string file in files)
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file, cancellationToken));
            string id = doc.RootElement.GetProperty("scenario").GetProperty("id").GetString() ?? Path.GetFileNameWithoutExtension(file);
            bool passed = doc.RootElement.GetProperty("gate").GetProperty("passed").GetBoolean();
            string cls = passed ? "pass" : "fail";
            string status = passed ? "PASS" : "FAIL";
            string md = WebUtility.HtmlEncode("runs/" + Path.GetFileNameWithoutExtension(file) + ".md");
            sb.AppendLine($"<tr><td>{WebUtility.HtmlEncode(id)}</td><td class='{cls}'>{status}</td><td><a href='{md}'>markdown</a></td></tr>");
        }

        sb.AppendLine("</tbody></table></body></html>");
        await File.WriteAllTextAsync(Path.Combine(_root, "index.html"), sb.ToString(), cancellationToken);
    }

    private static string ToMarkdown(
        AiScenario scenario,
        AiRunResult runResult,
        IReadOnlyList<MetricScore> scores,
        IReadOnlyList<AgentReview> reviews,
        IReadOnlyList<string> serviceBoundaryFailures,
        GateResult gate)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {scenario.Id}");
        sb.AppendLine();
        sb.AppendLine($"Gate: **{(gate.Passed ? "PASS" : "FAIL")}**");
        sb.AppendLine();
        sb.AppendLine("## User input");
        sb.AppendLine(scenario.UserInput);
        sb.AppendLine();
        sb.AppendLine("## Final answer");
        sb.AppendLine(runResult.FinalAnswer);
        sb.AppendLine();
        sb.AppendLine("## Evaluator scores");
        foreach (MetricScore score in scores)
        {
            sb.AppendLine($"- {score.Name}: {score.Value:0.###} — {score.Interpretation} {score.Reason}");
        }
        sb.AppendLine();
        sb.AppendLine("## Reviewer agents");
        foreach (AgentReview review in reviews)
        {
            sb.AppendLine($"- {review.Reviewer}: {(review.Passed ? "PASS" : "FAIL")}, score={review.Score:0.###}, severity={review.Severity}");
            foreach (string finding in review.Findings)
            {
                sb.AppendLine($"  - {finding}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("## Service-boundary failures");
        if (serviceBoundaryFailures.Count == 0) sb.AppendLine("None.");
        foreach (string failure in serviceBoundaryFailures) sb.AppendLine($"- {failure}");
        sb.AppendLine();
        sb.AppendLine("## Gate blockers");
        if (gate.Blockers.Count == 0) sb.AppendLine("None.");
        foreach (string blocker in gate.Blockers) sb.AppendLine($"- {blocker}");
        return sb.ToString();
    }

    private static string SafeFileName(string value)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '-');
        }

        return value;
    }
}
