using System.Globalization;
using TmrOverlay.App.Overlays.Content;
using TmrOverlay.App.Overlays.SimpleTelemetry;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;
using InputGeometry = TmrOverlay.App.Overlays.OverlayGeometryContractValues.InputState;

namespace TmrOverlay.App.Overlays.InputState;

internal sealed record InputStateRenderModel(
    string Status,
    string Source,
    SimpleTelemetryTone Tone,
    bool IsAvailable,
    double? Throttle,
    double? Brake,
    double? Clutch,
    double? SteeringWheelAngle,
    double? SteeringWheelVisualAngle,
    double? SpeedMetersPerSecond,
    int? Gear,
    string SpeedText,
    string GearText,
    string SteeringText,
    bool BrakeAbsActive,
    bool ShowThrottleTrace,
    bool ShowBrakeTrace,
    bool ShowClutchTrace,
    bool ShowThrottle,
    bool ShowBrake,
    bool ShowClutch,
    bool ShowSteering,
    bool ShowGear,
    bool ShowSpeed,
    bool HasGraph,
    bool HasRail,
    bool HasContent,
    int SampleIntervalMilliseconds,
    int MaximumTracePoints,
    IReadOnlyList<InputStateTracePoint> Trace);

internal sealed record InputStateTracePoint(
    double Throttle,
    double Brake,
    double Clutch,
    bool BrakeAbsActive);

internal static class InputStateRenderModelBuilder
{
    public const int RefreshIntervalMilliseconds = InputGeometry.RefreshIntervalMilliseconds;
    public const int MaximumTracePoints = InputGeometry.MaximumTracePoints;
    public const int GraphOnlyBaseWidth = InputGeometry.GraphOnlyBaseWidth;
    public const int RailOnlyBaseWidth = InputGeometry.RailOnlyBaseWidth;

