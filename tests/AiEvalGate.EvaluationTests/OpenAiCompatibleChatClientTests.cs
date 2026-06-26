using System.Net;
using System.Text;
using System.Text.Json;
using AiEvalGate.Core.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AiEvalGate.EvaluationTests;

/// <summary>
/// Deterministic tests for <see cref="OpenAiCompatibleChatClient"/> using a capturing
/// <see cref="HttpMessageHandler"/> — they verify the request shape and response parsing without any
/// live model, so the local-judge wiring is provable in CI.
/// </summary>
[TestClass]
public sealed class OpenAiCompatibleChatClientTests
{
    private sealed class CapturingHandler(string responseJson) : HttpMessageHandler
    {
        public string? CapturedBody { get; private set; }
        public Uri? CapturedUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedUri = request.RequestUri;
            CapturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static OpenAiCompatibleChatClient Client(CapturingHandler handler) =>
        new(new Uri("http://localhost:11434/v1/"), "bitnet-b1.58-2B-4T", httpClient: new HttpClient(handler));

    [TestMethod]
    public async Task GetResponseAsync_AssistantContent_IsReturned()
    {
        var handler = new CapturingHandler("""{"choices":[{"message":{"role":"assistant","content":"verdict json"}}]}""");

        ChatResponse response = await Client(handler).GetResponseAsync([new ChatMessage(ChatRole.User, "judge this")]);

        Assert.AreEqual("verdict json", response.Text);
    }

    [TestMethod]
    public async Task GetResponseAsync_Request_UsesLegacyMaxTokensAndModel()
    {
        var handler = new CapturingHandler("""{"choices":[{"message":{"content":"ok"}}]}""");

        await Client(handler).GetResponseAsync(
            [new ChatMessage(ChatRole.System, "sys"), new ChatMessage(ChatRole.User, "u")],
            new ChatOptions { MaxOutputTokens = 256, Temperature = 0.0f });

        using JsonDocument body = JsonDocument.Parse(handler.CapturedBody!);
        Assert.AreEqual(256, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.IsFalse(handler.CapturedBody!.Contains("max_completion_tokens", StringComparison.Ordinal));
        Assert.AreEqual("bitnet-b1.58-2B-4T", body.RootElement.GetProperty("model").GetString());
        Assert.AreEqual("system", body.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
    }

    [TestMethod]
    public async Task GetResponseAsync_Request_TargetsChatCompletionsPath()
    {
        var handler = new CapturingHandler("""{"choices":[]}""");

        await Client(handler).GetResponseAsync([new ChatMessage(ChatRole.User, "x")]);

        StringAssert.EndsWith(handler.CapturedUri!.AbsolutePath, "/v1/chat/completions");
    }

    public TestContext TestContext { get; set; } = null!;
}
