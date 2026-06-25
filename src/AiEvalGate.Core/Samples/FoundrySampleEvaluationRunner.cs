using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace AiEvalGate.Core.Samples;

/// <summary>
/// Input options for a Foundry sample evaluation run, selecting either an already checked-out
/// repository or a fresh shallow clone, and naming the directory where logs and the JSON report
/// are written.
/// </summary>
public sealed record FoundrySampleEvaluationOptions
{
    /// <summary>
    /// Absolute or relative path to an existing checkout to evaluate in place. When set (non-blank),
    /// the runner skips cloning and uses this directory; if it does not exist the run is failed.
    /// When null or blank, the runner shallow-clones <see cref="RepositoryUrl"/> at <see cref="Branch"/> instead.
    /// </summary>
    public string? RepositoryRoot { get; init; }

    /// <summary>
    /// Git URL cloned when <see cref="RepositoryRoot"/> is not supplied. Defaults to the
    /// agent-framework-codex-pr repository that hosts the Foundry hosted-agent samples.
    /// </summary>
    public string RepositoryUrl { get; init; } = "https://github.com/ANcpLua/agent-framework-codex-pr.git";

    /// <summary>
    /// Branch passed to the shallow <c>git clone --branch</c> when cloning. Defaults to <c>main</c>.
    /// Ignored when <see cref="RepositoryRoot"/> is supplied.
    /// </summary>
    public string Branch { get; init; } = "main";

    /// <summary>
    /// Required output directory for run artifacts: the clone log, per-sample build logs, and the
    /// <c>foundry-sample-evaluation.json</c> report. Created if it does not already exist.
    /// </summary>
    public required string ArtifactRoot { get; init; }
}

/// <summary>
/// The serialized outcome of one Foundry sample evaluation run: the overall pass/fail decision,
/// the repository- and artifact-root paths used, the start/finish timestamps, any repository-level
/// failures, and the per-sample results. Persisted to <c>foundry-sample-evaluation.json</c>.
/// </summary>
public sealed record FoundrySampleEvaluationReport
{
    /// <summary>
    /// Whether the whole run passed: true only when there are no <see cref="RepositoryFailures"/>
    /// and every entry in <see cref="Samples"/> passed.
    /// </summary>
    public required bool Passed { get; init; }

    /// <summary>
    /// The repository directory that was evaluated: the supplied existing checkout, or the path the
    /// repository was shallow-cloned into.
    /// </summary>
    public required string RepositoryRoot { get; init; }

    /// <summary>
    /// The fully-qualified artifact directory where the clone log, per-sample build logs, and this
    /// report were written.
    /// </summary>
    public required string ArtifactRoot { get; init; }

    /// <summary>The UTC instant the run began, captured before repository preparation.</summary>
    public required DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>The UTC instant the run finished, captured after all samples were evaluated.</summary>
    public required DateTimeOffset FinishedAtUtc { get; init; }

    /// <summary>
    /// Repository-level blockers that short-circuit the run (for example a missing repository root,
    /// a failed clone, or tracked-file-tree mismatches). When non-empty, no samples are evaluated.
    /// </summary>
    public required IReadOnlyList<string> RepositoryFailures { get; init; }

    /// <summary>
    /// The per-sample evaluation results, one per definition in the default plan. Empty when a
    /// repository-level failure prevented sample evaluation.
    /// </summary>
    public required IReadOnlyList<FoundrySampleResult> Samples { get; init; }

    /// <summary>
    /// Flattens every failure into a human-readable sequence: the raw <see cref="RepositoryFailures"/>
    /// followed by each sample failure prefixed with its sample name (<c>Name: failure</c>).
    /// </summary>
    public IEnumerable<string> FailureMessages =>
        RepositoryFailures.Concat(Samples.SelectMany(sample => sample.Failures.Select(failure => $"{sample.Name}: {failure}")));
}

