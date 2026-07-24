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
}
