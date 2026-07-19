using TmrOverlay.App.Cars;
using TmrOverlay.Core.History;

namespace TmrOverlay.App.Overlays.CarRadar;

internal sealed record CarRadarCalibrationProfile(
    double BodyLengthMeters,
    bool IsHistoryBacked,
    string Source)
{
    public const double DefaultBodyLengthMeters = 4.746d;

    public static CarRadarCalibrationProfile Default { get; } = new(
        DefaultBodyLengthMeters,
        IsHistoryBacked: false,
        Source: "default");

    public static CarRadarCalibrationProfile FromHistory(
        CarRadarCalibrationLookupResult? history,
        CarSpecificationCatalog? specificationCatalog = null)
    {
        var spec = history is not null
            ? (specificationCatalog ?? CarSpecificationCatalog.Bundled).TryFind(history.Combo)
            : null;
        if (spec is { BodyLengthMeters: { } exactBodyLengthMeters } && spec.IsExact)
        {
            return new CarRadarCalibrationProfile(
                exactBodyLengthMeters,
                IsHistoryBacked: false,
                Source: "bundled-spec");
        }

        if (TryCreate(history?.CurrentSessionAggregate, "current-session", out var currentSessionProfile))
        {
            return currentSessionProfile;
        }

        if (TryCreate(history?.UserAggregate, "user", out var userProfile))
        {
            return userProfile;
        }

        if (TryCreate(history?.BaselineAggregate, "baseline", out var baselineProfile))
        {
            return baselineProfile;
        }

        if (spec is { BodyLengthMeters: { } estimatedBodyLengthMeters })
        {
            return new CarRadarCalibrationProfile(
                estimatedBodyLengthMeters,
                IsHistoryBacked: false,
                Source: "bundled-estimate");
        }

        return Default;
    }

    private static bool TryCreate(
        HistoricalCarRadarCalibrationAggregate? aggregate,
        string source,
        out CarRadarCalibrationProfile profile)
    {
        profile = Default;
        var metric = aggregate?.RadarCalibration?.EstimatedBodyLengthMeters;
        if (!HistoricalRadarCalibrationTrust.TryGetTrustedBodyLengthMeters(metric, out var bodyLengthMeters))
        {
            return false;
        }

        profile = new CarRadarCalibrationProfile(
            bodyLengthMeters,
            IsHistoryBacked: true,
            Source: source);
        return true;
    }
}
