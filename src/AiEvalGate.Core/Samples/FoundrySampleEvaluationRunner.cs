using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace AiEvalGate.Core.Samples;

public sealed record FoundrySampleEvaluationOptions
{
    public string? RepositoryRoot { get; init; }
    public string RepositoryUrl { get; init; } = "https://github.com/ANcpLua/agent-framework-codex-pr.git";
    public string Branch { get; init; } = "main";
    public required string ArtifactRoot { get; init; }
}

public sealed record FoundrySampleEvaluationReport
{
    public required bool Passed { get; init; }
    public required string RepositoryRoot { get; init; }
    public required string ArtifactRoot { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset FinishedAtUtc { get; init; }
    public required IReadOnlyList<string> RepositoryFailures { get; init; }
    public required IReadOnlyList<FoundrySampleResult> Samples { get; init; }

    public IEnumerable<string> FailureMessages =>
        RepositoryFailures.Concat(Samples.SelectMany(sample => sample.Failures.Select(failure => $"{sample.Name}: {failure}")));
}

public sealed record FoundrySampleResult
{
    public required string Name { get; init; }
    public required string RelativePath { get; init; }
    public required bool Passed { get; init; }
    public required IReadOnlyList<string> Failures { get; init; }
    public required string BuildLogPath { get; init; }
    public required int BuildExitCode { get; init; }
}

public static class FoundrySampleEvaluationRunner
{
    private static readonly FoundrySampleEvaluationPlan DefaultPlan = FoundrySampleEvaluationPlan.CreateDefault();

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

        string cloneParent = Path.Combine(Path.GetTempPath(), "ai-only-eval-foundry-sample-clones");
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

        XDocument document = XDocument.Load(projectPath);
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
