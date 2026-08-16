$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "..\MarshallWake.Service\MarshallWake.Service.csproj"
$installDir = Join-Path $env:ProgramFiles "MarshallWake"
$serviceName = "MarshallWake"

if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an Administrator PowerShell session."
}

if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $serviceName -ErrorAction SilentlyContinue
    & sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 1
}

dotnet publish $project -c Release -r win-x64 --self-contained true -o $installDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$executable = Join-Path $installDir "MarshallWake.Service.exe"
& sc.exe create $serviceName binPath= "`"$executable`"" start= auto DisplayName= "Marshall Wake" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Failed to create the Windows service." }

& sc.exe description $serviceName "Keeps nearby Marshall speakers awake over Bluetooth LE." | Out-Null
Start-Service -Name $serviceName
Write-Host "Marshall Wake is running at http://127.0.0.1:5050"
