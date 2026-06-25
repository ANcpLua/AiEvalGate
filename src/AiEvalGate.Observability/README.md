# AiEvalGate.Observability

A deterministic observability evaluation harness for incident-triage and agent-telemetry flows. It runs **without an LLM judge or API key** — every metric is a `BooleanMetric` derived from the JSONL scenario fixture via Microsoft `IEvaluator` adapters, so it is a fast, free, reproducible gate (it stays green in CI even when `ANTHROPIC_API_KEY` is absent).

It validates five observable behaviors per scenario:

- **`observability.telemetry.evidence`** — every required telemetry evidence id exists and is cited in the answer, and no forbidden claim appears.
- **`observability.tool.call.accuracy`** — every required tool call is present with a matching argument subset.
- **`observability.trace.correlation`** — every span's parent resolves within its trace (no orphan spans).
- **`observability.cardinality.safety`** — no high-cardinality or sensitive telemetry attributes (PII, secrets, raw prompts).
- **`observability.output.schema`** — the agent's structured output is a JSON object that has every required property (with the required value kind) and none of the forbidden ones.

A scenario **passes** when the set of failed metrics exactly matches its `expectedFailedMetrics`, so each pack pins both good behavior and known-bad behavior.

## Run

```bash
dotnet run --project src/AiEvalGate.Observability
```

Pass scenario packs explicitly:

```bash
dotnet run --project src/AiEvalGate.Observability -- path/to/pack.jsonl
```

Exit code is `0` when every scenario matches its expected outcome, `1` otherwise.

## Scenario schema (JSONL, one object per line)

- `id`, `source`, `scenario` — identifiers.
- `agent` — agent metadata and visible tool names.
- `userInput`, `finalResponse` — the request and the system's answer.
- `toolCalls` — tool/API calls the agent issued.
- `telemetry` — trace/metric/log evidence available to the agent.
- `requiredEvidenceIds`, `forbiddenClaims` — citation requirements.
- `expectedToolCalls` — required tool calls and argument subsets.
- `structuredOutput`, `outputContract` — the agent's structured JSON output and the required/forbidden properties it must satisfy.
- `expectedFailedMetrics` — the exact deterministic metrics expected to fail.
- `shouldPass` — gate shorthand; pass records must have no expected failed metrics.

Adapted from an internal deterministic observability-evaluation harness, generalized to this scaffold's conventions.
