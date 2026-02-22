using System.Drawing;
using System.Runtime.InteropServices;

namespace DesktopEarth.UI;

/// <summary>
/// A TabControl subclass that eliminates the white native border and paints dark tab headers
/// in dark mode. In light mode, behaves identically to a standard TabControl.
///
/// Strategy: After the base class paints, we paint over the ENTIRE tab strip area
/// (including borders between tabs and the content border) with our own dark rendering.
/// This completely covers all native chrome — no partial painting or gap-filling.
/// </summary>
public class DarkTabControl : TabControl
{
    private const int WM_PAINT = 0x000F;
    private const int TCM_ADJUSTRECT = 0x1328;

    /// <summary>
    /// Apply dark mode styling. Call once after construction.
    /// In light mode this is a no-op.
    /// </summary>
    public void ApplyTheme()
    {
        if (!Theme.IsDarkMode) return;

        BackColor = Theme.TabBackground;

        // We do NOT use DrawMode.OwnerDrawFixed — we paint everything ourselves
        // in WM_PAINT after base renders. This avoids conflicts with the native
        // tab chrome that OwnerDrawFixed doesn't fully suppress.
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

        // After base paints, paint over ALL native chrome with our dark rendering
        if (m.Msg == WM_PAINT && TabCount > 0)
        {
            PaintOverNativeChrome();
        }
    }

    /// <summary>
    /// Paint over the entire tab strip + border areas. This is a complete repaint of all
    /// native chrome — the tab strip background, each tab header, the border between
    /// tabs and content, and the left/right/bottom edges.
    /// </summary>
    private void PaintOverNativeChrome()
    {
        using var g = CreateGraphics();
        var formBg = Theme.FormBackground;
        var selectedBg = Color.FromArgb(48, 48, 48);
        var unselectedBg = Color.FromArgb(32, 32, 32);

        // 1. Fill the ENTIRE tab strip region (from top of control to bottom of tab rects)
        //    This covers all native borders, gaps between tabs, background behind tabs.
        var lastTab = GetTabRect(TabCount - 1);
        int tabStripBottom = lastTab.Bottom;

        using var stripBrush = new SolidBrush(formBg);
        g.FillRectangle(stripBrush, 0, 0, Width, tabStripBottom + 2);

        // 2. Draw each tab header on top of the clean dark background
        using var selectedBrush = new SolidBrush(selectedBg);
        using var unselectedBrush = new SolidBrush(unselectedBg);
        using var selectedTextBrush = new SolidBrush(Color.FromArgb(230, 230, 230));
        using var unselectedTextBrush = new SolidBrush(Color.FromArgb(150, 150, 150));
        var textFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        for (int i = 0; i < TabCount; i++)
        {
            var tabRect = GetTabRect(i);
            bool isSelected = SelectedIndex == i;

            // Fill tab background
            g.FillRectangle(isSelected ? selectedBrush : unselectedBrush, tabRect);

            // Draw tab text
            g.DrawString(
                TabPages[i].Text,
                Font,
                isSelected ? selectedTextBrush : unselectedTextBrush,
                tabRect,
                textFormat);
        }

        // 3. Draw a subtle line between the selected tab and the content area
        //    to create a visual connection (selected tab blends into content)
        if (SelectedIndex >= 0)
        {
            var selRect = GetTabRect(SelectedIndex);
            using var connectorBrush = new SolidBrush(selectedBg);
            g.FillRectangle(connectorBrush, selRect.Left, tabStripBottom, selRect.Width, 2);
        }

        // 4. Fill left, right, and bottom edges (native 1px border around content area)
        g.FillRectangle(stripBrush, 0, tabStripBottom, 1, Height - tabStripBottom);    // left
        g.FillRectangle(stripBrush, Width - 1, tabStripBottom, 1, Height - tabStripBottom); // right
        g.FillRectangle(stripBrush, 0, Height - 1, Width, 1);                           // bottom
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
