using System.Drawing;
using DesktopEarth.UI.Controls;

namespace DesktopEarth.UI;

/// <summary>My Collection page: favorites and the user's own images.</summary>
public partial class SettingsForm
{
    private ThumbnailGridPanel? _favoritesGrid;
    private ThumbnailGridPanel? _userCollectionGrid;
    private StudioLabel _favoritesCount = null!, _userCount = null!, _collectionTarget = null!;
    private SelectionFooter? _collectionFooter;
    private ImageSourceInfo? _collectionSelection;
    private ThumbnailGridPanel? _collectionSelectionGrid;

    private Panel BuildCollectionPage()
    {
        var page = new Panel { BackColor = Theme.Ground };

        var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Ground, Padding = new Padding(24, 18, 12, 8) };

        // Favorites
        var favHeader = new Panel { Height = 44, BackColor = Theme.Ground };
        var favTitle = new StudioLabel { Text = "Favorites", Font = Theme.DisplayFont(15f), Location = new Point(0, 2) };
        _favoritesCount = new StudioLabel { ForeColor = Theme.TextMuted, Location = new Point(110, 12) };
        var export = new StudioButton { Text = "Export…", Size = new Size(104, 32), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        export.Click += (_, _) => ExportFavorites();
        var clearFavs = new StudioButton { Text = "Clear all", Size = new Size(92, 32), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        clearFavs.Click += (_, _) => ClearAllFavorites();
        favHeader.Controls.AddRange([favTitle, _favoritesCount, export, clearFavs]);
        favHeader.Layout += (_, _) =>
        {
            _favoritesCount.Left = favTitle.Right + Dpi(10);
            clearFavs.Location = new Point(favHeader.Width - clearFavs.Width - Dpi(12), Dpi(4));
            export.Location = new Point(clearFavs.Left - export.Width - Dpi(8), Dpi(4));
        };

        _collectionTarget = new StudioLabel
        {
            AutoSize = false, Height = 26, ForeColor = Theme.TextMuted, Font = Theme.UiFont(9f),
            TextAlign = ContentAlignment.TopLeft
        };

        _favoritesGrid = new ThumbnailGridPanel(_imageCache) { Dock = DockStyle.Fill, ShowSourceBadge = true };
        WireCollectionGrid(_favoritesGrid, fromFavorites: true);

        // My images
        var userSection = new Panel { Dock = DockStyle.Bottom, Height = 290, BackColor = Theme.Ground, Padding = new Padding(0, 14, 0, 0) };
        var userHeader = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Ground };
        var userTitle = new StudioLabel { Text = "My images", Font = Theme.DisplayFont(15f), Location = new Point(0, 2) };
        _userCount = new StudioLabel { ForeColor = Theme.TextMuted, Location = new Point(120, 12) };
        var add = new StudioButton { Text = "Add images…", IconGlyph = Glyph.Add, Size = new Size(140, 32) };
        add.Click += async (_, _) =>
        {
            if (await ImportUserImagesAsync() > 0) { RefreshCollection(); _loadedSources.Remove(ImageSource.UserImages); }
        };
        var clearUser = new StudioButton { Text = "Delete all", Size = new Size(100, 32) };
        clearUser.Click += (_, _) => ClearAllUserImages();
        userHeader.Controls.AddRange([userTitle, _userCount, add, clearUser]);
        userHeader.Layout += (_, _) =>
        {
            _userCount.Left = userTitle.Right + Dpi(10);
            clearUser.Location = new Point(userHeader.Width - clearUser.Width - Dpi(12), Dpi(4));
            add.Location = new Point(clearUser.Left - add.Width - Dpi(8), Dpi(4));
        };
        _userCollectionGrid = new ThumbnailGridPanel(_imageCache) { Dock = DockStyle.Fill };
        WireCollectionGrid(_userCollectionGrid, fromFavorites: false);
        userSection.Controls.Add(_userCollectionGrid);
        userSection.Controls.Add(userHeader);

        body.Controls.Add(_favoritesGrid);
        body.Controls.Add(userSection);
        StackTop(body, favHeader, _collectionTarget);

        _collectionFooter = new SelectionFooter { Dock = DockStyle.Bottom };
        _collectionFooter.SetClicked += (_, _) => { if (_collectionSelection != null) ApplyImage(_collectionSelection); };
        _collectionFooter.FavoriteClicked += (_, _) =>
        {
            if (_collectionSelection == null) return;
            _collectionSelection.IsFavorited = !_collectionSelection.IsFavorited;
            ToggleFavorite(_collectionSelection);
            UpdateCollectionFooter();
        };

        page.Controls.Add(body);
        page.Controls.Add(_collectionFooter);
        return page;
    }

    private void WireCollectionGrid(ThumbnailGridPanel grid, bool fromFavorites)
    {
        grid.ImageSelected += (_, img) =>
        {
            // One selection across both grids
            var other = grid == _favoritesGrid ? _userCollectionGrid : _favoritesGrid;
            other?.ClearSelection();
            _collectionSelection = img;
            _collectionSelectionGrid = grid;
            UpdateCollectionFooter();
        };
        grid.ImageActivated += (_, img) =>
        {
            _collectionSelection = img;
            _collectionSelectionGrid = grid;
            ApplyImage(img);
        };
        grid.FavoriteToggled += (_, img) =>
        {
            ToggleFavorite(img);
            if (fromFavorites && !img.IsFavorited) RefreshCollection(); // Un-starred: drop it from the list
            else UpdateCollectionFooter();
        };
    }

