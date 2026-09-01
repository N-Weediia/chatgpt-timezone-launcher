using System.Reflection;

namespace ChatGptTimezoneLauncher;

public static class ShortcutService
{
    public static string CreateDesktopShortcut()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定启动器路径");
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var shortcutPath = Path.Combine(desktop, "ChatGPT 时区启动器.lnk");
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows 快捷方式服务不可用");
        var shell = Activator.CreateInstance(shellType)!;
        var shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [shortcutPath])!;
        var type = shortcut.GetType();
        type.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, [executable]);
        type.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, [Path.GetDirectoryName(executable)!]);
        type.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, ["按 ChatGPT 实际出口或手动时区启动 ChatGPT"]);
        type.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, [$"{executable},0"]);
        type.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        return shortcutPath;
    }
}
