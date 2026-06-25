---
status: accepted
contact: ANcpLua
date: 2026-06-25
deciders: ANcpLua
---

# Automate the entire evaluation gate, with zero human approval steps

## Context and Problem Statement

Evaluating a .NET AI system before release usually ends at a human approval
gate: a person reads the output and signs off. That gate is slow, subjective,
and unreproducible. Can the *whole* verdict be produced by automation —
deterministic validators, model-judge evaluators, and an AI reviewer panel — so
that the same inputs always yield the same pass/fail decision in CI, with no
manual step?

## Decision Drivers

- Reproducibility: the same scenario pack must yield the same verdict on every run.
- No human bottleneck: the gate must run unattended in CI.
- Auditability: every blocker must be machine-attributable to a metric, a reviewer, or a boundary check.
- Fail-closed integrity: it must be impossible to quietly re-introduce a human override.

## Considered Options

- Human-in-the-loop final gate (status quo).
- AI-only gate: deterministic validators + LLM-judge evaluators + AI reviewer panel.
- Deterministic-only gate (no model judge at all).

## Decision Outcome

Chosen option: "AI-only gate", because it is the only option that removes the
human bottleneck while still judging open-ended answer quality. The policy is
self-enforcing: `AiOnlyPolicy` (`humanReviewRequired=false`,
`manualOverrideAllowed=false`, `manualApprovalSteps=0`) is itself a gate input,
and `AiEvalGatekeeper.Evaluate` adds a blocker if any of those are flipped — so
the gate fails closed the moment someone tries to re-enable manual review.

### Consequences

- Good, because verdicts are reproducible and CI-native — no person on the critical path.
- Good, because the AI-only invariant is structural: re-enabling human review breaks the build, it does not silently pass.
- Bad, because a model judge is non-deterministic; mitigated by [ADR-0003](0003-llm-judge-panel.md) (temperature 0 + median-of-three).
- Bad, because the gate trusts the judge model's competence; mitigated by combining it with deterministic validators and a multi-persona reviewer panel.

## Validation

`AiEvalGatekeeper.Evaluate` asserts the AI-only invariants and aggregates
blockers from metrics, reviewers, and service boundaries.
`AiEvalGateEvaluationTests` drives the scenario packs through the full gate; the
`dotnet-ai-evals` CI job runs it on every push.

## More Information

The deterministic half of the gate is covered by
[ADR-0002](0002-deterministic-observability-harness.md); the model-judge half by
[ADR-0003](0003-llm-judge-panel.md); build-time enforcement of the policy by
[ADR-0004](0004-msbuild-sdk-enforcement.md).
