# AiEvalGate

> An **AI-only** .NET 10 evaluation gate -- automated evaluation of .NET AI systems with **0 manual review gates**, built on `Microsoft.Extensions.AI.Evaluation`.

This repository is a concrete scaffold for automated evaluation of .NET AI systems with **0 manual review gates**.

It combines:

- `Microsoft.Extensions.AI.Evaluation` quality evaluators.
- Scenario files in JSONL.
- A panel of AI reviewer agents that review the system under test.
- Hard gates for safety, grounding, tool use, service-boundary behavior, and regression checks.
- CI automation with `dotnet test`, an executable evaluation runner, and Claude review automation.
- Reporting hooks for local artifacts, JUnit XML, and the Microsoft `dotnet aieval` report tool.
- A Meziantou-style scaffold generator under `eng/AiEvalGate.ScaffoldGenerator`.

## What this scaffold tests

```text
user input
  -> input/service-boundary checks
  -> retrieval/context selection
  -> prompt/system under test
  -> model/tool/service trace
  -> Microsoft quality evaluators
  -> AI reviewer-agent panel
  -> hard gate
  -> JSON/Markdown/JUnit/HTML artifacts
```

## Three gates

These are independent evaluation pipelines with different requirements. They do not share a pass/fail verdict.

| Gate | CI job (`.github/workflows/ai-evaluation.yml`) | API key | How it judges |
| --- | --- | --- | --- |
| AI-only evaluation | `dotnet-ai-evals` (+ `claude-ai-review`) | `ANTHROPIC_API_KEY` + `AI_EVAL_REVIEW_MODEL` | LLM-judge: Microsoft quality evaluators and the AI reviewer-agent panel |
| Observability | `observability-evals` | none | Deterministic `BooleanMetric` checks over JSONL fixtures (`src/AiEvalGate.Observability/`) |
| Foundry sample | none (opt-in, local/MSTest) | none | Deterministic; run via `scripts/run-foundry-sample-evals.*` or `AI_EVAL_ENABLE_FOUNDRY_SAMPLE_TESTS=true` |

## Requirements

- .NET 10 SDK or later.
- `ANTHROPIC_API_KEY` for evaluator/reviewer agents.
- `AI_EVAL_REVIEW_MODEL`, for example `claude-haiku-4-5` or your chosen review model.
- Optional: add the `Microsoft.Extensions.AI.Evaluation.Safety` package (+ Azure AI Foundry credentials) to enable content-safety evaluators.

## Run locally

Linux/macOS:

```bash
export ANTHROPIC_API_KEY="..."
export AI_EVAL_REVIEW_MODEL="claude-haiku-4-5"
./scripts/run-evals.sh
```

Windows PowerShell:

```powershell
$env:ANTHROPIC_API_KEY="..."
$env:AI_EVAL_REVIEW_MODEL="claude-haiku-4-5"
./scripts/run-evals.ps1
```

Direct runner:

```bash
dotnet run --project src/AiEvalGate.Runner/AiEvalGate.Runner.csproj -- \
  --scenario evals/scenarios/refund-policy.jsonl \
  --scenario evals/scenarios/security.jsonl \
  --out artifacts/ai-eval
```

## Run the Foundry sample evaluation pipeline

The Foundry sample pipeline is deterministic and does not require an LLM judge or API key.
It fresh-clones the sample review repository by default, verifies the tracked file tree, checks the C# and Rider 2026.2 guide rules, rejects Python and Visual Studio Code carryover text, validates required package references, and builds both sample projects.

Linux/macOS:

```bash
./scripts/run-foundry-sample-evals.sh
```

Windows PowerShell:

```powershell
./scripts/run-foundry-sample-evals.ps1
```

Focused MSTest gate:

```bash
AI_EVAL_ENABLE_FOUNDRY_SAMPLE_TESTS=true \
dotnet test tests/AiEvalGate.EvaluationTests/AiEvalGate.EvaluationTests.csproj \
  --filter FullyQualifiedName~FoundrySampleEvaluationTests
```

To evaluate an existing local clone instead of cloning from GitHub:

