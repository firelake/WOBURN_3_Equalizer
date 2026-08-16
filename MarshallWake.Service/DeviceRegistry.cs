using System.Collections.Concurrent;

namespace MarshallWake.Service;

public sealed class DeviceRegistry
{
    private readonly ConcurrentDictionary<string, MarshallDevice> _devices =
        new(StringComparer.OrdinalIgnoreCase);

    public void Replace(IEnumerable<MarshallDevice> devices)
    {
        foreach (var device in devices)
            _devices[device.Id] = device;
    }

    public IReadOnlyList<MarshallDevice> GetAll() =>
        _devices.Values
            .OrderByDescending(device => device.LastSeenAt)
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public MarshallDevice? Get(string id) =>
        _devices.TryGetValue(id, out var device) ? device : null;
}
