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
            var settings = JsonSerializer.Deserialize<AppSettings>(plaintext)
                ?? throw new InvalidDataException("设置内容为空。");
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
}
