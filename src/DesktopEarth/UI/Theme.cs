using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace DesktopEarth.UI;

/// <summary>
/// The "Studio" design tokens (colors, type, radii) for both themes, plus the
/// Win32 calls that make native window chrome and scrollbars follow the theme.
/// Custom-painted controls read these at paint time, so they always match.
/// </summary>
public static partial class Theme
{
    public static bool IsDarkMode { get; set; }

    // ─── Surfaces ───
    /// <summary>Main content background.</summary>
    public static Color Ground => IsDarkMode ? Hex(0x0E1116) : Hex(0xF4F6F9);
    /// <summary>Sidebar, footers and title bar.</summary>
    public static Color Chrome => IsDarkMode ? Hex(0x0B0D12) : Hex(0xEAEDF2);
    /// <summary>Cards.</summary>
    public static Color Surface => IsDarkMode ? Hex(0x141820) : Hex(0xFFFFFF);
    /// <summary>Buttons, hovered rows, inputs on cards.</summary>
    public static Color Raised => IsDarkMode ? Hex(0x1A1F28) : Hex(0xF0F3F7);
    public static Color RaisedHover => IsDarkMode ? Hex(0x232936) : Hex(0xE4E9F0);
    /// <summary>Text boxes and wells sitting on a card.</summary>
    public static Color Well => IsDarkMode ? Hex(0x0E1116) : Hex(0xF6F8FB);

    public static Color Border => IsDarkMode ? Hex(0x1F2530) : Hex(0xDDE2EA);
    public static Color BorderStrong => IsDarkMode ? Hex(0x2A313D) : Hex(0xC9D0DA);
    public static Color Divider => IsDarkMode ? Hex(0x1C212A) : Hex(0xE1E5EC);

    // ─── Text ───
    public static Color Text => IsDarkMode ? Hex(0xE9EDF3) : Hex(0x121620);
    public static Color TextSecondary => IsDarkMode ? Hex(0xC9D1DC) : Hex(0x3A4352);
    /// <summary>Captions and labels (≥4.5:1 on Surface in both themes).</summary>
    public static Color TextMuted => IsDarkMode ? Hex(0x949EAD) : Hex(0x5B6676);

    // ─── Accent and status ───
    public static Color Accent => IsDarkMode ? Hex(0x5B9DFF) : Hex(0x2F6FE0);
    public static Color AccentHover => IsDarkMode ? Hex(0x79AEFF) : Hex(0x285FC2);
    /// <summary>Text drawn on an Accent fill.</summary>
    public static Color OnAccent => IsDarkMode ? Hex(0x0B1220) : Hex(0xFFFFFF);
    public static Color AccentSoft => IsDarkMode ? Color.FromArgb(36, 0x5B, 0x9D, 0xFF) : Color.FromArgb(28, 0x2F, 0x6F, 0xE0);
    public static Color Success => IsDarkMode ? Hex(0x4CC38A) : Hex(0x1E8A55);
    public static Color Warning => IsDarkMode ? Hex(0xF2B45A) : Hex(0xA8650B);
    public static Color Error => IsDarkMode ? Hex(0xF07178) : Hex(0xC23B44);
    public static Color Star => Hex(0xF5C04A);
    public static Color Gold => Hex(0xF2C14E);

    // ─── Legacy names still used by a few dialogs ───
    public static Color FormBackground => Ground;
    public static Color PrimaryText => Text;
    public static Color SecondaryText => TextMuted;
    public static Color ControlBackground => Raised;
    public static Color Placeholder => IsDarkMode ? Hex(0x1A1F28) : Hex(0xE6EAF0);
    public static Color ErrorText => Error;
    public static Color SuccessText => Success;
    public static Color WarningText => Warning;

    // ─── Shape ───
    public const int RadiusCard = 12;
    public const int RadiusControl = 8;

    // ─── Type ───
    // Segoe UI Variable is the Windows 11 UI face; fall back to Segoe UI on 10
    private static readonly Lazy<string> TextFamily = new(() => FirstInstalled("Segoe UI Variable Text", "Segoe UI"));
    private static readonly Lazy<string> DisplayFamily = new(() => FirstInstalled("Segoe UI Variable Display", "Segoe UI Variable Text", "Segoe UI"));
    private static readonly Dictionary<(string, float, FontStyle), Font> FontCache = new();

    /// <summary>Body font at a size in points (shared, do not dispose).</summary>
    public static Font UiFont(float points = 9.75f, FontStyle style = FontStyle.Regular) =>
        CachedFont(TextFamily.Value, points, style);

    /// <summary>Headline font (shared, do not dispose).</summary>
    public static Font DisplayFont(float points, FontStyle style = FontStyle.Bold) =>
        CachedFont(DisplayFamily.Value, points, style);

