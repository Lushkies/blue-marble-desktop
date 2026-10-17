using System.Drawing;

namespace DesktopEarth.UI.Controls;

/// <summary>
/// Bottom bar for image pickers: what's selected, what will happen, a
/// favorite toggle and the one explicit "Set on …" action. Selecting an image
/// never changes the wallpaper by itself; this button does.
/// </summary>
public class SelectionFooter : Panel
{
    private readonly PreviewPanel _thumb;
    private readonly StudioLabel _title;
    private readonly StudioLabel _note;
    private readonly StudioButton _favorite;
    private readonly StudioButton _set;

    public event EventHandler? SetClicked;
    public event EventHandler? FavoriteClicked;

    public SelectionFooter()
    {
        BackColor = Theme.Chrome;
        Height = 68;
        Padding = new Padding(24, 12, 24, 12);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);

        _thumb = new PreviewPanel { Size = new Size(70, 44), EmptyText = "", Location = new Point(24, 12) };

        _title = new StudioLabel { AutoSize = false, AutoEllipsis = true, Font = Theme.Semibold(9.75f), Location = new Point(106, 13), Size = new Size(300, 20) };
        _note = new StudioLabel { AutoSize = false, AutoEllipsis = true, ForeColor = Theme.TextMuted, Font = Theme.UiFont(8.75f), Location = new Point(106, 35), Size = new Size(300, 18) };

        _favorite = new StudioButton
        {
            Text = "Favorite", IconGlyph = Glyph.Star, Variant = ButtonVariant.Secondary,
            Size = new Size(112, 36), Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _favorite.Click += (_, _) => FavoriteClicked?.Invoke(this, EventArgs.Empty);

        _set = new StudioButton
        {
            Text = "Set", Variant = ButtonVariant.Primary,
            Size = new Size(200, 36), Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _set.Click += (_, _) => SetClicked?.Invoke(this, EventArgs.Empty);

        Controls.AddRange([_thumb, _title, _note, _favorite, _set]);
        ShowEmpty("Pick a picture, then press Set", "");
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_set is null) return; // Base construction lays out before the children exist
        int gap = (int)(10 * DeviceDpi / 96f);
        int y = (Height - _set.Height) / 2;
        _set.Location = new Point(Width - Padding.Right - _set.Width, y);
        _favorite.Location = new Point(_set.Left - gap - _favorite.Width, y);
        int textRight = _favorite.Left - gap * 2;
        _title.Width = Math.Max(10, textRight - _title.Left);
        _note.Width = Math.Max(10, textRight - _note.Left);
        _thumb.Top = (Height - _thumb.Height) / 2;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Divider);
        e.Graphics.DrawLine(pen, 0, 0, Width, 0);
    }

    /// <summary>Nothing selected: grey out the actions and show a hint.</summary>
    public void ShowEmpty(string hint, string note)
    {
        _thumb.Image = null;
        _title.Text = hint;
        _title.ForeColor = Theme.TextMuted;
        _note.Text = note;
        _favorite.Enabled = false;
        _set.Enabled = false;
        _set.Text = "Set";
    }

    /// <summary>Show a selection. <paramref name="thumbnail"/> is copied, not owned.</summary>
    public void ShowSelection(string title, string note, Image? thumbnail, bool favorited,
        string setLabel, bool canSet)
    {
        _thumb.Image = thumbnail != null ? new Bitmap(thumbnail) : null;
        _title.Text = title;
        _title.ForeColor = Theme.Text;
        _note.Text = note;
        SetFavorited(favorited);
        _favorite.Enabled = true;
        _set.Text = setLabel;
        _set.Enabled = canSet;
    }

    public void SetNote(string note) => _note.Text = note;

    public void SetFavorited(bool favorited)
    {
        _favorite.IconGlyph = favorited ? Glyph.StarFilled : Glyph.Star;
        _favorite.IconColor = favorited ? Theme.Star : null;
        _favorite.Text = favorited ? "Favorited" : "Favorite";
    }

    public void SetCanSet(bool canSet, string label)
    {
        _set.Text = label;
        _set.Enabled = canSet;
    }
}
