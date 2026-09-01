using System.Text.Json;

namespace ChatGptTimezoneLauncher;

public sealed class ConfigStore
{
    private readonly string _directory;
    public string ConfigPath => Path.Combine(_directory, "settings.json");
    public string BackupPath => Path.Combine(_directory, "settings.json.bak");

    public ConfigStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChatGPTTimezoneLauncher");
    }

    public (LauncherConfig Config, string? Warning) Load()
    {
        if (!File.Exists(ConfigPath)) return (new LauncherConfig(), null);
        try
        {
            var config = JsonSerializer.Deserialize<LauncherConfig>(File.ReadAllText(ConfigPath), JsonOptions());
            if (config is null || config.SchemaVersion != 1) throw new InvalidDataException("配置版本无效");
            Sanitize(config);
            return (config, null);
        }
        catch (Exception ex)
        {
            return (new LauncherConfig(), $"配置文件损坏，已安全使用默认设置。原文件未删除。\r\n{ex.Message}");
        }
    }

    public void Save(LauncherConfig config)
    {
        Sanitize(config);
        Directory.CreateDirectory(_directory);
        var tempPath = ConfigPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(config, JsonOptions()));
        using (var stream = File.OpenRead(tempPath))
            _ = JsonSerializer.Deserialize<LauncherConfig>(stream) ?? throw new InvalidDataException("新配置验证失败");

        if (File.Exists(ConfigPath))
            File.Replace(tempPath, ConfigPath, BackupPath, true);
        else
            File.Move(tempPath, ConfigPath);
    }

    private static void Sanitize(LauncherConfig config)
    {
        if (!Enum.IsDefined(config.Mode)) config.Mode = TimeZoneMode.Auto;
        if (!TimeZoneCatalog.IsValid(config.ManualTimeZone)) config.ManualTimeZone = "Asia/Shanghai";
        if (config.LastSuccessfulAutoDetection is { } last &&
            (!TimeZoneCatalog.IsValid(last.TimeZone) || string.IsNullOrWhiteSpace(last.DetectionMethod)))
            config.LastSuccessfulAutoDetection = null; // 丢弃旧版“GeoIP 服务自身出口”缓存。
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}
