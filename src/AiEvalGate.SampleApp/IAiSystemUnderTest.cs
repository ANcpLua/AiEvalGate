using AiEvalGate.Core.Models;

namespace AiEvalGate.SampleApp;

public interface IAiSystemUnderTest
{
    Task<AiRunResult> RunAsync(AiScenario scenario, CancellationToken cancellationToken = default);
}
