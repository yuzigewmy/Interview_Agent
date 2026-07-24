using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InterviewAgent.Core.Abstractions;
using InterviewAgent.Core.Settings;

namespace InterviewAgent.Infrastructure.Security;

[SupportedOSPlatform("windows")]
public sealed class DpapiSettingsStore : ISettingsStore
{
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("InterviewAgent.Settings.v1");
    private readonly string _path;

    public DpapiSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return AppSettings.Default;
        }

        byte[]? plaintext = null;
        try
        {
            var encrypted = await File.ReadAllBytesAsync(_path, cancellationToken);
            plaintext = ProtectedData.Unprotect(
                encrypted,
                Entropy,
                DataProtectionScope.CurrentUser);
            var persisted = JsonSerializer.Deserialize<PersistedSettings>(plaintext)
                ?? throw new InvalidDataException("设置内容为空。");
            var settings = persisted.ToAppSettings();
            settings.Validate();
            return settings;
        }
        catch (Exception exception)
            when (exception is CryptographicException or JsonException or ArgumentException)
        {
            throw new InvalidDataException("无法读取加密设置，请重新配置云服务凭据。", exception);
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    public async Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(settings);
        byte[] encrypted;
        try
        {
            encrypted = ProtectedData.Protect(
                plaintext,
                Entropy,
                DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(
                temporaryPath,
                encrypted,
                cancellationToken);
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record PersistedSettings
    {
        public bool DemoMode { get; init; }

        public string? ModelProvider { get; init; }

        public string? ModelName { get; init; }

        public string? ModelBaseUrl { get; init; }

        public string? ModelApiKey { get; init; }

        public int? ModelMaxTokens { get; init; }

        public string? DeepSeekApiKey { get; init; }

        public string? VolcanoApiKey { get; init; }

        public string? VolcanoAppKey { get; init; }

        public string? VolcanoAccessKey { get; init; }

        public string? VolcanoResourceId { get; init; }

        public bool? EnableFloatingOverlay { get; init; }

        public double? FloatingOverlayDiameter { get; init; }

        public double? FloatingOverlayLeft { get; init; }

        public double? FloatingOverlayTop { get; init; }

        public AppSettings ToAppSettings() =>
            new(
                DemoMode,
                ModelProvider ?? AppSettings.DefaultModelProvider,
                ModelName ?? AppSettings.DefaultModelName,
                ModelBaseUrl ?? AppSettings.DefaultModelBaseUrl,
                ModelApiKey ?? DeepSeekApiKey ?? string.Empty,
                ModelMaxTokens ?? AppSettings.DefaultModelMaxTokens,
                VolcanoApiKey ?? string.Empty,
                VolcanoAppKey ?? string.Empty,
                VolcanoAccessKey ?? string.Empty,
                VolcanoResourceId ?? "volc.seedasr.sauc.duration")
            {
                EnableFloatingOverlay = EnableFloatingOverlay ?? false,
                FloatingOverlayDiameter =
                    FloatingOverlayDiameter ??
                    AppSettings.DefaultFloatingOverlayDiameter,
                FloatingOverlayLeft = FloatingOverlayLeft,
                FloatingOverlayTop = FloatingOverlayTop,
            };
    }
}
