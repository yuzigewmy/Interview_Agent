using System.Net;
using System.Text;
using System.Text.Json;
using InterviewAgent.Core.Answers;
using InterviewAgent.Core.Knowledge;
using InterviewAgent.Infrastructure.Generation;

namespace InterviewAgent.Core.Tests;

public sealed class OpenAiCompatibleAnswerGeneratorTests
{
    [Fact]
    public async Task StreamAsync_UsesConfiguredEndpointModelAndOptionalBearerKey()
    {
        var handler = new CapturingHandler(
            """
            data: {"choices":[{"delta":{"content":"我先回答结论。"}}]}

            data: [DONE]

            """);
        using var client = new HttpClient(handler);
        var generator = new OpenAiCompatibleAnswerGenerator(
            client,
            () => new OpenAiCompatibleOptions(
                Provider: "Qwen",
                Model: "qwen-plus",
                BaseUrl: "https://dashscope.example.test/compatible-mode/v1",
                ApiKey: "runtime-secret",
                MaxTokens: 900));
        var deltas = new List<string>();

        await foreach (var delta in generator.StreamAsync(
                           new AnswerRequest(
                               "什么是 SQL 注入？",
                               Array.Empty<KnowledgeHit>()),
                           CancellationToken.None))
        {
            deltas.Add(delta.Text);
        }

        Assert.Equal(
            "https://dashscope.example.test/compatible-mode/v1/chat/completions",
            handler.RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("runtime-secret", handler.AuthorizationParameter);
        Assert.Equal(new[] { "我先回答结论。" }, deltas);

        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(
            "qwen-plus",
            body.RootElement.GetProperty("model").GetString());
        Assert.Equal(
            900,
            body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.False(body.RootElement.TryGetProperty("thinking", out _));
        Assert.False(body.RootElement.TryGetProperty("stream_options", out _));
        Assert.False(body.RootElement.TryGetProperty("temperature", out _));
    }

    [Fact]
    public async Task StreamAsync_RestoresLowLatencyDeepSeekRequestProfile()
    {
        var handler = new CapturingHandler("data: [DONE]\n\n");
        using var client = new HttpClient(handler);
        var generator = new OpenAiCompatibleAnswerGenerator(
            client,
            () => new OpenAiCompatibleOptions(
                Provider: "DeepSeek",
                Model: "deepseek-v4-flash",
                BaseUrl: "https://api.deepseek.com",
                ApiKey: "runtime-secret",
                MaxTokens: 600));

        await foreach (var _ in generator.StreamAsync(
                           new AnswerRequest(
                               "什么是 SQL 注入？",
                               Array.Empty<KnowledgeHit>()),
                           CancellationToken.None))
        {
        }

        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(
            "disabled",
            body.RootElement
                .GetProperty("thinking")
                .GetProperty("type")
                .GetString());
        Assert.Equal(
            0.2,
            body.RootElement.GetProperty("temperature").GetDouble(),
            precision: 3);
        Assert.True(
            body.RootElement
                .GetProperty("stream_options")
                .GetProperty("include_usage")
                .GetBoolean());
    }

    [Fact]
    public async Task StreamAsync_OmitsAuthorizationWhenLocalModelHasNoKey()
    {
        var handler = new CapturingHandler("data: [DONE]\n\n");
        using var client = new HttpClient(handler);
        var generator = new OpenAiCompatibleAnswerGenerator(
            client,
            () => new OpenAiCompatibleOptions(
                Provider: "Ollama",
                Model: "qwen2.5:7b",
                BaseUrl: "http://localhost:11434/v1",
                ApiKey: string.Empty,
                MaxTokens: 600));

        await foreach (var _ in generator.StreamAsync(
                           new AnswerRequest(
                               "解释一下命令执行",
                               Array.Empty<KnowledgeHit>()),
                           CancellationToken.None))
        {
        }

        Assert.Null(handler.AuthorizationScheme);
        Assert.Null(handler.AuthorizationParameter);
    }

    private sealed class CapturingHandler(string responseBody) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? AuthorizationScheme { get; private set; }

        public string? AuthorizationParameter { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    responseBody,
                    Encoding.UTF8,
                    "text/event-stream"),
            };
        }
    }
}
