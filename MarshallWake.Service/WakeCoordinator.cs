using System.Diagnostics;

namespace MarshallWake.Service;

public sealed class WakeCoordinator(
    IBluetoothMarshallClient bluetooth,
    DeviceRegistry registry,
    WakeHistoryStore history,
    TimeProvider timeProvider,
    ILogger<WakeCoordinator> logger)
{
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private int _isRunning;

    public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

    public DateTimeOffset? LastScanAt { get; private set; }

    public async Task<IReadOnlyList<MarshallDevice>> RefreshDevicesAsync(
        CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken);
        Interlocked.Exchange(ref _isRunning, 1);
        try
        {
            var devices = await bluetooth.ScanAsync(cancellationToken);
            registry.Replace(devices);
            LastScanAt = timeProvider.GetUtcNow();
            logger.LogInformation("Discovered {DeviceCount} Marshall device(s)", devices.Count);
            return devices;
        }
        finally
        {
            Interlocked.Exchange(ref _isRunning, 0);
            _operationLock.Release();
        }
    }

    public async Task<IReadOnlyList<WakeAttempt>> WakeAsync(
        string? deviceId,
        string trigger,
        CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken);
        Interlocked.Exchange(ref _isRunning, 1);
        try
        {
            var devices = registry.GetAll();
            if (devices.Count == 0 || (deviceId is not null && registry.Get(deviceId) is null))
            {
                try
                {
                    var discovered = await bluetooth.ScanAsync(cancellationToken);
                    registry.Replace(discovered);
                    LastScanAt = timeProvider.GetUtcNow();
                    devices = registry.GetAll();
                }
                catch (Exception ex) when (
                    ex is not OperationCanceledException
                    || !cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Marshall device discovery failed");
                    var discoveryFailure = new WakeAttempt(
                        0,
                        deviceId ?? "",
                        "Marshall discovery",
                        timeProvider.GetUtcNow(),
                        false,
                        trigger,
                        $"Device discovery failed: {ex.Message}",
                        0);
                    return [await history.AddAsync(discoveryFailure, cancellationToken)];
                }
            }

            if (!string.IsNullOrWhiteSpace(deviceId))
                devices = devices.Where(device =>
                    string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase)).ToArray();

            if (devices.Count == 0)
            {
                var missing = new WakeAttempt(
                    0,
                    deviceId ?? "",
                    "No Marshall device",
                    timeProvider.GetUtcNow(),
                    false,
                    trigger,
                    "No matching Marshall Bluetooth device was discovered.",
                    0);
                return [await history.AddAsync(missing, cancellationToken)];
            }

            var attempts = new List<WakeAttempt>();
            foreach (var device in devices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempts.Add(await WakeDeviceAsync(device, trigger, cancellationToken));
            }

            return attempts;
        }
        finally
        {
            Interlocked.Exchange(ref _isRunning, 0);
            _operationLock.Release();
        }
    }

    private async Task<WakeAttempt> WakeDeviceAsync(
        MarshallDevice device,
        string trigger,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();
        BluetoothWakeResult result;

        try
        {
            result = await bluetooth.WakeAsync(device.Id, cancellationToken);
        }
        catch (Exception ex) when (
            ex is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Wake attempt failed for {DeviceName}", device.Name);
            result = new BluetoothWakeResult(false, ex.Message);
        }

        stopwatch.Stop();
        var attempt = new WakeAttempt(
            0,
            device.Id,
            device.Name,
            startedAt,
            result.Succeeded,
            trigger,
            result.Message,
            stopwatch.ElapsedMilliseconds);
        return await history.AddAsync(attempt, cancellationToken);
    }
}
