using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Atlas.Edge.Core;

namespace Atlas.Edge.Enrollment;

public sealed class HttpTokenRefreshClient : ITokenRefreshClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly EndpointSecurityPolicy _endpointSecurityPolicy;
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;

    public HttpTokenRefreshClient(
        HttpClient httpClient,
        EndpointSecurityPolicy endpointSecurityPolicy,
        TimeProvider timeProvider)
    {
        _httpClient = httpClient;
        _endpointSecurityPolicy = endpointSecurityPolicy;
        _timeProvider = timeProvider;
    }

    public async Task<TokenRefreshResult> RefreshAsync(
        Uri refreshEndpoint,
        TokenRefreshRequest request,
        CancellationToken cancellationToken)
    {
        if (!_endpointSecurityPolicy.IsAllowed(refreshEndpoint))
        {
            return TokenRefreshResult.Failure(TokenRefreshFailureKind.EndpointRejected, null, "https_required");
        }

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(refreshEndpoint, request, SerializerOptions, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                TokenRefreshResponse? payload;
                try
                {
                    payload = await response.Content.ReadFromJsonAsync<TokenRefreshResponse>(SerializerOptions, cancellationToken);
                }
                catch (JsonException)
                {
                    return TokenRefreshResult.Failure(TokenRefreshFailureKind.InvalidResponse, (int)response.StatusCode, "invalid_json");
                }

                if (payload is null)
                {
                    return TokenRefreshResult.Failure(TokenRefreshFailureKind.InvalidResponse, (int)response.StatusCode, "empty_response");
                }

                var validationError = TokenRefreshResponseValidator.Validate(request, payload, _timeProvider.GetUtcNow());
                return validationError is null
                    ? TokenRefreshResult.Success(payload)
                    : TokenRefreshResult.Failure(TokenRefreshFailureKind.InvalidResponse, (int)response.StatusCode, validationError);
            }

            var statusCode = (int)response.StatusCode;
            var errorCode = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "invalid_refresh_token",
                HttpStatusCode.Forbidden => "refresh_token_revoked",
                HttpStatusCode.Gone => "refresh_token_expired",
                HttpStatusCode.Conflict => "binding_mismatch",
                _ => $"http_{statusCode}"
            };

            var kind = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => TokenRefreshFailureKind.InvalidRefreshToken,
                HttpStatusCode.Forbidden => TokenRefreshFailureKind.RevokedRefreshToken,
                HttpStatusCode.Gone => TokenRefreshFailureKind.ExpiredRefreshToken,
                HttpStatusCode.Conflict => TokenRefreshFailureKind.BindingMismatch,
                HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests => TokenRefreshFailureKind.Transient,
                _ when statusCode >= 500 => TokenRefreshFailureKind.Transient,
                _ => TokenRefreshFailureKind.EndpointRejected
            };

            return TokenRefreshResult.Failure(kind, statusCode, errorCode);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return TokenRefreshResult.Failure(TokenRefreshFailureKind.Transient, null, "timeout");
        }
        catch (HttpRequestException)
        {
            return TokenRefreshResult.Failure(TokenRefreshFailureKind.Transient, null, "network_error");
        }
    }
}