/// <summary>
/// The evaluation result for a single Foundry sample: its identity and location, whether it passed,
/// the accumulated failures, and where its build log lives along with the build's exit code.
/// </summary>
public sealed record FoundrySampleResult
{
    /// <summary>The sample's identifier (for example <c>Hosted-FoundryIQ</c>), also used as its artifact subdirectory name.</summary>
    public required string Name { get; init; }

    /// <summary>The sample's path relative to the repository root.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Whether the sample passed: true only when <see cref="Failures"/> is empty.</summary>
    public required bool Passed { get; init; }

    /// <summary>
    /// The blockers found for this sample: missing required files, missing required or forbidden
    /// carryover text, project-file violations (target framework or package references), and build failure.
    /// </summary>
    public required IReadOnlyList<string> Failures { get; init; }

    /// <summary>The path to the captured <c>dotnet build</c> log written for this sample.</summary>
    public required string BuildLogPath { get; init; }

    /// <summary>The exit code of the sample's <c>dotnet build</c>; non-zero contributes a build-failure entry to <see cref="Failures"/>.</summary>
    public required int BuildExitCode { get; init; }
}

/// <summary>
/// Prepares a repository (existing checkout or shallow clone), validates that its tracked file tree
/// matches the expected Foundry hosted-agent samples, then evaluates each sample for required files,
/// required/forbidden text, project-file targeting and package references, and a clean
/// <c>dotnet build</c>. Writes per-step logs and a JSON report under the artifact root.
/// </summary>
/// <remarks>
/// This runner exercises the AI-authored Foundry samples and enforces the repository's AI-only
/// authoring policy through its text rules: every sample must carry C#/Rider 2026.2 wording and the
/// Microsoft copyright header, and must not contain forbidden carryover terms such as Python or
/// VS Code. A repository-level failure (missing root, clone failure, or tracked-file-tree mismatch)
/// short-circuits the run before any sample is evaluated.
/// </remarks>
public static class FoundrySampleEvaluationRunner
{
    private static readonly FoundrySampleEvaluationPlan DefaultPlan = FoundrySampleEvaluationPlan.CreateDefault();

    /// <summary>
    /// Runs the full Foundry sample evaluation: resolves the repository, validates the tracked file
    /// tree against the default plan, evaluates each sample, then serializes and writes the report.
    /// </summary>
    /// <param name="options">The run inputs, selecting the repository source and the artifact output directory.</param>
    /// <param name="cancellationToken">Token that cancels the git, build, and file-write operations.</param>
    /// <returns>
    /// A <see cref="FoundrySampleEvaluationReport"/> describing the outcome; it is also written to
    /// <c>foundry-sample-evaluation.json</c> in the artifact root. <see cref="FoundrySampleEvaluationReport.Passed"/>
    /// is true only when there are no repository-level failures and every sample passed.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown when <c>git ls-files</c> fails while listing the repository's tracked files.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    public static async Task<FoundrySampleEvaluationReport> RunAsync(
        FoundrySampleEvaluationOptions options,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
        string artifactRoot = Path.GetFullPath(options.ArtifactRoot);
        Directory.CreateDirectory(artifactRoot);

        var repositoryFailures = new List<string>();
        string repositoryRoot = await PrepareRepositoryAsync(options, artifactRoot, repositoryFailures, cancellationToken);
        IReadOnlyList<string> trackedFiles = repositoryFailures.Count == 0
            ? await GetTrackedFilesAsync(repositoryRoot, cancellationToken)
            : Array.Empty<string>();

        if (repositoryFailures.Count == 0)
        {
            repositoryFailures.AddRange(ValidateTrackedFileTree(trackedFiles));
        }

        var sampleResults = new List<FoundrySampleResult>();
        if (repositoryFailures.Count == 0)
        {
            foreach (FoundrySampleDefinition sample in DefaultPlan.Samples)
            {
                sampleResults.Add(await EvaluateSampleAsync(repositoryRoot, artifactRoot, sample, cancellationToken));
            }
        }

