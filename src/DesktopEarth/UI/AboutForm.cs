using System.Diagnostics;
using System.Drawing;
using System.Reflection;

namespace DesktopEarth.UI;

public class AboutForm : Form
{
    public AboutForm()
    {
        Text = "About Desktop Earth";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(380, 240);
        ShowInTaskbar = false;

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        var titleLabel = new Label
        {
            Text = "Desktop Earth",
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

        var creditLabel = new Label
        {
            Text = "Inspired by Desktop Earth by Marton Anka.\nRebuilt with .NET 8 and OpenGL.",
            Font = new Font("Segoe UI", 8),
            ForeColor = Color.Gray,
            AutoSize = true,
            Location = new Point(22, 130)
        };

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Size = new Size(80, 30),
            Location = new Point(270, 165)
        };
        AcceptButton = okButton;

        Controls.AddRange([titleLabel, versionLabel, descLabel, creditLabel, okButton]);
    }
}
