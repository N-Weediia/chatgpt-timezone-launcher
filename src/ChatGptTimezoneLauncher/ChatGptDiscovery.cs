using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatGptTimezoneLauncher;

public sealed class ChatGptDiscovery
{
    private const string DiscoveryScript = """
        $ErrorActionPreference = 'Stop'
        $startApps = @(Get-StartApps | Where-Object { $_.Name -match '(?i)ChatGPT' })
        $familyIds = @($startApps | ForEach-Object { ($_.AppID -split '!')[0] } | Where-Object { $_ })
        $packages = @(Get-AppxPackage | Where-Object {
            $familyIds -contains $_.PackageFamilyName -or
            $_.Name -match '(?i)ChatGPT|OpenAI|Codex' -or
            $_.PackageFullName -match '(?i)ChatGPT|OpenAI|Codex'
        })
        $result = foreach ($package in $packages) {
            try {
                $manifest = $package | Get-AppxPackageManifest
                foreach ($app in @($manifest.Package.Applications.Application)) {
                    $aumid = $package.PackageFamilyName + '!' + $app.Id
                    $startName = ($startApps | Where-Object { $_.AppID -eq $aumid } | Select-Object -First 1).Name
                    [pscustomobject]@{
                        PackageName = $package.Name
                        PackageFullName = $package.PackageFullName
                        PackageFamilyName = $package.PackageFamilyName
                        Version = $package.Version.ToString()
                        InstallLocation = $package.InstallLocation
                        AppId = [string]$app.Id
                        Executable = [string]$app.Executable
                        Parameters = [string]$app.Parameters
                        EntryPoint = [string]$app.EntryPoint
                        StartName = [string]$startName
                    }
                }
            } catch { }
        }
        @($result) | ConvertTo-Json -Compress -Depth 4
        """;

    private readonly Func<Task<string>> _query;

    public ChatGptDiscovery(Func<Task<string>>? query = null) => _query = query ?? RunPowerShellAsync;

    public async Task<DiscoveryResult> DiscoverAsync()
    {
        try
        {
            var json = await _query();
            var rows = DeserializeRows(json);
            var diagnostics = new List<string> { $"候选入口：{rows.Count}" };

            var candidates = rows.Select(row => ToCandidate(row, diagnostics))
                .Where(x => x.Installation is not null)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Installation!.Version)
                .ToList();

            if (candidates.Count == 0)
                return new DiscoveryResult(null, string.Join(Environment.NewLine, diagnostics.Append(
                    "未发现名称、开始菜单入口或清单与 ChatGPT 匹配的当前用户 AppX/MSIX 包。")));

            var best = candidates[0].Installation!;
            diagnostics.Add($"选中：{best.PackageFullName}");
            diagnostics.Add($"入口：{best.ExecutablePath}");
            return new DiscoveryResult(best, string.Join(Environment.NewLine, diagnostics));
        }
        catch (Exception ex)
        {
            return new DiscoveryResult(null, $"包查询失败：{ex.Message}");
        }
    }

    public static IReadOnlyList<PackageRow> DeserializeRows(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<PackageRow>();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind switch
        {
            JsonValueKind.Array => JsonSerializer.Deserialize<List<PackageRow>>(json, JsonOptions()) ?? [],
            JsonValueKind.Object => [JsonSerializer.Deserialize<PackageRow>(json, JsonOptions())!],
            _ => []
        };
    }

    private static (ChatGptInstallation? Installation, int Score) ToCandidate(PackageRow row, List<string> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(row.InstallLocation) || string.IsNullOrWhiteSpace(row.Executable) ||
            string.IsNullOrWhiteSpace(row.PackageFamilyName) || string.IsNullOrWhiteSpace(row.AppId))
            return (null, 0);

        var relative = row.Executable.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(row.InstallLocation);
        var executablePath = Path.GetFullPath(Path.Combine(root, relative));
        if (!executablePath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add($"忽略越界入口：{row.Executable}");
            return (null, 0);
        }

        var text = string.Join(" ", row.PackageName, row.PackageFullName, row.StartName,
            row.Executable, row.EntryPoint);
        var score = 0;
        if (row.StartName.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase)) score += 100;
        if (row.PackageName.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase)) score += 60;
        if (Path.GetFileName(row.Executable).Contains("ChatGPT", StringComparison.OrdinalIgnoreCase)) score += 40;
        if (text.Contains("OpenAI", StringComparison.OrdinalIgnoreCase)) score += 15;
        if (score < 40) return (null, 0);

        if (!Version.TryParse(row.Version, out var version)) version = new Version(0, 0);
        return (new ChatGptInstallation(row.PackageName, row.PackageFullName, row.PackageFamilyName,
            version, root, row.AppId, executablePath, NullIfWhiteSpace(row.Parameters)), score);
    }

    private static async Task<string> RunPowerShellAsync()
    {
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(DiscoveryScript));
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(encoded);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 Windows PowerShell");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException((await stderr).Trim());
        return (await stdout).Trim();
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
    private static string? NullIfWhiteSpace(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    public sealed class PackageRow
    {
        public string PackageName { get; set; } = "";
        public string PackageFullName { get; set; } = "";
        public string PackageFamilyName { get; set; } = "";
        public string Version { get; set; } = "0.0";
        public string InstallLocation { get; set; } = "";
        public string AppId { get; set; } = "";
        public string Executable { get; set; } = "";
        public string Parameters { get; set; } = "";
        public string EntryPoint { get; set; } = "";
        public string StartName { get; set; } = "";
    }
}
