using System.Drawing;
using System.Globalization;
using DesktopEarth.UI.Controls;

namespace DesktopEarth.UI;

/// <summary>
/// Photos view of the Wallpaper page: browse a source, select an image, and
/// press "Set" — selecting alone never changes the wallpaper.
/// </summary>
public partial class SettingsForm
{
    private static readonly ImageSource[] SourceOrder =
    [
        ImageSource.NasaGallery, ImageSource.NasaApod, ImageSource.NasaEpic,
        ImageSource.NationalParks, ImageSource.Smithsonian, ImageSource.UserImages
    ];
    private static readonly string[] SourceLabels =
        ["NASA Gallery", "Picture of the Day", "EPIC · Earth now", "National Parks", "Smithsonian", "My Images"];

    private static readonly RotationSource[] RotationOrder =
    [
        RotationSource.Favorites, RotationSource.All, RotationSource.NasaGallery, RotationSource.NasaApod,
        RotationSource.NasaEpic, RotationSource.NationalParks, RotationSource.Smithsonian, RotationSource.UserImages
    ];
    private static readonly string[] RotationLabels =
        ["Favorites", "All sources", "NASA Gallery", "Picture of the Day", "EPIC", "National Parks", "Smithsonian", "My Images"];

    private readonly StudioButton[] _sourceChips = new StudioButton[6];
    private readonly Dictionary<ImageSource, ThumbnailGridPanel> _grids = new();
    private readonly Dictionary<ImageSource, Panel> _toolbars = new();
    private readonly HashSet<ImageSource> _loadedSources = new();
    private readonly Dictionary<ImageSource, int> _loadGenerations = new();
    private ImageSource _browseSource = ImageSource.NasaGallery;

    private ToggleSwitch _rotateSwitch = null!;
    private ModernComboBox _rotateSourceCombo = null!;
    private Panel _exclusionRow = null!;
    private readonly List<StudioButton> _exclusionChips = new();
    private StudioLabel _rotationNote = null!;
    private Card _rotationCard = null!;
    private Panel _rotationNoteRow = null!;
    private Panel _toolbarHost = null!;
    private SelectionFooter _photosFooter = null!;
    private ImageSourceInfo? _photosSelection;

    // Source toolbars
    private TextField _gallerySearch = null!, _npsSearch = null!, _smithsonianSearch = null!;
    private SegmentedControl _apodModeSegment = null!, _epicTypeSegment = null!, _epicWhenSegment = null!;
    private ModernComboBox _apodDaysCombo = null!;
    private DateTimePicker _apodDatePicker = null!, _epicDatePicker = null!;
    private StudioLabel _epicLatestLabel = null!, _userImagesCount = null!;
    private DateTime? _epicLatestDate;

    private readonly ApodApiClient _apodApi = new();
    private readonly EpicApiClient _epicApi = new();
    private readonly NpsApiClient _npsApi = new();
    private readonly SmithsonianApiClient _smithsonianApi = new();
    private readonly NasaGalleryApiClient _galleryApi = new();

    private static readonly int[] ApodDaysValues = [7, 14, 30, 60, 90];

    private Panel BuildPhotosView()
    {
        var view = new Panel { BackColor = Theme.Ground };

        // Source chips + auto-rotate
        var sourceBar = new Panel { Height = 58, BackColor = Theme.Ground, Padding = new Padding(24, 14, 24, 0) };
        var chips = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Ground };
        for (int i = 0; i < SourceOrder.Length; i++)
        {
            var source = SourceOrder[i];
            var chip = new StudioButton
            {
                Text = SourceLabels[i], Variant = ButtonVariant.Chip, AutoSize = true, AutoHeight = 32,
                Margin = new Padding(0, 0, 8, 0), Font = Theme.UiFont(9.25f)
            };
            chip.Click += (_, _) => ShowSource(source, userPicked: true);
            _sourceChips[i] = chip;
            chips.Controls.Add(chip);
        }

        sourceBar.Controls.Add(chips);

