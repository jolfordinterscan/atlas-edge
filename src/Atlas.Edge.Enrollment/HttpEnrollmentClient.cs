using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Atlas.Edge.Core;

namespace Atlas.Edge.Enrollment;

public sealed class HttpEnrollmentClient : IEnrollmentClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly EndpointSecurityPolicy _endpointSecurityPolicy;
    private readonly HttpClient _httpClient;

    public HttpEnrollmentClient(HttpClient httpClient, EndpointSecurityPolicy endpointSecurityPolicy)
    {
        _httpClient = httpClient;
        _endpointSecurityPolicy = endpointSecurityPolicy;
    }

    public async Task<EnrollmentResult> EnrollAsync(EnrollmentRequest request, CancellationToken cancellationToken)
    {
        if (_httpClient.BaseAddress is null || !_endpointSecurityPolicy.IsAllowed(_httpClient.BaseAddress))
        {
            return EnrollmentResult.NonRetryableFailure("Enrollment endpoint must use HTTPS.");
        }

        using var response = await _httpClient.PostAsJsonAsync("api/edge/v1/enroll", request, SerializerOptions, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            EnrollmentResponse? payload;
            try
            {
                payload = await response.Content.ReadFromJsonAsync<EnrollmentResponse>(SerializerOptions, cancellationToken);
            }
            catch (JsonException)
            {
                return EnrollmentResult.RetryableFailure("Enrollment response could not be parsed.");
            }

            if (payload is null)
            {
                return EnrollmentResult.RetryableFailure("Enrollment response body was empty.");
            }

            var validationError = EnrollmentResponseValidator.Validate(payload, _endpointSecurityPolicy);
            if (validationError is not null)
            {
                return EnrollmentResult.NonRetryableFailure(validationError);
            }

            return EnrollmentResult.Success(payload);
        }

        if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout)
        {
            return EnrollmentResult.RetryableFailure($"Enrollment failed with status {(int)response.StatusCode}.");
        }

        return EnrollmentResult.NonRetryableFailure($"Enrollment failed with status {(int)response.StatusCode}.");
    }
}
