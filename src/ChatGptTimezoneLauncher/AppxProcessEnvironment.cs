using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ChatGptTimezoneLauncher;

/// <summary>
/// Activates a packaged Win32 app through the supported AppX activation path,
/// then applies a process-local environment override before resuming it.
/// This avoids trying to execute a file directly from WindowsApps.
/// </summary>
public static class AppxProcessEnvironment
{
    private const string ActivationManagerClsid = "45BA127D-10A8-46EA-8AB7-56EA9078943C";
    private const int ProcessBasicInfoClass = 0;
    private const int PebProcessParametersOffset = 0x20;
    private const int ProcessParametersEnvironmentOffset = 0x80;
    private const int MaxEnvironmentBytes = 4 * 1024 * 1024;
    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint PageReadWrite = 0x04;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint ProcessSuspendResume = 0x0800;

    public static bool LaunchWithEnvironment(ChatGptInstallation installation, string ianaTimeZone,
        out string message)
    {
        if (IntPtr.Size != 8)
        {
            message = "当前版本只支持 64 位 Windows，与 ChatGPT x64 包匹配。";
            return false;
        }

        uint processId = 0;
        IntPtr processHandle = IntPtr.Zero;
        IntPtr replacementEnvironment = IntPtr.Zero;
        var suspended = false;
        try
        {
            processId = AppxActivator.Activate(installation.Aumid, installation.Parameters);
            processHandle = OpenProcess(
                ProcessQueryInformation | ProcessVmOperation | ProcessVmRead |
                ProcessVmWrite | ProcessSuspendResume, false, processId);
            if (processHandle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法打开刚刚激活的 ChatGPT 进程");

            ThrowNtStatus(NtSuspendProcess(processHandle), "无法暂停刚刚激活的 ChatGPT 进程");
            suspended = true;

            var peb = QueryPeb(processHandle);
            var parameters = ReadPointer(processHandle, IntPtr.Add(peb, PebProcessParametersOffset));
            if (parameters == IntPtr.Zero)
                throw new InvalidOperationException("ChatGPT 进程参数尚未初始化");

            var environmentAddress = ReadPointer(processHandle,
                IntPtr.Add(parameters, ProcessParametersEnvironmentOffset));
            if (environmentAddress == IntPtr.Zero)
                throw new InvalidOperationException("ChatGPT 环境块尚未初始化");

            var currentBlock = ReadEnvironmentBlock(processHandle, environmentAddress);
            var newBlock = BuildEnvironmentBlock(currentBlock, "TZ", ianaTimeZone);
            replacementEnvironment = VirtualAllocEx(processHandle, IntPtr.Zero,
                (UIntPtr)newBlock.Length, MemCommit | MemReserve, PageReadWrite);
            if (replacementEnvironment == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法为 ChatGPT 分配环境块");

            if (!WriteProcessMemory(processHandle, replacementEnvironment, newBlock,
                    (UIntPtr)newBlock.Length, out var bytesWritten) ||
                bytesWritten.ToUInt64() != (ulong)newBlock.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法写入 ChatGPT 环境块");

            WritePointer(processHandle, IntPtr.Add(parameters, ProcessParametersEnvironmentOffset),
                replacementEnvironment);
            ThrowNtStatus(NtResumeProcess(processHandle), "无法恢复 ChatGPT 进程");
            suspended = false;

            var focused = FocusMainWindow(processId);
            message = focused
                ? $"已通过 AppX 激活 ChatGPT，注入本次进程时区并已切到前台：{ianaTimeZone}"
                : $"已通过 AppX 激活 ChatGPT，并在恢复运行前注入本次进程时区：{ianaTimeZone}\r\nChatGPT 已启动，但窗口尚未创建；请查看任务栏。";
            return true;
        }
        catch (Exception ex)
        {
            message = $"时区覆盖启动失败：{ex.Message}";
            return false;
        }
        finally
        {
            if (suspended && processHandle != IntPtr.Zero)
                _ = NtResumeProcess(processHandle);
            if (replacementEnvironment != IntPtr.Zero && processHandle != IntPtr.Zero)
                _ = VirtualFreeEx(processHandle, replacementEnvironment, UIntPtr.Zero, 0x8000);
            if (processHandle != IntPtr.Zero)
                _ = CloseHandle(processHandle);
        }
    }

    // Kept public for the lightweight regression test; it does not touch another process.
    public static byte[] BuildEnvironmentBlock(byte[] currentBlock, string name, string value)
    {
        var text = Encoding.Unicode.GetString(currentBlock);
        var entries = text.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(entry => !entry.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            .ToList();
        entries.Add(name + "=" + value);
        entries.Sort(StringComparer.OrdinalIgnoreCase);
        return Encoding.Unicode.GetBytes(string.Join('\0', entries) + "\0\0");
    }

    private static IntPtr QueryPeb(IntPtr processHandle)
    {
        var information = new ProcessBasicInformation();
        var status = NtQueryInformationProcess(processHandle, ProcessBasicInfoClass, ref information,
            (uint)Marshal.SizeOf<ProcessBasicInformation>(), out _);
        ThrowNtStatus(status, "无法读取 ChatGPT 进程信息");
        return information.PebBaseAddress;
    }

    private static IntPtr ReadPointer(IntPtr processHandle, IntPtr address)
    {
        var bytes = new byte[IntPtr.Size];
        if (!ReadProcessMemory(processHandle, address, bytes, (UIntPtr)bytes.Length, out var read) ||
            read.ToUInt64() != (ulong)bytes.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 ChatGPT 进程参数");
        return new IntPtr(BitConverter.ToInt64(bytes, 0));
    }

    private static void WritePointer(IntPtr processHandle, IntPtr address, IntPtr value)
    {
        var bytes = BitConverter.GetBytes(value.ToInt64());
        if (!WriteProcessMemory(processHandle, address, bytes, (UIntPtr)bytes.Length, out var written) ||
            written.ToUInt64() != (ulong)bytes.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法更新 ChatGPT 环境指针");
    }

    private static byte[] ReadEnvironmentBlock(IntPtr processHandle, IntPtr address)
    {
        using var data = new MemoryStream();
        for (var offset = 0; offset < MaxEnvironmentBytes; offset += 4096)
        {
            var chunk = new byte[Math.Min(4096, MaxEnvironmentBytes - offset)];
            if (!ReadProcessMemory(processHandle, IntPtr.Add(address, offset), chunk,
                    (UIntPtr)chunk.Length, out var read) || read.ToUInt64() != (ulong)chunk.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 ChatGPT 环境块");
            data.Write(chunk);
            var bytes = data.GetBuffer();
            var count = checked((int)data.Length);
            for (var i = 0; i + 3 < count; i += 2)
            {
                if (bytes[i] == 0 && bytes[i + 1] == 0 && bytes[i + 2] == 0 && bytes[i + 3] == 0)
                    return bytes[..(i + 4)];
            }
        }
        throw new InvalidOperationException("ChatGPT 环境块超过安全读取上限");
    }

    private static void ThrowNtStatus(int status, string operation)
    {
        if (status != 0)
            throw new Win32Exception(unchecked((int)status), operation);
    }

    private static bool FocusMainWindow(uint processId)
    {
        // Electron can create its window a short time after AppX activation returns.
        // Give the new main process a bounded grace period without blocking forever.
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                using var process = Process.GetProcessById(checked((int)processId));
                process.Refresh();
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(process.MainWindowHandle, ShowNormal);
                    _ = SetForegroundWindow(process.MainWindowHandle);
                    return true;
                }
                if (process.HasExited) return false;
            }
            catch { return false; }
            Thread.Sleep(100);
        }
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr Reserved3;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass,
        ref ProcessBasicInformation processInformation, uint processInformationLength, out uint returnLength);

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr processHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr processHandle, IntPtr baseAddress, byte[] buffer,
        UIntPtr size, out UIntPtr numberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr processHandle, IntPtr baseAddress, byte[] buffer,
        UIntPtr size, out UIntPtr numberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr processHandle, IntPtr address, UIntPtr size,
        uint allocationType, uint protection);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr processHandle, IntPtr address, UIntPtr size, uint freeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    private const int ShowNormal = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr handle);
}

internal static class AppxActivator
{
    public static uint Activate(string aumid, string? arguments)
    {
        var type = Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"), true)!;
        var manager = (IApplicationActivationManager)Activator.CreateInstance(type)!;
        var hr = manager.ActivateApplication(aumid, arguments ?? string.Empty, ActivateOptions.None, out var processId);
        Marshal.ThrowExceptionForHR(hr);
        return processId;
    }

    [ComImport]
    [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments, ActivateOptions options, out uint processId);

        int ActivateForFile(IntPtr appUserModelId, IntPtr itemArray,
            [MarshalAs(UnmanagedType.LPWStr)] string verb, out uint processId);

        int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string protocol,
            IntPtr itemArray, out uint processId);
    }

    [Flags]
    private enum ActivateOptions
    {
        None = 0,
        DesignMode = 1,
        NoErrorUI = 2,
        NoSplashScreen = 4
    }
}
