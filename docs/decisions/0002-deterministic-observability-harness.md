---
status: accepted
contact: ANcpLua
date: 2026-06-25
deciders: ANcpLua
---

# Run observability/telemetry checks as a separate, deterministic, no-key gate

## Context and Problem Statement

Some of what we need to evaluate is not a matter of taste: did the agent call
the required tools with the right arguments, cite the telemetry evidence it
claimed, keep its spans correlated, and avoid emitting high-cardinality or
sensitive attributes? All of that is decidable from a recorded run, with no
model in the loop. Folding it into the LLM-judge run would make a deterministic
signal depend on an API key and a non-deterministic judge.

## Decision Drivers

- The signal is deterministic — it should be computed deterministically.
- It must stay green in CI even when `ANTHROPIC_API_KEY` is absent (forks, PRs from untrusted contributors).
- Fast and free: no model round-trips for facts a fixture already settles.

## Considered Options

- Fold observability checks into the LLM-judge evaluation run.
- A separate deterministic harness with its own CI gate.
- Skip observability evaluation entirely.

## Decision Outcome

Chosen option: "separate deterministic harness", implemented as
`src/AiEvalGate.Observability`. Each check (`ToolCallAccuracy`,
`TelemetryEvidence`, `TraceCorrelation`, `CardinalitySafety`) is a Microsoft
`IEvaluator` producing a `BooleanMetric` from a JSONL scenario fixture. It runs
as its own `observability-evals` CI job that requires no secrets.

### Consequences

- Good, because the gate is reproducible and runs without any API key.
- Good, because failures are precise (`BooleanMetric` + a reason), not a judge's prose.
- Bad, because there are now two gates to maintain; fixtures must declare their expected failed metrics exactly (an order-independent set match).
- Neutral, because it deliberately starts without a model judge — a judge can be layered on later without changing the deterministic contract.

## Validation

`ScenarioRunResult.Create` reconciles each scenario's actual failed metrics
against its declared `expectedFailedMetrics`; the harness exits non-zero on any
mismatch. The `observability-evals` CI job runs it on every push.
