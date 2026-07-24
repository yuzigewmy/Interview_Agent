using InterviewAgent.Core.Settings;

namespace InterviewAgent.Core.Tests;

public sealed class ModelCredentialPolicyTests
{
    [Fact]
    public void Resolve_PreservesExistingKeyWhenEndpointIsUnchanged()
    {
        var key = ModelCredentialPolicy.Resolve(
            enteredKey: string.Empty,
            existingKey: "existing-secret",
            existingBaseUrl: "https://api.example.com/v1/",
            newBaseUrl: "https://api.example.com/v1",
            clearCredentials: false);

        Assert.Equal("existing-secret", key);
    }

    [Fact]
    public void Resolve_ClearsExistingKeyWhenEndpointChanges()
    {
        var key = ModelCredentialPolicy.Resolve(
            enteredKey: string.Empty,
            existingKey: "existing-secret",
            existingBaseUrl: "https://api.first.example/v1",
            newBaseUrl: "https://api.second.example/v1",
            clearCredentials: false);

        Assert.Empty(key);
    }

    [Fact]
    public void Resolve_UsesEnteredKeyForChangedEndpoint()
    {
        var key = ModelCredentialPolicy.Resolve(
            enteredKey: " new-secret ",
            existingKey: "existing-secret",
            existingBaseUrl: "https://api.first.example/v1",
            newBaseUrl: "https://api.second.example/v1",
            clearCredentials: false);

        Assert.Equal("new-secret", key);
    }
}
