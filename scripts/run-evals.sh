#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${AI_EVAL_ARTIFACT_DIR:-$ROOT/artifacts/ai-eval}"

dotnet test "$ROOT/tests/AiEvalGate.EvaluationTests/AiEvalGate.EvaluationTests.csproj" \
  --configuration Release \
  --logger "trx;LogFileName=ai-eval-tests.trx" \
  --results-directory "$ROOT/artifacts/test-results"

dotnet run --configuration Release --project "$ROOT/src/AiEvalGate.Runner/AiEvalGate.Runner.csproj" -- \
  --scenario "$ROOT/evals/scenarios/refund-policy.jsonl" \
  --scenario "$ROOT/evals/scenarios/security.jsonl" \
  --out "$OUT"

echo "AI-only evaluation report: $OUT/index.html"
