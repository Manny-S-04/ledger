using System.Globalization;
using System.Net;
using System.Text;
using FakeProvider.Data;

namespace FakeProvider.Delivery;

public sealed record CallbackSettings(Uri CallbackUrl, byte[] Secret, TimeSpan PollInterval);

// Sends due callbacks from the callback_deliveries table. Because deliveries are persisted, anything not yet
// delivered when the process stops is picked up on the next boot, with its attempt count carried on.
public sealed class CallbackDispatcher : BackgroundService
{
    // Retries after the first attempt (T-022.3: 3 for dev, so 4 attempts total). Adjustable.
    public static int MaxRetries = 3;

    // Delay before the first retry; doubles on each further retry (5 s, 10 s, 20 s). Adjustable.
    public static TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(5);

    public const string HttpClientName = "ledger-callbacks";
    private const int BatchSize = 50;

    private readonly IChargeStore _store;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CallbackSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CallbackDispatcher> _logger;

    public CallbackDispatcher(
        IChargeStore store,
        IHttpClientFactory httpClientFactory,
        CallbackSettings settings,
        TimeProvider timeProvider,
        ILogger<CallbackDispatcher> logger)
    {
        _store = store;
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<Data.Delivery> due = await _store.GetDueDeliveriesAsync(
                    _timeProvider.GetUtcNow().UtcDateTime, BatchSize, stoppingToken);
                foreach (Data.Delivery delivery in due)
                {
                    await DeliverAsync(delivery, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Callback dispatch loop failed; retrying after the poll interval");
            }

            try
            {
                await Task.Delay(_settings.PollInterval, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task DeliverAsync(Data.Delivery delivery, CancellationToken stoppingToken)
    {
        int attempts = delivery.Attempts + 1;
        string? error = await SendAsync(delivery, stoppingToken);

        if (error is null)
        {
            await _store.MarkDeliveredAsync(delivery.Id, attempts, stoppingToken);
            _logger.LogInformation(
                "Callback delivered eventId={EventId} charge={ProviderReference} attempt={Attempt}",
                delivery.EventId, delivery.ProviderReference, attempts);
            return;
        }

        DateTime? nextAttemptAt = attempts > MaxRetries
            ? null
            : _timeProvider.GetUtcNow().UtcDateTime + FirstRetryDelay * Math.Pow(2, attempts - 1);
        await _store.RecordFailedAttemptAsync(delivery.Id, attempts, nextAttemptAt, error, stoppingToken);

        if (nextAttemptAt is null)
        {
            _logger.LogWarning(
                "Callback gave up eventId={EventId} charge={ProviderReference} attempts={Attempts} error={Error}",
                delivery.EventId, delivery.ProviderReference, attempts, error);
        }
        else
        {
            _logger.LogWarning(
                "Callback failed eventId={EventId} charge={ProviderReference} attempt={Attempt} error={Error} nextAttemptAt={NextAttemptAt:o}",
                delivery.EventId, delivery.ProviderReference, attempts, error, nextAttemptAt);
        }
    }

    // Returns null when the ledger answered 200, otherwise a short description of what went wrong.
    private async Task<string?> SendAsync(Data.Delivery delivery, CancellationToken stoppingToken)
    {
        byte[] body = Encoding.UTF8.GetBytes(delivery.Payload);

        // Signed afresh on every attempt, so a retry carries a current timestamp.
        string timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        string signature = CallbackSigner.Sign(_settings.Secret, timestamp, body);

        using HttpRequestMessage request = new(HttpMethod.Post, _settings.CallbackUrl);
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add(CallbackSigner.TimestampHeader, timestamp);
        request.Headers.Add(CallbackSigner.SignatureHeader, signature);

        HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, stoppingToken);
            return response.StatusCode == HttpStatusCode.OK ? null : $"HTTP {(int)response.StatusCode}";
        }
        catch (HttpRequestException exception)
        {
            return $"unreachable: {exception.Message}";
        }
        catch (TaskCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            return "timeout";
        }
    }
}
