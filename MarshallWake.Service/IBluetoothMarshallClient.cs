namespace MarshallWake.Service;

public interface IBluetoothMarshallClient
{
    Task<IReadOnlyList<MarshallDevice>> ScanAsync(CancellationToken cancellationToken);

    Task<BluetoothWakeResult> WakeAsync(string deviceId, CancellationToken cancellationToken);
}
