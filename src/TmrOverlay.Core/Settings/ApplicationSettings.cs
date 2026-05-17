namespace TmrOverlay.Core.Settings;

internal sealed class ApplicationSettings
{
    public int SettingsVersion { get; set; } = AppSettingsMigrator.CurrentVersion;

    public ApplicationGeneralSettings General { get; set; } = new();

    public List<OverlaySettings> Overlays { get; set; } = [];

    public OverlaySettings GetOrAddOverlay(
        string id,
        int defaultWidth,
        int defaultHeight,
        int defaultX = 24,
        int defaultY = 24,
        bool defaultEnabled = false,
        double defaultOpacity = 1d)
    {
        var existing = Overlays.FirstOrDefault(overlay => string.Equals(overlay.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var overlay = new OverlaySettings
        {
            Id = id,
            Enabled = defaultEnabled,
            X = defaultX,
            Y = defaultY,
            Width = defaultWidth,
            Height = defaultHeight,
            Opacity = double.IsFinite(defaultOpacity) ? Math.Clamp(defaultOpacity, 0d, 1d) : 1d
        };
        Overlays.Add(overlay);
        return overlay;
    }
}

internal sealed class ApplicationGeneralSettings
{
    public string FontFamily { get; set; } = SharedOverlayContract.Current.DefaultFontFamily;

    public string UnitSystem { get; set; } = SharedOverlayContract.Current.DefaultUnitSystem;
}
