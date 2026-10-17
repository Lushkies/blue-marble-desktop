namespace DesktopEarth;

/// <summary>
/// The settings that can differ per display. Implemented by <see cref="AppSettings"/>
/// (the global values, used by every display without its own config) and by
/// <see cref="DisplayConfig"/>, so the settings window and the renderer can work
/// on either without caring which one it is.
/// </summary>
public interface IDisplaySettings
{
    DisplayMode DisplayMode { get; set; }
    ImageSource StillImageSource { get; set; }
    float ZoomLevel { get; set; }
    float FieldOfView { get; set; }
    float CameraTilt { get; set; }
    float LongitudeOffset { get; set; }
    float ImageOffsetX { get; set; }
    float ImageOffsetY { get; set; }
    bool NightLightsEnabled { get; set; }
    float NightLightsBrightness { get; set; }
    float AmbientLight { get; set; }
    ImageStyle ImageStyle { get; set; }

    EpicImageType EpicImageType { get; set; }
    bool EpicUseLatest { get; set; }
    string EpicSelectedDate { get; set; }
    string EpicSelectedImage { get; set; }

    bool ApodUseLatest { get; set; }
    int ApodRecentDays { get; set; }
    string ApodSelectedDate { get; set; }
    string ApodSelectedImageId { get; set; }
    string ApodSelectedImageUrl { get; set; }

    string NpsSearchQuery { get; set; }
    string NpsSelectedParkCode { get; set; }
    string NpsSelectedImageId { get; set; }
    string NpsSelectedImageUrl { get; set; }

    string SmithsonianSearchQuery { get; set; }
    string SmithsonianSelectedId { get; set; }
    string SmithsonianSelectedImageUrl { get; set; }

    string NasaGallerySearchQuery { get; set; }
    string NasaGallerySelectedId { get; set; }
    string NasaGallerySelectedImageUrl { get; set; }

    bool RandomRotationEnabled { get; set; }
    RotationSource RandomRotationSource { get; set; }

    string UserImageSelectedId { get; set; }
    string UserImageSelectedPath { get; set; }
}

public static class DisplaySettingsCopier
{
    /// <summary>
    /// Copy every per-display setting. This is the ONE list of them: a property
    /// added to <see cref="IDisplaySettings"/> must be added here too, or
    /// per-display mode silently ignores it.
    /// </summary>
    public static void Copy(IDisplaySettings from, IDisplaySettings to)
    {
        to.DisplayMode = from.DisplayMode;
        to.StillImageSource = from.StillImageSource;
        to.ZoomLevel = from.ZoomLevel;
        to.FieldOfView = from.FieldOfView;
        to.CameraTilt = from.CameraTilt;
        to.LongitudeOffset = from.LongitudeOffset;
        to.ImageOffsetX = from.ImageOffsetX;
        to.ImageOffsetY = from.ImageOffsetY;
        to.NightLightsEnabled = from.NightLightsEnabled;
        to.NightLightsBrightness = from.NightLightsBrightness;
        to.AmbientLight = from.AmbientLight;
        to.ImageStyle = from.ImageStyle;

        to.EpicImageType = from.EpicImageType;
        to.EpicUseLatest = from.EpicUseLatest;
        to.EpicSelectedDate = from.EpicSelectedDate;
        to.EpicSelectedImage = from.EpicSelectedImage;

        to.ApodUseLatest = from.ApodUseLatest;
        to.ApodRecentDays = from.ApodRecentDays;
        to.ApodSelectedDate = from.ApodSelectedDate;
        to.ApodSelectedImageId = from.ApodSelectedImageId;
        to.ApodSelectedImageUrl = from.ApodSelectedImageUrl;

        to.NpsSearchQuery = from.NpsSearchQuery;
        to.NpsSelectedParkCode = from.NpsSelectedParkCode;
        to.NpsSelectedImageId = from.NpsSelectedImageId;
        to.NpsSelectedImageUrl = from.NpsSelectedImageUrl;

        to.SmithsonianSearchQuery = from.SmithsonianSearchQuery;
        to.SmithsonianSelectedId = from.SmithsonianSelectedId;
        to.SmithsonianSelectedImageUrl = from.SmithsonianSelectedImageUrl;

        to.NasaGallerySearchQuery = from.NasaGallerySearchQuery;
        to.NasaGallerySelectedId = from.NasaGallerySelectedId;
        to.NasaGallerySelectedImageUrl = from.NasaGallerySelectedImageUrl;

        to.RandomRotationEnabled = from.RandomRotationEnabled;
        to.RandomRotationSource = from.RandomRotationSource;

        to.UserImageSelectedId = from.UserImageSelectedId;
        to.UserImageSelectedPath = from.UserImageSelectedPath;
    }
}
