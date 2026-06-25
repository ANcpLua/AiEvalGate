using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using AiEvalGate.Core.Models;
using AiEvalGate.Core;

namespace AiEvalGate.Core.Reporting;

/// <summary>
/// Writes the on-disk artifacts produced by an AI-only evaluation run: per-scenario JSON and
/// Markdown reports, an aggregate run summary, a JUnit XML file for CI, and a browsable HTML index.
/// </summary>
/// <remarks>
/// All artifacts are written under a single root directory; per-scenario reports live in a
/// <c>runs</c> subdirectory and the summary, JUnit, and index files sit at the root. JSON is
/// serialized with the shared <see cref="AiEvalGate.Core.JsonOptions.Default"/> options so every
/// artifact uses the same camelCase, indented shape. The writer only records evidence and the
/// already-computed gate verdicts; it performs no scoring or gating of its own, consistent with the
/// AI-only pipeline where verdicts are produced upstream by the gatekeeper. Instances are cheap and
/// hold only the root path; the type is not guaranteed to be thread-safe for concurrent writes that
/// touch the shared index and summary files.
/// </remarks>
public sealed class EvaluationArtifactWriter
{
    private readonly string _root;

    /// <summary>
    /// Initializes a new writer rooted at <paramref name="root"/>, creating the <c>runs</c>
    /// subdirectory (and any missing parent directories) so per-scenario reports can be written.
    /// </summary>
    /// <param name="root">
    /// The base output directory for all evaluation artifacts. Per-scenario JSON/Markdown files are
    /// written under its <c>runs</c> subdirectory, while <c>summary.json</c>, <c>junit-ai-eval.xml</c>,
    /// and <c>index.html</c> are written directly under it.
    /// </param>
    public EvaluationArtifactWriter(string root)
    {
        _root = root;
        Directory.CreateDirectory(Path.Combine(_root, "runs"));
    }

    /// <summary>
    /// Writes the full per-scenario report as both a JSON artifact and a human-readable Markdown file
    /// under the <c>runs</c> directory, then regenerates the HTML index over all run artifacts.
    /// </summary>
    /// <remarks>
    /// Both files are named from the scenario id with invalid filename characters replaced by hyphens,
    /// so distinct scenario ids that sanitize to the same name will overwrite one another. The JSON
    /// artifact bundles the scenario, run result, scores, reviews, service-boundary failures, and gate
    /// together with a UTC <c>generatedAtUtc</c> timestamp; the Markdown mirrors the same evidence in a
    /// readable layout. The index is rebuilt on every call so it always reflects the latest set of runs.
    /// </remarks>
    /// <param name="scenario">The scenario that was evaluated; its <see cref="AiEvalGate.Core.Models.AiScenario.Id"/> determines the output file names.</param>
    /// <param name="runResult">The captured AI run output being reported (final answer, retrieved sources/context, tool and service traces).</param>
    /// <param name="scores">The evaluator metric scores, rendered in the report with name, value, interpretation, and reason.</param>
    /// <param name="reviews">The AI reviewer verdicts, rendered with pass/fail, score, P0&#8211;P3 severity, and findings.</param>
    /// <param name="serviceBoundaryFailures">The service-boundary violation messages detected for this scenario; rendered as "None." when empty.</param>
    /// <param name="gate">The already-computed gate decision whose <see cref="AiEvalGate.Core.Models.GateResult.Passed"/> flag and blockers are surfaced in both artifacts.</param>
    /// <param name="cancellationToken">A token to cancel the file-write operations.</param>
    /// <returns>A task that completes once the JSON, Markdown, and HTML index files have been written.</returns>
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

    /// <summary>
    /// Writes the aggregate <c>summary.json</c> at the root, rolling up the gate results across all
    /// evaluated scenarios into total, passed, and failed counts plus the individual gates.
    /// </summary>
    /// <remarks>
    /// The summary records the total number of gates, how many passed
    /// (<see cref="AiEvalGate.Core.Models.GateResult.Passed"/> is <see langword="true"/>) and failed, a
    /// UTC <c>generatedAtUtc</c> timestamp, and the full list of gate results, serialized with the
    /// shared JSON options.
    /// </remarks>
    /// <param name="gates">The gate results for every scenario in the run; an empty list yields zero counts.</param>
    /// <param name="cancellationToken">A token to cancel the file-write operation.</param>
    /// <returns>A task that completes once <c>summary.json</c> has been written.</returns>
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

    /// <summary>
    /// Writes <c>junit-ai-eval.xml</c> at the root, mapping each scenario's gate result to a JUnit
    /// test case so CI systems can surface evaluation pass/fail status as test results.
    /// </summary>
    /// <remarks>
    /// A single <c>testsuite</c> named <c>AiEvalGateEvaluation</c> is emitted with its <c>tests</c> count
    /// set to the number of gates and its <c>failures</c> count set to the number of failed gates. Each
    /// gate becomes a <c>testcase</c> named by its
    /// <see cref="AiEvalGate.Core.Models.GateResult.ScenarioId"/>; a failed gate adds a <c>failure</c>
    /// element whose message reports the blocker count and whose body lists the blockers, one per line.
    /// </remarks>
    /// <param name="gates">The gate results to render as JUnit test cases.</param>
    /// <param name="cancellationToken">A token to cancel the file-write operation.</param>
    /// <returns>A task that completes once <c>junit-ai-eval.xml</c> has been written.</returns>
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
