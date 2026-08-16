using System.ComponentModel.DataAnnotations;

namespace MarshallWake.Service;

public sealed class MarshallWakeOptions
{
    public const string SectionName = "MarshallWake";

    public bool Enabled { get; init; } = true;

    [Range(1, 1440)]
    public int IntervalMinutes { get; init; } = 10;

    [Range(0, 3600)]
    public int InitialDelaySeconds { get; init; } = 5;

    [Range(1, 120)]
    public int ScanTimeoutSeconds { get; init; } = 12;

    [Range(1, 120)]
    public int OperationTimeoutSeconds { get; init; } = 30;

    [MinLength(1)]
    public string[] DeviceNamePrefixes { get; init; } = ["WOBURN", "MARSHALL"];

    public string DatabasePath { get; init; } = "";
}
