using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using InterviewAgent.Core.Abstractions;
using InterviewAgent.Core.Answers;
using InterviewAgent.Infrastructure.DeepSeek;
using InterviewAgent.Infrastructure.Prompts;

namespace InterviewAgent.Infrastructure.Generation;

public sealed class OpenAiCompatibleAnswerGenerator : IAnswerGenerator
{
    private readonly HttpClient _httpClient;
    private readonly Func<OpenAiCompatibleOptions> _optionsAccessor;

    public OpenAiCompatibleAnswerGenerator(
        HttpClient httpClient,
        Func<OpenAiCompatibleOptions> optionsAccessor)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _optionsAccessor = optionsAccessor ??
            throw new ArgumentNullException(nameof(optionsAccessor));
    }

    public async IAsyncEnumerable<AnswerDelta> StreamAsync(
        AnswerRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var options = _optionsAccessor();
        options.Validate();
        var prompt = InterviewPromptBuilder.Build(request);
        var payload = new Dictionary<string, object?>
        {
            ["model"] = options.Model.Trim(),
            ["messages"] = new[]
            {
                new { role = "system", content = prompt.System },
                new { role = "user", content = prompt.User },
            },
            ["stream"] = true,
            ["max_tokens"] = options.MaxTokens,
        };
        if (options.UsesDeepSeekLowLatencyProfile())
        {
            payload["thinking"] = new { type = "disabled" };
            payload["stream_options"] = new { include_usage = true };
            payload["temperature"] = 0.2;
        }

        var body = JsonSerializer.Serialize(payload);

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            options.ResolveEndpoint());
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            message.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", options.ApiKey.Trim());
        }

        message.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/event-stream"));
        message.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"模型服务请求失败（HTTP {(int)response.StatusCode}）。",
                null,
                response.StatusCode);
        }

        await using var stream =
            await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            var parsed = DeepSeekSseParser.Parse(line);
            if (parsed.IsDone)
            {
                yield break;
            }

            if (!string.IsNullOrEmpty(parsed.Content))
            {
                yield return new AnswerDelta(parsed.Content);
            }
        }
    }
}