        // Auto-rotate card: switch, source, a one-line explanation, and (for
        // "All sources") which sources to include
        _rotationNoteRow = new Panel { Height = 62, BackColor = Theme.Ground, Padding = new Padding(24, 6, 24, 6) };
        _rotationCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(14, 0, 14, 0) };
        _rotateSwitch = new ToggleSwitch { Location = new Point(14, 14), AccessibleName = "Auto-rotate" };
        _rotateSwitch.Toggled += (_, _) => OnRotateToggled();
        var rotateLabel = new StudioLabel { Text = "Auto-rotate", Font = Theme.Semibold(9.75f), Location = new Point(66, 15) };
        _rotateSourceCombo = new ModernComboBox { Location = new Point(166, 9), Width = 170, AccessibleName = "Rotate through" };
        _rotateSourceCombo.Items.AddRange(RotationLabels);
        _rotateSourceCombo.SelectedIndexChanged += (_, _) => OnRotationSourceChanged();
        _rotationNote = new StudioLabel
        {
            AutoSize = false, AutoEllipsis = true, Location = new Point(350, 15), Size = new Size(400, 20),
            ForeColor = Theme.TextMuted, Font = Theme.UiFont(9f)
        };
        _rotationCard.Controls.AddRange([_rotateSwitch, rotateLabel, _rotateSourceCombo, _rotationNote]);
        _rotationCard.Layout += (_, _) => _rotationNote.Width = Math.Max(Dpi(60), _rotationCard.Width - _rotationNote.Left - Dpi(14));
        _rotationNoteRow.Controls.Add(_rotationCard);

        // "All sources" inclusion chips, second line of the card
        _exclusionRow = new Panel { Location = new Point(14, 50), Size = new Size(800, 34), BackColor = Theme.Surface, Visible = false };
        var exFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Surface };
        exFlow.Controls.Add(new StudioLabel { Text = "Include", ForeColor = Theme.TextMuted, Margin = new Padding(0, 7, 10, 0) });
        (string, RotationSource)[] exclusions =
        [
            ("Gallery", RotationSource.NasaGallery), ("APOD", RotationSource.NasaApod), ("EPIC", RotationSource.NasaEpic),
            ("Parks", RotationSource.NationalParks), ("Smithsonian", RotationSource.Smithsonian),
            ("My Images", RotationSource.UserImages), ("Favorites", RotationSource.Favorites)
        ];
        foreach (var (label, src) in exclusions)
        {
            var chip = new StudioButton
            {
                Text = label, Variant = ButtonVariant.Chip, AutoSize = true, AutoHeight = 28,
                Margin = new Padding(0, 0, 6, 0), Font = Theme.UiFont(8.75f), Tag = src
            };
            chip.Click += (_, _) =>
            {
                chip.Selected = !chip.Selected;
                _settings.ExcludedRotationSources = _exclusionChips
                    .Where(c => !c.Selected).Select(c => (RotationSource)c.Tag!).ToList();
                _settingsManager.Save(); // Global; takes effect on the next rotation
            };
            _exclusionChips.Add(chip);
            exFlow.Controls.Add(chip);
        }
        _exclusionRow.Controls.Add(exFlow);
        _rotationCard.Controls.Add(_exclusionRow);

        // Per-source toolbars
        _toolbarHost = new Panel { Height = 52, BackColor = Theme.Ground, Padding = new Padding(24, 10, 24, 0) };
        _toolbars[ImageSource.NasaGallery] = SearchToolbar(out _gallerySearch, "Search NASA's image library",
            NasaGalleryApiClient.SuggestedQueries, () => RefreshGallery());
        _toolbars[ImageSource.NationalParks] = SearchToolbar(out _npsSearch, "Search a national park",
            NpsApiClient.ParkCodes.Keys.OrderBy(k => k).ToArray(), () => RefreshNps());
        _toolbars[ImageSource.Smithsonian] = SearchToolbar(out _smithsonianSearch, "Search the Smithsonian collection",
            ["Landscape Painting", "Sunset", "Mountain", "Ocean", "Forest", "Butterfly", "Architecture",
             "Waterfall", "Desert", "Volcano", "Glacier", "Photography", "Night Sky"], () => RefreshSmithsonian());
        _toolbars[ImageSource.NasaApod] = ApodToolbar();
        _toolbars[ImageSource.NasaEpic] = EpicToolbar();
        _toolbars[ImageSource.UserImages] = UserImagesToolbar();
        foreach (var tb in _toolbars.Values)
        {
            tb.Dock = DockStyle.Fill;
            tb.Visible = false;
            _toolbarHost.Controls.Add(tb);
        }

        // Grids
        var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Ground, Padding = new Padding(24, 12, 12, 0) };
        foreach (var source in SourceOrder)
        {
            var grid = new ThumbnailGridPanel(_imageCache) { Dock = DockStyle.Fill, Visible = false };
            grid.ImageSelected += (_, img) => { _photosSelection = img; UpdatePhotosFooter(); };
            grid.ImageActivated += (_, img) => { _photosSelection = img; ApplyImage(img); };
            grid.FavoriteToggled += (_, img) => { ToggleFavorite(img); UpdatePhotosFooter(); };
            _grids[source] = grid;
            gridHost.Controls.Add(grid);
        }

        _photosFooter = new SelectionFooter { Dock = DockStyle.Bottom };
        _photosFooter.SetClicked += (_, _) => { if (_photosSelection != null) ApplyImage(_photosSelection); };
        _photosFooter.FavoriteClicked += (_, _) =>
        {
            if (_photosSelection == null) return;
            _photosSelection.IsFavorited = !_photosSelection.IsFavorited;
            ToggleFavorite(_photosSelection);
            _grids[_browseSource].RepaintCells();
            UpdatePhotosFooter();
        };

        view.Controls.Add(gridHost);
        view.Controls.Add(_photosFooter);
        StackTop(view, sourceBar, _rotationNoteRow, _toolbarHost);
        return view;
    }

    // ─── Toolbars ───

    private Panel SearchToolbar(out TextField box, string placeholder, string[] suggestions, Action search)
    {
        var bar = new Panel { BackColor = Theme.Ground };
        var sb = new TextField { Dock = DockStyle.Left, Width = 300, PlaceholderText = placeholder + " (Enter)" };
        sb.Submitted += (_, _) => search();
        box = sb;

        var more = new StudioButton
        {
            Text = "More", IconGlyph = Glyph.ChevronDown, Variant = ButtonVariant.Chip,
            Dock = DockStyle.Right, Width = 86, Font = Theme.UiFont(9f)
        };
        var menu = new ContextMenuStrip { Renderer = new StudioMenuRenderer() };
        foreach (var s in suggestions)
        {
            var text = s;
            menu.Items.Add(text, null, (_, _) => { sb.Text = text; search(); });
        }
        more.Click += (_, _) => menu.Show(more, new Point(0, more.Height + 2));

        // One row of suggestion chips; the ones that don't fit stay in "More"
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, BackColor = Theme.Ground, Padding = new Padding(12, 1, 0, 0) };
        foreach (var s in suggestions.Take(10))
        {
            var text = s;
            var chip = new StudioButton
            {
                Text = text, Variant = ButtonVariant.Chip, AutoSize = true, AutoHeight = 32,
                Margin = new Padding(0, 0, 6, 12), Font = Theme.UiFont(9f)
            };
            chip.Click += (_, _) => { sb.Text = text; search(); };
            flow.Controls.Add(chip);
        }

        bar.Controls.Add(flow);
        bar.Controls.Add(more);
        bar.Controls.Add(sb);
        return bar;
    }

    private Panel ApodToolbar()
    {
        var bar = new Panel { BackColor = Theme.Ground };
        _apodModeSegment = new SegmentedControl { Location = new Point(0, 0), Size = new Size(230, 34), AccessibleName = "Which pictures" };
        _apodModeSegment.SetItems("Recent", "On a date");
        _apodModeSegment.SelectedIndex = 0;
        _apodModeSegment.SelectionChanged += (_, _) =>
        {
            bool recent = _apodModeSegment.SelectedIndex == 0;
            _apodDaysCombo.Visible = recent;
            _apodDatePicker.Visible = !recent;
            RefreshApod();
        };
        _apodDaysCombo = new ModernComboBox { Location = new Point(242, 1), Width = 130, AccessibleName = "How many days" };
        _apodDaysCombo.Items.AddRange(["Last 7 days", "Last 14 days", "Last 30 days", "Last 60 days", "Last 90 days"]);
        _apodDaysCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            int days = ApodDaysValues[Math.Max(0, _apodDaysCombo.SelectedIndex)];
            Edit(t => t.ApodRecentDays = days, render: false);
            RefreshApod();
        };
        _apodDatePicker = new DateTimePicker
        {
            Location = new Point(242, 4), Width = 170, Format = DateTimePickerFormat.Long,
            MinDate = new DateTime(1995, 6, 16), MaxDate = DateTime.Today, Value = DateTime.Today, Visible = false
        };
        Theme.StyleDateTimePicker(_apodDatePicker);
        _apodDatePicker.CloseUp += (_, _) => RefreshApod();
        var refresh = new StudioButton { Text = "Refresh", IconGlyph = Glyph.Refresh, Location = new Point(424, 0), Size = new Size(108, 34) };
        refresh.Click += (_, _) => RefreshApod();
        bar.Controls.AddRange([_apodModeSegment, _apodDaysCombo, _apodDatePicker, refresh]);
        return bar;
    }

    private Panel EpicToolbar()
    {
        var bar = new Panel { BackColor = Theme.Ground };
        _epicTypeSegment = new SegmentedControl { Location = new Point(0, 0), Size = new Size(220, 34), AccessibleName = "Color" };
        _epicTypeSegment.SetItems("Natural", "Enhanced");
        _epicTypeSegment.SelectedIndex = 0;
        _epicTypeSegment.SelectionChanged += (_, _) => RefreshEpic();
        _epicWhenSegment = new SegmentedControl { Location = new Point(232, 0), Size = new Size(210, 34), AccessibleName = "When" };
        _epicWhenSegment.SetItems("Latest", "On a date");
        _epicWhenSegment.SelectedIndex = 0;
        _epicWhenSegment.SelectionChanged += (_, _) =>
        {
            _epicDatePicker.Visible = _epicWhenSegment.SelectedIndex == 1;
            RefreshEpic();
        };
        _epicDatePicker = new DateTimePicker
        {
            Location = new Point(454, 4), Width = 170, Format = DateTimePickerFormat.Long,
            MinDate = new DateTime(2015, 6, 13), MaxDate = DateTime.Today, Value = DateTime.Today.AddDays(-1), Visible = false
        };
        Theme.StyleDateTimePicker(_epicDatePicker);
        _epicDatePicker.CloseUp += (_, _) => RefreshEpic();
        _epicLatestLabel = new StudioLabel { ForeColor = Theme.TextMuted, Font = Theme.UiFont(8.75f), Location = new Point(640, 9) };
        bar.Controls.AddRange([_epicTypeSegment, _epicWhenSegment, _epicDatePicker, _epicLatestLabel]);
        return bar;
    }

    private Panel UserImagesToolbar()
    {
        var bar = new Panel { BackColor = Theme.Ground };
        var add = new StudioButton { Text = "Add images…", IconGlyph = Glyph.Add, Location = new Point(0, 0), Size = new Size(140, 34) };
        add.Click += async (_, _) =>
        {
            int added = await ImportUserImagesAsync();
            if (added > 0) RefreshUserImages();
        };
        var open = new StudioButton { Text = "Open folder", IconGlyph = Glyph.Folder, Location = new Point(150, 0), Size = new Size(136, 34) };
        open.Click += (_, _) =>
        {
            Directory.CreateDirectory(UserImageManager.ImagesDirectory);
            try { System.Diagnostics.Process.Start("explorer.exe", UserImageManager.ImagesDirectory); } catch { }
        };
        _userImagesCount = new StudioLabel { ForeColor = Theme.TextMuted, Location = new Point(300, 9) };
        bar.Controls.AddRange([add, open, _userImagesCount]);
        return bar;
    }

    // ─── State ───

    /// <summary>Sync the Photos view with the selected display (source, rotation, markers).</summary>
    private void LoadPhotosState()
    {
        var v = ViewSettings;
        // Rotation only runs while the display shows photos, so it reads "off" otherwise
        _rotateSwitch.Checked = v.RandomRotationEnabled && v.DisplayMode == DisplayMode.StillImage;
        _rotateSourceCombo.SelectedIndex = Math.Max(0, Array.IndexOf(RotationOrder, v.RandomRotationSource));
        foreach (var chip in _exclusionChips)
            chip.Selected = !_settings.ExcludedRotationSources.Contains((RotationSource)chip.Tag!);
        _apodDaysCombo.SelectedIndex = Math.Max(0, Array.IndexOf(ApodDaysValues, v.ApodRecentDays) is int i and >= 0 ? i : 1);

        // Each display remembers its own searches; a different query means a reload
        SyncQuery(_gallerySearch, v.NasaGallerySearchQuery, ImageSource.NasaGallery);
        SyncQuery(_npsSearch, v.NpsSearchQuery, ImageSource.NationalParks);
        SyncQuery(_smithsonianSearch, v.SmithsonianSearchQuery, ImageSource.Smithsonian);

        UpdateRotationUi();
        if (_viewSegment.SelectedIndex == 3)
            ShowSource(v.DisplayMode == DisplayMode.StillImage ? v.StillImageSource : _browseSource, userPicked: false);
        RefreshOnDisplayMarkers();
    }

    private void SyncQuery(TextField box, string saved, ImageSource source)
    {
        if (string.IsNullOrEmpty(saved) || box.Text == saved) return;
        box.Text = saved;
        _loadedSources.Remove(source);
    }

    private void ShowSource(ImageSource source, bool userPicked)
    {
        _browseSource = source;
        for (int i = 0; i < SourceOrder.Length; i++)
            _sourceChips[i].Selected = SourceOrder[i] == source;
        foreach (var (src, tb) in _toolbars) tb.Visible = src == source;
        foreach (var (src, grid) in _grids) grid.Visible = src == source;

        if (!_loadedSources.Contains(source))
        {
            _loadedSources.Add(source);
            RefreshSource(source);
        }
        else
        {
            RestoreGridSelection(source);
        }
        if (userPicked) _grids[source].Focus();
    }

    private void RefreshSource(ImageSource source)
    {
        switch (source)
        {
            case ImageSource.NasaGallery: RefreshGallery(); break;
            case ImageSource.NasaApod: RefreshApod(); break;
            case ImageSource.NasaEpic: RefreshEpic(); break;
            case ImageSource.NationalParks:
                if (_npsSearch.Text.Trim().Length > 0) RefreshNps();
                else _grids[source].EmptyText = "Search for a park, or pick one from the suggestions.";
                break;
            case ImageSource.Smithsonian: RefreshSmithsonian(); break;
            case ImageSource.UserImages: RefreshUserImages(); break;
        }
    }

    private void UpdateRotationUi()
    {
        var v = ViewSettings;
        bool rotating = v.RandomRotationEnabled && v.DisplayMode == DisplayMode.StillImage;
        _rotateSourceCombo.Visible = rotating;
        bool showInclude = rotating && v.RandomRotationSource == RotationSource.All;
        _exclusionRow.Visible = showInclude;
        _rotationNoteRow.Height = Dpi(showInclude ? 100 : 62);
        _rotationNote.Left = rotating ? _rotateSourceCombo.Right + Dpi(14) : _rotateSourceCombo.Left;
        _rotationNote.Text = rotating
            ? $"New picture {IntervalPhrase(_settings.UpdateIntervalSeconds)} on {TargetName}"
            : $"Off — turn on to change the picture on {TargetName} automatically";
        _rotationNote.Width = Math.Max(Dpi(60), _rotationCard.Width - _rotationNote.Left - Dpi(14));
    }

    private static string IntervalPhrase(int seconds) => seconds switch
    {
        int.MaxValue => "whenever you press Next image",
        < 3600 => $"every {seconds / 60} minutes",
        3600 => "every hour",
        < 86400 => $"every {seconds / 3600} hours",
        86400 => "every day",
        _ => $"every {seconds / 86400} days"
    };

    private void OnRotateToggled()
    {
        bool on = _rotateSwitch.Checked;
        if (on)
        {
            var source = RotationOrder[Math.Max(0, _rotateSourceCombo.SelectedIndex)];
            var browse = _browseSource;
            Edit(t =>
            {
                t.RandomRotationEnabled = true;
                t.RandomRotationSource = source;
                if (t.DisplayMode != DisplayMode.StillImage)
                {
                    t.DisplayMode = DisplayMode.StillImage;
                    t.StillImageSource = browse;
                }
            });
            _advanceRotationOnCommit = true; // Start rotating now, not at the next tick
            _browsingPhotos.Remove(CurrentImageKey);
        }
        else
        {
            // Keep the picture that is showing: make it the display's selection,
            // or the next update would fall back to an older saved pick
            var current = _renderScheduler.GetCurrentImage(CurrentImageKey);
            Edit(t =>
            {
                t.RandomRotationEnabled = false;
                if (current != null) PinAsSelection(t, current);
            }, render: false);
        }
        UpdateRotationUi();
        UpdatePhotosFooter();
        UpdateStatusCard();
    }

    /// <summary>Make an image that is on screen the display's saved selection.</summary>
    private void PinAsSelection(IDisplaySettings t, RenderScheduler.CurrentImageInfo c)
    {
        t.StillImageSource = c.Source;
        string url = c.Url ?? "";
        switch (c.Source)
        {
            case ImageSource.NasaEpic:
                t.EpicUseLatest = false;
                t.EpicSelectedImage = c.Id;
                t.EpicImageType = c.Id.StartsWith("epic_RGB", StringComparison.OrdinalIgnoreCase)
                    ? EpicImageType.Enhanced : EpicImageType.Natural;
                if (ParseEpicDate(c.Id) is string date) t.EpicSelectedDate = date;
                break;
            case ImageSource.NasaApod: t.ApodSelectedImageId = c.Id; t.ApodSelectedImageUrl = url; break;
            case ImageSource.NasaGallery: t.NasaGallerySelectedId = c.Id; t.NasaGallerySelectedImageUrl = url; break;
            case ImageSource.NationalParks: t.NpsSelectedImageId = c.Id; t.NpsSelectedImageUrl = url; break;
            case ImageSource.Smithsonian: t.SmithsonianSelectedId = c.Id; t.SmithsonianSelectedImageUrl = url; break;
            case ImageSource.UserImages:
                t.UserImageSelectedId = c.Id;
                t.UserImageSelectedPath = c.Path ?? _userImageManager.GetImagePath(c.Id) ?? "";
                break;
        }
    }

    private void OnRotationSourceChanged()
    {
        if (_loading || _rotateSourceCombo.SelectedIndex < 0 || !_rotateSwitch.Checked) return;
        var source = RotationOrder[_rotateSourceCombo.SelectedIndex];
        if (source == ViewSettings.RandomRotationSource) return;
        Edit(t => t.RandomRotationSource = source);
        _advanceRotationOnCommit = true;
        UpdateRotationUi();
    }

    // ─── Selection and Set ───

    private bool IsOnDisplay(ImageSourceInfo img)
    {
        var current = _renderScheduler.GetCurrentImage(CurrentImageKey);
        if (current == null || current.Source != img.Source) return false;
        return string.Equals(current.Id, img.Id, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(current.Id, ImageCache.SanitizeFileName(img.Id), StringComparison.OrdinalIgnoreCase);
    }

    private string SetLabel => $"Set on {TargetName}";

    private void UpdatePhotosFooter() => UpdateFooter(_photosFooter, _photosSelection, _grids[_browseSource]);

    private void UpdateFooter(SelectionFooter footer, ImageSourceInfo? img, ThumbnailGridPanel grid)
    {
        if (img == null)
        {
            footer.ShowEmpty("Pick a picture, then press Set",
                _browsingPhotos.Contains(CurrentImageKey) ? $"{TargetName} won't change until you do" : "");
            return;
        }

        bool onDisplay = IsOnDisplay(img);
        var v = ViewSettings;
        string note = onDisplay ? $"Showing on {TargetName} now"
            : v.DisplayMode != DisplayMode.StillImage || _browsingPhotos.Contains(CurrentImageKey)
                ? $"Nothing changes until you press Set"
            : v.RandomRotationEnabled ? "Set shows it now — auto-rotate carries on afterwards"
            : $"Press Set to show it on {TargetName}";
        if (img.Source == ImageSource.NasaEpic && !onDisplay)
            note += " · pins this exact capture";

        footer.ShowSelection(img.Title.Length > 0 ? img.Title : img.Id, note, grid.GetThumbnail(img.Id),
            img.IsFavorited, onDisplay ? "On display" : SetLabel, !onDisplay);
    }

    /// <summary>Show this image on the edited display(s) now.</summary>
    private void ApplyImage(ImageSourceInfo img)
    {
        string bestUrl = !string.IsNullOrEmpty(img.HdImageUrl) ? img.HdImageUrl
            : !string.IsNullOrEmpty(img.FullImageUrl) ? img.FullImageUrl : img.ThumbnailUrl;
        string? userPath = img.Source == ImageSource.UserImages
            ? _userImageManager.GetImagePath(img.Id) ?? (File.Exists(img.FullImageUrl) ? img.FullImageUrl : null)
            : null;
        if (img.Source == ImageSource.UserImages && userPath == null)
        {
            SetStatusOverride("That image file is missing", Theme.Warning);
            return;
        }

        // Coming from globe/map/moon the switch showed auto-rotate as off, so
        // commit exactly that rather than reviving an old rotation setting
        bool wasShowingPhotos = ViewSettings.DisplayMode == DisplayMode.StillImage;
        Edit(t =>
        {
            t.DisplayMode = DisplayMode.StillImage;
            t.StillImageSource = img.Source;
            if (!wasShowingPhotos) t.RandomRotationEnabled = false;
            switch (img.Source)
            {
                case ImageSource.NasaEpic:
                    // Pin this exact capture (id: epic_1b_yyyyMMddHHmmss / epic_RGB_…)
                    t.EpicUseLatest = false;
                    t.EpicSelectedImage = img.Id;
                    t.EpicImageType = img.Id.StartsWith("epic_RGB", StringComparison.OrdinalIgnoreCase)
                        ? EpicImageType.Enhanced : EpicImageType.Natural;
                    if (ParseEpicDate(img.Id) is string date) t.EpicSelectedDate = date;
                    break;
                case ImageSource.NasaApod:
                    t.ApodSelectedImageId = img.Id;
                    t.ApodSelectedImageUrl = ApodApiClient.GetBestUrl(img);
                    break;
                case ImageSource.NasaGallery:
                    t.NasaGallerySelectedId = img.Id;
                    t.NasaGallerySelectedImageUrl = bestUrl;
                    break;
                case ImageSource.NationalParks:
                    t.NpsSelectedImageId = img.Id;
                    t.NpsSelectedImageUrl = bestUrl;
                    break;
                case ImageSource.Smithsonian:
                    t.SmithsonianSelectedId = img.Id;
                    t.SmithsonianSelectedImageUrl = bestUrl;
                    break;
                case ImageSource.UserImages:
                    t.UserImageSelectedId = img.Id;
                    t.UserImageSelectedPath = userPath!;
                    break;
            }
        });
        _showSelectionOnCommit = true;
        CommitNow(); // Explicit action: apply immediately, no debounce
        _browsingPhotos.Remove(CurrentImageKey);

        _loading = true;
        try
        {
            _viewSegment.SelectedIndex = 3;
            ApplyViewVisibility();
        }
        finally { _loading = false; }
        _photosFooter.SetCanSet(false, "Setting…");
        _collectionFooter?.SetCanSet(false, "Setting…");
    }

    private static string? ParseEpicDate(string imageId)
    {
        var match = System.Text.RegularExpressions.Regex.Match(imageId, @"(\d{8})\d{6}$");
        if (!match.Success) return null;
        return DateTime.TryParseExact(match.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d)
            ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Mark which images are on screen in every grid, and refresh footers.</summary>
    private void RefreshOnDisplayMarkers()
    {
        var current = _renderScheduler.GetCurrentImage(CurrentImageKey);
        foreach (var (src, grid) in _grids)
        {
            grid.OnDisplayIds.Clear();
            if (current != null && current.Source == src) grid.OnDisplayIds.Add(current.Id);
            grid.RepaintCells();
        }
        if (_favoritesGrid != null)
        {
            _favoritesGrid.OnDisplayIds.Clear();
            _userCollectionGrid!.OnDisplayIds.Clear();
            if (current != null)
            {
                _favoritesGrid.OnDisplayIds.Add(current.Id);
                if (current.Source == ImageSource.UserImages) _userCollectionGrid.OnDisplayIds.Add(current.Id);
            }
            _favoritesGrid.RepaintCells();
            _userCollectionGrid.RepaintCells();
        }
        UpdatePhotosFooter();
        UpdateCollectionFooter();
    }

    /// <summary>Highlight the display's committed pick for this source, if listed.</summary>
    private void RestoreGridSelection(ImageSource source)
    {
        var v = ViewSettings;
        string id = source switch
        {
            ImageSource.NasaEpic => v.EpicSelectedImage,
            ImageSource.NasaApod => v.ApodSelectedImageId,
            ImageSource.NasaGallery => v.NasaGallerySelectedId,
            ImageSource.NationalParks => v.NpsSelectedImageId,
            ImageSource.Smithsonian => v.SmithsonianSelectedId,
            ImageSource.UserImages => v.UserImageSelectedId,
            _ => ""
        };
        var grid = _grids[source];
        if (grid.SelectById(id))
            _photosSelection = grid.SelectedImage;
        else if (grid.SelectedImage == null)
            _photosSelection = null;
        else
            _photosSelection = grid.SelectedImage;
        UpdatePhotosFooter();
    }

    // ─── Loading sources ───

    /// <summary>Run a fetch off the UI thread and fill a grid, with loading/empty messages.</summary>
    private async void LoadGrid(ImageSource source, string loadingText, Func<Task<List<ImageSourceInfo>?>> fetch,
        string emptyText, string failText)
    {
        // Only the newest load for a source may fill its grid: two quick searches
        // can finish out of order
        int generation = _loadGenerations[source] = _loadGenerations.GetValueOrDefault(source) + 1;
        var grid = _grids[source];
        grid.SetImages(new List<ImageSourceInfo>());
        grid.EmptyText = loadingText;
        if (source == _browseSource) { _photosSelection = null; UpdatePhotosFooter(); }

        List<ImageSourceInfo>? images;
        try { images = await Task.Run(fetch); }
        catch { images = null; }
        if (IsDisposed) return;
        if (_loadGenerations[source] != generation) return;

        if (images == null || images.Count == 0)
        {
            grid.EmptyText = images == null ? failText : emptyText;
            if (images == null) _loadedSources.Remove(source); // Retry when the source is shown again
            return;
        }
        lock (_settings.FavoritesLock)
        {
            foreach (var img in images)
                img.IsFavorited = _settings.Favorites.Any(f => f.Source == img.Source && f.ImageId == img.Id);
        }
        grid.EmptyText = "";
        grid.SetImages(images);
        RefreshOnDisplayMarkers();
        if (source == _browseSource) RestoreGridSelection(source);
    }

    private void RefreshGallery()
    {
        string query = _gallerySearch.Text.Trim();
        if (query.Length == 0) query = "nebula";
        Edit(t => t.NasaGallerySearchQuery = query, render: false);
        LoadGrid(ImageSource.NasaGallery, $"Searching NASA for “{query}”…",
            () => _galleryApi.SearchImagesAsync(query),
            $"Nothing found for “{query}”. Try another search.",
            "Couldn't reach NASA's image library. Check your connection.");
    }

    private void RefreshNps()
    {
        string query = _npsSearch.Text.Trim();
        if (query.Length == 0) return;
        string key = _settings.ApiDataGovKey;
        Edit(t => t.NpsSearchQuery = query, render: false);
        LoadGrid(ImageSource.NationalParks, $"Searching parks for “{query}”…",
            () => NpsApiClient.ParkCodes.TryGetValue(query, out var code)
                ? _npsApi.GetParkImagesAsync(key, code) : _npsApi.SearchParksAsync(key, query),
            "No park photos found. Try a park name like Yosemite.",
            "Couldn't reach the National Park Service. Check your API key in Settings.");
    }

    private void RefreshSmithsonian()
    {
        string query = _smithsonianSearch.Text.Trim();
        if (query.Length == 0) query = "landscape painting";
        string key = _settings.ApiDataGovKey;
        Edit(t => t.SmithsonianSearchQuery = query, render: false);
        LoadGrid(ImageSource.Smithsonian, $"Searching the Smithsonian for “{query}”…",
            () => _smithsonianApi.SearchImagesAsync(key, query),
            "Nothing found. Try a suggestion.",
            "Couldn't reach the Smithsonian. Check your API key in Settings.");
    }

    private void RefreshApod()
    {
        string key = _settings.ApiDataGovKey;
        if (_apodModeSegment.SelectedIndex == 0)
        {
            int days = ApodDaysValues[Math.Max(0, _apodDaysCombo.SelectedIndex)];
            LoadGrid(ImageSource.NasaApod, "Loading NASA's Picture of the Day…",
                () => _apodApi.GetRecentAsync(key, days),
                "NASA's Picture of the Day feed is sending placeholders instead of pictures right now. Your saved APOD favorites still work.", "Couldn't reach NASA APOD. Check your API key in Settings.");
        }
        else
        {
            string date = _apodDatePicker.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            LoadGrid(ImageSource.NasaApod, "Loading…",
                async () => await _apodApi.GetByDateAsync(key, date) is { } one ? new List<ImageSourceInfo> { one } : null,
                "No picture that day.", "No picture for that date (it may be a video, or NASA's feed is sending placeholders).");
        }
    }

    private void RefreshEpic()
    {
        var type = _epicTypeSegment.SelectedIndex == 1 ? EpicImageType.Enhanced : EpicImageType.Natural;
        bool latest = _epicWhenSegment.SelectedIndex == 0;
        string date = _epicDatePicker.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string collection = type == EpicImageType.Enhanced ? "enhanced" : "natural";
        if (_epicLatestDate == null) FetchEpicLatestDate(type);

        LoadGrid(ImageSource.NasaEpic, "Loading Earth from DSCOVR…", async () =>
        {
            var images = latest ? await _epicApi.GetLatestImagesAsync(type) : await _epicApi.GetImagesByDateAsync(type, date);
            return images?.Select(img =>
            {
                DateTime.TryParse(img.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt);
                string path = $"{dt.Year:D4}/{dt.Month:D2}/{dt.Day:D2}";
                return new ImageSourceInfo
                {
                    Source = ImageSource.NasaEpic,
                    Id = img.Image,
                    Title = $"{dt:MMM d} · {dt:HH:mm} UTC",
                    Date = img.Date,
                    ThumbnailUrl = $"https://epic.gsfc.nasa.gov/archive/{collection}/{path}/thumbs/{img.Image}.jpg",
                    FullImageUrl = $"https://epic.gsfc.nasa.gov/archive/{collection}/{path}/jpg/{img.Image}.jpg",
                    HdImageUrl = $"https://epic.gsfc.nasa.gov/archive/{collection}/{path}/png/{img.Image}.png",
                    ImageWidth = 2048, ImageHeight = 2048,
                    QualityTier = ImageQualityTier.HD,
                    SourceAttribution = "NASA EPIC / DSCOVR"
                };
            }).ToList();
        }, latest ? "No images yet today." : "No images that day — EPIC skips some days.",
        "Couldn't reach NASA EPIC. Check your connection.");
    }

    private async void FetchEpicLatestDate(EpicImageType type)
    {
        try
        {
            var dates = await _epicApi.GetAvailableDatesAsync(type);
            if (IsDisposed || dates == null || dates.Count == 0) return;
            if (!DateTime.TryParse(dates[0].Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var latest)) return;
            _epicLatestDate = latest;
            _epicLatestLabel.Text = $"Newest: {latest:MMM d, yyyy}";
            // Out-of-range assignment throws; clamp before tightening MaxDate
            if (_epicDatePicker.Value > latest) _epicDatePicker.Value = latest;
            _epicDatePicker.MaxDate = latest;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"EPIC latest date fetch failed: {ex.Message}");
        }
    }

    private void RefreshUserImages()
    {
        LoadGrid(ImageSource.UserImages, "Loading your images…", () =>
        {
            var images = _userImageManager.GetAllImages();
            return Task.FromResult<List<ImageSourceInfo>?>(images);
        }, "No images yet. Add your own with “Add images…”.", "Couldn't read your images folder.");
        UpdateUserImageCount();
    }

    private async void UpdateUserImageCount()
    {
        int count = await Task.Run(() => _userImageManager.GetAllImagePaths().Count);
        if (IsDisposed) return;
        _userImagesCount.Text = $"{count} image{(count == 1 ? "" : "s")}";
    }

    /// <summary>Pick files on an STA thread (the render thread blocks STA dialogs) and import them.</summary>
    private async Task<int> ImportUserImagesAsync()
    {
        var tcs = new TaskCompletionSource<string[]?>();
        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = new OpenFileDialog
                {
                    Title = "Add images to Blue Marble Desktop",
                    Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.webp|All files|*.*",
                    Multiselect = true
                };
                tcs.SetResult(dialog.ShowDialog() == DialogResult.OK ? dialog.FileNames : null);
            }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        try
        {
            var files = await tcs.Task;
            if (files == null || files.Length == 0) return 0;
            SetStatusOverride("Importing images…", Theme.Accent);
            int count = await Task.Run(() => _userImageManager.ImportImages(files));
            SetStatusOverride("", Theme.Success);
            return count;
        }
        catch (Exception ex)
        {
            SetStatusOverride("", Theme.Success);
            MessageBox.Show(this, $"Couldn't import images: {ex.Message}", "Blue Marble Desktop",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 0;
        }
    }

    // ─── Favorites ───

    /// <summary>Flip an image's favorite state in the global list (no re-render).</summary>
    private void ToggleFavorite(ImageSourceInfo img)
    {
        // The render thread iterates Favorites for rotation and pruning
        lock (_settings.FavoritesLock)
        {
            var existing = _settings.Favorites.Find(f => f.Source == img.Source && f.ImageId == img.Id);
            if (existing != null && !img.IsFavorited)
            {
                _settings.Favorites.Remove(existing);
            }
            else if (existing == null && img.IsFavorited)
            {
                var fav = new FavoriteImage
                {
                    Source = img.Source,
                    ImageId = img.Id,
                    Title = img.Title,
                    ThumbnailUrl = img.ThumbnailUrl,
                    FullImageUrl = !string.IsNullOrEmpty(img.HdImageUrl) ? img.HdImageUrl : img.FullImageUrl,
                    QualityTier = img.QualityTier
                };
                // Record the cached file so rotation/export find it even for
                // sources with non-standard cache layouts (EPIC)
                fav.LocalCachePath = _imageCache.GetCachedPathForFavorite(fav) ?? "";
                _settings.Favorites.Add(fav);
            }
        }
        _settingsManager.Save();
    }
}
