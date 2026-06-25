#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${AI_EVAL_FOUNDRY_SAMPLE_ARTIFACT_DIR:-$ROOT/artifacts/foundry-sample-eval}"

args=(--foundry-samples --out "$OUT")

if [[ -n "${AI_EVAL_FOUNDRY_SAMPLE_REPO_ROOT:-}" ]]; then
  args+=(--sample-repo-root "$AI_EVAL_FOUNDRY_SAMPLE_REPO_ROOT")
fi

if [[ -n "${AI_EVAL_FOUNDRY_SAMPLE_REPO_URL:-}" ]]; then
  args+=(--sample-repo-url "$AI_EVAL_FOUNDRY_SAMPLE_REPO_URL")
fi

if [[ -n "${AI_EVAL_FOUNDRY_SAMPLE_BRANCH:-}" ]]; then
  args+=(--sample-branch "$AI_EVAL_FOUNDRY_SAMPLE_BRANCH")
fi

dotnet run --configuration Release --project "$ROOT/src/AiEvalGate.Runner/AiEvalGate.Runner.csproj" -- "${args[@]}"
