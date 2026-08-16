# WoburnEQ + Marshall Wake

This repository contains two applications for Marshall Bluetooth speakers:

- **WoburnEQ** — the original Windows tray application for controlling Bass,
  Treble, and Volume on a Marshall Woburn III over Bluetooth Low Energy (BLE).
- **Marshall Wake** — a cross-platform local web service that discovers nearby
  Marshall speakers and creates a BLE GATT connection every 10 minutes to wake
  them from standby. It includes manual wake controls and persistent history.

Marshall Wake connects to the speaker and reads its EQ characteristic. This
produces actual BLE traffic rather than treating an advertisement as a
successful wake-up.

![WoburnEQ screenshot](2026-03-30_18-18-59.png)

## Run Marshall Wake

### Requirements

- Windows 10/11 or a Linux distribution with BlueZ and D-Bus
- .NET 10 SDK (tested with runtime 10.0.11)
- A BLE-capable Bluetooth adapter
- On Linux, `bluetoothd` must be running and the current user must be allowed to
  access the system Bluetooth D-Bus service
- Put the speaker in Bluetooth pairing mode before its first connection

Start the service from the repository root:

```powershell
dotnet run --project .\MarshallWake.Service\MarshallWake.Service.csproj
```

On Linux, use the equivalent path:

```bash
dotnet run --project ./MarshallWake.Service/MarshallWake.Service.csproj
```

Open <http://127.0.0.1:5050>. The service scans automatically after startup,
then attempts to wake every discovered Marshall device every 10 minutes. The
dashboard supports rescanning, manual wake, and filtering paginated execution
history. History is stored in:

- Windows: `%LOCALAPPDATA%\MarshallWake\marshall-wake.db`
- Linux: `~/.local/share/MarshallWake/marshall-wake.db`

Configuration lives in
`MarshallWake.Service/appsettings.json`. All values can be overridden with
standard ASP.NET Core environment variables, for example:

```powershell
$env:MarshallWake__IntervalMinutes = "5"
$env:ASPNETCORE_URLS = "http://0.0.0.0:5050"
dotnet run --project .\MarshallWake.Service\MarshallWake.Service.csproj
```

Only bind to `0.0.0.0` on a trusted home network; the service has no
authentication.

### Install as a background service

Windows (run PowerShell as Administrator):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1
```

Linux (installs a systemd user service):

```bash
chmod +x ./scripts/install.sh
./scripts/install.sh
```

The scripts create a self-contained build, install it, start it automatically,
and keep the dashboard at <http://127.0.0.1:5050>.

## Run the original WoburnEQ tray application

### Requirements

- Windows 10/11
- .NET 10 SDK (tested with runtime 10.0.11)
- Bluetooth adapter with BLE support
- Marshall Woburn III paired via Bluetooth

From the repository root:

```powershell
dotnet restore .\WoburnEQ.csproj
dotnet run --project .\WoburnEQ.csproj
```

The application appears in the Windows notification area. Left-click its tray
icon to open the EQ and volume controls; right-click and choose **Exit** to stop
it.

## Build

Build the web service:

```powershell
dotnet build .\MarshallWake.Service\MarshallWake.Service.csproj
```

Build the Windows tray application:

```powershell
dotnet build .\WoburnEQ.csproj
```

Run the Marshall Wake tests:

```powershell
dotnet test .\MarshallWake.Service.Tests\MarshallWake.Service.Tests.csproj
```

## Compatibility

The BLE protocol was tested on Marshall Woburn III firmware 5.0.34. Marshall
Wake matches device names beginning with `WOBURN` or `MARSHALL` by default.
Other Marshall models may use different GATT services and require protocol
adjustments.

## License

MIT