    /// <summary>Semibold where the family has it (Variable), else bold.</summary>
    public static Font Semibold(float points = 9.75f)
    {
        string semibold = TextFamily.Value == "Segoe UI Variable Text" ? "Segoe UI Variable Text Semibold" : "";
        return semibold.Length > 0 && IsInstalled(semibold)
            ? CachedFont(semibold, points, FontStyle.Regular)
            : CachedFont(TextFamily.Value, points, FontStyle.Bold);
    }

    private static Font CachedFont(string family, float points, FontStyle style)
    {
        lock (FontCache)
        {
            if (!FontCache.TryGetValue((family, points, style), out var font))
            {
                font = new Font(family, points, style);
                FontCache[(family, points, style)] = font;
            }
            return font;
        }
    }

    private static string FirstInstalled(params string[] families) =>
        families.FirstOrDefault(IsInstalled) ?? "Segoe UI";

    private static HashSet<string>? _installed;
    private static bool IsInstalled(string family)
    {
        _installed ??= new HashSet<string>(
            new InstalledFontCollection().Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        return _installed.Contains(family);
    }

    // ─── Drawing helpers ───
    public static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Anti-aliased, ClearType-text Graphics setup for custom paint.</summary>
    public static void HighQuality(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    public static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
        (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    // ─── Win32: window chrome and scrollbars ───
    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;
    private const int DWMWCP_ROUND = 2;

    [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int SetPreferredAppMode(int preferredAppMode);

    [DllImport("uxtheme.dll", EntryPoint = "#133", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool AllowDarkModeForWindow(IntPtr hwnd, bool allow);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? pszSubAppName, string? pszSubIdList);

    /// <summary>Opt the process into dark native scrollbars/menus (call before creating windows).</summary>
    public static void EnableDarkScrollbars()
    {
        try { SetPreferredAppMode(IsDarkMode ? 1 : 0); }
        catch { } // Older Windows versions
    }

    /// <summary>
    /// Theme the native title bar: dark mode flag, plus on Windows 11 a caption
    /// and border colored like the app chrome and rounded corners.
    /// </summary>
    public static void ApplyDarkMode(Form form)
    {
        try
        {
            int dark = IsDarkMode ? 1 : 0;
            DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            if (IsDarkMode) AllowDarkModeForWindow(form.Handle, true);

            // Windows 11 only — earlier versions ignore these attributes
            int caption = ToColorRef(Chrome);
            DwmSetWindowAttribute(form.Handle, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            int border = ToColorRef(Divider);
            DwmSetWindowAttribute(form.Handle, DWMWA_BORDER_COLOR, ref border, sizeof(int));
            int text = ToColorRef(TextSecondary);
            DwmSetWindowAttribute(form.Handle, DWMWA_TEXT_COLOR, ref text, sizeof(int));
            int round = DWMWCP_ROUND;
            DwmSetWindowAttribute(form.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        }
        catch { } // Graceful fallback on older Windows versions
    }

    private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    /// <summary>
    /// Dark native scrollbars for an AutoScroll container. Never use on
    /// TrackBars (it makes them invisible) — they are custom-drawn anyway.
    /// </summary>
    public static void StyleScrollableControl(Control control)
    {
        if (!IsDarkMode) return;
        if (control.IsHandleCreated)
            ApplyExplorerDark(control);
        else
            control.HandleCreated += (sender, _) => { if (sender is Control c) ApplyExplorerDark(c); };
    }

    private static void ApplyExplorerDark(Control control)
    {
        try { SetWindowTheme(control.Handle, "DarkMode_Explorer", null); }
        catch { }
    }

    /// <summary>Flat button styling for plain dialogs (MessageBox-like prompts).</summary>
    public static void StyleButton(Button btn)
    {
        btn.FlatStyle = FlatStyle.Flat;
        btn.BackColor = Raised;
        btn.ForeColor = Text;
        btn.FlatAppearance.BorderColor = BorderStrong;
        btn.FlatAppearance.MouseOverBackColor = RaisedHover;
        btn.Font = UiFont(9.5f);
    }

    public static void StyleTextBox(TextBox textBox)
    {
        textBox.BackColor = Well;
        textBox.ForeColor = Text;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.Font = UiFont(10f);
    }

    public static void StyleDateTimePicker(DateTimePicker dtp)
    {
        dtp.CalendarMonthBackground = Surface;
        dtp.CalendarForeColor = Text;
        dtp.CalendarTitleBackColor = Raised;
        dtp.CalendarTitleForeColor = Text;
        dtp.CalendarTrailingForeColor = TextMuted;
        dtp.Font = UiFont(9.75f);
    }
}
