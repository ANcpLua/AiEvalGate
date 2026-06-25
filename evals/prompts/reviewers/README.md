# Reviewer prompts

Runtime reviewer prompts live in `src/AiEvalGate.Core/Reviewers/ReviewerPromptLibrary.cs`.

Project-scoped AI reviewer personas live in `.github/ai-review/agents/*.toml`.

The same reviewer names are enforced by `evals/thresholds/default-gates.json`.

## Single source of truth

`eng/AiEvalGate.ScaffoldGenerator` is the single source of truth for the reviewer
roster. Both generated locations — `.github/ai-review/agents/*.toml`
(`GenerateAiReviewAgents`) and the `requiredReviewers` list in
`evals/thresholds/default-gates.json` (`GeneratePolicies`) — are emitted from the
reviewer table in `eng/AiEvalGate.ScaffoldGenerator/Program.cs`. Do not hand-edit
the generated `.toml` agents or `default-gates.json`; edit the generator's reviewer
table and regenerate so the locations cannot drift:

```bash
dotnet run --project eng/AiEvalGate.ScaffoldGenerator/AiEvalGate.ScaffoldGenerator.csproj
```
