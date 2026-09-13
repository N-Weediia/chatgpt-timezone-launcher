using System.Drawing;

namespace ChatGptTimezoneLauncher;

public sealed class MainForm : Form
{
    private readonly ConfigStore _configStore;
    private readonly GeoIpService _geoIp = new();
    private readonly ChatGptDiscovery _discovery = new();
    private readonly ChatGptLauncher _launcher = new();
    private readonly UsageService _usageService = new();
    private readonly CancellationTokenSource _lifetime = new();
    private LauncherConfig _config;
    private UsageSnapshot? _usageSnapshot;
    private ChatGptInstallation? _knownInstallation;
    private string? _activeTimeZone;
    private bool _launchStatePending;
    private bool _usageFetchInProgress;
    private bool _allowExit;
    private readonly HashSet<string> _usageReminderKeys = [];
    private NotifyIcon? _trayIcon;
    private ContextMenuStrip? _trayMenu;

    private readonly RadioButton _autoRadio = new() { Text = "自动跟随 ChatGPT 实际出口", AutoSize = true };
    private readonly RadioButton _manualRadio = new() { Text = "手动选择时区", AutoSize = true };
    private readonly ComboBox _timeZoneBox = new();
    private readonly Label _overrideState = new();
    private readonly Label _detectionState = new();
    private readonly Label _ipValue = ValueLabel();
    private readonly Label _locationValue = ValueLabel();
    private readonly Label _zoneValue = ValueLabel();
    private readonly Label _proxyGroupValue = ValueLabel();
    private readonly Label _proxyNodeValue = ValueLabel();
    private readonly Label _providerValue = ValueLabel();
    private readonly Label _runtimeState = ValueLabel();
    private readonly Label _usageState = ValueLabel();
    private readonly Label _fiveHourValue = ValueLabel();
    private readonly Label _weeklyValue = ValueLabel();
    private readonly Button _detectButton = new() { Text = "检测 ChatGPT 出口", AutoSize = true };
    private readonly Button _refreshUsageButton = new() { Text = "刷新限额", AutoSize = true };
    private readonly Button _launchButton = new() { Text = "保存并启动 ChatGPT", Height = 42, AutoSize = true };
    private readonly Button _restoreButton = new() { Text = "恢复 ChatGPT 默认启动方式", Height = 42, AutoSize = true };
    private readonly Button _shortcutButton = new() { Text = "创建桌面快捷方式", AutoSize = true };
    private readonly CheckBox _usageReminderCheck = new() { Text = "接近重置时提醒（北京时间）", AutoSize = true };
    private readonly CheckBox _closeToTrayCheck = new() { Text = "关闭窗口时隐藏到托盘", AutoSize = true };
    private readonly TextBox _details = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 84, Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _usageRefreshTimer = new() { Interval = 60_000 };
    private readonly System.Windows.Forms.Timer _usageCountdownTimer = new() { Interval = 1000 };

