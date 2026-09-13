using System.Text.Json.Serialization;

namespace ChatGptTimezoneLauncher;

public enum TimeZoneMode { Auto, Manual }

public sealed class LauncherConfig
{
    public int SchemaVersion { get; set; } = 1;
    public bool TimeZoneOverrideEnabled { get; set; }
    public TimeZoneMode Mode { get; set; } = TimeZoneMode.Auto;
    public string ManualTimeZone { get; set; } = "Asia/Shanghai";
    public GeoLocation? LastSuccessfulAutoDetection { get; set; }
    public bool CloseToTrayOnClose { get; set; } = true;
    public bool UsageResetReminderEnabled { get; set; } = true;
}

public sealed record GeoLocation(string Ip, string CountryCode, string CountryName,
    string? City, string TimeZone, DateTimeOffset DetectedAt, string Provider,
    string DetectionMethod = "", string? ProxyGroup = null, string? ProxyNode = null)
{
    public string LocationText => string.IsNullOrWhiteSpace(City)
        ? $"{CountryName} ({CountryCode})"
        : $"{City}, {CountryCode}";
}

public sealed record DetectionResult(bool Success, GeoLocation? Location, string Message)
{
    public static DetectionResult Ok(GeoLocation value) => new(true, value, "检测成功");
    public static DetectionResult Fail(string message) => new(false, null, message);
}

public sealed record ChatGptInstallation(string PackageName, string PackageFullName,
    string PackageFamilyName, Version Version, string InstallLocation,
    string AppId, string ExecutablePath, string? Parameters)
{
    public string Aumid => $"{PackageFamilyName}!{AppId}";
}

public sealed record DiscoveryResult(ChatGptInstallation? Installation, string Diagnostics)
{
    public bool Found => Installation is not null;
}

public sealed record LaunchResult(bool Success, bool WasAlreadyRunning, string Message);

public sealed record UsageWindow(int UsedPercent, long WindowSeconds, long ResetAfterSeconds,
    DateTimeOffset ResetAtUtc)
{
    public int RemainingPercent => Math.Clamp(100 - UsedPercent, 0, 100);
    public DateTimeOffset ResetAtBeijing => ResetAtUtc.ToOffset(TimeSpan.FromHours(8));
}

public sealed record UsageSnapshot(string PlanType, UsageWindow Primary, UsageWindow Secondary,
    DateTimeOffset FetchedAtUtc)
{
    public DateTimeOffset FetchedAtBeijing => FetchedAtUtc.ToOffset(TimeSpan.FromHours(8));
}

internal sealed class UsageResponse
{
    [JsonPropertyName("plan_type")]
    public string? PlanType { get; set; }

    [JsonPropertyName("rate_limit")]
    public RateLimitResponse? RateLimit { get; set; }
}

internal sealed class RateLimitResponse
{
    [JsonPropertyName("primary_window")]
    public UsageWindowResponse? PrimaryWindow { get; set; }

    [JsonPropertyName("secondary_window")]
    public UsageWindowResponse? SecondaryWindow { get; set; }
}

internal sealed class UsageWindowResponse
{
    [JsonPropertyName("used_percent")]
    public int UsedPercent { get; set; }

    [JsonPropertyName("limit_window_seconds")]
    public long WindowSeconds { get; set; }

    [JsonPropertyName("reset_after_seconds")]
    public long ResetAfterSeconds { get; set; }

    [JsonPropertyName("reset_at")]
    public long ResetAt { get; set; }
}
