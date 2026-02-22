using System.Drawing;

namespace DesktopEarth.UI;

/// <summary>
/// Centralized theme colors for the settings UI.
/// When IsDarkMode is false, returns the original light-mode colors.
/// When IsDarkMode is true, returns dark-mode equivalents.
/// </summary>
public static class Theme
{
    public static bool IsDarkMode { get; set; }

    // --- Backgrounds ---

    public static Color FormBackground =>
        IsDarkMode ? Color.FromArgb(32, 32, 32) : SystemColors.Control;

    public static Color PanelBackground =>
        IsDarkMode ? Color.FromArgb(45, 45, 48) : SystemColors.Control;

    public static Color ControlBackground =>
        IsDarkMode ? Color.FromArgb(51, 51, 55) : SystemColors.Window;

    public static Color GroupBoxBackground =>
        IsDarkMode ? Color.FromArgb(40, 40, 43) : SystemColors.Control;

    public static Color TabBackground =>
        IsDarkMode ? Color.FromArgb(38, 38, 38) : SystemColors.Control;

    // --- Text ---

    public static Color PrimaryText =>
        IsDarkMode ? Color.FromArgb(230, 230, 230) : SystemColors.ControlText;

    public static Color SecondaryText =>
        IsDarkMode ? Color.FromArgb(160, 160, 160) : Color.Gray;

    public static Color DimText =>
        IsDarkMode ? Color.FromArgb(100, 100, 100) : Color.FromArgb(170, 170, 170);

    public static Color DescriptionText =>
        IsDarkMode ? Color.FromArgb(140, 140, 140) : Color.FromArgb(80, 80, 80);

    public static Color DetailText =>
        IsDarkMode ? Color.FromArgb(130, 130, 130) : Color.FromArgb(100, 100, 100);

    // --- Status colors ---

    public static Color ErrorText =>
        IsDarkMode ? Color.FromArgb(220, 100, 100) : Color.FromArgb(180, 80, 80);

    public static Color SuccessText =>
        IsDarkMode ? Color.FromArgb(80, 180, 80) : Color.FromArgb(60, 130, 60);

    public static Color WarningText =>
        IsDarkMode ? Color.FromArgb(220, 160, 40) : Color.FromArgb(180, 120, 0);

    // --- Accent panel (blue info box around view selection) ---

    public static Color AccentPanelBackground =>
        IsDarkMode ? Color.FromArgb(35, 40, 50) : Color.FromArgb(248, 250, 253);

    public static Color AccentPanelBorder =>
        IsDarkMode ? Color.FromArgb(60, 65, 75) : Color.FromArgb(204, 204, 204);

    public static Color AccentPanelStripe =>
        Color.FromArgb(74, 144, 217); // Same in both modes

    // --- Search chips: Blue (NPS park chips) ---

    public static Color BlueChipBackground =>
        IsDarkMode ? Color.FromArgb(35, 45, 65) : Color.FromArgb(235, 240, 250);

    public static Color BlueChipText =>
        IsDarkMode ? Color.FromArgb(140, 170, 220) : Color.FromArgb(40, 60, 100);

    public static Color BlueChipBorder =>
        IsDarkMode ? Color.FromArgb(60, 75, 100) : Color.FromArgb(180, 195, 220);

    // --- Search chips: Tan (Smithsonian art chips) ---

    public static Color TanChipBackground =>
        IsDarkMode ? Color.FromArgb(50, 45, 35) : Color.FromArgb(240, 235, 225);

    public static Color TanChipText =>
        IsDarkMode ? Color.FromArgb(200, 180, 140) : Color.FromArgb(80, 60, 30);

    public static Color TanChipBorder =>
        IsDarkMode ? Color.FromArgb(80, 70, 55) : Color.FromArgb(210, 195, 170);

    // --- Search chips: NASA Gallery ---

    public static Color GalleryChipBackground =>
        IsDarkMode ? Color.FromArgb(30, 40, 55) : Color.FromArgb(225, 235, 245);

    public static Color GalleryChipText =>
        IsDarkMode ? Color.FromArgb(130, 165, 215) : Color.FromArgb(30, 60, 100);

    public static Color GalleryChipBorder =>
        IsDarkMode ? Color.FromArgb(55, 70, 95) : Color.FromArgb(170, 190, 220);

    // --- ThumbnailGridPanel ---

    public static Color ThumbnailTitle =>
        IsDarkMode ? Color.FromArgb(200, 200, 200) : Color.FromArgb(60, 60, 60);

    public static Color Placeholder =>
        IsDarkMode ? Color.FromArgb(55, 55, 55) : Color.FromArgb(230, 230, 230);

    public static Color StarOutline =>
        IsDarkMode ? Color.FromArgb(120, 120, 120) : Color.FromArgb(180, 180, 180);

    // --- Button styling helper ---

    public static void StyleButton(Button btn)
    {
        if (IsDarkMode)
        {
            btn.FlatStyle = FlatStyle.Flat;
            btn.BackColor = Color.FromArgb(55, 55, 58);
            btn.ForeColor = Color.FromArgb(220, 220, 220);
            btn.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 75);
        }
        else
        {
            btn.FlatStyle = FlatStyle.Standard;
            btn.BackColor = SystemColors.Control;
            btn.ForeColor = SystemColors.ControlText;
        }
    }

    /// <summary>
    /// Style a ComboBox for the current theme.
    /// </summary>
    public static void StyleComboBox(ComboBox combo)
    {
        if (IsDarkMode)
        {
            combo.BackColor = Color.FromArgb(51, 51, 55);
            combo.ForeColor = Color.FromArgb(220, 220, 220);
            combo.FlatStyle = FlatStyle.Flat;
        }
    }

    /// <summary>
    /// Style a TextBox for the current theme.
    /// </summary>
    public static void StyleTextBox(TextBox textBox)
    {
        if (IsDarkMode)
        {
            textBox.BackColor = Color.FromArgb(51, 51, 55);
            textBox.ForeColor = Color.FromArgb(220, 220, 220);
            textBox.BorderStyle = BorderStyle.FixedSingle;
        }
    }

    /// <summary>
    /// Style a GroupBox for the current theme.
    /// </summary>
    public static void StyleGroupBox(GroupBox group)
    {
        if (IsDarkMode)
        {
            group.ForeColor = Color.FromArgb(200, 200, 200);
            group.BackColor = GroupBoxBackground;
        }
    }

    /// <summary>
    /// Style a CheckBox for the current theme.
    /// </summary>
    public static void StyleCheckBox(CheckBox check)
    {
        if (IsDarkMode)
        {
            check.ForeColor = Color.FromArgb(220, 220, 220);
        }
    }

    /// <summary>
    /// Style a TabControl for the current theme.
    /// </summary>
    public static void StyleTabControl(TabControl tabControl)
    {
        if (IsDarkMode)
        {
            tabControl.BackColor = TabBackground;
        }
    }
}
