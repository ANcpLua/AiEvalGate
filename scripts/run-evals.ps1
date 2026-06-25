$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Out = if ($env:AI_EVAL_ARTIFACT_DIR) { $env:AI_EVAL_ARTIFACT_DIR } else { Join-Path $Root "artifacts/ai-eval" }

dotnet test (Join-Path $Root "tests/AiEvalGate.EvaluationTests/AiEvalGate.EvaluationTests.csproj") `
  --configuration Release `
  --logger "trx;LogFileName=ai-eval-tests.trx" `
  --results-directory (Join-Path $Root "artifacts/test-results")

dotnet run --configuration Release --project (Join-Path $Root "src/AiEvalGate.Runner/AiEvalGate.Runner.csproj") -- `
  --scenario (Join-Path $Root "evals/scenarios/refund-policy.jsonl") `
  --scenario (Join-Path $Root "evals/scenarios/security.jsonl") `
  --out $Out

Write-Host "AI-only evaluation report: $Out/index.html"
