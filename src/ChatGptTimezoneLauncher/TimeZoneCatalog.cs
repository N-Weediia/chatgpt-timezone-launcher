using NodaTime;

namespace ChatGptTimezoneLauncher;

public static class TimeZoneCatalog
{
    public static IReadOnlyList<string> All { get; } = DateTimeZoneProviders.Tzdb.Ids
        .Where(id => !id.StartsWith("Etc/", StringComparison.Ordinal) &&
                     !id.StartsWith("SystemV/", StringComparison.Ordinal) &&
                     id is not "UTC" and not "GMT")
        .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static bool IsValid(string? id) => !string.IsNullOrWhiteSpace(id) &&
        DateTimeZoneProviders.Tzdb.GetZoneOrNull(id.Trim()) is not null;
}
