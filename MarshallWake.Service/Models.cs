namespace MarshallWake.Service;

public sealed record MarshallDevice(
    string Id,
    string Name,
    bool IsPaired,
    DateTimeOffset LastSeenAt);

public sealed record WakeAttempt(
    long Id,
    string DeviceId,
    string DeviceName,
    DateTimeOffset AttemptedAt,
    bool Succeeded,
    string Trigger,
    string Message,
    long DurationMilliseconds);

public sealed record HistoryPage(
    IReadOnlyList<WakeAttempt> Items,
    int Page,
    int PageSize,
    long Total);

public sealed record BluetoothWakeResult(bool Succeeded, string Message);
