using System.Drawing;
using System.Runtime.InteropServices;

namespace DesktopEarth.UI;

/// <summary>
/// A TabControl subclass that eliminates the white native border and paints dark tab headers
/// in dark mode. In light mode, behaves identically to a standard TabControl.
/// </summary>
public class DarkTabControl : TabControl
{
    private const int WM_PAINT = 0x000F;
    private const int TCM_ADJUSTRECT = 0x1328;

    /// <summary>
    /// Apply dark mode styling. Call once after construction.
    /// In light mode this is a no-op; in dark mode it sets up owner-draw tab painting.
    /// </summary>
    public void ApplyTheme()
    {
        if (!Theme.IsDarkMode) return;

        BackColor = Theme.TabBackground;
        DrawMode = TabDrawMode.OwnerDrawFixed;
        DrawItem += OnDrawTab;
    }

    private static void OnDrawTab(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tc) return;
        var page = tc.TabPages[e.Index];
        var bounds = e.Bounds;
        bool isSelected = tc.SelectedIndex == e.Index;

        using var bgBrush = new SolidBrush(isSelected
            ? Color.FromArgb(48, 48, 48)
            : Color.FromArgb(32, 32, 32));
        e.Graphics.FillRectangle(bgBrush, bounds);

        using var textBrush = new SolidBrush(isSelected
            ? Color.FromArgb(230, 230, 230)
            : Color.FromArgb(150, 150, 150));
        var textFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        e.Graphics.DrawString(page.Text, tc.Font, textBrush, bounds, textFormat);
    }

    protected override void WndProc(ref Message m)
    {
        if (!Theme.IsDarkMode)
        {
            base.WndProc(ref m);
            return;
        }

        // Let the base class process the message first
        base.WndProc(ref m);

        // After base processes TCM_ADJUSTRECT, expand the display rect
        // to eliminate the native 3D border around tab pages
        if (m.Msg == TCM_ADJUSTRECT && !DesignMode && m.LParam != IntPtr.Zero)
        {
            var rc = Marshal.PtrToStructure<RECT>(m.LParam);
            rc.Left -= 4;
            rc.Right += 4;
            rc.Top -= 2;
            rc.Bottom += 4;
            Marshal.StructureToPtr(rc, m.LParam, false);
        }

        // After base paints, paint over remaining native chrome with dark colors
        if (m.Msg == WM_PAINT && TabCount > 0)
        {
            PaintDarkChrome();
        }
    }

    /// <summary>
    /// Paint over the native tab strip background and any border remnants.
    /// </summary>
    private void PaintDarkChrome()
    {
        using var g = CreateGraphics();
        var bgColor = Theme.FormBackground;

        // Fill the area behind the tab headers (the strip above the tab pages)
        var lastTab = GetTabRect(TabCount - 1);
        int tabStripHeight = lastTab.Bottom;

        using var bgBrush = new SolidBrush(bgColor);

        // Fill area to the right of the last tab (empty tab strip background)
        int rightStart = lastTab.Right;
        if (rightStart < Width)
        {
            g.FillRectangle(bgBrush, rightStart, 0, Width - rightStart, tabStripHeight);
        }

        // Fill area to the left of the first tab (if any padding)
        var firstTab = GetTabRect(0);
        if (firstTab.Left > 0)
        {
            g.FillRectangle(bgBrush, 0, 0, firstTab.Left, tabStripHeight);
        }

        // Fill bottom edge below tab strip and above content (native border line)
        using var tabBgBrush = new SolidBrush(Color.FromArgb(48, 48, 48));
        g.FillRectangle(tabBgBrush, 0, tabStripHeight, Width, 2);

        // Fill left, right, and bottom edges (1px native border that shows through)
        g.FillRectangle(bgBrush, 0, 0, 1, Height);                    // left
        g.FillRectangle(bgBrush, Width - 1, 0, 1, Height);            // right
        g.FillRectangle(bgBrush, 0, Height - 1, Width, 1);            // bottom
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
