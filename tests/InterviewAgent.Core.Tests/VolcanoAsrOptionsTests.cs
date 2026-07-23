using InterviewAgent.Infrastructure.Volcano;

namespace InterviewAgent.Core.Tests;

public sealed class VolcanoAsrOptionsTests
{
    [Fact]
    public void Validate_AcceptsNewConsoleApiKey()
    {
        var options = new VolcanoAsrOptions(ApiKey: "configured-at-runtime");

        var exception = Record.Exception(options.Validate);

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_AcceptsLegacyCredentialPair()
    {
        var options = new VolcanoAsrOptions(
            AppKey: "app-id",
            AccessKey: "access-token");

        var exception = Record.Exception(options.Validate);

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(null, "app-only", null)]
    [InlineData(null, null, "access-only")]
    public void Validate_RejectsIncompleteCredentials(
        string? apiKey,
        string? appKey,
        string? accessKey)
    {
        var options = new VolcanoAsrOptions(apiKey, appKey, accessKey);

        Assert.Throws<ArgumentException>(options.Validate);
    }
}

