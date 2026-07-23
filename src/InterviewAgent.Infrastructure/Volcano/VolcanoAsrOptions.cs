namespace InterviewAgent.Infrastructure.Volcano;

public sealed record VolcanoAsrOptions(
    string? ApiKey = null,
    string? AppKey = null,
    string? AccessKey = null,
    string ResourceId = "volc.seedasr.sauc.duration")
{
    public static readonly Uri Endpoint =
        new("wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async");

    public void Validate()
    {
        var hasNewCredential = !string.IsNullOrWhiteSpace(ApiKey);
        var hasLegacyCredentials =
            !string.IsNullOrWhiteSpace(AppKey) &&
            !string.IsNullOrWhiteSpace(AccessKey);
        if (!hasNewCredential && !hasLegacyCredentials)
        {
            throw new ArgumentException(
                "请配置火山引擎 API Key，或同时配置 App ID 与 Access Token。");
        }

        if (string.IsNullOrWhiteSpace(ResourceId) ||
            !ResourceId.StartsWith("volc.", StringComparison.Ordinal))
        {
            throw new ArgumentException("火山引擎 Resource ID 无效。", nameof(ResourceId));
        }
    }
}

