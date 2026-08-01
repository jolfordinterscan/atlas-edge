using Atlas.Edge.Configuration;
using Microsoft.Extensions.Options;

namespace Atlas.Edge.Security;

public sealed class CredentialExpiryPolicy
{
    private readonly AtlasEdgeOptions _options;
    private readonly TimeProvider _timeProvider;

    public CredentialExpiryPolicy(IOptions<AtlasEdgeOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    public bool IsRefreshDue(CredentialLease lease) =>
        UtcNow >= lease.AccessTokenExpiryUtc - TimeSpan.FromSeconds(_options.TokenRefreshLeadTimeSeconds);

    public bool CanUseAccessToken(CredentialLease lease) =>
        UtcNow < lease.AccessTokenExpiryUtc - TimeSpan.FromSeconds(_options.TokenClockSkewSeconds);

    public bool CanUseRefreshToken(CredentialLease lease) =>
        UtcNow < lease.RefreshTokenExpiryUtc - TimeSpan.FromSeconds(_options.TokenClockSkewSeconds);

    public TimeSpan GetRetryDelay(int failureCount)
    {
        var exponent = Math.Clamp(failureCount - 1, 0, 20);
        var seconds = _options.TokenRefreshRetryBaseSeconds * Math.Pow(2, exponent);
        return TimeSpan.FromSeconds(Math.Min(seconds, _options.TokenRefreshRetryMaxSeconds));
    }
}