    public static InputStateRenderModel Build(
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now,
        string unitSystem,
        OverlaySettings settings,
        List<InputStateTracePoint> trace)
    {
        var inputs = snapshot.Models.Inputs;
        var viewModel = InputStateOverlayViewModel.From(snapshot, now, unitSystem);
        var isAvailable = viewModel.Tone is not SimpleTelemetryTone.Waiting && inputs.HasData;
        if (isAvailable)
        {
            trace.Add(new InputStateTracePoint(
                Clamp01(inputs.Throttle),
                Clamp01(inputs.Brake),
                Clamp01(inputs.Clutch),
                inputs.BrakeAbsActive == true));
            if (trace.Count > MaximumTracePoints)
            {
                trace.RemoveRange(0, trace.Count - MaximumTracePoints);
            }
        }
        else
        {
            trace.Clear();
        }

        var sessionKind = OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot);
        var showThrottleTrace = BlockEnabled(settings, OverlayContentColumnSettings.InputThrottleTraceBlockId, sessionKind);
        var showBrakeTrace = BlockEnabled(settings, OverlayContentColumnSettings.InputBrakeTraceBlockId, sessionKind);
        var showClutchTrace = BlockEnabled(settings, OverlayContentColumnSettings.InputClutchTraceBlockId, sessionKind);
        var showThrottle = BlockEnabled(settings, OverlayContentColumnSettings.InputThrottleBlockId, sessionKind);
        var showBrake = BlockEnabled(settings, OverlayContentColumnSettings.InputBrakeBlockId, sessionKind);
        var showClutch = BlockEnabled(settings, OverlayContentColumnSettings.InputClutchBlockId, sessionKind);
        var showSteering = BlockEnabled(settings, OverlayContentColumnSettings.InputSteeringBlockId, sessionKind);
        var showGear = BlockEnabled(settings, OverlayContentColumnSettings.InputGearBlockId, sessionKind);
        var showSpeed = BlockEnabled(settings, OverlayContentColumnSettings.InputSpeedBlockId, sessionKind);
        var configuredGraph = showThrottleTrace || showBrakeTrace || showClutchTrace;
        var configuredRail = showThrottle || showBrake || showClutch || showSteering || showGear || showSpeed;
        var hasGraph = isAvailable && configuredGraph;
        var hasRail = isAvailable && configuredRail;
        var hasContent = hasGraph || hasRail;
        var displayInputs = isAvailable ? inputs : LiveInputTelemetryModel.Empty;
        var status = hasContent
            ? viewModel.Status
            : isAvailable
                ? "no input content enabled"
                : viewModel.Status;
        return new InputStateRenderModel(
            status,
            viewModel.Source,
            viewModel.Tone,
            isAvailable,
            displayInputs.Throttle,
            displayInputs.Brake,
            displayInputs.Clutch,
            displayInputs.SteeringWheelAngle,
            VisualSteeringWheelAngle(displayInputs.SteeringWheelAngle),
            displayInputs.SpeedMetersPerSecond,
            displayInputs.Gear,
            SimpleTelemetryOverlayViewModel.FormatSpeed(displayInputs.SpeedMetersPerSecond, unitSystem),
            FormatGear(displayInputs.Gear),
            FormatSteering(displayInputs.SteeringWheelAngle),
            displayInputs.BrakeAbsActive == true,
            showThrottleTrace,
            showBrakeTrace,
            showClutchTrace,
            showThrottle,
            showBrake,
            showClutch,
            showSteering,
            showGear,
            showSpeed,
            hasGraph,
            hasRail,
            hasContent,
            RefreshIntervalMilliseconds,
            MaximumTracePoints,
            trace.ToArray());
    }

    public static bool HasEnabledContent(OverlaySettings settings, OverlaySessionKind? sessionKind = null)
    {
        return BlockEnabled(settings, OverlayContentColumnSettings.InputThrottleTraceBlockId, sessionKind)
            || BlockEnabled(settings, OverlayContentColumnSettings.InputBrakeTraceBlockId, sessionKind)
            || BlockEnabled(settings, OverlayContentColumnSettings.InputClutchTraceBlockId, sessionKind)
            || BlockEnabled(settings, OverlayContentColumnSettings.InputThrottleBlockId, sessionKind)
            || BlockEnabled(settings, OverlayContentColumnSettings.InputBrakeBlockId, sessionKind)
            || BlockEnabled(settings, OverlayContentColumnSettings.InputClutchBlockId, sessionKind)
            || BlockEnabled(settings, OverlayContentColumnSettings.InputSteeringBlockId, sessionKind)
            || BlockEnabled(settings, OverlayContentColumnSettings.InputGearBlockId, sessionKind)
            || BlockEnabled(settings, OverlayContentColumnSettings.InputSpeedBlockId, sessionKind);
    }

    public static int BaseWidthForEnabledContent(
        OverlaySettings settings,
        int fullWidth,
        OverlaySessionKind? sessionKind = null)
    {
        var hasGraph = BlockEnabled(settings, OverlayContentColumnSettings.InputThrottleTraceBlockId, sessionKind)
            || BlockEnabled(settings, OverlayContentColumnSettings.InputBrakeTraceBlockId, sessionKind)
            || BlockEnabled(settings, OverlayContentColumnSettings.InputClutchTraceBlockId, sessionKind);
        var hasRail = BlockEnabled(settings, OverlayContentColumnSettings.InputThrottleBlockId, sessionKind)
            || BlockEnabled(settings, OverlayContentColumnSettings.InputBrakeBlockId, sessionKind)
            || BlockEnabled(settings, OverlayContentColumnSettings.InputClutchBlockId, sessionKind)
            || BlockEnabled(settings, OverlayContentColumnSettings.InputSteeringBlockId, sessionKind)
            || BlockEnabled(settings, OverlayContentColumnSettings.InputGearBlockId, sessionKind)
            || BlockEnabled(settings, OverlayContentColumnSettings.InputSpeedBlockId, sessionKind);

        if (hasGraph && hasRail)
        {
            return fullWidth;
        }

        if (hasGraph)
        {
            return GraphOnlyBaseWidth;
        }

        return RailOnlyBaseWidth;
    }

    private static bool BlockEnabled(
        OverlaySettings settings,
        string blockId,
        OverlaySessionKind? sessionKind = null)
    {
        var block = OverlayContentColumnSettings.InputState.Blocks?.FirstOrDefault(
            block => string.Equals(block.Id, blockId, StringComparison.Ordinal));
        return block is null || OverlayContentColumnSettings.BlockEnabled(settings, block, sessionKind);
    }

    private static double Clamp01(double? value)
    {
        return value is { } finite && double.IsFinite(finite)
            ? Math.Clamp(finite, 0d, 1d)
            : 0d;
    }

    private static string FormatGear(int? gear)
    {
        return gear switch
        {
            -1 => "R",
            0 => "N",
            > 0 => gear.Value.ToString(CultureInfo.InvariantCulture),
            _ => "--"
        };
    }

    private static string FormatSteering(double? radians)
    {
        return radians is { } value && double.IsFinite(value)
            ? $"{(value * 180d / Math.PI).ToString("+0;-0;0", CultureInfo.InvariantCulture)} deg"
            : "--";
    }

    public static double? VisualSteeringWheelAngle(double? radians)
    {
        return radians is { } value && double.IsFinite(value) ? -value : null;
    }
}