        DateTimeOffset finishedAtUtc = DateTimeOffset.UtcNow;
        bool passed = repositoryFailures.Count == 0 && sampleResults.All(sample => sample.Passed);
        var report = new FoundrySampleEvaluationReport
        {
            Passed = passed,
            RepositoryRoot = repositoryRoot,
            ArtifactRoot = artifactRoot,
            StartedAtUtc = startedAtUtc,
            FinishedAtUtc = finishedAtUtc,
            RepositoryFailures = repositoryFailures,
            Samples = sampleResults
        };

        string reportPath = Path.Combine(artifactRoot, "foundry-sample-evaluation.json");
        string reportJson = JsonSerializer.Serialize(report, JsonOptions.Default);
        await File.WriteAllTextAsync(reportPath, reportJson, cancellationToken);

        return report;
    }

    private static async Task<string> PrepareRepositoryAsync(
        FoundrySampleEvaluationOptions options,
        string artifactRoot,
        List<string> repositoryFailures,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.RepositoryRoot))
        {
            string existingRoot = Path.GetFullPath(options.RepositoryRoot);
            if (!Directory.Exists(existingRoot))
            {
                repositoryFailures.Add($"Repository root does not exist: {existingRoot}");
            }

            return existingRoot;
        }

        string cloneParent = Path.Combine(Path.GetTempPath(), "aievalgate-foundry-sample-clones");
        Directory.CreateDirectory(cloneParent);
        string cloneRoot = Path.Combine(cloneParent, $"agent-framework-codex-pr-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}");

        CommandResult clone = await RunProcessAsync(
            "git",
            ["clone", "--depth", "1", "--branch", options.Branch, options.RepositoryUrl, cloneRoot],
            artifactRoot,
            cancellationToken);

        string logPath = Path.Combine(artifactRoot, "git-clone.log");
        await File.WriteAllTextAsync(logPath, clone.ToLog(), cancellationToken);

        if (clone.ExitCode != 0)
        {
            repositoryFailures.Add($"git clone failed with exit code {clone.ExitCode}. See {logPath}");
        }

        return cloneRoot;
    }

    private static async Task<IReadOnlyList<string>> GetTrackedFilesAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        CommandResult result = await RunProcessAsync("git", ["ls-files"], repositoryRoot, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"git ls-files failed in {repositoryRoot}: {result.StandardError}");
        }

        return result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizePath)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> ValidateTrackedFileTree(IReadOnlyList<string> trackedFiles)
    {
        string[] expectedFiles = DefaultPlan.Samples
            .SelectMany(sample => sample.RequiredFiles.Select(file => NormalizePath(Path.Combine(sample.RelativePath, file))))
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] missing = expectedFiles.Except(trackedFiles, StringComparer.Ordinal).ToArray();
        string[] unexpected = trackedFiles.Except(expectedFiles, StringComparer.Ordinal).ToArray();

        var failures = new List<string>();
        failures.AddRange(missing.Select(path => $"Missing tracked file: {path}"));
        failures.AddRange(unexpected.Select(path => $"Unexpected tracked file: {path}"));
        return failures;
    }

    private static async Task<FoundrySampleResult> EvaluateSampleAsync(
        string repositoryRoot,
        string artifactRoot,
        FoundrySampleDefinition sample,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        string sampleRoot = Path.Combine(repositoryRoot, sample.RelativePath);

        foreach (string requiredFile in sample.RequiredFiles)
        {
            string path = Path.Combine(sampleRoot, requiredFile);
            if (!File.Exists(path))
            {
                failures.Add($"Missing required file: {requiredFile}");
            }
        }

        foreach (FileTextRule rule in sample.TextRules)
        {
            string path = Path.Combine(sampleRoot, rule.RelativePath);
            if (!File.Exists(path))
            {
                continue;
            }

            string text = await File.ReadAllTextAsync(path, cancellationToken);
            failures.AddRange(rule.RequiredTerms
                .Where(term => !text.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Select(term => $"{rule.RelativePath} is missing required text: {term}"));
            failures.AddRange(rule.ForbiddenTerms
                .Where(term => text.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Select(term => $"{rule.RelativePath} contains forbidden carryover text: {term}"));
        }

        failures.AddRange(ValidateProjectFile(Path.Combine(sampleRoot, sample.ProjectFile), sample));

        string sampleArtifactRoot = Path.Combine(artifactRoot, sample.Name);
        Directory.CreateDirectory(sampleArtifactRoot);
        string buildLogPath = Path.Combine(sampleArtifactRoot, "dotnet-build.log");
        CommandResult build = await RunProcessAsync(
            "dotnet",
            ["build", Path.Combine(sampleRoot, sample.ProjectFile), "--tl:off"],
            repositoryRoot,
            cancellationToken);
        await File.WriteAllTextAsync(buildLogPath, build.ToLog(), cancellationToken);

        if (build.ExitCode != 0)
        {
            failures.Add($"dotnet build failed with exit code {build.ExitCode}. See {buildLogPath}");
        }

        return new FoundrySampleResult
        {
            Name = sample.Name,
            RelativePath = sample.RelativePath,
            Passed = failures.Count == 0,
            Failures = failures,
            BuildLogPath = buildLogPath,
            BuildExitCode = build.ExitCode
        };
    }

    private static IReadOnlyList<string> ValidateProjectFile(string projectPath, FoundrySampleDefinition sample)
    {
        if (!File.Exists(projectPath))
        {
            return [$"Missing project file: {sample.ProjectFile}"];
        }

        XDocument document;
        try
        {
            document = XDocument.Load(projectPath);
        }
        catch (System.Xml.XmlException ex)
        {
            return [$"{sample.ProjectFile} is not valid XML: {ex.Message}"];
        }

        string? targetFramework = document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "TargetFramework")
            ?.Value;

        var failures = new List<string>();
        if (!string.Equals(targetFramework, "net10.0", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{sample.ProjectFile} must target net10.0.");
        }

        string[] packageReferences = document.Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();

        failures.AddRange(sample.RequiredPackageReferences
            .Where(package => !packageReferences.Contains(package, StringComparer.OrdinalIgnoreCase))
            .Select(package => $"{sample.ProjectFile} is missing PackageReference: {package}"));

        return failures;
    }

    private static async Task<CommandResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                standardOutput.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                standardError.AppendLine(args.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        return new CommandResult(
            fileName,
            arguments,
            workingDirectory,
            process.ExitCode,
            standardOutput.ToString(),
            standardError.ToString());
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private sealed record CommandResult(
        string FileName,
        IReadOnlyList<string> Arguments,
        string WorkingDirectory,
        int ExitCode,
        string StandardOutput,
        string StandardError)
    {
        public string ToLog()
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Command: {FileName} {string.Join(' ', Arguments)}");
            builder.AppendLine($"WorkingDirectory: {WorkingDirectory}");
            builder.AppendLine($"ExitCode: {ExitCode}");
            builder.AppendLine();
            builder.AppendLine("STDOUT:");
            builder.AppendLine(StandardOutput);
            builder.AppendLine("STDERR:");
            builder.AppendLine(StandardError);
            return builder.ToString();
        }
    }
}

internal sealed record FoundrySampleEvaluationPlan(IReadOnlyList<FoundrySampleDefinition> Samples)
{
    public static FoundrySampleEvaluationPlan CreateDefault() =>
        new(
        [
            new FoundrySampleDefinition(
                Name: "Hosted-FoundryIQ",
                RelativePath: "samples/04-hosting/FoundryHostedAgents/responses/Hosted-FoundryIQ",
                ProjectFile: "HostedFoundryIQ.csproj",
                RequiredFiles: ["AGENTS.md", "HostedFoundryIQ.csproj", "Program.cs", "README.md", "SKILL.md"],
                RequiredPackageReferences:
                [
                    "Azure.AI.Projects",
                    "Azure.Identity",
                    "Microsoft.Agents.AI.Foundry",
                    "Microsoft.Extensions.AI"
                ],
                TextRules:
                [
                    new FileTextRule(
                        "README.md",
                        ["Foundry IQ", "C#", "JetBrains Rider 2026.2", ".NET 10 SDK", "dotnet run", "AZURE_AI_PROJECT_ENDPOINT", "AGENT_NAME", "ToolApprovalRequestContent", "AgentSession"],
                        ["Python", "Visual Studio Code", "VS Code"]),
                    new FileTextRule(
                        "AGENTS.md",
                        ["Foundry IQ", "C#", "Rider 2026.2", "Do not introduce alternate editor wording"],
                        ["Python", "Visual Studio Code", "VS Code"]),
                    new FileTextRule(
                        "SKILL.md",
                        ["Hosted Foundry IQ C# Guide", "Foundry IQ", "C#", "Rider 2026.2", "ToolApprovalRequestContent"],
                        ["Python", "Visual Studio Code", "VS Code"]),
                    new FileTextRule(
                        "Program.cs",
                        ["// Copyright (c) Microsoft. All rights reserved.", "DefaultAzureCredential", "AIProjectClient", "AsAIAgent", "CreateSessionAsync", "ToolApprovalRequestContent", "McpServerToolCallContent", "CreateResponse", "AZURE_AI_PROJECT_ENDPOINT", "AGENT_NAME"],
                        ["Python", "Visual Studio Code", "VS Code"])
                ]),
            new FoundrySampleDefinition(
                Name: "Hosted-FoundryMcpTools",
                RelativePath: "samples/04-hosting/FoundryHostedAgents/responses/Hosted-FoundryMcpTools",
                ProjectFile: "HostedFoundryMcpTools.csproj",
                RequiredFiles: ["AGENTS.md", "HostedFoundryMcpTools.csproj", "Program.cs", "README.md", "SKILL.md"],
                RequiredPackageReferences:
                [
                    "Azure.AI.Projects",
                    "Azure.Identity",
                    "Microsoft.Agents.AI.Foundry",
                    "Microsoft.Extensions.AI",
                    "Microsoft.Extensions.Hosting",
                    "Microsoft.Extensions.Logging.Console",
                    "ModelContextProtocol"
                ],
                TextRules:
                [
                    new FileTextRule(
                        "README.md",
                        ["Model Context Protocol", "MCP", "C#", "Rider 2026.2", "dotnet run -- remote", "dotnet run -- inventory", "AZURE_AI_MODEL_DEPLOYMENT_NAME"],
                        ["Python", "Visual Studio Code", "VS Code"]),
                    new FileTextRule(
                        "AGENTS.md",
                        ["MCP", "Rider 2026.2", "WithStdioServerTransport", "McpClient.CreateAsync"],
                        ["Python", "Visual Studio Code", "VS Code"]),
                    new FileTextRule(
                        "SKILL.md",
                        ["Hosted Foundry MCP Tools C# Guide", "MCP", "C#", "Rider 2026.2", "HostedMcpServerTool", "WithStdioServerTransport"],
                        ["Python", "Visual Studio Code", "VS Code"]),
                    new FileTextRule(
                        "Program.cs",
                        ["// Copyright (c) Microsoft. All rights reserved.", "HostedMcpServerTool", "HostedMcpServerToolApprovalMode.AlwaysRequire", "McpClient.CreateAsync", "StdioClientTransport", "WithStdioServerTransport", "WithTools<InventoryTools>", "McpServerTool", "ToolApprovalRequestContent", "CreateResponse"],
                        ["Python", "Visual Studio Code", "VS Code"])
                ])
        ]);
}

internal sealed record FoundrySampleDefinition(
    string Name,
    string RelativePath,
    string ProjectFile,
    IReadOnlyList<string> RequiredFiles,
    IReadOnlyList<string> RequiredPackageReferences,
    IReadOnlyList<FileTextRule> TextRules);

internal sealed record FileTextRule(
    string RelativePath,
    IReadOnlyList<string> RequiredTerms,
    IReadOnlyList<string> ForbiddenTerms);
