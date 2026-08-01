namespace Atlas.Edge.Core;

public sealed class EndpointSecurityPolicy
{
    public EndpointSecurityPolicy(bool allowInsecureHttp)
    {
        AllowInsecureHttp = allowInsecureHttp;
    }

    public bool AllowInsecureHttp { get; }

    public bool IsAllowed(Uri endpoint) =>
        string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
        (AllowInsecureHttp && string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase));

    public static bool IsDevelopmentOverrideEnabled(string? environmentName, bool allowInsecureHttpForDevelopment) =>
        allowInsecureHttpForDevelopment &&
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
}
