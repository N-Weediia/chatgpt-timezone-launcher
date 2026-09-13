using System.Net.Http.Headers;
using System.Text.Json;

namespace ChatGptTimezoneLauncher;

public sealed class UsageService : IDisposable
{
    private const string UsageEndpoint = "https://chatgpt.com/backend-api/wham/usage";
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly string[] _authCandidates;

    public UsageService(HttpClient? httpClient = null, string? authPath = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _ownsClient = httpClient is null;
        _authCandidates = authPath is null ? FindAuthCandidates() : [authPath];
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        var authPath = _authCandidates.FirstOrDefault(File.Exists);
        if (authPath is null)
            throw new InvalidOperationException("未找到 Codex 登录会话（未找到 .codex\\auth.json）。");

        string accessToken;
        try
        {
            using var auth = JsonDocument.Parse(await File.ReadAllTextAsync(authPath, cancellationToken));
            accessToken = auth.RootElement.GetProperty("tokens").GetProperty("access_token").GetString() ?? "";
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IOException)
        {
            throw new InvalidOperationException("无法读取 Codex 登录会话，请重新登录 Codex。", ex);
        }

        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Codex 登录会话中没有可用的访问令牌，请重新登录 Codex。");

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.UserAgent.ParseAdd("ChatGPT-TimeZone-Launcher/1.2.0");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"官方限额接口返回 HTTP {(int)response.StatusCode}。请确认 Codex 登录状态。");

        return ParseResponse(body, DateTimeOffset.UtcNow);
    }

    public static UsageSnapshot ParseResponse(string json, DateTimeOffset? fetchedAtUtc = null)
    {
        var response = JsonSerializer.Deserialize<UsageResponse>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        var rate = response?.RateLimit;
        if (rate?.PrimaryWindow is null || rate.SecondaryWindow is null)
            throw new InvalidDataException("官方限额接口缺少 5 小时或每周窗口数据。");

        return new UsageSnapshot(
            string.IsNullOrWhiteSpace(response?.PlanType) ? "未知" : response.PlanType!,
            ToWindow(rate.PrimaryWindow), ToWindow(rate.SecondaryWindow),
            fetchedAtUtc ?? DateTimeOffset.UtcNow);
    }

    private static UsageWindow ToWindow(UsageWindowResponse value)
    {
        if (value.ResetAt <= 0 || value.WindowSeconds <= 0)
            throw new InvalidDataException("官方限额接口返回了无效的重置时间。");
        return new UsageWindow(Math.Clamp(value.UsedPercent, 0, 100), value.WindowSeconds,
            Math.Max(0, value.ResetAfterSeconds), DateTimeOffset.FromUnixTimeSeconds(value.ResetAt));
    }

    private static string[] FindAuthCandidates()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return
        [
            Path.Combine(profile, ".codex", "auth.json"),
            Path.Combine(localAppData, "OpenAI", "Codex", "auth.json")
        ];
    }

    public void Dispose()
    {
        if (_ownsClient) _httpClient.Dispose();
    }
}
