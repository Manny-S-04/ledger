using Npgsql;

namespace FakeProvider.Data;

public sealed class NpgsqlChargeStore : IChargeStore
{
    private readonly NpgsqlDataSource _dataSource;

    public NpgsqlChargeStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task SaveAsync(Charge charge, IReadOnlyList<NewDelivery> deliveries, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (NpgsqlCommand insertCharge = new(
            """
            INSERT INTO charges (provider_reference, reference, amount_pence, currency, scenario, status, failure_reason, sequence, outcome_at)
            VALUES (@provider_reference, @reference, @amount_pence, @currency, @scenario, @status, @failure_reason, @sequence, @outcome_at)
            """, connection, transaction))
        {
            insertCharge.Parameters.AddWithValue("provider_reference", charge.ProviderReference);
            insertCharge.Parameters.AddWithValue("reference", charge.Reference);
            insertCharge.Parameters.AddWithValue("amount_pence", charge.AmountPence);
            insertCharge.Parameters.AddWithValue("currency", charge.Currency);
            insertCharge.Parameters.AddWithValue("scenario", charge.Scenario);
            insertCharge.Parameters.AddWithValue("status", charge.Status);
            insertCharge.Parameters.AddWithValue("failure_reason", (object?)charge.FailureReason ?? DBNull.Value);
            insertCharge.Parameters.AddWithValue("sequence", charge.Sequence);
            insertCharge.Parameters.AddWithValue("outcome_at", charge.OutcomeAt);
            await insertCharge.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (NewDelivery delivery in deliveries)
        {
            await using NpgsqlCommand insertDelivery = new(
                """
                INSERT INTO callback_deliveries (event_id, provider_reference, payload, status, next_attempt_at)
                VALUES (@event_id, @provider_reference, @payload, 'pending', @next_attempt_at)
                """, connection, transaction);
            insertDelivery.Parameters.AddWithValue("event_id", delivery.EventId);
            insertDelivery.Parameters.AddWithValue("provider_reference", charge.ProviderReference);
            insertDelivery.Parameters.AddWithValue("payload", delivery.Payload);
            insertDelivery.Parameters.AddWithValue("next_attempt_at", delivery.NextAttemptAt);
            await insertDelivery.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Charge?> GetAsync(string providerReference, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = new(
            """
            SELECT provider_reference, reference, amount_pence, currency, scenario, status, failure_reason, sequence, outcome_at, created_at
            FROM charges WHERE provider_reference = @provider_reference
            """, connection);
        command.Parameters.AddWithValue("provider_reference", providerReference);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new Charge(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetInt64(7),
            reader.GetDateTime(8),
            reader.GetDateTime(9));
    }

    public async Task<IReadOnlyList<Delivery>> GetDeliveriesAsync(string providerReference, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = new(
            """
            SELECT id, event_id, provider_reference, payload, status, attempts, next_attempt_at, last_error
            FROM callback_deliveries WHERE provider_reference = @provider_reference ORDER BY id
            """, connection);
        command.Parameters.AddWithValue("provider_reference", providerReference);
        return await ReadDeliveriesAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<Delivery>> GetDueDeliveriesAsync(DateTime now, int limit, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = new(
            """
            SELECT id, event_id, provider_reference, payload, status, attempts, next_attempt_at, last_error
            FROM callback_deliveries
            WHERE status = 'pending' AND next_attempt_at <= @now
            ORDER BY next_attempt_at, id
            LIMIT @limit
            """, connection);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("limit", limit);
        return await ReadDeliveriesAsync(command, cancellationToken);
    }

    public async Task MarkDeliveredAsync(long deliveryId, int attempts, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = new(
            """
            UPDATE callback_deliveries
            SET status = 'delivered', attempts = @attempts, last_error = NULL, updated_at = NOW()
            WHERE id = @id
            """, connection);
        command.Parameters.AddWithValue("id", deliveryId);
        command.Parameters.AddWithValue("attempts", attempts);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordFailedAttemptAsync(long deliveryId, int attempts, DateTime? nextAttemptAt, string error, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = new(
            """
            UPDATE callback_deliveries
            SET status = CASE WHEN @next_attempt_at IS NULL THEN 'gave_up' ELSE 'pending' END,
                attempts = @attempts,
                next_attempt_at = COALESCE(@next_attempt_at, next_attempt_at),
                last_error = @last_error,
                updated_at = NOW()
            WHERE id = @id
            """, connection);
        command.Parameters.AddWithValue("id", deliveryId);
        command.Parameters.AddWithValue("attempts", attempts);
        command.Parameters.Add(new NpgsqlParameter<DateTime?>("next_attempt_at", NpgsqlTypes.NpgsqlDbType.TimestampTz) { TypedValue = nextAttemptAt });
        command.Parameters.AddWithValue("last_error", error);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<Delivery>> ReadDeliveriesAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        List<Delivery> deliveries = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            deliveries.Add(new Delivery(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetDateTime(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }
        return deliveries;
    }
}