    private void UpdateCollectionFooter()
    {
        if (_collectionFooter == null) return;
        if (_collectionSelection == null || _collectionSelectionGrid == null)
            _collectionFooter.ShowEmpty("Pick a picture, then press Set", $"It goes on {TargetName}");
        else
            UpdateFooter(_collectionFooter, _collectionSelection, _collectionSelectionGrid);
    }

    private async void RefreshCollection()
    {
        if (_favoritesGrid == null || _userCollectionGrid == null) return;

        _collectionTarget.Text = PerDisplay
            ? $"Pictures you set here go on {TargetName}. Pick a different display on the Wallpaper page."
            : $"Pictures you set here go on {TargetName}.";

        List<ImageSourceInfo> favs;
        lock (_settings.FavoritesLock)
        {
            favs = _settings.Favorites.Select(f => new ImageSourceInfo
            {
                Source = f.Source,
                Id = f.ImageId,
                Title = string.IsNullOrEmpty(f.Title) ? f.ImageId : f.Title,
                ThumbnailUrl = f.ThumbnailUrl,
                FullImageUrl = f.FullImageUrl,
                QualityTier = f.QualityTier,
                IsFavorited = true
            }).ToList();
        }
        _favoritesGrid.SetImages(favs);
        _favoritesGrid.EmptyText = "No favorites yet. Star any picture to keep it here.";
        _favoritesCount.Text = $"{favs.Count}";
        _collectionSelection = null;
        _collectionSelectionGrid = null;

        var userImages = await Task.Run(() => _userImageManager.GetAllImages());
        if (IsDisposed) return;
        lock (_settings.FavoritesLock)
        {
            foreach (var img in userImages)
                img.IsFavorited = _settings.Favorites.Any(f => f.Source == ImageSource.UserImages && f.ImageId == img.Id);
        }
        _userCollectionGrid.SetImages(userImages);
        _userCollectionGrid.EmptyText = "Add your own photos to use them as wallpapers.";
        _userCount.Text = $"{userImages.Count}";

        RefreshOnDisplayMarkers();
    }

    private async void ExportFavorites()
    {
        List<FavoriteImage> favorites;
        lock (_settings.FavoritesLock) favorites = _settings.Favorites.ToList();
        if (favorites.Count == 0)
        {
            MessageBox.Show(this, "No favorites to export yet.", "Export favorites", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new FolderBrowserDialog { Description = "Choose a folder for your favorites", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string destDir = dialog.SelectedPath;

        // Copy off the UI thread — dozens of copies would freeze the window
        int exported = 0, skipped = 0;
        await Task.Run(() =>
        {
            foreach (var fav in favorites)
            {
                string? path = fav.Source == ImageSource.UserImages
                    ? _userImageManager.GetImagePath(fav.ImageId)
                    : _imageCache.GetCachedPathForFavorite(fav);
                if (path == null || !File.Exists(path)) { skipped++; continue; }

                var safeName = ImageCache.SanitizeFileName(string.IsNullOrEmpty(fav.Title) ? fav.ImageId : fav.Title);
                var dest = Path.Combine(destDir, safeName + Path.GetExtension(path));
                for (int n = 1; File.Exists(dest); n++)
                    dest = Path.Combine(destDir, $"{safeName}_{n}{Path.GetExtension(path)}");
                try { File.Copy(path, dest); exported++; }
                catch { skipped++; }
            }
        });
        if (IsDisposed) return;

        MessageBox.Show(this, skipped == 0
                ? $"Exported {exported} favorite{(exported == 1 ? "" : "s")}."
                : $"Exported {exported} of {favorites.Count} favorites.\n{skipped} aren't downloaded yet, so they were skipped.",
            "Export favorites", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ClearAllFavorites()
    {
        int count;
        lock (_settings.FavoritesLock) count = _settings.Favorites.Count;
        if (count == 0) return;
        if (MessageBox.Show(this, $"Remove all {count} favorites?\n\nThe downloaded images themselves are not deleted.",
                "Clear favorites", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        lock (_settings.FavoritesLock) _settings.Favorites.Clear();
        _settingsManager.Save();
        RefreshCollection();
    }

    private async void ClearAllUserImages()
    {
        var paths = _userImageManager.GetAllImagePaths();
        if (paths.Count == 0) return;
        if (MessageBox.Show(this, $"Delete all {paths.Count} of your images from Blue Marble Desktop?\n\n" +
                "This removes the copies in the app's folder (your originals elsewhere are untouched).",
                "Delete my images", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        lock (_settings.FavoritesLock)
            _settings.Favorites.RemoveAll(f => f.Source == ImageSource.UserImages);
        await Task.Run(() => _userImageManager.DeleteAllImages());
        if (IsDisposed) return;

        // Clear dangling selections so rendering doesn't chase deleted files
        _settings.UserImageSelectedId = "";
        _settings.UserImageSelectedPath = "";
        lock (_settings.DisplayConfigsLock)
        {
            foreach (var config in _settings.DisplayConfigs)
            {
                config.UserImageSelectedId = "";
                config.UserImageSelectedPath = "";
            }
        }
        _settingsManager.Save();
        _loadedSources.Remove(ImageSource.UserImages);
        RefreshCollection();
    }
}
