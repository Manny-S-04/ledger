using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Interfaces;
using Application.Providers;

namespace Infrastructure.Providers;

// HTTP client for FakeProvider. HttpClient.Timeout (Provider:TimeoutSeconds, 1 minute) bounds every call;
// running out of time, failing to connect, or a 5xx all mean Unavailable. A 4xx means the provider refused
// the request itself (Rejected). A cancelled caller token is not swallowed: the cancellation propagates.
public sealed class HttpProviderService : IProviderService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public HttpProviderService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    private sealed record ChargeRequestBody(string Reference, long AmountPence, string Currency);
    private sealed record ChargeAcceptedBody(string? ProviderReference);
    private sealed record ChargeStatusBody(string? Status, string? FailureReason);
    private sealed record ProblemBody(string? Title);

    public async Task<ProviderChargeResult> RequestChargeAsync(ProviderChargeRequest request, CancellationToken cancellationToken = default)
    {
        ChargeRequestBody body = new(
            request.Reference.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.Amount.Pence,
            request.Currency.Code);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("charges", body, Json, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            return new ProviderChargeResult.Unavailable($"Provider unreachable: {exception.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProviderChargeResult.Unavailable("Provider timed out");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                ChargeAcceptedBody? accepted = await ReadAsync<ChargeAcceptedBody>(response, cancellationToken);
                return string.IsNullOrWhiteSpace(accepted?.ProviderReference)
                    ? new ProviderChargeResult.Unavailable("Provider accepted the charge without a charge id")
                    : new ProviderChargeResult.Accepted(accepted.ProviderReference);
            }

            if ((int)response.StatusCode is >= 400 and < 500)
            {
                ProblemBody? problem = await ReadAsync<ProblemBody>(response, cancellationToken);
                return new ProviderChargeResult.Rejected(problem?.Title ?? $"Provider rejected the charge ({(int)response.StatusCode})");
            }

            return new ProviderChargeResult.Unavailable($"Provider error ({(int)response.StatusCode})");
        }
    }

    public async Task<ProviderChargeStatus> GetChargeStatusAsync(string providerReference, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync($"charges/{Uri.EscapeDataString(providerReference)}", cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            return new ProviderChargeStatus.Unavailable($"Provider unreachable: {exception.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProviderChargeStatus.Unavailable("Provider timed out");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new ProviderChargeStatus.NotFound();
            }

            if (!response.IsSuccessStatusCode)
            {
                return new ProviderChargeStatus.Unavailable($"Provider error ({(int)response.StatusCode})");
            }

            ChargeStatusBody? status = await ReadAsync<ChargeStatusBody>(response, cancellationToken);
            return status?.Status switch
            {
                "Processing" => new ProviderChargeStatus.Processing(),
                "Succeeded" => new ProviderChargeStatus.Succeeded(),
                "Failed" when Enum.TryParse(status.FailureReason, out ProviderFailureReason reason) => new ProviderChargeStatus.Failed(reason),
                _ => new ProviderChargeStatus.Unavailable($"Provider sent an unknown status '{status?.Status}' / reason '{status?.FailureReason}'"),
            };
        }
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return null;
        }
    }
}
