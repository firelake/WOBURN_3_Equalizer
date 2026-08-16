using MarshallWake.Service;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
    builder.WebHost.UseUrls("http://127.0.0.1:5050");

builder.Host.UseWindowsService(options => options.ServiceName = "Marshall Wake");
builder.Host.UseSystemd();

builder.Services
    .AddOptions<MarshallWakeOptions>()
    .Bind(builder.Configuration.GetSection(MarshallWakeOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<IBluetoothMarshallClient, BluetoothMarshallClient>();
builder.Services.AddSingleton<DeviceRegistry>();
builder.Services.AddSingleton<WakeHistoryStore>();
builder.Services.AddSingleton<WakeCoordinator>();
builder.Services.AddHostedService<WakeScheduler>();

var app = builder.Build();

await app.Services.GetRequiredService<WakeHistoryStore>().InitializeAsync();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/status", async (
    WakeCoordinator coordinator,
    WakeHistoryStore history,
    IOptions<MarshallWakeOptions> options,
    CancellationToken cancellationToken) =>
{
    var latest = await history.GetLatestAsync(cancellationToken);
    return Results.Ok(new
    {
        enabled = options.Value.Enabled,
        intervalMinutes = options.Value.IntervalMinutes,
        coordinator.IsRunning,
        coordinator.LastScanAt,
        latest
    });
});

app.MapGet("/api/devices", (DeviceRegistry registry) => Results.Ok(registry.GetAll()));

app.MapPost("/api/devices/refresh", async (
    WakeCoordinator coordinator,
    CancellationToken cancellationToken) =>
{
    var devices = await coordinator.RefreshDevicesAsync(cancellationToken);
    return Results.Ok(devices);
});

app.MapPost("/api/wake", async (
    WakeRequest request,
    WakeCoordinator coordinator,
    CancellationToken cancellationToken) =>
{
    var attempts = await coordinator.WakeAsync(request.DeviceId, "manual", cancellationToken);
    return Results.Ok(attempts);
});

app.MapGet("/api/history", async (
    string? deviceId,
    bool? succeeded,
    int? page,
    int? pageSize,
    WakeHistoryStore history,
    CancellationToken cancellationToken) =>
{
    var result = await history.QueryAsync(
        deviceId,
        succeeded,
        Math.Max(page ?? 1, 1),
        Math.Clamp(pageSize ?? 25, 1, 100),
        cancellationToken);
    return Results.Ok(result);
});

app.MapFallbackToFile("index.html");

await app.RunAsync();

public sealed record WakeRequest(string? DeviceId);
