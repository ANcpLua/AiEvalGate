# Architecture

## Evaluation flow

```text
Scenario JSONL
   ↓
IAiSystemUnderTest
   ↓
AiRunResult: final answer + retrieved sources + tool calls + service traces
   ↓
Microsoft.Extensions.AI.Evaluation quality metrics
   ↓
AI reviewer team: architecture, grounding, retrieval, tool use, safety, security, red-team, regression
   ↓
ServiceBoundaryValidator
   ↓
AiEvalGatekeeper
   ↓
artifacts/ai-eval
```

## Monolith adapter

A monolith adapter should emit one `AiRunResult` for the full pipeline. Use stage-like service trace names so the same boundary validator can localize failures:

```text
MonolithAiPipeline.intent.resolve
MonolithRetrievalStage.retrieval.search
MonolithAnswerStage.answer.compose
MonolithSafetyStage.safety.check
```

## Microservice adapter

A microservice adapter should emit one trace per service boundary:

```text
ConversationOrchestratorService.intent.resolve
RetrievalService.retrieval.search
AnswerComposerService.answer.compose
SafetyPolicyService.safety.check
```

## Hard gates

Gates are configured in `evals/thresholds/default-gates.json`:

- Required reviewer agents must all return a review.
- `passed=false` blocks.
- `P0` and `P1` severities block.
- Minimum reviewer score is enforced.
- Required Microsoft evaluator metrics are enforced.
- Service-boundary failures block when `serviceBoundaryStrict=true`.
- Manual override properties must stay disabled.

## Scenario file contract

Each line in `evals/scenarios/*.jsonl` is an `AiScenario`:

```json
{
  "id": "refund-after-45-days-microservices",
  "area": "refunds",
  "architecture": "microservices",
  "userInput": "Can I get a refund for an order from 45 days ago?",
  "context": ["refund-policy-v3: Standard refunds are available within 30 days..."],
  "requiredSources": ["refund-policy-v3"],
  "requiredClaims": ["Do not guarantee refund after 45 days"],
  "forbiddenClaims": ["All refunds are automatically approved"],
  "expectedTools": ["retrieval.search"],
  "forbiddenTools": ["refund.issue"]
}
```

## Claude review integration

`.github/ai-review/agents/*.toml` defines reviewer personas. `.github/workflows/ai-evaluation.yml` runs Claude Code headless (`claude -p`) with a committed orchestrator prompt and JSON schema. The node validator fails the gate unless the review returns `passed=true`, `score >= 0.85`, and non-blocking severity.
