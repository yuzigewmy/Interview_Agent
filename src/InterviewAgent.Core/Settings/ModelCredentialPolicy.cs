namespace InterviewAgent.Core.Settings;

public static class ModelCredentialPolicy
{
    public static string Resolve(
        string enteredKey,
        string existingKey,
        string existingBaseUrl,
        string newBaseUrl,
        bool clearCredentials)
    {
        ArgumentNullException.ThrowIfNull(enteredKey);
        ArgumentNullException.ThrowIfNull(existingKey);
        ArgumentNullException.ThrowIfNull(existingBaseUrl);
        ArgumentNullException.ThrowIfNull(newBaseUrl);

        if (clearCredentials)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(enteredKey))
        {
            return enteredKey.Trim();
        }

        if (string.IsNullOrEmpty(existingKey) ||
            !IsSameEndpoint(existingBaseUrl, newBaseUrl))
        {
            return string.Empty;
        }

        return existingKey;
    }

    private static bool IsSameEndpoint(string left, string right)
    {
        try
        {
            var leftEndpoint =
                ModelEndpointPolicy.ResolveChatCompletionsEndpoint(left);
            var rightEndpoint =
                ModelEndpointPolicy.ResolveChatCompletionsEndpoint(right);
            return string.Equals(
                       leftEndpoint.Scheme,
                       rightEndpoint.Scheme,
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       leftEndpoint.Host,
                       rightEndpoint.Host,
                       StringComparison.OrdinalIgnoreCase) &&
                   leftEndpoint.Port == rightEndpoint.Port &&
                   string.Equals(
                       leftEndpoint.AbsolutePath,
                       rightEndpoint.AbsolutePath,
                       StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
