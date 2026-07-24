using InterviewAgent.Core.Settings;

namespace InterviewAgent.Infrastructure.Generation;

public sealed record OpenAiCompatibleOptions(
    string Provider,
    string Model,
    string BaseUrl,
    string ApiKey,
    int MaxTokens = AppSettings.DefaultModelMaxTokens)
{
    public void Validate() =>
        ModelEndpointPolicy.Validate(
            Provider,
            Model,
            BaseUrl,
            ApiKey,
            MaxTokens);

    public Uri ResolveEndpoint() =>
        ModelEndpointPolicy.ResolveChatCompletionsEndpoint(BaseUrl);

    public bool UsesDeepSeekLowLatencyProfile()
    {
        var endpoint = ResolveEndpoint();
        return Provider.Trim().StartsWith(
                   "DeepSeek",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   endpoint.Host,
                   "api.deepseek.com",
                   StringComparison.OrdinalIgnoreCase);
    }
}
