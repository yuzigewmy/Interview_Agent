using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using InterviewAgent.Core.Abstractions;
using InterviewAgent.Core.Answers;
using InterviewAgent.Infrastructure.Prompts;

namespace InterviewAgent.Infrastructure.DeepSeek;

public sealed class DeepSeekAnswerGenerator : IAnswerGenerator
{
    private readonly HttpClient _httpClient;
    private readonly Func<DeepSeekOptions> _optionsAccessor;

    public DeepSeekAnswerGenerator(
        HttpClient httpClient,
        Func<DeepSeekOptions> optionsAccessor)
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
        var body = JsonSerializer.Serialize(new
        {
            model = options.Model,
            messages = new[]
            {
                new { role = "system", content = prompt.System },
                new { role = "user", content = prompt.User },
            },
            thinking = new { type = "disabled" },
            stream = true,
            stream_options = new { include_usage = true },
            temperature = 0.2,
            max_tokens = options.MaxTokens,
        });

        using var message = new HttpRequestMessage(HttpMethod.Post, DeepSeekOptions.Endpoint);
        message.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", options.ApiKey);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        message.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"DeepSeek 请求失败（HTTP {(int)response.StatusCode}）。",
                null,
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
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

