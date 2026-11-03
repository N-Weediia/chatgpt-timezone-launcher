using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ChatGptTimezoneLauncher;

// Standard yellow / neutral paper / tight cut / plain ground / compact / calm.
internal sealed record IndustrialPalette(
    Color Paper, Color Surface, Color Ink, Color Muted, Color Rule, Color Soft,
    Color Header, Color HeaderText, Color HeaderMuted, Color Accent, Color OnAccent,
    Color Primary, Color PrimaryText)
{
    public static IndustrialPalette FromSystem(bool? darkOverride = null)
    {
        if (SystemInformation.HighContrast)
            return new(SystemColors.Window, SystemColors.Window, SystemColors.WindowText, SystemColors.WindowText,
                SystemColors.WindowText, SystemColors.Control, SystemColors.Window, SystemColors.WindowText,
                SystemColors.WindowText, SystemColors.Highlight, SystemColors.HighlightText,
                SystemColors.Highlight, SystemColors.HighlightText);

        var dark = darkOverride ?? (Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1) is int value && value == 0);
        var yellow = Color.FromArgb(255, 250, 0);
        var header = Color.FromArgb(22, 22, 22);
        return dark
            ? new(Color.FromArgb(25, 25, 25), Color.FromArgb(34, 34, 34), Color.FromArgb(237, 237, 237),
                Color.FromArgb(185, 185, 185), Color.FromArgb(75, 75, 75), Color.FromArgb(46, 46, 46),
                header, Color.White, Color.FromArgb(198, 198, 198), yellow, header,
                Color.FromArgb(62, 62, 62), Color.White)
            : new(Color.FromArgb(244, 244, 242), Color.White, header, Color.FromArgb(92, 92, 92),
                Color.FromArgb(217, 217, 214), Color.FromArgb(228, 228, 224),
                header, Color.White, Color.FromArgb(198, 198, 198), yellow, header, header, Color.White);
    }
}

internal enum UiRole { Paper, Surface, Soft, Accent, Text, Muted, Rule, Header, HeaderText, HeaderMuted, Input, Choice }

internal static class IndustrialFonts
{
    private static readonly PrivateFontCollection Fonts = new();
    private static readonly IntPtr FontMemory;
    private static readonly FontFamily Family;

    static IndustrialFonts()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "ChatGptTimezoneLauncher.Fonts.Saira-Wide.ttf")
            ?? throw new InvalidOperationException("无法加载界面字体。");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        FontMemory = Marshal.AllocCoTaskMem(bytes.Length);
        Marshal.Copy(bytes, 0, FontMemory, bytes.Length);
        Fonts.AddMemoryFont(FontMemory, bytes.Length);
        _ = AddFontMemResourceEx(FontMemory, (uint)bytes.Length, IntPtr.Zero, out _);
        Family = Fonts.Families[0];
    }

    public static Font Wide(float size) => new(Family, size, FontStyle.Regular, GraphicsUnit.Point);

    [DllImport("gdi32.dll")]
    private static extern IntPtr AddFontMemResourceEx(IntPtr font, uint size, IntPtr reserved, out uint count);
}

internal sealed class IndustrialButton : Button
{
    private bool _hovered;
    private bool _pressed;
    private IndustrialPalette _palette = IndustrialPalette.FromSystem();

    public bool Primary { get; init; }
    public IndustrialPalette Palette { set { _palette = value; Invalidate(); } }

    public IndustrialButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        AutoSize = false;
        AccessibleRole = AccessibleRole.PushButton;
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? _palette.Paper);
        var active = Enabled && (_hovered || _pressed);
        var fill = !Enabled ? _palette.Soft : active ? _palette.Accent : Primary ? _palette.Primary : _palette.Soft;
        var ink = !Enabled ? _palette.Muted : active ? _palette.OnAccent : Primary ? _palette.PrimaryText : _palette.Ink;
        var cut = Math.Max(5, (int)Math.Round(7 * DeviceDpi / 96f));
        using var shape = new GraphicsPath();
        shape.AddPolygon(new Point[] { new(0, 0), new(Width - 1, 0), new(Width - 1, Height - cut),
            new(Width - cut, Height - 1), new(0, Height - 1) });
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, shape);
        var bounds = new Rectangle(10, 2, Math.Max(0, Width - cut - 18), Math.Max(0, Height - 4));
        TextRenderer.DrawText(g, Text, Font, bounds, ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -5, -5), ink, fill);
    }
}
