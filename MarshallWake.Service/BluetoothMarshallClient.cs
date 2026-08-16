using InTheHand.Bluetooth;
using Microsoft.Extensions.Options;

namespace MarshallWake.Service;

public sealed class BluetoothMarshallClient(
    IOptions<MarshallWakeOptions> options,
    TimeProvider timeProvider,
    ILogger<BluetoothMarshallClient> logger) : IBluetoothMarshallClient
{
    private static readonly Guid EqServiceUuid = Guid.Parse("0000aa00-0000-1000-8000-00805f9b34fb");
    private static readonly Guid EqCharacteristicUuid = Guid.Parse("0000aa16-0000-1000-8000-00805f9b34fb");
    private readonly MarshallWakeOptions _options = options.Value;

    public async Task<IReadOnlyList<MarshallDevice>> ScanAsync(CancellationToken cancellationToken)
    {
        if (!await Bluetooth.GetAvailabilityAsync())
            throw new InvalidOperationException("Bluetooth is unavailable or permission was denied.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.ScanTimeoutSeconds));

        var scanOptions = new RequestDeviceOptions
        {
            AcceptAllDevices = true,
            Timeout = TimeSpan.FromSeconds(_options.ScanTimeoutSeconds)
        };

        var devices = await Bluetooth.ScanForDevicesAsync(scanOptions, timeout.Token);
        var now = timeProvider.GetUtcNow();

        try
        {
            return devices
                .Where(device => IsMarshall(device.Name))
                .GroupBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var device = group.First();
                    return new MarshallDevice(device.Id, device.Name, device.IsPaired, now);
                })
                .OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            foreach (var device in devices)
                device.Dispose();
        }
    }

    public async Task<BluetoothWakeResult> WakeAsync(
        string deviceId,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.OperationTimeoutSeconds));

        var device = await BluetoothDevice.FromIdAsync(deviceId)
            ?? throw new InvalidOperationException("The Bluetooth device is no longer available.");

        using (device)
        {
            try
            {
                logger.LogInformation("Connecting to {DeviceName} ({DeviceId})", device.Name, device.Id);
                await device.Gatt.ConnectAsync().WaitAsync(timeout.Token);
                var service = await device.Gatt.GetPrimaryServiceAsync(EqServiceUuid)
                    .WaitAsync(timeout.Token);
                var characteristic = await service.GetCharacteristicAsync(EqCharacteristicUuid)
                    .WaitAsync(timeout.Token);

                if (characteristic is null)
                    throw new InvalidOperationException("Marshall EQ characteristic was not found.");

                var value = await characteristic.ReadValueAsync().WaitAsync(timeout.Token);
                return new BluetoothWakeResult(
                    true,
                    $"Connected and read {value.Length} byte(s) from the Marshall EQ service.");
            }
            finally
            {
                if (device.Gatt.IsConnected)
                    device.Gatt.Disconnect();
            }
        }
    }

    private bool IsMarshall(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && _options.DeviceNamePrefixes.Any(prefix =>
            name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
