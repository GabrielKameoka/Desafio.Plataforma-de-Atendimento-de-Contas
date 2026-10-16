using Npgsql;

public sealed class CrmAvailability
{
    private volatile bool available = true;
    public bool IsAvailable
    {
        get => available;
        set => available = value;
    }
}

public sealed record CrmEvent(Guid EventId, string CorrelationId, string Protocol, Guid ClientId,
    string EventType, long RequestVersion, string Status, Guid AssigneeId, DateTimeOffset OccurredAtUtc)
{
    public bool IsValid() => EventId != Guid.Empty && ClientId != Guid.Empty && AssigneeId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(CorrelationId) && CorrelationId.Length <= 100 &&
        !string.IsNullOrWhiteSpace(Protocol) && Protocol.Length <= 40 &&
        EventType is "RequestOpened" or "RequestStatusChanged" or "RequestAssigneeChanged" &&
        Status is "Open" or "UnderReview" or "WaitingForCustomer" or "Resolved" &&
        RequestVersion > 0 && OccurredAtUtc.Offset == TimeSpan.Zero;
}

public sealed record CrmProjection(string Protocol, Guid ClientId, long RequestVersion,
    string Status, Guid AssigneeId, Guid LastEventId, DateTimeOffset UpdatedAtUtc);

public sealed class CrmStore(NpgsqlDataSource dataSource)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE SCHEMA IF NOT EXISTS crm_simulator;
            CREATE TABLE IF NOT EXISTS crm_simulator.events (
                event_id uuid PRIMARY KEY,
                correlation_id varchar(100) NOT NULL,
                protocol varchar(40) NOT NULL,
                request_version bigint NOT NULL,
                received_at_utc timestamptz NOT NULL
            );
            CREATE TABLE IF NOT EXISTS crm_simulator.cases (
                protocol varchar(40) PRIMARY KEY,
                client_id uuid NOT NULL,
                request_version bigint NOT NULL,
                status varchar(30) NOT NULL,
                assignee_id uuid NOT NULL,
                last_event_id uuid NOT NULL,
                updated_at_utc timestamptz NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> AcceptAsync(CrmEvent message, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO crm_simulator.events
                    (event_id, correlation_id, protocol, request_version, received_at_utc)
                VALUES (@event_id, @correlation_id, @protocol, @request_version, @received_at)
                ON CONFLICT (event_id) DO NOTHING RETURNING event_id
                """;
            insert.Parameters.AddWithValue("event_id", message.EventId);
            insert.Parameters.AddWithValue("correlation_id", message.CorrelationId);
            insert.Parameters.AddWithValue("protocol", message.Protocol);
            insert.Parameters.AddWithValue("request_version", message.RequestVersion);
            insert.Parameters.AddWithValue("received_at", DateTimeOffset.UtcNow);
            if (await insert.ExecuteScalarAsync(cancellationToken) is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return true;
            }
        }

        await using (var upsert = connection.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO crm_simulator.cases
                    (protocol, client_id, request_version, status, assignee_id, last_event_id, updated_at_utc)
                VALUES (@protocol, @client_id, @request_version, @status, @assignee_id, @event_id, @updated_at)
                ON CONFLICT (protocol) DO UPDATE SET
                    client_id = EXCLUDED.client_id,
                    request_version = EXCLUDED.request_version,
                    status = EXCLUDED.status,
                    assignee_id = EXCLUDED.assignee_id,
                    last_event_id = EXCLUDED.last_event_id,
                    updated_at_utc = EXCLUDED.updated_at_utc
                WHERE crm_simulator.cases.request_version < EXCLUDED.request_version
                """;
            upsert.Parameters.AddWithValue("protocol", message.Protocol);
            upsert.Parameters.AddWithValue("client_id", message.ClientId);
            upsert.Parameters.AddWithValue("request_version", message.RequestVersion);
            upsert.Parameters.AddWithValue("status", message.Status);
            upsert.Parameters.AddWithValue("assignee_id", message.AssigneeId);
            upsert.Parameters.AddWithValue("event_id", message.EventId);
            upsert.Parameters.AddWithValue("updated_at", message.OccurredAtUtc);
            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return false;
    }

    public async Task<CrmProjection?> FindAsync(string protocol, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT protocol, client_id, request_version, status, assignee_id, last_event_id, updated_at_utc
            FROM crm_simulator.cases WHERE protocol = @protocol
            """;
        command.Parameters.AddWithValue("protocol", protocol);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new CrmProjection(reader.GetString(0), reader.GetGuid(1), reader.GetInt64(2),
                reader.GetString(3), reader.GetGuid(4), reader.GetGuid(5), reader.GetFieldValue<DateTimeOffset>(6))
            : null;
    }

    public async Task<bool> HasEventAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM crm_simulator.events WHERE event_id = @event_id";
        command.Parameters.AddWithValue("event_id", eventId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }
}
