# Architecture Decision Records

This directory records the architecture decisions for AiEvalGate using the
[MADR](https://adr.github.io/madr/) format (the same template the
[Microsoft Agent Framework](https://github.com/microsoft/agent-framework/blob/main/docs/decisions/adr-template.md)
uses).

| ADR | Decision |
| --- | --- |
| [0001](0001-ai-only-gating.md) | Automate the entire evaluation gate, with zero human approval steps |
| [0002](0002-deterministic-observability-harness.md) | Run observability/telemetry checks as a separate, deterministic, no-key gate |
| [0003](0003-llm-judge-panel.md) | Judge open-ended quality with median-of-three sampling and an AI reviewer panel |
| [0004](0004-msbuild-sdk-enforcement.md) | Enforce the AI-only policy at build time via a custom MSBuild SDK facade |

New decisions get the next number and link back to the ones they build on.
