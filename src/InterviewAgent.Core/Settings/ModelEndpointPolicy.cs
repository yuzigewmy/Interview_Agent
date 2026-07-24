namespace InterviewAgent.Core.Settings;

public static class ModelEndpointPolicy
{
    public const int MaxProviderCharacters = 80;
    public const int MaxModelCharacters = 200;
    public const int MaxBaseUrlCharacters = 2_048;
    public const int MaxApiKeyCharacters = 512;
    public const int MinimumOutputTokens = 64;
    public const int MaximumOutputTokens = 8_192;

    public static void Validate(
        string provider,
        string model,
        string baseUrl,
        string apiKey,
        int maxTokens)
    {
        ValidateText(
            provider,
            MaxProviderCharacters,
            nameof(provider),
            "模型提供商");
        ValidateText(
            model,
            MaxModelCharacters,
            nameof(model),
            "模型名称");
        ArgumentNullException.ThrowIfNull(apiKey);
        if (apiKey.Length > MaxApiKeyCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(apiKey),
                "模型 API Key 长度超过上限。");
        }

        _ = ResolveChatCompletionsEndpoint(baseUrl);
        if (maxTokens is < MinimumOutputTokens or > MaximumOutputTokens)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxTokens),
                $"最大输出 Token 必须在 {MinimumOutputTokens} 到 " +
                $"{MaximumOutputTokens} 之间。");
        }
    }

    public static Uri ResolveChatCompletionsEndpoint(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        if (baseUrl.Length > MaxBaseUrlCharacters ||
            !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException(
                "模型 Base URL 格式无效，且不能包含账号、查询参数或片段。",
                nameof(baseUrl));
        }

        var isHttps = string.Equals(
            uri.Scheme,
            Uri.UriSchemeHttps,
            StringComparison.OrdinalIgnoreCase);
        var isLoopbackHttp =
            string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase) &&
            uri.IsLoopback;
        if (!isHttps && !isLoopbackHttp)
        {
            throw new ArgumentException(
                "远程模型必须使用 HTTPS；HTTP 仅允许 localhost/127.0.0.1/::1。",
                nameof(baseUrl));
        }

        var trimmed = uri.AbsoluteUri.TrimEnd('/');
        if (trimmed.EndsWith(
                "/chat/completions",
                StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(trimmed, UriKind.Absolute);
        }

        return new Uri($"{trimmed}/chat/completions", UriKind.Absolute);
    }

    private static void ValidateText(
        string value,
        int maximumCharacters,
        string parameterName,
        string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var trimmed = value.Trim();
        if (trimmed.Length > maximumCharacters ||
            trimmed.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"{displayName}无效或长度超过上限。",
                parameterName);
        }
    }
}