```bash
export AI_EVAL_FOUNDRY_SAMPLE_REPO_ROOT="/path/to/agent-framework-codex-pr"
./scripts/run-foundry-sample-evals.sh
```

Optional environment variables:

```text
AI_EVAL_FOUNDRY_SAMPLE_REPO_ROOT      Existing local repo path. When absent, the runner clones.
AI_EVAL_FOUNDRY_SAMPLE_REPO_URL       Defaults to https://github.com/ANcpLua/agent-framework-codex-pr.git
AI_EVAL_FOUNDRY_SAMPLE_BRANCH         Defaults to main.
AI_EVAL_FOUNDRY_SAMPLE_ARTIFACT_DIR   Defaults to artifacts/foundry-sample-eval.
AI_EVAL_ENABLE_FOUNDRY_SAMPLE_TESTS   Enables the opt-in MSTest gate for private repo access.
```

## Main outputs

```text
artifacts/ai-eval/runs/*.json
artifacts/ai-eval/runs/*.md
artifacts/ai-eval/summary.json
artifacts/ai-eval/junit-ai-eval.xml
artifacts/ai-eval/index.html
artifacts/foundry-sample-eval/foundry-sample-evaluation.json
artifacts/foundry-sample-eval/*/dotnet-build.log
artifacts/test-results/*.trx
artifacts/ai-review/ai-review.json
```

## AI-only policy

The gate policy is encoded in:

```text
evals/thresholds/default-gates.json
```

The default policy has:

```json
{
  "aiOnlyPolicy": {
    "humanReviewRequired": false,
    "manualOverrideAllowed": false,
    "manualApprovalSteps": 0
  }
}
```

The build accepts automated evaluator verdicts, AI reviewer-agent verdicts, service-boundary validators, and CI checks only.

## Architecture-specific use

### Monolith

Point `IAiSystemUnderTest` to the monolith's real AI endpoint or internal pipeline. Run the scenario suite against the full monolith flow. Keep stage traces such as `MonolithRetrievalStage`, `MonolithAnswerStage`, and `MonolithSafetyStage` so failures remain localizable.

### Microservices

Keep each service contract in:

```text
evals/service-boundaries/*.json
```

The validator checks that traces emitted by the system under test include required service calls, operations, source IDs, and tool decisions.

## Repository map

```text
src/AiEvalGate.Core/                 Evaluation framework, gates, agents, artifact writer
src/AiEvalGate.SampleApp/            Example system under test
src/AiEvalGate.Runner/               CLI runner for CI and local execution
src/AiEvalGate.Observability/        Deterministic, no-API-key observability/telemetry eval harness — separate CI gate
src/Sdk/                             Generated SDK facade/enforcement files
eng/AiEvalGate.ScaffoldGenerator/    Regenerates policy, SDK, AI reviewer agents, and CI scaffolding
tests/AiEvalGate.EvaluationTests/    CI-ready MSTest evaluation suite
evals/scenarios/                     JSONL scenario packs
evals/thresholds/                    Hard-gate policy
evals/service-boundaries/            Microservice/monolith boundary contracts
.github/ai-review/agents/            Project-scoped AI reviewer personas
.github/workflows/                   CI pipeline
.github/ai-review/                   Claude review prompt and JSON schema
scripts/                             Local and CI runner helpers
```

## Replace the sample app

Replace `PolicyAssistantSystemUnderTest` with an adapter to your real system:

```csharp
public sealed class ProductionSystemUnderTest : IAiSystemUnderTest
{
    public async Task<AiRunResult> RunAsync(AiScenario scenario, CancellationToken cancellationToken = default)
    {
        // Call your monolith endpoint, orchestrator service, or local app pipeline.
        // Capture service traces, retrieved sources, tool calls, and final answer.
    }
}
```

The evaluator layer does not care whether the system under test is a monolith or microservices. It only needs an `AiRunResult`.

## Regenerate scaffold files

```bash
dotnet run --project eng/AiEvalGate.ScaffoldGenerator/AiEvalGate.ScaffoldGenerator.csproj
```
