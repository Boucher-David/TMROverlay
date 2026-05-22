using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Overlays.CarRadar;

internal sealed record CarRadarOverlayViewModel(
    string Title,
    string Status,
    string Source,
    bool IsAvailable,
    bool HasCarLeft,
    bool HasCarRight,
    IReadOnlyList<LiveSpatialCar> Cars,
    LiveMulticlassApproach? StrongestMulticlassApproach,
    bool ShowMulticlassWarning,
    int MulticlassWarningRangeSeconds,
    int RadarVisibilitySeconds,
    bool PreviewVisible,
    LiveSpatialModel Spatial)
{
    private const double RadarRangeSeconds = 2d;
    public const int DefaultMulticlassWarningRangeSeconds = 5;
    public const int MinimumMulticlassWarningRangeSeconds = 3;
    public const int MaximumMulticlassWarningRangeSeconds = 10;
    public const int DefaultRadarVisibilitySeconds = 2;
    public const int MinimumRadarVisibilitySeconds = 2;
    public const int MaximumRadarVisibilitySeconds = 5;
    public const double FocusedCarLengthMeters = CarRadarCalibrationProfile.DefaultBodyLengthMeters;
    public const double PhysicalRadarRangeMeters = FocusedCarLengthMeters * 6d;
    public const double MaximumTimingAwareRangeMeters = FocusedCarLengthMeters * 15d;

    public bool HasCurrentSignal =>
        PreviewVisible
        || HasCarLeft
        || HasCarRight
        || Cars.Count > 0
        || StrongestMulticlassApproach is not null;

    public static CarRadarOverlayViewModel Empty { get; } = new(
        Title: "Car Radar",
        Status: "waiting",
        Source: "source: waiting",
        IsAvailable: false,
        HasCarLeft: false,
        HasCarRight: false,
        Cars: [],
        StrongestMulticlassApproach: null,
        ShowMulticlassWarning: true,
        MulticlassWarningRangeSeconds: DefaultMulticlassWarningRangeSeconds,
        RadarVisibilitySeconds: DefaultRadarVisibilitySeconds,
        PreviewVisible: false,
        Spatial: LiveSpatialModel.Empty);

    public static CarRadarOverlayViewModel From(
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now,
        bool previewVisible,
        bool showMulticlassWarning,
        CarRadarCalibrationProfile? calibrationProfile = null,
        int multiclassWarningRangeSeconds = DefaultMulticlassWarningRangeSeconds,
        int radarVisibilitySeconds = DefaultRadarVisibilitySeconds)
    {
        snapshot = snapshot with { Models = snapshot.CompleteModels() };
        var calibration = calibrationProfile ?? CarRadarCalibrationProfile.Default;
        var warningRangeSeconds = ClampMulticlassWarningRangeSeconds(multiclassWarningRangeSeconds);
        var visibilitySeconds = ClampRadarVisibilitySeconds(radarVisibilitySeconds);
        var availability = OverlayAvailabilityEvaluator.FromSnapshot(snapshot, now);
        var localContext = LiveLocalStrategyContext.ForRequirement(
            snapshot,
            now,
            OverlayContextRequirement.LocalPlayerInCar);
        var localContextAvailable = previewVisible || localContext.IsAvailable;
        var canRender = availability.IsAvailable && localContextAvailable;
        var spatial = canRender ? snapshot.Models.Spatial : LiveSpatialModel.Empty;
        var hasSpatialData = spatial.HasData;
        var cars = spatial.Cars
            .Where(car => IsInRadarRange(car, calibration, visibilitySeconds))
            .GroupBy(car => car.CarIdx)
            .Select(group => group.MinBy(car => Math.Abs(RangeRatio(car, calibration, visibilitySeconds)))!)
            .ToArray();
        LiveMulticlassApproach? multiclass = showMulticlassWarning
            ? spatial.MulticlassApproaches
                .Where(approach => IsInMulticlassWarningRange(approach, warningRangeSeconds))
                .OrderBy(approach => approach.RelativeSeconds is { } seconds ? Math.Abs(seconds) : double.MaxValue)
                .ThenByDescending(approach => approach.Urgency)
                .FirstOrDefault()
            : null;
        var status = !availability.IsAvailable && !previewVisible
            ? availability.StatusText
            : !localContextAvailable
                ? localContext.StatusText
            : !hasSpatialData
                ? "waiting for radar"
            : spatial.HasCarLeft && spatial.HasCarRight
                ? "cars both sides"
                : spatial.HasCarLeft
                    ? "car left"
                    : spatial.HasCarRight
                        ? "car right"
                        : multiclass is not null
                            ? "faster class"
                            : "clear";

        return new CarRadarOverlayViewModel(
            Title: "Car Radar",
            Status: status,
            Source: hasSpatialData ? "source: spatial telemetry" : "source: waiting",
            IsAvailable: canRender || previewVisible,
            HasCarLeft: spatial.HasCarLeft,
            HasCarRight: spatial.HasCarRight,
            Cars: cars,
            StrongestMulticlassApproach: multiclass,
            ShowMulticlassWarning: showMulticlassWarning,
            MulticlassWarningRangeSeconds: warningRangeSeconds,
            RadarVisibilitySeconds: visibilitySeconds,
            PreviewVisible: previewVisible,
            Spatial: spatial);
    }

    public static bool IsInRadarRange(LiveSpatialCar car)
    {
        return IsInRadarRange(car, CarRadarCalibrationProfile.Default);
    }

    public static bool IsInRadarRange(LiveSpatialCar car, CarRadarCalibrationProfile calibration)
    {
        return IsInRadarRange(car, calibration, DefaultRadarVisibilitySeconds);
    }

    public static bool IsInRadarRange(LiveSpatialCar car, CarRadarCalibrationProfile calibration, int radarVisibilitySeconds)
    {
        if (ReliableRelativeMeters(car) is { } meters)
        {
            return Math.Abs(meters) <= VisualRadarRangeMeters(car, calibration, radarVisibilitySeconds);
        }

        return false;
    }

    public static double? ReliableRelativeMeters(LiveSpatialCar car)
    {
        return car.RelativeMeters is { } meters && !double.IsNaN(meters) && !double.IsInfinity(meters)
            ? meters
            : null;
    }

    private static double RangeRatio(LiveSpatialCar car)
    {
        return RangeRatio(car, CarRadarCalibrationProfile.Default);
    }

    private static double RangeRatio(LiveSpatialCar car, CarRadarCalibrationProfile calibration)
    {
        return RangeRatio(car, calibration, DefaultRadarVisibilitySeconds);
    }

    private static double RangeRatio(LiveSpatialCar car, CarRadarCalibrationProfile calibration, int radarVisibilitySeconds)
    {
        if (ReliableRelativeMeters(car) is { } meters)
        {
            return Math.Clamp(meters / VisualRadarRangeMeters(car, calibration, radarVisibilitySeconds), -1d, 1d);
        }

        return Math.Sign(car.RelativeLaps);
    }

    public static double VisualRadarRangeMeters(LiveSpatialCar car)
    {
        return VisualRadarRangeMeters(car, CarRadarCalibrationProfile.Default);
    }

    public static double VisualRadarRangeMeters(LiveSpatialCar car, CarRadarCalibrationProfile calibration)
    {
        return VisualRadarRangeMeters(car, calibration, DefaultRadarVisibilitySeconds);
    }

    public static double VisualRadarRangeMeters(LiveSpatialCar car, CarRadarCalibrationProfile calibration, int radarVisibilitySeconds)
    {
        var bodyLengthMeters = Math.Max(0.001d, calibration.BodyLengthMeters);
        var range = bodyLengthMeters * 6d;
        if (ReliableRelativeMeters(car) is not { } meters
            || car.RelativeSeconds is not { } seconds
            || double.IsNaN(seconds)
            || double.IsInfinity(seconds))
        {
            return range;
        }

        var absMeters = Math.Abs(meters);
        var absSeconds = Math.Abs(seconds);
        if (absMeters <= range || absSeconds <= 0.05d)
        {
            return range;
        }

        var inferredMetersPerSecond = absMeters / absSeconds;
        if (double.IsNaN(inferredMetersPerSecond)
            || double.IsInfinity(inferredMetersPerSecond)
            || inferredMetersPerSecond <= 0d)
        {
            return range;
        }

        var timingAwareRange = inferredMetersPerSecond * ClampRadarVisibilitySeconds(radarVisibilitySeconds);
        return Math.Clamp(
            Math.Max(range, timingAwareRange),
            range,
            bodyLengthMeters * 15d);
    }

    public static bool IsInMulticlassWarningRange(LiveMulticlassApproach approach)
    {
        return IsInMulticlassWarningRange(approach, DefaultMulticlassWarningRangeSeconds);
    }

    public static bool IsInMulticlassWarningRange(LiveMulticlassApproach approach, int multiclassWarningRangeSeconds)
    {
        if (approach.RelativeSeconds is not { } seconds || double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            return false;
        }

        return seconds < -RadarRangeSeconds && seconds >= -ClampMulticlassWarningRangeSeconds(multiclassWarningRangeSeconds);
    }

    public static int ClampMulticlassWarningRangeSeconds(int seconds)
    {
        return Math.Clamp(seconds, MinimumMulticlassWarningRangeSeconds, MaximumMulticlassWarningRangeSeconds);
    }

    public static int ClampRadarVisibilitySeconds(int seconds)
    {
        return Math.Clamp(seconds, MinimumRadarVisibilitySeconds, MaximumRadarVisibilitySeconds);
    }
}
