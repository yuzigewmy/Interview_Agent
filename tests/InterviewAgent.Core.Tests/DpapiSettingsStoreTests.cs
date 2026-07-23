using System.Runtime.Versioning;
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
            DeepSeekApiKey: "deepseek-secret-value",
            VolcanoApiKey: "volcano-secret-value",
            VolcanoAppKey: string.Empty,
            VolcanoAccessKey: string.Empty,
            VolcanoResourceId: "volc.seedasr.sauc.duration");

        await store.SaveAsync(expected, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);
        var raw = await File.ReadAllTextAsync(_path);

        Assert.Equal(expected, loaded);
        Assert.DoesNotContain("deepseek-secret-value", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("volcano-secret-value", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_ReturnsSafeDefaultsWhenFileDoesNotExist()
    {
        var store = new DpapiSettingsStore(_path);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.True(loaded.DemoMode);
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
