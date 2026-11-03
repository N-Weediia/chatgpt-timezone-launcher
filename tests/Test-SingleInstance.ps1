param([Parameter(Mandatory)][string]$ExePath, [Parameter(Mandatory)][string]$TestRoot)
$ErrorActionPreference = 'Stop'

if (-not ('SingleInstanceTestWindows' -as [type])) { Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class SingleInstanceTestWindows
{
    private delegate bool EnumProc(IntPtr window, IntPtr param);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr param);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr param);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private static string Title(IntPtr window)
    {
        var text = new StringBuilder(256);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    public static IntPtr FindWindow(int processId)
    {
        var result = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == processId && Title(window) == "ChatGPT 时区启动器")
            {
                result = window;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static IntPtr FindControl(IntPtr parent, string title)
    {
        var result = IntPtr.Zero;
        EnumChildWindows(parent, (window, _) =>
        {
            if (Title(window) == title)
            {
                result = window;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
'@
}

$directory = Join-Path $TestRoot 'single-instance'
New-Item -ItemType Directory -Path $directory | Out-Null
$secondExe = Join-Path $TestRoot 'another-launcher-copy.exe'
Copy-Item -LiteralPath $ExePath -Destination $secondExe
$processes = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()

function Start-TestLauncher([string]$executable) {
    $start = [Diagnostics.ProcessStartInfo]::new($executable)
    $start.UseShellExecute = $false
    $start.WorkingDirectory = $directory
    $start.Arguments = '--self-test "' + $directory + '" --single-instance'
    $start.EnvironmentVariables['DOTNET_ROOT'] = Join-Path $TestRoot 'no-installed-runtime'
    $start.EnvironmentVariables['DOTNET_ROOT_X64'] = Join-Path $TestRoot 'no-installed-runtime'
    $start.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    $start.EnvironmentVariables['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = Join-Path $TestRoot 'extract'
    $process = [Diagnostics.Process]::Start($start)
    $processes.Add($process)
    return $process
}

function Wait-Until([scriptblock]$condition, [string]$message, [int]$seconds = 30) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (& $condition) { return }
        Start-Sleep -Milliseconds 100
    }
    throw "$message; artifacts: $TestRoot"
}

function Start-Repeat([string]$executable) {
    $repeat = Start-TestLauncher $executable
    if (-not $repeat.WaitForExit(30000)) { throw 'Repeated launcher did not exit.' }
    if ($repeat.ExitCode -ne 0) { throw "Repeated launcher failed: $($repeat.ExitCode)." }
}

try {
    # Starting both copies together covers the case where the first window is not ready yet.
    $first = Start-TestLauncher $ExePath
    $second = Start-TestLauncher $secondExe
    Wait-Until { @(@($first, $second) | Where-Object { -not $_.HasExited }).Count -eq 1 } 'Concurrent launches did not settle to one process' 45
    $primary = @(@($first, $second) | Where-Object { -not $_.HasExited })[0]
    $duplicate = @(@($first, $second) | Where-Object { $_.HasExited })[0]
    if ($duplicate.ExitCode -ne 0) { throw "Concurrent duplicate failed: $($duplicate.ExitCode)." }
    Wait-Until { (Test-Path (Join-Path $directory 'startup-ok.txt')) -and
        [SingleInstanceTestWindows]::FindWindow($primary.Id) -ne [IntPtr]::Zero } 'Primary window did not start' 45
    Write-Output 'PASS single instance: simultaneous launches and different EXE paths'

    $window = [SingleInstanceTestWindows]::FindWindow($primary.Id)
    [void][SingleInstanceTestWindows]::ShowWindow($window, 3) # SW_SHOWMAXIMIZED
    Wait-Until { [SingleInstanceTestWindows]::IsZoomed($window) } 'Window did not maximize'
    Start-Repeat $secondExe
    if (-not [SingleInstanceTestWindows]::IsZoomed($window)) { throw 'Repeated launch changed the maximized window size.' }
    [void][SingleInstanceTestWindows]::ShowWindow($window, 6) # SW_MINIMIZE
    Wait-Until { [SingleInstanceTestWindows]::IsIconic($window) } 'Window did not minimize'
    Start-Repeat $ExePath
    Wait-Until { $handle = [SingleInstanceTestWindows]::FindWindow($primary.Id)
        $handle -ne [IntPtr]::Zero -and [SingleInstanceTestWindows]::IsWindowVisible($handle) -and
        -not [SingleInstanceTestWindows]::IsIconic($handle) } 'Repeated launch did not restore the minimized window'
    $window = [SingleInstanceTestWindows]::FindWindow($primary.Id)
    if (-not [SingleInstanceTestWindows]::IsZoomed($window)) { throw 'Restoring lost the previous maximized state.' }
    Write-Output 'PASS single instance: visible and minimized window'

    [void][SingleInstanceTestWindows]::PostMessage($window, 0x112, [IntPtr]0xF060, [IntPtr]::Zero) # WM_SYSCOMMAND / SC_CLOSE
    Wait-Until { $handle = [SingleInstanceTestWindows]::FindWindow($primary.Id)
        $handle -ne [IntPtr]::Zero -and -not [SingleInstanceTestWindows]::IsWindowVisible($handle) } 'Window did not hide to the tray'
    Start-Repeat $secondExe
    Wait-Until { $handle = [SingleInstanceTestWindows]::FindWindow($primary.Id)
        $handle -ne [IntPtr]::Zero -and [SingleInstanceTestWindows]::IsWindowVisible($handle) } 'Repeated launch did not restore the tray window'
    if ($primary.HasExited) { throw 'The original process exited while restoring from the tray.' }
    Write-Output 'PASS single instance: restore from tray'

    # Use the real close preference to exit normally, then verify the instance lock is released.
    $window = [SingleInstanceTestWindows]::FindWindow($primary.Id)
    $check = [SingleInstanceTestWindows]::FindControl($window, '关闭窗口时隐藏到托盘')
    if ($check -eq [IntPtr]::Zero) { throw 'Could not find the close-to-tray preference.' }
    [void][SingleInstanceTestWindows]::SendMessage($check, 0xF5, [IntPtr]::Zero, [IntPtr]::Zero) # BM_CLICK
    [void][SingleInstanceTestWindows]::PostMessage($window, 0x112, [IntPtr]0xF060, [IntPtr]::Zero)
    if (-not $primary.WaitForExit(15000) -or $primary.ExitCode -ne 0) { throw 'Primary launcher did not exit normally.' }
    Remove-Item -LiteralPath (Join-Path $directory 'startup-ok.txt')
    $replacement = Start-TestLauncher $ExePath
    Wait-Until { (Test-Path (Join-Path $directory 'startup-ok.txt')) -and -not $replacement.HasExited } 'A new launch did not start after exit' 45
    $window = [SingleInstanceTestWindows]::FindWindow($replacement.Id)
    [void][SingleInstanceTestWindows]::PostMessage($window, 0x112, [IntPtr]0xF060, [IntPtr]::Zero)
    if (-not $replacement.WaitForExit(15000) -or $replacement.ExitCode -ne 0) { throw 'Replacement launcher did not exit normally.' }
    Write-Output 'PASS single instance: restart after exit'
}
finally {
    # These are only the isolated self-test processes started above.
    foreach ($process in $processes) {
        if (-not $process.HasExited) { $process.Kill(); [void]$process.WaitForExit(5000) }
        $process.Dispose()
    }
}
