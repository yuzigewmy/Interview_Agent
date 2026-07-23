namespace InterviewAgent.Infrastructure.DeepSeek;

public sealed record DeepSeekOptions(
    string ApiKey,
    string Model = "deepseek-v4-flash",
    int MaxTokens = 600)
{
    public static readonly Uri Endpoint =
        new("https://api.deepseek.com/chat/completions");

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ApiKey);
        if (Model is not ("deepseek-v4-flash" or "deepseek-v4-pro"))
        {
            throw new ArgumentException("不支持的 DeepSeek 模型。", nameof(Model));
        }

        if (MaxTokens is < 64 or > 2_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTokens));
        }
    }
}

