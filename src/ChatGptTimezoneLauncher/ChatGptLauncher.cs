using System.Diagnostics;

namespace ChatGptTimezoneLauncher;

public sealed class ChatGptLauncher
{
    private readonly Func<ProcessStartInfo, Process?> _startProcess;
    private readonly Func<ChatGptInstallation, string, (bool Success, string Message)> _launchWithEnvironment;

    public ChatGptLauncher(
        Func<ProcessStartInfo, Process?>? startProcess = null,
        Func<ChatGptInstallation, string, (bool Success, string Message)>? launchWithEnvironment = null)
    {
        _startProcess = startProcess ?? Process.Start;
        _launchWithEnvironment = launchWithEnvironment ?? ((installation, timeZone) =>
        {
            var success = AppxProcessEnvironment.LaunchWithEnvironment(installation, timeZone, out var message);
            return (success, message);
        });
    }

    public bool IsRunning(ChatGptInstallation installation) => FindRunning(installation).Count > 0;

    public LaunchResult Launch(ChatGptInstallation installation, string? ianaTimeZone)
    {
        var running = IsRunning(installation);
        if (running && ianaTimeZone is not null)
            return new LaunchResult(false, true, "ChatGPT 已经在运行。新的时区将在 ChatGPT 重启后生效。");

        try
        {
            ProcessStartInfo start;
            if (ianaTimeZone is null)
            {
                start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                start.ArgumentList.Add($"shell:AppsFolder\\{installation.Aumid}");
            }
            else
            {
                if (!TimeZoneCatalog.IsValid(ianaTimeZone))
                    return new LaunchResult(false, false, "时区无效，未启动 ChatGPT。");

                var injected = _launchWithEnvironment(installation, ianaTimeZone);
                return new LaunchResult(injected.Success, false, injected.Message);
            }

            _startProcess(start);
            return new LaunchResult(true, false, ianaTimeZone is null
                ? "已按 ChatGPT 默认方式启动（未注入 TZ）。"
                : $"已启动 ChatGPT，本次进程时区：{ianaTimeZone}");
        }
        catch (Exception ex)
        {
            return new LaunchResult(false, false, $"启动失败：{ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> CloseGracefullyAsync(ChatGptInstallation installation)
    {
        var processes = FindRunning(installation);
        if (processes.Count == 0) return (true, "ChatGPT 当前未运行。");
        foreach (var process in processes)
        {
            try { process.CloseMainWindow(); } catch { }
        }

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
            if (!IsRunning(installation)) return (true, "ChatGPT 已正常关闭。");
        }
        return (false, "ChatGPT 未能正常关闭。启动器不会强制结束进程，请手动退出 ChatGPT 后重试。");
    }

    private static List<Process> FindRunning(ChatGptInstallation installation)
    {
        var result = new List<Process>();
        var expectedName = Path.GetFileNameWithoutExtension(installation.ExecutablePath);
        foreach (var process in Process.GetProcessesByName(expectedName))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (path is not null && path.StartsWith(installation.InstallLocation + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase)) result.Add(process);
                else if (path is null) result.Add(process);
            }
            catch { result.Add(process); }
        }
        return result;
    }

}
