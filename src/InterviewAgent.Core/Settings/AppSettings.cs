namespace InterviewAgent.Core.Settings;

public sealed record AppSettings(
    bool DemoMode,
    string DeepSeekApiKey,
    string VolcanoApiKey,
    string VolcanoAppKey,
    string VolcanoAccessKey,
    string VolcanoResourceId)
{
    public static AppSettings Default { get; } = new(
        DemoMode: true,
        DeepSeekApiKey: string.Empty,
        VolcanoApiKey: string.Empty,
        VolcanoAppKey: string.Empty,
        VolcanoAccessKey: string.Empty,
        VolcanoResourceId: "volc.seedasr.sauc.duration");

    public bool HasDeepSeekCredential =>
        !string.IsNullOrWhiteSpace(DeepSeekApiKey);

    public bool HasVolcanoCredential =>
        !string.IsNullOrWhiteSpace(VolcanoApiKey) ||
        (!string.IsNullOrWhiteSpace(VolcanoAppKey) &&
         !string.IsNullOrWhiteSpace(VolcanoAccessKey));

    public void Validate()
    {
        ValidateSecret(DeepSeekApiKey, nameof(DeepSeekApiKey));
        ValidateSecret(VolcanoApiKey, nameof(VolcanoApiKey));
        ValidateSecret(VolcanoAppKey, nameof(VolcanoAppKey));
        ValidateSecret(VolcanoAccessKey, nameof(VolcanoAccessKey));
        if (string.IsNullOrWhiteSpace(VolcanoResourceId) ||
            VolcanoResourceId.Length > 128 ||
            !VolcanoResourceId.StartsWith("volc.", StringComparison.Ordinal))
        {
            throw new ArgumentException("火山 Resource ID 无效。", nameof(VolcanoResourceId));
        }
    }

    private static void ValidateSecret(string value, string parameterName)
    {
        if (value.Length > 512)
        {
            throw new ArgumentOutOfRangeException(parameterName, "凭据长度超过上限。");
        }
    }
}

