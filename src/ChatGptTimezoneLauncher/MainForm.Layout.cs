using System.Drawing;
using Microsoft.Win32;

namespace ChatGptTimezoneLauncher;

public sealed partial class MainForm
{
    private IndustrialPalette _palette = IndustrialPalette.FromSystem();
    private bool? _darkThemeOverride;
    private bool _themeEventsSubscribed;
    private TableLayoutPanel? _workspaceGrid;
    private TableLayoutPanel? _leftColumn;
    private TableLayoutPanel? _rightColumn;
    private bool _stacked;

    private void BuildUi()
    {
        SuspendLayout();
        Tag = UiRole.Paper;

        var shell = new Panel { Dock = DockStyle.Fill, Tag = UiRole.Paper };
        var header = BuildHeader();
        var actions = BuildActionBar();
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24, 18, 24, 16), Tag = UiRole.Paper };
        _workspaceGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Tag = UiRole.Paper
        };
        _workspaceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        _workspaceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        _workspaceGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _leftColumn = BuildLeftColumn();
        _rightColumn = BuildRightColumn();
        _workspaceGrid.Controls.Add(_leftColumn, 0, 0);
        _workspaceGrid.Controls.Add(_rightColumn, 1, 0);
        scroll.Controls.Add(_workspaceGrid);
        scroll.SizeChanged += (_, _) => UpdateColumns(scroll.ClientSize.Width);

        shell.Controls.Add(scroll);
        shell.Controls.Add(actions);
        shell.Controls.Add(header);
        Controls.Add(shell);
        ApplyPalette();
        ResumeLayout(true);
    }

    private Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 108, Padding = new Padding(25, 12, 25, 10), Tag = UiRole.Header };
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Tag = UiRole.Header };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        var branding = Stack(UiRole.Header);
        AddStack(branding, Eyebrow("// CHATGPT / TIME ZONE", UiRole.HeaderMuted));
        AddStack(branding, new Label
        {
            Text = "ChatGPT 时区启动器", AutoSize = true, Font = new Font("Microsoft YaHei UI", 17, FontStyle.Bold),
            Margin = new Padding(0, 1, 0, 0), Tag = UiRole.HeaderText
        });
        content.Controls.Add(branding, 0, 0);
        var environment = new Label
        {
            Text = "WINDOWS  /  X64", AutoSize = false, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight, Font = IndustrialFonts.Wide(8.2f),
            UseCompatibleTextRendering = true, Tag = UiRole.HeaderMuted
        };
        content.Controls.Add(environment, 1, 0);
        header.Controls.Add(content);
        var stripe = new Panel { Dock = DockStyle.Bottom, Height = 3, Tag = UiRole.Header };
        stripe.Paint += (_, e) =>
        {
            var unit = Math.Max(1, (int)Math.Round(32 * DeviceDpi / 96f));
            using var magenta = new SolidBrush(Color.FromArgb(255, 0, 240));
            using var yellow = new SolidBrush(_palette.Accent);
            using var mint = new SolidBrush(Color.FromArgb(0, 255, 162));
            e.Graphics.FillRectangle(magenta, 0, 0, unit, stripe.Height);
            e.Graphics.FillRectangle(yellow, unit, 0, unit, stripe.Height);
            e.Graphics.FillRectangle(mint, unit * 2, 0, unit, stripe.Height);
        };
        header.Controls.Add(stripe);
        return header;
    }

    private Control BuildActionBar()
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = LogicalPixels(68), Padding = new Padding(24, 12, 24, 12),
            ColumnCount = 2, RowCount = 1, Tag = UiRole.Surface
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        _launchButton.Dock = DockStyle.Fill;
        _restoreButton.Dock = DockStyle.Fill;
        _launchButton.Margin = new Padding(0, 0, 6, 0);
        _restoreButton.Margin = new Padding(6, 0, 0, 0);
        bar.Controls.Add(_launchButton, 0, 0);
        bar.Controls.Add(_restoreButton, 1, 0);
        return bar;
    }

    private TableLayoutPanel BuildLeftColumn()
    {
        var column = Stack(UiRole.Paper);
        column.Margin = new Padding(0, 0, 12, 0);
        AddStack(column, SectionHeading("// LAUNCH MODE", "启动模式"));

        var mode = Stack(UiRole.Surface, new Padding(16, 12, 16, 14));
        ConfigureChoice(_autoRadio);
        ConfigureChoice(_manualRadio);
        AddStack(mode, _autoRadio);
        AddStack(mode, _manualRadio);
        AddStack(mode, Caption("手动时区", UiRole.Muted, 8.5f, new Padding(0, 8, 0, 5)));
        _timeZoneBox.Dock = DockStyle.Top;
        _timeZoneBox.DropDownStyle = ComboBoxStyle.DropDown;
        _timeZoneBox.FlatStyle = FlatStyle.Flat;
        _timeZoneBox.Font = Font;
        _timeZoneBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _timeZoneBox.AutoCompleteSource = AutoCompleteSource.ListItems;
        _timeZoneBox.MaxDropDownItems = 14;
        _timeZoneBox.Margin = Padding.Empty;
        _timeZoneBox.Tag = UiRole.Input;
        _timeZoneBox.Items.AddRange(TimeZoneCatalog.All.Cast<object>().ToArray());
        var timeZoneFrame = new Panel { Dock = DockStyle.Top, Height = _timeZoneBox.PreferredHeight + 2, Padding = new Padding(1), Margin = new Padding(0, 0, 0, 5), Tag = UiRole.Rule };
        timeZoneFrame.Controls.Add(_timeZoneBox);
        AddStack(mode, timeZoneFrame);
        AddStack(mode, Caption("输入城市或 IANA 时区时自动切换为手动模式", UiRole.Muted, 8, Padding.Empty));
        AddStack(column, mode);

        AddStack(column, SectionHeading("// CONNECTION", "ChatGPT 出口检测"));
        var connection = Stack(UiRole.Surface, new Padding(16, 10, 16, 14));
        var rows = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Tag = UiRole.Surface, Margin = Padding.Empty };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddInfoRow(rows, "检测状态", _detectionState, 30);
        AddInfoRow(rows, "出口 IP", _ipValue, 30);
        AddInfoRow(rows, "位置", _locationValue, 30);
        AddInfoRow(rows, "IANA 时区", _zoneValue, 30);
        AddInfoRow(rows, "代理组", _proxyGroupValue, 38);
        AddInfoRow(rows, "当前节点", _proxyNodeValue, 38);
        AddInfoRow(rows, "检测方式", _providerValue, 52);
        AddStack(connection, rows);
        _detectButton.Width = 230;
        _detectButton.Height = LogicalPixels(44);
        _detectButton.Margin = new Padding(0, 10, 0, 0);
        AddStack(connection, _detectButton, DockStyle.Left);
        AddStack(column, connection);

        AddStack(column, SectionHeading("// PREFERENCES", "偏好设置"));
        var settings = Stack(UiRole.Surface, new Padding(16, 10, 16, 14));
        ConfigureChoice(_usageReminderCheck);
        ConfigureChoice(_closeToTrayCheck);
        AddStack(settings, _usageReminderCheck);
        AddStack(settings, _closeToTrayCheck);
        _shortcutButton.Width = 205;
        _shortcutButton.Height = LogicalPixels(44);
        _shortcutButton.Margin = new Padding(0, 10, 0, 0);
        AddStack(settings, _shortcutButton, DockStyle.Left);
        AddStack(column, settings);
        return column;
    }

    private TableLayoutPanel BuildRightColumn()
    {
        var column = Stack(UiRole.Paper);
        column.Margin = new Padding(12, 0, 0, 0);
        AddStack(column, SectionHeading("// CURRENT STATUS", "当前状态"));
        var status = Stack(UiRole.Surface, new Padding(16, 12, 16, 14));
        AddStack(status, Caption("启动方式", UiRole.Muted, 8.5f, new Padding(0, 0, 0, 4)));
        var activeMode = new TableLayoutPanel
        {
            Dock = DockStyle.Top, Height = 48, ColumnCount = 2, Margin = new Padding(0, 0, 0, 10), Tag = UiRole.Soft
        };
        activeMode.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        activeMode.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var current = new Label
        {
            Text = "当前", Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), Margin = Padding.Empty, Tag = UiRole.Accent
        };
        _overrideState.AutoSize = false;
        _overrideState.Dock = DockStyle.Fill;
        _overrideState.Height = 48;
        _overrideState.Padding = new Padding(10, 4, 10, 4);
        _overrideState.TextAlign = ContentAlignment.MiddleLeft;
        _overrideState.Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold);
        _overrideState.Tag = UiRole.Soft;
        _overrideState.Margin = Padding.Empty;
        activeMode.Controls.Add(current, 0, 0);
        activeMode.Controls.Add(_overrideState, 1, 0);
        AddStack(status, activeMode);
        AddStack(status, Caption("ChatGPT 进程", UiRole.Muted, 8.5f, new Padding(0, 0, 0, 4)));
        PrepareValue(_runtimeState, 42);
        AddStack(status, _runtimeState);
        AddStack(column, status);

        AddStack(column, SectionHeading("// CODEX USAGE", "Codex 限额"));
        var usage = Stack(UiRole.Surface, new Padding(16, 12, 16, 14));
        _usageState.Text = "尚未读取限额";
        PrepareValue(_usageState, 42, false);
        AddStack(usage, _usageState);
        AddStack(usage, Rule());
        AddStack(usage, Caption("5 小时限额 / 剩余", UiRole.Muted, 8.5f, new Padding(0, 8, 0, 2)));
        _fiveHourValue.Text = "—";
        PrepareMetric(_fiveHourValue);
        AddStack(usage, _fiveHourValue);
        PrepareValue(_fiveHourDetail, 0, false);
        AddStack(usage, _fiveHourDetail);
        AddStack(usage, Rule());
        AddStack(usage, Caption("每周限额 / 剩余", UiRole.Muted, 8.5f, new Padding(0, 8, 0, 2)));
        _weeklyValue.Text = "—";
        PrepareMetric(_weeklyValue);
        AddStack(usage, _weeklyValue);
        PrepareValue(_weeklyDetail, 0, false);
        AddStack(usage, _weeklyDetail);
        _refreshUsageButton.Width = 140;
        _refreshUsageButton.Height = LogicalPixels(44);
        _refreshUsageButton.Margin = new Padding(0, 8, 0, 0);
        AddStack(usage, _refreshUsageButton, DockStyle.Left);
        AddStack(column, usage);

        AddStack(column, SectionHeading("// DIAGNOSTICS", "状态与诊断"));
        _details.Text = "操作结果和诊断信息将显示在这里。";
        _details.BorderStyle = BorderStyle.FixedSingle;
        _details.Height = 96;
        _details.Dock = DockStyle.Top;
        _details.Margin = Padding.Empty;
        _details.Font = new Font("Microsoft YaHei UI", 8.5f);
        _details.Tag = UiRole.Input;
        AddStack(column, _details);

        return column;
    }

    private static TableLayoutPanel Stack(UiRole role, Padding? padding = null) => new()
    {
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top,
        ColumnCount = 1, RowCount = 0, Padding = padding ?? Padding.Empty,
        Margin = Padding.Empty, Tag = role
    };

    private static void AddStack(TableLayoutPanel stack, Control control, DockStyle dock = DockStyle.Top)
    {
        var row = stack.RowCount++;
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Dock = dock;
        stack.Controls.Add(control, 0, row);
    }

    private static Control SectionHeading(string english, string chinese)
    {
        var section = Stack(UiRole.Paper);
        section.Margin = new Padding(0, 6, 0, 8);
        AddStack(section, Eyebrow(english, UiRole.Muted));
        AddStack(section, new Label
        {
            Text = chinese, AutoSize = true, Font = new Font("Microsoft YaHei UI", 12, FontStyle.Bold),
            Margin = new Padding(0, 1, 0, 5), Tag = UiRole.Text
        });
        AddStack(section, Rule());
        return section;
    }

    private static Label Eyebrow(string text, UiRole role) => new()
    {
        Text = text, AutoSize = true, Font = IndustrialFonts.Wide(8.1f), UseCompatibleTextRendering = true,
        Margin = Padding.Empty, Tag = role
    };

    private static Label Caption(string text, UiRole role, float size, Padding margin) => new()
    {
        Text = text, AutoSize = true, Font = new Font("Microsoft YaHei UI", size), Margin = margin, Tag = role
    };

    private static Panel Rule() => new() { Height = 1, Margin = Padding.Empty, Tag = UiRole.Rule };

    private int LogicalPixels(int pixels) => (int)Math.Round(pixels * DeviceDpi / 96f);

    private void ConfigureChoice(ButtonBase choice)
    {
        choice.AutoSize = false;
        choice.Height = LogicalPixels(36);
        choice.Dock = DockStyle.Top;
        choice.FlatStyle = FlatStyle.Flat;
        choice.Margin = Padding.Empty;
        choice.Tag = UiRole.Choice;
    }

    private static void PrepareValue(Label label, int height, bool bold = true)
    {
        label.AutoSize = false;
        label.Height = height;
        label.Dock = DockStyle.Top;
        label.AutoEllipsis = true;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.Font = new Font("Microsoft YaHei UI", 9, bold ? FontStyle.Bold : FontStyle.Regular);
        label.Margin = Padding.Empty;
        label.Tag = UiRole.Text;
    }

    private void PrepareMetric(Label label)
    {
        PrepareValue(label, LogicalPixels(34));
        label.Font = IndustrialFonts.Wide(17);
        label.UseCompatibleTextRendering = true;
    }

    private static void AddInfoRow(TableLayoutPanel rows, string caption, Label value, int height)
    {
        var row = rows.RowCount++;
        rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var name = new Label
        {
            Text = caption, AutoSize = false, Height = height, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty, Tag = UiRole.Muted
        };
        PrepareValue(value, height);
        value.Dock = DockStyle.Fill;
        rows.Controls.Add(name, 0, row);
        rows.Controls.Add(value, 1, row);
    }

    private void UpdateColumns(int width)
    {
        if (_workspaceGrid is null || _leftColumn is null || _rightColumn is null) return;
        var shouldStack = width * 96f / DeviceDpi < 780;
        if (shouldStack == _stacked) return;
        _stacked = shouldStack;
        _workspaceGrid.SuspendLayout();
        _workspaceGrid.Controls.Clear();
        _workspaceGrid.ColumnStyles.Clear();
        _workspaceGrid.RowStyles.Clear();
        if (shouldStack)
        {
            _workspaceGrid.ColumnCount = 1;
            _workspaceGrid.RowCount = 2;
            _workspaceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _workspaceGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _workspaceGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _leftColumn.Margin = Padding.Empty;
            _rightColumn.Margin = new Padding(0, 12, 0, 0);
            _workspaceGrid.Controls.Add(_leftColumn, 0, 0);
            _workspaceGrid.Controls.Add(_rightColumn, 0, 1);
        }
        else
        {
            _workspaceGrid.ColumnCount = 2;
            _workspaceGrid.RowCount = 1;
            _workspaceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            _workspaceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            _workspaceGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _leftColumn.Margin = new Padding(0, 0, 12, 0);
            _rightColumn.Margin = new Padding(12, 0, 0, 0);
            _workspaceGrid.Controls.Add(_leftColumn, 0, 0);
            _workspaceGrid.Controls.Add(_rightColumn, 1, 0);
        }
        _workspaceGrid.ResumeLayout(true);
    }

    private void ApplyPalette()
    {
        _palette = IndustrialPalette.FromSystem(_darkThemeOverride);
        ApplyPaletteTo(this);
        Invalidate(true);
    }

    private void ApplyPaletteTo(Control control)
    {
        if (control is IndustrialButton button) button.Palette = _palette;
        if (control.Tag is UiRole role)
        {
            switch (role)
            {
                case UiRole.Paper: control.BackColor = _palette.Paper; control.ForeColor = _palette.Ink; break;
                case UiRole.Surface: control.BackColor = _palette.Surface; control.ForeColor = _palette.Ink; break;
                case UiRole.Soft: control.BackColor = _palette.Soft; control.ForeColor = _palette.Ink; break;
                case UiRole.Accent: control.BackColor = _palette.Accent; control.ForeColor = _palette.OnAccent; break;
                case UiRole.Text: control.ForeColor = _palette.Ink; break;
                case UiRole.Muted: control.ForeColor = _palette.Muted; break;
                case UiRole.Rule: control.BackColor = _palette.Rule; break;
                case UiRole.Header: control.BackColor = _palette.Header; control.ForeColor = _palette.HeaderText; break;
                case UiRole.HeaderText: control.ForeColor = _palette.HeaderText; break;
                case UiRole.HeaderMuted: control.ForeColor = _palette.HeaderMuted; break;
                case UiRole.Input: control.BackColor = _palette.Surface; control.ForeColor = _palette.Ink; break;
                case UiRole.Choice: control.BackColor = _palette.Surface; control.ForeColor = _palette.Ink; break;
            }
        }
        foreach (Control child in control.Controls) ApplyPaletteTo(child);
    }

    private void SubscribeToThemeChanges()
    {
        if (_darkThemeOverride is not null || _themeEventsSubscribed) return;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _themeEventsSubscribed = true;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(ApplyPalette); }
        catch (InvalidOperationException) when (IsDisposed || Disposing || !IsHandleCreated) { }
    }
}
