using System.Text.Json.Serialization;

namespace InterviewAgent.Core.Settings;

public sealed record AppSettings(
    bool DemoMode,
    string ModelProvider,
    string ModelName,
    string ModelBaseUrl,
    string ModelApiKey,
    int ModelMaxTokens,
    string VolcanoApiKey,
    string VolcanoAppKey,
    string VolcanoAccessKey,
    string VolcanoResourceId)
{
    public const string DefaultModelProvider = "DeepSeek";
    public const string DefaultModelName = "deepseek-v4-flash";
    public const string DefaultModelBaseUrl = "https://api.deepseek.com";
    public const int DefaultModelMaxTokens = 600;
    public const double DefaultFloatingOverlayDiameter = 72;

    public bool EnableFloatingOverlay { get; init; }

    public double FloatingOverlayDiameter { get; init; } =
        DefaultFloatingOverlayDiameter;

    public double? FloatingOverlayLeft { get; init; }

    public double? FloatingOverlayTop { get; init; }

    public static AppSettings Default { get; } = new(
        DemoMode: true,
        ModelProvider: DefaultModelProvider,
        ModelName: DefaultModelName,
        ModelBaseUrl: DefaultModelBaseUrl,
        ModelApiKey: string.Empty,
        ModelMaxTokens: DefaultModelMaxTokens,
        VolcanoApiKey: string.Empty,
        VolcanoAppKey: string.Empty,
        VolcanoAccessKey: string.Empty,
        VolcanoResourceId: "volc.seedasr.sauc.duration");

    public AppSettings(
        bool DemoMode,
        string DeepSeekApiKey,
        string VolcanoApiKey,
        string VolcanoAppKey,
        string VolcanoAccessKey,
        string VolcanoResourceId)
        : this(
            DemoMode,
            DefaultModelProvider,
            DefaultModelName,
            DefaultModelBaseUrl,
            DeepSeekApiKey,
            DefaultModelMaxTokens,
            VolcanoApiKey,
            VolcanoAppKey,
            VolcanoAccessKey,
            VolcanoResourceId)
    {
    }

    [JsonIgnore]
    public bool HasModelConfiguration =>
        !string.IsNullOrWhiteSpace(ModelProvider) &&
        !string.IsNullOrWhiteSpace(ModelName) &&
        !string.IsNullOrWhiteSpace(ModelBaseUrl);

    [JsonIgnore]
    public string DeepSeekApiKey => ModelApiKey;

    [JsonIgnore]
    public bool HasDeepSeekCredential =>
        !string.IsNullOrWhiteSpace(ModelApiKey);

    [JsonIgnore]
    public bool HasVolcanoCredential =>
        !string.IsNullOrWhiteSpace(VolcanoApiKey) ||
        (!string.IsNullOrWhiteSpace(VolcanoAppKey) &&
         !string.IsNullOrWhiteSpace(VolcanoAccessKey));

    public void Validate()
    {
        ModelEndpointPolicy.Validate(
            ModelProvider,
            ModelName,
            ModelBaseUrl,
            ModelApiKey,
            ModelMaxTokens);
        ValidateSecret(VolcanoApiKey, nameof(VolcanoApiKey));
        ValidateSecret(VolcanoAppKey, nameof(VolcanoAppKey));
        ValidateSecret(VolcanoAccessKey, nameof(VolcanoAccessKey));
        if (!double.IsFinite(FloatingOverlayDiameter) ||
            FloatingOverlayDiameter is < 56 or > 128)
        {
            throw new ArgumentOutOfRangeException(
                nameof(FloatingOverlayDiameter),
                "悬浮球大小必须在 56 到 128 之间。");
        }

        ValidateCoordinate(FloatingOverlayLeft, nameof(FloatingOverlayLeft));
        ValidateCoordinate(FloatingOverlayTop, nameof(FloatingOverlayTop));
        if (string.IsNullOrWhiteSpace(VolcanoResourceId) ||
            VolcanoResourceId.Length > 128 ||
            !VolcanoResourceId.StartsWith("volc.", StringComparison.Ordinal))
        {
            throw new ArgumentException("火山 Resource ID 无效。", nameof(VolcanoResourceId));
        }
    }

    private static void ValidateSecret(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length > 512)
        {
            throw new ArgumentOutOfRangeException(parameterName, "凭据长度超过上限。");
        }
    }

    private static void ValidateCoordinate(double? value, string parameterName)
    {
        if (value.HasValue && !double.IsFinite(value.Value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "悬浮球位置必须是有限数值。");
        }
    }
}
