using System.Globalization;
using TmrOverlay.App.Overlays.SimpleTelemetry;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Overlays.InputState;

internal static class InputStateOverlayViewModel
{
    public static SimpleTelemetryOverlayViewModel From(
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now,
        string unitSystem)
    {
        snapshot = snapshot with { Models = snapshot.CompleteModels() };
        var availability = OverlayAvailabilityEvaluator.FromSnapshot(snapshot, now);
        if (!availability.IsAvailable)
        {
            return SimpleTelemetryOverlayViewModel.Waiting("Input / Car State", availability.StatusText);
        }

        if (!IsPlayerInCar(snapshot, now))
        {
            return SimpleTelemetryOverlayViewModel.Waiting("Input / Car State", "waiting for player in car");
        }

        var inputs = snapshot.Models.Inputs;
        if (!inputs.HasData)
        {
            return SimpleTelemetryOverlayViewModel.Waiting("Input / Car State", "waiting for car telemetry");
        }

        var warningTone = inputs.EngineWarnings is > 0 ? SimpleTelemetryTone.Warning : SimpleTelemetryTone.Normal;
        var status = inputs.EngineWarnings is > 0
            ? "engine warning"
            : FormatStatus(inputs);
        var rows = new[]
        {
            new SimpleTelemetryRowViewModel("Speed", SimpleTelemetryOverlayViewModel.FormatSpeed(inputs.SpeedMetersPerSecond, unitSystem)),
            new SimpleTelemetryRowViewModel("Gear / RPM", SimpleTelemetryOverlayViewModel.JoinAvailable(FormatGear(inputs.Gear), FormatRpm(inputs.Rpm))),
            new SimpleTelemetryRowViewModel("Pedals", FormatPedals(inputs)),
            new SimpleTelemetryRowViewModel("Steering", FormatSteering(inputs.SteeringWheelAngle)),
            new SimpleTelemetryRowViewModel("Warnings", FormatWarnings(inputs.EngineWarnings), warningTone),
            new SimpleTelemetryRowViewModel("Electrical", FormatVoltage(inputs.Voltage)),
            new SimpleTelemetryRowViewModel("Cooling", SimpleTelemetryOverlayViewModel.FormatTemperature(inputs.WaterTempC, unitSystem)),
            new SimpleTelemetryRowViewModel("Oil / Fuel", FormatOilFuel(inputs, unitSystem))
        };

        return new SimpleTelemetryOverlayViewModel(
            Title: "Input / Car State",
            Status: status,
            Source: "source: local car telemetry",
            Tone: inputs.EngineWarnings is > 0 ? SimpleTelemetryTone.Warning : SimpleTelemetryTone.Normal,
            Rows: rows);
    }

    private static string FormatPedals(LiveInputTelemetryModel inputs)
    {
        if (!inputs.HasPedalInputs)
        {
            return "--";
        }

        return SimpleTelemetryOverlayViewModel.JoinAvailable(
            FormatPedal("T", inputs.Throttle, activity: null),
            FormatPedal("B", inputs.Brake, inputs.BrakeAbsActive == true ? "ABS" : null),
            $"C {SimpleTelemetryOverlayViewModel.FormatPercent(inputs.Clutch)}");
    }

    private static bool IsPlayerInCar(LiveTelemetrySnapshot snapshot, DateTimeOffset now)
    {
        return LiveLocalStrategyContext.ForRequirement(
            snapshot,
            now,
            OverlayContextRequirement.LocalPlayerInCarOrPit).IsAvailable;
    }

    private static string FormatStatus(LiveInputTelemetryModel inputs)
    {
        return SimpleTelemetryOverlayViewModel.JoinAvailable(
            FormatGear(inputs.Gear),
            FormatRpm(inputs.Rpm),
            inputs.BrakeAbsActive == true ? "ABS" : null);
    }

    private static string FormatPedal(string label, double? value, string? activity)
    {
        var formatted = $"{label} {SimpleTelemetryOverlayViewModel.FormatPercent(value)}";
        return string.IsNullOrWhiteSpace(activity)
            ? formatted
            : $"{formatted} {activity}";
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

    private static string FormatRpm(double? rpm)
    {
        return rpm is { } value && SimpleTelemetryOverlayViewModel.IsFinite(value)
            ? $"{value.ToString("0", CultureInfo.InvariantCulture)} rpm"
            : "--";
    }

    private static string FormatSteering(double? radians)
    {
        return radians is { } value && SimpleTelemetryOverlayViewModel.IsFinite(value)
            ? $"{(value * 180d / Math.PI).ToString("+0;-0;0", CultureInfo.InvariantCulture)} deg"
            : "--";
    }

    private static string FormatWarnings(int? warnings)
    {
        return warnings is { } value
            ? value == 0
                ? "none"
                : $"0x{value.ToString("X", CultureInfo.InvariantCulture)}"
            : "--";
    }

    private static string FormatVoltage(double? voltage)
    {
        return voltage is { } value && SimpleTelemetryOverlayViewModel.IsFinite(value)
            ? $"{value.ToString("0.0", CultureInfo.InvariantCulture)} V"
            : "--";
    }

    private static string FormatOilFuel(LiveInputTelemetryModel inputs, string unitSystem)
    {
        var oilTemp = SimpleTelemetryOverlayViewModel.FormatTemperature(inputs.OilTempC, unitSystem);
        var oilPressure = SimpleTelemetryOverlayViewModel.FormatPressure(inputs.OilPressureBar, unitSystem);
        var fuelPressure = SimpleTelemetryOverlayViewModel.FormatPressure(inputs.FuelPressureBar, unitSystem);
        return SimpleTelemetryOverlayViewModel.JoinAvailable(
            oilTemp == "--" ? null : $"oil {oilTemp}",
            oilPressure == "--" ? null : $"oil {oilPressure}",
            fuelPressure == "--" ? null : $"fuel {fuelPressure}");
    }
}
