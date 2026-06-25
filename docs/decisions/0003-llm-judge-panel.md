---
status: accepted
contact: ANcpLua
date: 2026-06-25
deciders: ANcpLua
---

# Judge open-ended quality with median-of-three sampling and an AI reviewer panel

## Context and Problem Statement

Answer quality — relevance, coherence, completeness, groundedness — and holistic
concerns like safety, retrieval, tool-use, and architecture attribution cannot be
settled by a fixture. They need a model judge. But a single judge call flips
borderline scores between runs, which would make the gate ([ADR-0001](0001-ai-only-gating.md))
non-reproducible.

## Decision Drivers

- Verdict stability: a borderline scenario must not oscillate pass/fail across runs.
- Idiomatic alignment with `Microsoft.Extensions.AI.Evaluation` rather than a bespoke judge harness.
- Provider choice: the judge is Claude (Anthropic), reached through the `Microsoft.Extensions.AI` `IChatClient` seam.

## Considered Options

- A single judge call per metric.
- Median-of-N sampling per metric.
- Self-consistency with majority voting over discrete labels.

## Decision Outcome

Chosen option: "median-of-three sampling". `MicrosoftQualityEvaluator` runs each
Microsoft quality evaluator three times concurrently at temperature 0 and takes
the median, which stabilizes borderline verdicts cheaply. Alongside it, an
eight-persona `AiReviewerTeam` (architecture, grounding, retrieval, tool-use,
safety, security, red-team, regression) returns strict JSON pass/fail with a
severity. The judge client is built in `AiClientFactory`: because Claude 4+
rejects `temperature` and `top_p` together while the Microsoft evaluators send
both, the factory drops `top_p` at the seam and keeps `temperature`.

### Consequences

- Good, because three samples remove most borderline flapping at a predictable 3x judge cost.
- Good, because using Microsoft's evaluators keeps us aligned with the ecosystem instead of maintaining a private judge prompt set.
- Bad, because the verdict still depends on a model and an API key; the deterministic gate ([ADR-0002](0002-deterministic-observability-harness.md)) exists precisely so CI has a signal that does not.
- Neutral, because the provider is swappable — anything that satisfies `IChatClient` can be the judge.

## Validation

`MicrosoftQualityEvaluator.EvaluateAsync` performs the three-sample median;
`AiReviewerAgent` validates that each persona returns parseable JSON and stamps
the reviewer identity; `AiEvalGateEvaluationTests` exercises the full panel.

## More Information

The `top_p`/`temperature` seam in `AiClientFactory` is a Claude-4 API constraint,
documented inline at the call site so it is not "tidied away" by a future change.
