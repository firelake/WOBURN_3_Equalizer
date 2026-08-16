using MarshallWake.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MarshallWake.Service.Tests;

public sealed class WakeCoordinatorTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "marshall-wake-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WakeAsync_DiscoversWakesAndPersistsResult()
    {
        var device = new MarshallDevice(
            "AA:BB:CC:DD:EE:FF",
            "WOBURN III",
            true,
            DateTimeOffset.UtcNow);
        var bluetooth = new FakeBluetoothClient(
            [device],
            new BluetoothWakeResult(true, "awake"));
        var (coordinator, history) = await CreateCoordinatorAsync(bluetooth);

        var attempts = await coordinator.WakeAsync(null, "scheduled", CancellationToken.None);
        var page = await history.QueryAsync(null, null, 1, 25, CancellationToken.None);

        var attempt = Assert.Single(attempts);
        Assert.True(attempt.Succeeded);
        Assert.Equal("scheduled", attempt.Trigger);
        Assert.Equal(device.Id, Assert.Single(bluetooth.WokenDeviceIds));
        Assert.Equal(attempt, Assert.Single(page.Items));
    }

    [Fact]
    public async Task WakeAsync_RecordsFailureWhenNoDeviceIsDiscovered()
    {
        var bluetooth = new FakeBluetoothClient([], new BluetoothWakeResult(true, "unused"));
        var (coordinator, history) = await CreateCoordinatorAsync(bluetooth);

        var attempts = await coordinator.WakeAsync(null, "manual", CancellationToken.None);
        var latest = await history.GetLatestAsync(CancellationToken.None);

        var attempt = Assert.Single(attempts);
        Assert.False(attempt.Succeeded);
        Assert.Contains("No matching Marshall", attempt.Message);
        Assert.Equal(attempt, latest);
    }

    [Fact]
    public async Task HistoryQuery_FiltersByDeviceAndOutcome()
    {
        var (_, history) = await CreateCoordinatorAsync(
            new FakeBluetoothClient([], new BluetoothWakeResult(true, "unused")));
        await history.AddAsync(CreateAttempt("one", true), CancellationToken.None);
        await history.AddAsync(CreateAttempt("one", false), CancellationToken.None);
        await history.AddAsync(CreateAttempt("two", true), CancellationToken.None);

        var page = await history.QueryAsync("one", false, 1, 10, CancellationToken.None);

        var attempt = Assert.Single(page.Items);
        Assert.Equal("one", attempt.DeviceId);
        Assert.False(attempt.Succeeded);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task WakeAsync_RecordsDiscoveryErrors()
    {
        var bluetooth = new FakeBluetoothClient(
            [],
            new BluetoothWakeResult(true, "unused"))
        {
            ScanException = new InvalidOperationException("Bluetooth unavailable")
        };
        var (coordinator, history) = await CreateCoordinatorAsync(bluetooth);

        var attempts = await coordinator.WakeAsync(null, "scheduled", CancellationToken.None);
        var latest = await history.GetLatestAsync(CancellationToken.None);

        var attempt = Assert.Single(attempts);
        Assert.False(attempt.Succeeded);
        Assert.Contains("Bluetooth unavailable", attempt.Message);
        Assert.Equal(attempt, latest);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    private async Task<(WakeCoordinator Coordinator, WakeHistoryStore History)>
        CreateCoordinatorAsync(IBluetoothMarshallClient bluetooth)
    {
        var options = Options.Create(new MarshallWakeOptions
        {
            DatabasePath = Path.Combine(_directory, "history.db")
        });
        var history = new WakeHistoryStore(options);
        await history.InitializeAsync();
        var coordinator = new WakeCoordinator(
            bluetooth,
            new DeviceRegistry(),
            history,
            TimeProvider.System,
            NullLogger<WakeCoordinator>.Instance);
        return (coordinator, history);
    }

    private static WakeAttempt CreateAttempt(string deviceId, bool succeeded) =>
        new(
            0,
            deviceId,
            $"Device {deviceId}",
            DateTimeOffset.UtcNow,
            succeeded,
            "test",
            succeeded ? "ok" : "failed",
            10);

    private sealed class FakeBluetoothClient(
        IReadOnlyList<MarshallDevice> devices,
        BluetoothWakeResult wakeResult) : IBluetoothMarshallClient
    {
        public List<string> WokenDeviceIds { get; } = [];

        public Exception? ScanException { get; init; }

        public Task<IReadOnlyList<MarshallDevice>> ScanAsync(
            CancellationToken cancellationToken)
        {
            if (ScanException is not null)
                throw ScanException;
            return Task.FromResult(devices);
        }

        public Task<BluetoothWakeResult> WakeAsync(
            string deviceId,
            CancellationToken cancellationToken)
        {
            WokenDeviceIds.Add(deviceId);
            return Task.FromResult(wakeResult);
        }
    }
}
