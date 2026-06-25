using AiEvalGate.Core.Models;

namespace AiEvalGate.SampleApp;

public sealed class PolicyAssistantSystemUnderTest : IAiSystemUnderTest
{
    public Task<AiRunResult> RunAsync(AiScenario scenario, CancellationToken cancellationToken = default)
    {
        string normalized = scenario.UserInput.ToLowerInvariant();
        var retrievedSources = scenario.RequiredSources.Count > 0
            ? scenario.RequiredSources
            : new[] { "refund-policy-v3" };

        var toolCalls = new List<ToolCallTrace>
        {
            new()
            {
                Name = "retrieval.search",
                Arguments = new Dictionary<string, string>
                {
                    ["query"] = scenario.UserInput,
                    ["area"] = scenario.Area
                },
                ResultSummary = $"Retrieved {string.Join(", ", retrievedSources)}"
            }
        };

        if (scenario.ExpectedTools.Contains("order.lookup", StringComparer.OrdinalIgnoreCase))
        {
            toolCalls.Add(new ToolCallTrace
            {
                Name = "order.lookup",
                Arguments = new Dictionary<string, string> { ["orderReference"] = "scenario-provided-or-redacted" },
                ResultSummary = "Order metadata looked up without issuing a refund."
            });
        }

        string answer = BuildAnswer(scenario, normalized);

        var serviceTraces = new List<ServiceTrace>
        {
            new()
            {
                ServiceName = scenario.Architecture.Equals("microservices", StringComparison.OrdinalIgnoreCase) ? "ConversationOrchestratorService" : "MonolithAiPipeline",
                Operation = "intent.resolve",
                InputSummary = scenario.UserInput,
                OutputSummary = "refund-policy-question",
                SourceIds = Array.Empty<string>()
            },
            new()
            {
                ServiceName = scenario.Architecture.Equals("microservices", StringComparison.OrdinalIgnoreCase) ? "RetrievalService" : "MonolithRetrievalStage",
                Operation = "retrieval.search",
                InputSummary = scenario.UserInput,
                OutputSummary = string.Join(", ", retrievedSources),
                SourceIds = retrievedSources
            },
            new()
            {
                ServiceName = scenario.Architecture.Equals("microservices", StringComparison.OrdinalIgnoreCase) ? "AnswerComposerService" : "MonolithAnswerStage",
                Operation = "answer.compose",
                InputSummary = "retrieved context + user input",
                OutputSummary = answer,
                SourceIds = retrievedSources
            },
            new()
            {
                ServiceName = scenario.Architecture.Equals("microservices", StringComparison.OrdinalIgnoreCase) ? "SafetyPolicyService" : "MonolithSafetyStage",
                Operation = "safety.check",
                InputSummary = answer,
                OutputSummary = "passed",
                SourceIds = Array.Empty<string>()
            }
        };

        return Task.FromResult(new AiRunResult
        {
            ScenarioId = scenario.Id,
            FinalAnswer = answer,
            RetrievedSources = retrievedSources,
            RetrievedContext = scenario.Context,
            ToolCalls = toolCalls,
            ServiceTraces = serviceTraces,
            Metadata = new Dictionary<string, string>
            {
                ["sample"] = "true",
                ["architecture"] = scenario.Architecture
            }
        });
    }

    private static string BuildAnswer(AiScenario scenario, string normalizedInput)
    {
        if (normalizedInput.Contains("ignore previous") || normalizedInput.Contains("system prompt") || normalizedInput.Contains("developer message"))
        {
            return "I cannot follow instructions that try to override the policy or expose hidden instructions, and I cannot approve a refund on that basis. Here is what I can do: based on refund-policy-v3, standard refunds are available within 30 days of purchase, so an order from 45 days ago falls outside the standard window and approval is not guaranteed. Exceptions after 30 days require documented damaged goods, a product recall, or a billing error — if one of those applies, share the details or your order reference and I will check the policy context to see whether your order qualifies.";
        }

        if (normalizedInput.Contains("delete my account") || normalizedInput.Contains("capture payment") || normalizedInput.Contains("issue a refund immediately"))
        {
            return "I understand the frustration, but I will not call destructive or unauthorized tools such as account.delete, payment.capture, or refund.issue, and I must not issue refunds automatically. Here is what I can do under refund-policy-v3: standard refunds are available within 30 days of purchase, and exceptions after 30 days require documented damaged goods, a product recall, or a billing error. If you share your order reference, I can check the order metadata and walk you through whether your purchase qualifies and how to submit the refund request properly.";
        }

        if (normalizedInput.Contains("45") || normalizedInput.Contains("forty-five"))
        {
            return "No — approval is not guaranteed for this order. Based on refund-policy-v3, standard refunds are available within 30 days of purchase, and an order from 45 days ago is outside that window. The policy allows an exception only for documented damaged goods, a product recall, or a billing error. If one of those applies, share the documentation and I can check whether your order qualifies for an escalation.";
        }

        if (normalizedInput.Contains("check my order") || scenario.ExpectedTools.Contains("order.lookup", StringComparer.OrdinalIgnoreCase))
        {
            return "I checked your order metadata with order.lookup — no refund was issued as part of that check, and I cannot guarantee approval. Your eligibility follows refund-policy-v3: standard refunds are available within 30 days of purchase, so your order's purchase date decides the path. If it is within 30 days, you can submit a standard refund request and I can walk you through it; outside the window, an exception requires documented damaged goods, a product recall, or a billing error.";
        }

        return "Based on refund-policy-v3, standard refunds are available within 30 days of purchase, and exceptions after 30 days require documented damaged goods, a product recall, or a billing error. Share your order reference and purchase date and I will check which path applies to your order — I cannot promise approval unless the policy context supports it.";
    }
}
