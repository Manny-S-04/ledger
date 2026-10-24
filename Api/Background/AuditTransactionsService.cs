using Application.DbResult;
using Application.Interfaces;
using Application.Logging;

namespace Api.Background;

// Every interval, marks top ups that have been pending longer than expireAfter as expired: the callback
// never came, or the provider call failed after the row was inserted. The user can request a new top up.
public sealed class AuditTransactionsService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _expireAfter;
    private readonly ILogger<AuditTransactionsService> _logger;

    public AuditTransactionsService(IServiceScopeFactory scopes, TimeSpan interval, TimeSpan expireAfter, ILogger<AuditTransactionsService> logger)
    {
        _scopes = scopes;
        _interval = interval;
        _expireAfter = expireAfter;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ExpireStaleAsync();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    private async Task ExpireStaleAsync()
    {
        // The repository is scoped; a hosted service is a singleton, so it gets its own scope per run.
        await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
        IAccountRepository repository = scope.ServiceProvider.GetRequiredService<IAccountRepository>();

        DbResult<int> result = await repository.ExpireStalePendingAsync(DateTime.UtcNow - _expireAfter);
        switch (result)
        {
            case DbResult<int>.Success { Value: > 0 } success:
                _logger.Warn($"expired {success.Value} pending top up(s)");
                break;
            case DbResult<int>.Success:
                break;
            default:
                _logger.Warn("failed to expire pending top ups; will retry next run");
                break;
        }
    }
}
