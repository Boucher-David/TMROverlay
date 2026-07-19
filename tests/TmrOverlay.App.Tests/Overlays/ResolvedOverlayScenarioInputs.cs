using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;

namespace TmrOverlay.App.Tests.Overlays;

// Test scenarios start from the same defaults/migration path as a normal app
// load, then make their minimal, named patch. This avoids hand-assembled
// settings silently drifting from product defaults.
internal sealed record ResolvedOverlayScenarioInputs(
    ApplicationSettings Settings,
    OverlaySettings Overlay)
{
    public static ResolvedOverlayScenarioInputs Create(
        OverlayDefinition definition,
        Action<ApplicationSettings>? settingsPatch = null,
        Action<OverlaySettings>? overlayPatch = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // Scenario settings must start from the packaged contract just like a
        // normal application load.  In particular, do this before creating
        // ApplicationSettings so a prior test's process-local contract state
        // cannot silently determine the defaults under test.
        if (!SharedOverlayContract.TryLoadFromDefaultLocation(out var loadError))
        {
            throw new InvalidOperationException(
                $"Unable to load the packaged shared overlay contract for scenario inputs: {loadError}");
        }

        var settings = AppSettingsMigrator.Migrate(new ApplicationSettings());
        settingsPatch?.Invoke(settings);
        var overlay = settings.GetOrAddOverlay(
            definition.Id,
            definition.DefaultWidth,
            definition.DefaultHeight);
        overlayPatch?.Invoke(overlay);
        return new ResolvedOverlayScenarioInputs(settings, overlay);
    }
}
