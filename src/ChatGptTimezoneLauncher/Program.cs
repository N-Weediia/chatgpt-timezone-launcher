namespace ChatGptTimezoneLauncher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var instanceTest = args.Length == 3 && args[0] == "--self-test" && args[2] == "--single-instance";
        var selfTest = args.Length >= 2 && args[0] == "--self-test" && (args.Length == 2 || instanceTest);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            StartupDiagnostics.Report(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
        try
        {
            // The startup smoke test is isolated, while the instance test exercises the real handoff.
            using var instance = !selfTest || instanceTest
                ? SingleInstance.StartOrActivate(instanceTest ? Path.GetFullPath(args[1]) : null)
                : null;
            if ((!selfTest || instanceTest) && instance is null) return 0;

            Application.SetUnhandledExceptionMode(selfTest
                ? UnhandledExceptionMode.ThrowException : UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => StartupDiagnostics.Report(e.Exception);
            ApplicationConfiguration.Initialize();
            bool? testTheme = selfTest ? Environment.GetEnvironmentVariable("CHATGPT_TZ_TEST_THEME") switch
            {
                "dark" => true,
                "light" => false,
                _ => null
            } : null;
            using var form = new MainForm(selfTest ? new ConfigStore(Path.GetFullPath(args[1])) : null,
                enableTray: !selfTest || instanceTest, enableUsageRefresh: !selfTest, darkThemeOverride: testTheme);
            instance?.Listen(form);
            if (selfTest)
            {
                // Exercise the real window and message loop, without touching user settings or ChatGPT.
                form.Shown += (_, _) => form.BeginInvoke(() =>
                {
                    File.WriteAllText(Path.Combine(args[1], "startup-ok.txt"),
                        $"Window created; timezones={TimeZoneCatalog.All.Count}; runtime={Environment.Version}");
                    if (!instanceTest) form.Close();
                });
            }
            Application.Run(form);
            return 0;
        }
        catch (Exception ex)
        {
            if (selfTest) StartupDiagnostics.Write(ex);
            else StartupDiagnostics.Report(ex);
            return 1;
        }
    }
}
