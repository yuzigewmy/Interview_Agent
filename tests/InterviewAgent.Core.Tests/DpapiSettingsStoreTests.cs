using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using InterviewAgent.Core.Settings;
using InterviewAgent.Infrastructure.Security;

namespace InterviewAgent.Core.Tests;

[SupportedOSPlatform("windows")]
public sealed class DpapiSettingsStoreTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"interview-agent-secrets-{Guid.NewGuid():N}.dat");

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsWithoutPlaintextSecrets()
    {
        var store = new DpapiSettingsStore(_path);
        var expected = new AppSettings(
            DemoMode: false,
            ModelProvider: "自定义提供商",
            ModelName: "custom-model-v2",
            ModelBaseUrl: "https://models.example.test/v1",
            ModelApiKey: "model-secret-value",
            ModelMaxTokens: 900,
            VolcanoApiKey: "volcano-secret-value",
            VolcanoAppKey: string.Empty,
            VolcanoAccessKey: string.Empty,
            VolcanoResourceId: "volc.seedasr.sauc.duration");

        await store.SaveAsync(expected, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);
        var raw = await File.ReadAllTextAsync(_path);

        Assert.Equal(expected, loaded);
        Assert.DoesNotContain("model-secret-value", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("volcano-secret-value", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_MigratesLegacyDeepSeekSettings()
    {
        const string legacyJson =
            """
            {
              "DemoMode": false,
              "DeepSeekApiKey": "legacy-deepseek-secret",
              "VolcanoApiKey": "volcano-secret",
              "VolcanoAppKey": "",
              "VolcanoAccessKey": "",
              "VolcanoResourceId": "volc.seedasr.sauc.duration"
            }
            """;
        var plaintext = Encoding.UTF8.GetBytes(legacyJson);
        var encrypted = ProtectedData.Protect(
            plaintext,
            Encoding.UTF8.GetBytes("InterviewAgent.Settings.v1"),
            DataProtectionScope.CurrentUser);
        CryptographicOperations.ZeroMemory(plaintext);
        await File.WriteAllBytesAsync(_path, encrypted);
        var store = new DpapiSettingsStore(_path);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal("DeepSeek", loaded.ModelProvider);
        Assert.Equal("deepseek-v4-flash", loaded.ModelName);
        Assert.Equal("https://api.deepseek.com", loaded.ModelBaseUrl);
        Assert.Equal("legacy-deepseek-secret", loaded.ModelApiKey);
        Assert.Equal(600, loaded.ModelMaxTokens);
    }

    [Fact]
    public async Task LoadAsync_ReturnsSafeDefaultsWhenFileDoesNotExist()
    {
        var store = new DpapiSettingsStore(_path);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.True(loaded.DemoMode);
        Assert.Equal("DeepSeek", loaded.ModelProvider);
        Assert.Equal("deepseek-v4-flash", loaded.ModelName);
        Assert.Equal("volc.seedasr.sauc.duration", loaded.VolcanoResourceId);
    }

    [Fact]
    public async Task LoadAsync_WithCorruptCiphertext_ReturnsSafeError()
    {
        var store = new DpapiSettingsStore(_path);
        await File.WriteAllBytesAsync(_path, [0x01, 0x02, 0x03, 0x04]);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => store.LoadAsync(CancellationToken.None));

        Assert.DoesNotContain("0x", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("无法读取加密设置", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
