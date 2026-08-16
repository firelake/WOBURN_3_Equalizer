using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace MarshallWake.Service;

public sealed class WakeHistoryStore
{
    private readonly string _connectionString;

    public WakeHistoryStore(IOptions<MarshallWakeOptions> options)
    {
        var configuredPath = options.Value.DatabasePath;
        var databasePath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MarshallWake",
                "marshall-wake.db")
            : Path.GetFullPath(configuredPath);

        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS wake_attempts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                device_id TEXT NOT NULL,
                device_name TEXT NOT NULL,
                attempted_at TEXT NOT NULL,
                succeeded INTEGER NOT NULL,
                trigger_name TEXT NOT NULL,
                message TEXT NOT NULL,
                duration_ms INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_wake_attempts_attempted_at
                ON wake_attempts(attempted_at DESC);
            CREATE INDEX IF NOT EXISTS ix_wake_attempts_device_id
                ON wake_attempts(device_id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<WakeAttempt> AddAsync(
        WakeAttempt attempt,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO wake_attempts (
                device_id, device_name, attempted_at, succeeded,
                trigger_name, message, duration_ms)
            VALUES (
                $deviceId, $deviceName, $attemptedAt, $succeeded,
                $trigger, $message, $durationMs);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$deviceId", attempt.DeviceId);
        command.Parameters.AddWithValue("$deviceName", attempt.DeviceName);
        command.Parameters.AddWithValue("$attemptedAt", attempt.AttemptedAt.ToString("O"));
        command.Parameters.AddWithValue("$succeeded", attempt.Succeeded);
        command.Parameters.AddWithValue("$trigger", attempt.Trigger);
        command.Parameters.AddWithValue("$message", attempt.Message);
        command.Parameters.AddWithValue("$durationMs", attempt.DurationMilliseconds);

        var id = (long)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Failed to save wake history."));
        return attempt with { Id = id };
    }

    public async Task<WakeAttempt?> GetLatestAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, device_id, device_name, attempted_at, succeeded,
                   trigger_name, message, duration_ms
            FROM wake_attempts
            ORDER BY attempted_at DESC
            LIMIT 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAttempt(reader) : null;
    }

    public async Task<HistoryPage> QueryAsync(
        string? deviceId,
        bool? succeeded,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var where = new List<string>();

        var count = connection.CreateCommand();
        var query = connection.CreateCommand();

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            where.Add("device_id = $deviceId");
            count.Parameters.AddWithValue("$deviceId", deviceId);
            query.Parameters.AddWithValue("$deviceId", deviceId);
        }

        if (succeeded.HasValue)
        {
            where.Add("succeeded = $succeeded");
            count.Parameters.AddWithValue("$succeeded", succeeded.Value);
            query.Parameters.AddWithValue("$succeeded", succeeded.Value);
        }

        var whereClause = where.Count == 0 ? "" : $" WHERE {string.Join(" AND ", where)}";
        count.CommandText = $"SELECT COUNT(*) FROM wake_attempts{whereClause};";
        var total = (long)(await count.ExecuteScalarAsync(cancellationToken) ?? 0L);

        query.CommandText =
            $"""
            SELECT id, device_id, device_name, attempted_at, succeeded,
                   trigger_name, message, duration_ms
            FROM wake_attempts
            {whereClause}
            ORDER BY attempted_at DESC
            LIMIT $pageSize OFFSET $offset;
            """;
        query.Parameters.AddWithValue("$pageSize", pageSize);
        query.Parameters.AddWithValue("$offset", (page - 1) * pageSize);

        var attempts = new List<WakeAttempt>();
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            attempts.Add(ReadAttempt(reader));

        return new HistoryPage(attempts, page, pageSize, total);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static WakeAttempt ReadAttempt(SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            DateTimeOffset.Parse(reader.GetString(3)),
            reader.GetBoolean(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetInt64(7));
}