    public MainForm(ConfigStore? configStore = null, bool enableTray = true)
    {
        _configStore = configStore ?? new ConfigStore();
        var loaded = _configStore.Load();
        _config = loaded.Config;
        Text = "ChatGPT 时区启动器";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 760);
        ClientSize = new Size(860, 980);
        Font = new Font("Microsoft YaHei UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ConfigureInitialBounds();

        BuildUi();
        LoadConfigIntoUi();
        ShowLastDetection();
        UpdateModeUi();
        UpdateRuntimeState();
        if (enableTray) InitializeTrayIcon();

        _autoRadio.CheckedChanged += (_, _) => UpdateModeUi();
        _manualRadio.CheckedChanged += (_, _) => UpdateModeUi();
        _detectButton.Click += async (_, _) => await DetectAsync();
        _refreshUsageButton.Click += async (_, _) => await RefreshUsageAsync(true);
        _launchButton.Click += async (_, _) => await SaveAndLaunchAsync();
        _restoreButton.Click += (_, _) => RestoreDefault();
        _shortcutButton.Click += (_, _) => CreateShortcut();
        _usageReminderCheck.CheckedChanged += (_, _) => SavePreference();
        _closeToTrayCheck.CheckedChanged += (_, _) => SavePreference();
        _timeZoneBox.SelectedIndexChanged += (_, _) => { if (_timeZoneBox.Focused) _manualRadio.Checked = true; };
        _statusTimer.Tick += (_, _) => UpdateRuntimeState();
        _usageRefreshTimer.Tick += async (_, _) => await RefreshUsageAsync(false);
        _usageCountdownTimer.Tick += (_, _) => UpdateUsageCountdown();
        FormClosing += OnFormClosing;
        FormClosed += OnFormClosed;
        Shown += async (_, _) =>
        {
            _statusTimer.Start();
            _usageRefreshTimer.Start();
            _usageCountdownTimer.Start();
            await RefreshUsageAsync(false);
        };

        if (loaded.Warning is not null)
            _details.Text = loaded.Warning;
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 20, 24, 20),
            ColumnCount = 1,
            RowCount = 7,
            AutoScroll = true,
            AutoSize = false,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label { Text = "ChatGPT 时区启动器", Font = new Font(Font.FontFamily, 18, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
        var subtitle = new Label { Text = "只为本次启动的 ChatGPT 进程设置时区，不修改 Windows 系统时区。", ForeColor = Color.DimGray, AutoSize = true, Margin = new Padding(0, 0, 0, 16) };
        var titlePanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill, WrapContents = false };
        titlePanel.Controls.Add(title); titlePanel.Controls.Add(subtitle);
        root.Controls.Add(titlePanel);

        var statePanel = new Panel { Height = 48, Dock = DockStyle.Fill, BackColor = Color.FromArgb(239, 246, 255), Margin = new Padding(0, 0, 0, 14) };
        _overrideState.AutoSize = true; _overrideState.Location = new Point(14, 14); _overrideState.Font = new Font(Font, FontStyle.Bold);
        statePanel.Controls.Add(_overrideState); root.Controls.Add(statePanel);

        var modeGroup = new GroupBox { Text = "模式", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(14, 10, 14, 12), Margin = new Padding(0, 0, 0, 12) };
        var modes = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        _autoRadio.Margin = new Padding(3, 5, 3, 6); _manualRadio.Margin = new Padding(3, 2, 3, 3);
        modes.Controls.Add(_autoRadio); modes.Controls.Add(_manualRadio); modeGroup.Controls.Add(modes); root.Controls.Add(modeGroup);

        var statusGroup = new GroupBox { Text = "ChatGPT 出口检测", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(14, 10, 14, 12), Margin = new Padding(0, 0, 0, 12) };
        var status = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 9 };
        status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145)); status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _detectionState.AutoSize = false; _detectionState.Dock = DockStyle.Fill; _detectionState.Height = 24;
        AddRow(status, 0, "检测状态", _detectionState); AddRow(status, 1, "ChatGPT 出口 IP", _ipValue);
        AddRow(status, 2, "实际命中代理组", _proxyGroupValue); AddRow(status, 3, "当前节点", _proxyNodeValue);
        AddRow(status, 4, "位置", _locationValue); AddRow(status, 5, "IANA 时区", _zoneValue);
        AddRow(status, 6, "检测方式", _providerValue); AddRow(status, 7, "ChatGPT 进程", _runtimeState);
        _detectButton.Margin = new Padding(0, 9, 0, 0); status.Controls.Add(_detectButton, 1, 8);
        statusGroup.Controls.Add(status); root.Controls.Add(statusGroup);

        var manualGroup = new GroupBox { Text = "手动时区", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(14, 10, 14, 12), Margin = new Padding(0, 0, 0, 12) };
        var manual = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2 };
        manual.Controls.Add(new Label { Text = "输入城市或时区片段搜索，或从列表选择标准 IANA 时区：", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 0, 0, 7) });
        _timeZoneBox.Dock = DockStyle.Top; _timeZoneBox.DropDownStyle = ComboBoxStyle.DropDown; _timeZoneBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _timeZoneBox.AutoCompleteSource = AutoCompleteSource.ListItems; _timeZoneBox.MaxDropDownItems = 14;
        _timeZoneBox.Items.AddRange(TimeZoneCatalog.All.Cast<object>().ToArray()); manual.Controls.Add(_timeZoneBox);
        manualGroup.Controls.Add(manual); root.Controls.Add(manualGroup);

        var usageGroup = new GroupBox { Text = "Codex 限额与北京时间重置提醒", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(14, 10, 14, 12), Margin = new Padding(0, 0, 0, 12) };
        var usage = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 4 };
        usage.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145)); usage.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(usage, 0, "更新时间", _usageState);
        AddRow(usage, 1, "5 小时限额", _fiveHourValue);
        AddRow(usage, 2, "每周限额", _weeklyValue);
        var usageActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 4, 0, 0) };
        usageActions.Controls.Add(_refreshUsageButton);
        usageActions.Controls.Add(_usageReminderCheck);
        usage.Controls.Add(usageActions, 1, 3);
        usageGroup.Controls.Add(usage); root.Controls.Add(usageGroup);

        var buttons = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = false, Height = 58, ColumnCount = 2, Margin = new Padding(24, 0, 24, 12) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _launchButton.AutoSize = false; _launchButton.Dock = DockStyle.Fill; _launchButton.BackColor = Color.FromArgb(37, 99, 235); _launchButton.ForeColor = Color.White; _launchButton.FlatStyle = FlatStyle.Flat;
        _restoreButton.AutoSize = false; _restoreButton.Dock = DockStyle.Fill; _restoreButton.BackColor = Color.FromArgb(255, 247, 237); _restoreButton.ForeColor = Color.FromArgb(154, 52, 18); _restoreButton.FlatStyle = FlatStyle.Flat;
        _launchButton.Margin = new Padding(0, 0, 6, 0); _restoreButton.Margin = new Padding(6, 0, 0, 0);
        buttons.Controls.Add(_launchButton); buttons.Controls.Add(_restoreButton);

        var detailsGroup = new GroupBox { Text = "状态与诊断", Dock = DockStyle.Fill, Padding = new Padding(10), Margin = new Padding(0, 0, 0, 12), MinimumSize = new Size(0, 100) };
        detailsGroup.Controls.Add(_details); root.Controls.Add(detailsGroup);
        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = false, Height = 34, ColumnCount = 3, Margin = new Padding(24, 0, 24, 8) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _shortcutButton.Dock = DockStyle.Fill; _shortcutButton.AutoSize = false; _shortcutButton.Margin = new Padding(0, 0, 6, 0);
        _closeToTrayCheck.Dock = DockStyle.Fill; _closeToTrayCheck.Margin = new Padding(4, 5, 4, 0);
        var configLabel = new Label { Text = "配置：%LocalAppData%\\ChatGPTTimezoneLauncher", AutoSize = false, Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Color.Gray, TextAlign = ContentAlignment.MiddleLeft };
        footer.Controls.Add(_shortcutButton, 0, 0); footer.Controls.Add(_closeToTrayCheck, 1, 0); footer.Controls.Add(configLabel, 2, 0);
        Controls.Add(root);
        Controls.Add(footer);
        Controls.Add(buttons);
    }

    private void ConfigureInitialBounds()
    {
        var workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        // On compact or high-DPI screens a restored 860x980 window cannot fit.
        // Maximizing here keeps the fixed action bar visible from the first launch.
        if (workArea.Width < 1100 || workArea.Height < 920)
        {
            WindowState = FormWindowState.Maximized;
            return;
        }

        var width = Math.Min(980, workArea.Width - 24);
        var height = Math.Min(1000, workArea.Height - 24);
        ClientSize = new Size(Math.Max(720, width), Math.Max(760, height));
    }

    private async Task DetectAsync()
    {
        SetBusy(true, "正在检测当前网络出口…");
        var result = await _geoIp.DetectAsync();
        SetBusy(false);
        if (result.Success)
        {
            _config.LastSuccessfulAutoDetection = result.Location;
            _configStore.Save(_config);
            ShowLocation(result.Location!, "检测成功");
            _details.Text = $"先通过 {result.Location!.DetectionMethod} 确定 ChatGPT 出口，再由 {result.Location.Provider} 查询该指定 IP。每次启动都会重新探测。";
        }
        else
        {
            _detectionState.Text = "ChatGPT 出口检测失败（未猜测时区）"; _detectionState.ForeColor = Color.Firebrick;
            _details.Text = result.Message + LastSuccessText();
            if (_config.LastSuccessfulAutoDetection is not null) ShowLocationValues(_config.LastSuccessfulAutoDetection);
        }
    }

    private async Task SaveAndLaunchAsync()
    {
        string? zone = null;
        if (_autoRadio.Checked)
        {
            SetBusy(true, "启动前重新检测 ChatGPT 出口…");
            var detection = await _geoIp.DetectAsync();
            SetBusy(false);
            if (!detection.Success)
            {
                _detectionState.Text = "检测失败（未启动）"; _detectionState.ForeColor = Color.Firebrick;
                _details.Text = detection.Message + LastSuccessText();
                MessageBox.Show(this, "自动检测失败，因此没有启动 ChatGPT，也没有使用本机时区或旧结果。\r\n\r\n你可以立即切换到“手动选择时区”。",
                    "无法确定时区", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _config.LastSuccessfulAutoDetection = detection.Location;
            ShowLocation(detection.Location!, "启动前检测成功");
            zone = detection.Location!.TimeZone;
            _config.Mode = TimeZoneMode.Auto; _config.TimeZoneOverrideEnabled = true;
        }
        else if (_manualRadio.Checked)
        {
            zone = _timeZoneBox.Text.Trim();
            if (!TimeZoneCatalog.IsValid(zone))
            {
                MessageBox.Show(this, "请选择有效的标准 IANA 时区，例如 Asia/Shanghai。", "时区无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _config.ManualTimeZone = zone; _config.Mode = TimeZoneMode.Manual; _config.TimeZoneOverrideEnabled = true;
        }
        else
        {
            _config.TimeZoneOverrideEnabled = false;
        }

        _configStore.Save(_config); UpdateOverrideState();
        SetBusy(true, "正在定位 Windows 版 ChatGPT…");
        var discovery = await _discovery.DiscoverAsync();
        SetBusy(false); _details.Text = discovery.Diagnostics;
        if (!discovery.Found)
        {
            MessageBox.Show(this, "未找到 Windows 版 ChatGPT。\r\n\r\n" + discovery.Diagnostics,
                "未找到 ChatGPT", MessageBoxButtons.OK, MessageBoxIcon.Error); return;
        }

        var installation = discovery.Installation!;
        _knownInstallation = installation;
        if (_config.TimeZoneOverrideEnabled && _launcher.IsRunning(installation))
        {
            var answer = MessageBox.Show(this, "ChatGPT 已经在运行。新的时区将在 ChatGPT 重启后生效。\r\n\r\n是否让启动器请求 ChatGPT 正常关闭并重新启动？不会强制结束进程。",
                "需要重启 ChatGPT", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer != DialogResult.Yes) return;
            SetBusy(true, "正在等待 ChatGPT 正常退出…");
            var closed = await _launcher.CloseGracefullyAsync(installation); SetBusy(false);
            if (!closed.Success) { MessageBox.Show(this, closed.Message, "未能关闭", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        }

        var launch = _launcher.Launch(installation, _config.TimeZoneOverrideEnabled ? zone : null);
        _details.Text = launch.Message + Environment.NewLine + discovery.Diagnostics;
        if (launch.Success)
        {
            _activeTimeZone = _config.TimeZoneOverrideEnabled ? zone : null;
            _launchStatePending = true;
            _detectionState.Text = "启动完成，正在自动刷新状态";
            _detectionState.ForeColor = Color.ForestGreen;
            UpdateRuntimeState();
            _statusTimer.Start();
        }
        else
        {
            _detectionState.Text = "启动失败";
            _detectionState.ForeColor = Color.Firebrick;
            MessageBox.Show(this, launch.Message, "启动结果", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RestoreDefault()
    {
        _config.TimeZoneOverrideEnabled = false;
        _configStore.Save(_config);
        _activeTimeZone = null;
        _autoRadio.Checked = false; _manualRadio.Checked = false;
        UpdateOverrideState(); UpdateModeUi();
        _details.Text = "已恢复默认：启动器未修改系统时区、注册表或全局环境变量。之后点击“启动 ChatGPT（默认方式）”将使用标准 AppX 激活且不注入 TZ。保存的手动选择和上次检测结果仍保留，便于以后重新启用。";
        MessageBox.Show(this, "已恢复 ChatGPT 默认启动方式。\r\n\r\n没有修改 Windows 系统时区，也没有删除 ChatGPT 数据。",
            "恢复完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void LoadConfigIntoUi()
    {
        _timeZoneBox.Text = _config.ManualTimeZone;
        _usageReminderCheck.Checked = _config.UsageResetReminderEnabled;
        _closeToTrayCheck.Checked = _config.CloseToTrayOnClose;
        if (_config.TimeZoneOverrideEnabled)
        {
            _autoRadio.Checked = _config.Mode == TimeZoneMode.Auto;
            _manualRadio.Checked = _config.Mode == TimeZoneMode.Manual;
        }
    }

    private void UpdateModeUi()
    {
        _timeZoneBox.Enabled = _manualRadio.Checked;
        _detectButton.Enabled = !_manualRadio.Checked;
        _launchButton.Text = _autoRadio.Checked || _manualRadio.Checked ? "保存并启动 ChatGPT" : "启动 ChatGPT（默认方式）";
        UpdateOverrideState();
    }

    private void UpdateOverrideState()
    {
        if (!_config.TimeZoneOverrideEnabled)
        {
            _overrideState.Text = "当前：ChatGPT 默认启动方式（不注入 TZ）"; _overrideState.ForeColor = Color.FromArgb(30, 64, 175);
        }
        else
        {
            var zone = _config.Mode == TimeZoneMode.Manual ? _config.ManualTimeZone : _config.LastSuccessfulAutoDetection?.TimeZone ?? "等待检测";
            _overrideState.Text = $"当前：{(_config.Mode == TimeZoneMode.Auto ? "自动时区" : "手动时区")} · {zone}"; _overrideState.ForeColor = Color.FromArgb(21, 128, 61);
        }
    }

    private void ShowLastDetection()
    {
        if (_config.LastSuccessfulAutoDetection is null)
        {
            _detectionState.Text = "尚未检测";
            _ipValue.Text = _proxyGroupValue.Text = _proxyNodeValue.Text = _locationValue.Text = _zoneValue.Text = _providerValue.Text = "—";
        }
        else { ShowLocation(_config.LastSuccessfulAutoDetection, "上次成功结果（仅供参考）"); }
    }

    private void ShowLocation(GeoLocation value, string state)
    {
        _detectionState.Text = state; _detectionState.ForeColor = state.Contains("成功") ? Color.ForestGreen : Color.DimGray;
        ShowLocationValues(value);
    }

    private void ShowLocationValues(GeoLocation value)
    {
        _ipValue.Text = value.Ip;
        _proxyGroupValue.Text = value.ProxyGroup ?? "未读取（检测不依赖 Clash API）";
        _proxyNodeValue.Text = value.ProxyNode ?? "未读取（检测不依赖 Clash API）";
        _locationValue.Text = value.LocationText; _zoneValue.Text = value.TimeZone;
        _providerValue.Text = $"{value.DetectionMethod} · {value.Provider} · {value.DetectedAt:yyyy-MM-dd HH:mm:ss}";
    }

    private string LastSuccessText() => _config.LastSuccessfulAutoDetection is { } last
        ? $"\r\n\r\n保留的上次成功结果（未自动使用）：{last.Ip} / {last.LocationText} / {last.TimeZone}"
        : "\r\n\r\n没有可显示的历史成功结果。";

    private async Task RefreshUsageAsync(bool userInitiated)
    {
        if (_usageFetchInProgress) return;
        _usageFetchInProgress = true;
        _refreshUsageButton.Enabled = false;
        if (userInitiated) _usageState.Text = "正在读取官方限额…";
        try
        {
            var snapshot = await _usageService.FetchAsync(_lifetime.Token);
            if (IsDisposed || _lifetime.IsCancellationRequested) return;
            _usageSnapshot = snapshot;
            _usageState.Text = $"{_usageSnapshot.PlanType} · 已更新 {_usageSnapshot.FetchedAtBeijing:yyyy-MM-dd HH:mm:ss}（北京时间）";
            UpdateUsageCountdown();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (IsDisposed || _lifetime.IsCancellationRequested) return;
            _usageState.Text = userInitiated ? $"获取失败：{ex.Message}" : "自动刷新失败，请点击“刷新限额”重试";
            if (userInitiated)
            {
                _fiveHourValue.Text = _weeklyValue.Text = "—";
                MessageBox.Show(this, ex.Message + "\r\n\r\n限额数据不会写入配置文件。", "限额读取失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _usageFetchInProgress = false;
            if (!IsDisposed) _refreshUsageButton.Enabled = !UseWaitCursor;
        }
    }

    private void UpdateUsageCountdown()
    {
        if (_usageSnapshot is null) return;
        var now = DateTimeOffset.UtcNow;
        _fiveHourValue.Text = FormatUsageWindow("5 小时", _usageSnapshot.Primary, now);
        _weeklyValue.Text = FormatUsageWindow("每周", _usageSnapshot.Secondary, now);
        MaybeRemind("5 小时限额", _usageSnapshot.Primary, now);
        MaybeRemind("每周限额", _usageSnapshot.Secondary, now);
    }

    private static string FormatUsageWindow(string name, UsageWindow window, DateTimeOffset now)
    {
        var remaining = window.ResetAtUtc - now;
        var countdown = remaining <= TimeSpan.Zero ? "等待下次刷新" : $"还有 {FormatDuration(remaining)}";
        return $"已用 {window.UsedPercent}% · 剩余 {window.RemainingPercent}% · 重置：{window.ResetAtBeijing:yyyy-MM-dd HH:mm:ss}（北京时间）· {countdown}";
    }

    private void MaybeRemind(string name, UsageWindow window, DateTimeOffset now)
    {
        if (!_config.UsageResetReminderEnabled || _trayIcon is null) return;
        var remaining = window.ResetAtUtc - now;
        var timestamp = window.ResetAtUtc.ToUnixTimeSeconds();
        if (remaining > TimeSpan.Zero && remaining <= TimeSpan.FromMinutes(10))
        {
            var key = $"{name}:soon:{timestamp}";
            if (_usageReminderKeys.Add(key))
                NotifyTray($"{name}即将在北京时间 {window.ResetAtBeijing:HH:mm:ss} 重置。", "限额重置提醒");
        }
        else if (remaining <= TimeSpan.Zero)
        {
            var key = $"{name}:reset:{timestamp}";
            if (_usageReminderKeys.Add(key))
                NotifyTray($"{name}已到重置时间，请点击“刷新限额”获取最新状态。", "限额已重置");
        }
        if (_usageReminderKeys.Count > 40) _usageReminderKeys.Clear();
    }

    private static string FormatDuration(TimeSpan value)
    {
        if (value.TotalDays >= 1) return $"{(int)value.TotalDays}天{value.Hours}小时{value.Minutes}分";
        if (value.TotalHours >= 1) return $"{(int)value.TotalHours}小时{value.Minutes}分";
        return $"{Math.Max(0, value.Minutes)}分{Math.Max(0, value.Seconds)}秒";
    }

    private void UpdateRuntimeState()
    {
        if (_knownInstallation is null)
        {
            _runtimeState.Text = "尚未定位 ChatGPT";
            _runtimeState.ForeColor = Color.DimGray;
            return;
        }

        var running = _launcher.IsRunning(_knownInstallation);
        _runtimeState.Text = running
            ? (_activeTimeZone is null ? "运行中（默认启动）" : $"运行中（本次进程时区：{_activeTimeZone}）")
            : "未运行";
        _runtimeState.ForeColor = running ? Color.ForestGreen : Color.DimGray;
        if (_launchStatePending && running)
        {
            _detectionState.Text = "ChatGPT 已启动，状态已自动更新";
            _detectionState.ForeColor = Color.ForestGreen;
            _launchStatePending = false;
        }
    }

    private void SavePreference()
    {
        if (IsDisposed) return;
        _config.UsageResetReminderEnabled = _usageReminderCheck.Checked;
        _config.CloseToTrayOnClose = _closeToTrayCheck.Checked;
        try { _configStore.Save(_config); } catch { }
    }

    private void InitializeTrayIcon()
    {
        _trayMenu = new ContextMenuStrip();
        _trayMenu.Items.Add("显示启动器", null, (_, _) => ShowFromTray());
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("退出", null, (_, _) => ExitApplication());
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "ChatGPT 时区启动器",
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private void NotifyTray(string message, string title)
    {
        _trayIcon?.ShowBalloonTip(5000, title, message, ToolTipIcon.Info);
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
        NotifyTray("启动器仍在后台运行，限额和 ChatGPT 状态会继续刷新。", "ChatGPT 时区启动器");
    }

    private void ExitApplication()
    {
        _allowExit = true;
        Close();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_allowExit && e.CloseReason == CloseReason.UserClosing && _config.CloseToTrayOnClose && _trayIcon is not null)
        {
            e.Cancel = true;
            HideToTray();
        }
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        _statusTimer.Stop(); _usageRefreshTimer.Stop(); _usageCountdownTimer.Stop();
        _lifetime.Cancel();
        _usageService.Dispose();
        if (_trayIcon is not null) _trayIcon.Visible = false;
        _trayIcon?.Dispose(); _trayMenu?.Dispose();
        _statusTimer.Dispose(); _usageRefreshTimer.Dispose(); _usageCountdownTimer.Dispose();
        _lifetime.Dispose();
    }

    private void SetBusy(bool busy, string? text = null)
    {
        UseWaitCursor = busy; _detectButton.Enabled = !busy && !_manualRadio.Checked; _launchButton.Enabled = !busy; _restoreButton.Enabled = !busy;
        _refreshUsageButton.Enabled = !busy && !_usageFetchInProgress;
        if (busy && text is not null) { _detectionState.Text = text; _detectionState.ForeColor = Color.DarkOrange; }
    }

    private void CreateShortcut()
    {
        try { var path = ShortcutService.CreateDesktopShortcut(); _details.Text = $"已创建桌面快捷方式：{path}"; }
        catch (Exception ex) { MessageBox.Show(this, $"创建快捷方式失败：{ex.Message}", "创建失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private static Label ValueLabel() => new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold)
    };
    private static void AddRow(TableLayoutPanel panel, int row, string key, Control value)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = key + "：", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 3, 0, 5) }, 0, row);
        value.Dock = DockStyle.Fill; value.Margin = new Padding(0, 3, 0, 5); panel.Controls.Add(value, 1, row);
    }
}
