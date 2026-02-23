using System.Diagnostics;
using System.Drawing;
using System.Reflection;

namespace DesktopEarth.UI;

public class AboutForm : Form
{
    public AboutForm()
    {
        Text = "About Blue Marble Desktop";
        Icon = TrayApplicationContext.LoadAppIcon();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(400, 280);
        ShowInTaskbar = false;
        BackColor = Theme.FormBackground;
        ForeColor = Theme.PrimaryText;

        // Dark title bar and window borders (Windows 10 1809+ / Windows 11)
        Theme.ApplyDarkMode(this);

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        var titleLabel = new Label
        {
            Text = "Blue Marble Desktop",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(20, 20)
        };

        var versionLabel = new Label
        {
            Text = $"Version {version}",
            Font = new Font("Segoe UI", 10),
            AutoSize = true,
            Location = new Point(22, 55)
        };

        var descLabel = new Label
        {
            Text = "A dynamic wallpaper app that renders a 3D Earth\nshowing real-time day/night illumination.",
            Font = new Font("Segoe UI", 9),
            AutoSize = true,
            Location = new Point(22, 85)
        };

        var authorLabel = new Label
        {
            Text = "Created by Alex and Claude (Anthropic)",
            Font = new Font("Segoe UI", 9),
            AutoSize = true,
            Location = new Point(22, 120)
        };

        var creditLabel = new Label
        {
            Text = "Built with .NET 8 and OpenGL.\nImagery from NASA, National Park Service, and\nSmithsonian Institution. All public domain.",
            Font = new Font("Segoe UI", 8),
            ForeColor = Theme.SecondaryText,
            AutoSize = true,
            Location = new Point(22, 150)
        };

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Size = new Size(80, 30),
            Location = new Point(290, 205)
        };
        Theme.StyleButton(okButton);
        AcceptButton = okButton;

        Controls.AddRange([titleLabel, versionLabel, descLabel, authorLabel, creditLabel, okButton]);
    }
}
