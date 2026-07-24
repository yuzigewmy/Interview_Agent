using InterviewAgent.Infrastructure.Generation;

namespace InterviewAgent.Core.Tests;

public sealed class OpenAiCompatibleOptionsTests
{
    [Theory]
    [InlineData(
        "https://models.example.com/v1",
        "https://models.example.com/v1/chat/completions")]
    [InlineData(
        "https://models.example.com/v1/",
        "https://models.example.com/v1/chat/completions")]
    [InlineData(
        "https://models.example.com/v1/chat/completions",
        "https://models.example.com/v1/chat/completions")]
    public void ResolveEndpoint_AcceptsBaseOrCompleteUrl(
        string baseUrl,
        string expected)
    {
        var options = CreateOptions(baseUrl);

        options.Validate();

        Assert.Equal(expected, options.ResolveEndpoint().AbsoluteUri);
    }

    [Theory]
    [InlineData("http://localhost:11434/v1")]
    [InlineData("http://127.0.0.1:1234/v1")]
    [InlineData("http://[::1]:1234/v1")]
    public void Validate_AllowsLoopbackHttpForLocalModels(string baseUrl)
    {
        var options = CreateOptions(baseUrl, apiKey: string.Empty);

        var exception = Record.Exception(options.Validate);

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("http://models.example.com/v1")]
    [InlineData("ftp://models.example.com/v1")]
    [InlineData("https://user:password@models.example.com/v1")]
    [InlineData("https://models.example.com/v1?token=secret")]
    public void Validate_RejectsUnsafeBaseUrls(string baseUrl)
    {
        var options = CreateOptions(baseUrl);

        Assert.Throws<ArgumentException>(options.Validate);
    }

    private static OpenAiCompatibleOptions CreateOptions(
        string baseUrl,
        string apiKey = "configured-at-runtime") =>
        new(
            Provider: "Custom",
            Model: "custom-model",
            BaseUrl: baseUrl,
            ApiKey: apiKey,
            MaxTokens: 700);
}
