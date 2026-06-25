$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Out = if ($env:AI_EVAL_FOUNDRY_SAMPLE_ARTIFACT_DIR) { $env:AI_EVAL_FOUNDRY_SAMPLE_ARTIFACT_DIR } else { Join-Path $Root "artifacts/foundry-sample-eval" }

$RunnerArgs = @("--foundry-samples", "--out", $Out)

if ($env:AI_EVAL_FOUNDRY_SAMPLE_REPO_ROOT) {
    $RunnerArgs += @("--sample-repo-root", $env:AI_EVAL_FOUNDRY_SAMPLE_REPO_ROOT)
}

if ($env:AI_EVAL_FOUNDRY_SAMPLE_REPO_URL) {
    $RunnerArgs += @("--sample-repo-url", $env:AI_EVAL_FOUNDRY_SAMPLE_REPO_URL)
}

if ($env:AI_EVAL_FOUNDRY_SAMPLE_BRANCH) {
    $RunnerArgs += @("--sample-branch", $env:AI_EVAL_FOUNDRY_SAMPLE_BRANCH)
}

dotnet run --configuration Release --project (Join-Path $Root "src/AiEvalGate.Runner/AiEvalGate.Runner.csproj") -- @RunnerArgs
