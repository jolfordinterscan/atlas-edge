namespace Atlas.Edge.Enrollment;

public static class TokenRefreshResponseValidator
{
    public static string? Validate(TokenRefreshRequest request, TokenRefreshResponse response, DateTimeOffset nowUtc)
    {
        if (!string.Equals(response.AgentId, request.AgentId, StringComparison.Ordinal) ||
            !string.Equals(response.DeviceId, request.DeviceId, StringComparison.Ordinal) ||
            !string.Equals(response.TenantBinding, request.TenantBinding, StringComparison.Ordinal))
        {
            return "binding_mismatch";
        }

        if (!string.Equals(response.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return "invalid_token_type";
        }

        if (string.IsNullOrWhiteSpace(response.AccessToken) || string.IsNullOrWhiteSpace(response.RefreshToken))
        {
            return "missing_token";
        }

        if (response.AccessTokenExpiryUtc <= nowUtc || response.RefreshTokenExpiryUtc <= response.AccessTokenExpiryUtc)
        {
            return "invalid_expiry";
        }

        return null;
    }
}
