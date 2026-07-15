using TmrOverlay.Core.History;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.Core.Overlays;

internal sealed record LiveLocalStrategyContextSnapshot(
    bool IsAvailable,
    string Reason,
    string StatusText);

internal static class LiveLocalStrategyContext
{
    public const string FuelWaitingStatus = "waiting for local fuel context";
    public const string PitServiceWaitingStatus = "waiting for local pit-service context";
    public const string LocalInCarWaitingStatus = "waiting for local in-car context";
    public const string LocalInCarOrPitWaitingStatus = "waiting for local in-car or pit context";

    public static LiveLocalStrategyContextSnapshot ForFuelCalculator(
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now)
    {
        return Evaluate(snapshot, now, FuelWaitingStatus);
    }

    // The factual Fuel V2 presenter may show a current fuel/capacity readout
    // during an iRacing grid or pit transition when the SDK has local scalar
    // fuel but cannot resolve a usable local player or focus/progress row yet. The
    // fallback requires the session-declared local driver to match the raw
    // camera identity and confirms that entry is not a spectator. This is
    // deliberately display-only: V1 and every strategy/burn gate continue to
    // require the normal local-focus context above.
    public static LiveLocalStrategyContextSnapshot ForFuelV2FactualDisplay(
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now)
    {
        var localContext = ForFuelV2FactualLocalContext(snapshot, now);
        if (!localContext.IsAvailable)
        {
            return localContext;
        }

        // The native manager uses this same context before it creates or shows
        // the V2 form. Keep all presenter prerequisites here so it cannot
        // briefly show a window which the 250ms V2 refresh immediately turns
        // into a no-render waiting model.
        if (!snapshot.HasFrameForCurrentContext
            || !snapshot.HasSessionInfoForCurrentCollection)
        {
            return Unavailable("current_fuel_telemetry_unavailable", "waiting for current fuel telemetry");
        }

        if (!HasReliableLocalFuel(snapshot))
        {
            return Unavailable("fuel_level_unavailable", "waiting for fuel level");
        }

        return localContext;
    }

    // Capture/replay collectors may need to preserve a verified local identity
    // while a Fuel V2 display is intentionally hidden for missing current-frame
    // or fuel facts. Keep those presentation prerequisites out of this method
    // so capture quality is not coupled to transient overlay visibility.
    public static LiveLocalStrategyContextSnapshot ForFuelV2FactualLocalContext(
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now)
    {
        var strategyContext = ForFuelCalculator(snapshot, now);
        if (strategyContext.IsAvailable
            || strategyContext.Reason is not ("focus_unavailable" or "player_car_unavailable"))
        {
            return strategyContext;
        }

        var sample = snapshot.LatestSample;
        if (sample is null
            || !IsFactualLocalActiveContext(sample)
            || !IsProgressOnlyFocusGap(sample)
            || !HasVerifiedSessionDriverCameraIdentity(snapshot.Context, sample))
        {
            return strategyContext;
        }

        return new LiveLocalStrategyContextSnapshot(
            IsAvailable: true,
            Reason: "session_driver_camera_identity_fallback",
            StatusText: "live factual fuel");
    }

    public static LiveLocalStrategyContextSnapshot ForPitService(
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now)
    {
        return Evaluate(snapshot, now, PitServiceWaitingStatus, allowPitContext: true);
    }

    public static LiveLocalStrategyContextSnapshot ForRequirement(
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now,
        OverlayContextRequirement requirement)
    {
        return requirement switch
        {
            OverlayContextRequirement.AnyTelemetry => new LiveLocalStrategyContextSnapshot(
                IsAvailable: true,
                Reason: "not_required",
                StatusText: "live"),
            OverlayContextRequirement.LocalPlayerInCar => Evaluate(
                snapshot,
                now,
                LocalInCarWaitingStatus,
                allowPitContext: false),
            OverlayContextRequirement.LocalPlayerInCarOrPit => Evaluate(
                snapshot,
                now,
                LocalInCarOrPitWaitingStatus,
                allowPitContext: true),
            _ => Unavailable("unknown_context_requirement", LocalInCarWaitingStatus)
        };
    }

    private static LiveLocalStrategyContextSnapshot Evaluate(
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now,
        string localWaitingStatus,
        bool allowPitContext = true)
    {
        var telemetryAvailability = OverlayAvailabilityEvaluator.FromSnapshot(snapshot, now);
        if (!telemetryAvailability.IsAvailable)
        {
            return Unavailable(ReasonCode(telemetryAvailability.Reason), telemetryAvailability.StatusText);
        }

        var models = snapshot.CompleteModels();
        var playerCarIdx = ValidCarIdx(
            models.DriverDirectory.PlayerCarIdx
            ?? models.Reference.PlayerCarIdx);
        if (playerCarIdx is null)
        {
            return Unavailable("player_car_unavailable", localWaitingStatus);
        }

        var focusCarIdx = ValidCarIdx(
            models.Reference.FocusCarIdx
            ?? models.DriverDirectory.FocusCarIdx);
        if (focusCarIdx is null)
        {
            return Unavailable("focus_unavailable", localWaitingStatus);
        }

        if (focusCarIdx != playerCarIdx)
        {
            return Unavailable("focus_on_another_car", localWaitingStatus);
        }

        if (IsGarageContext(models))
        {
            return Unavailable("garage", localWaitingStatus);
        }

        if (!IsLocalActiveContext(models, allowPitContext))
        {
            return Unavailable("not_in_car", localWaitingStatus);
        }

        return new LiveLocalStrategyContextSnapshot(
            IsAvailable: true,
            Reason: "available",
            StatusText: "live");
    }

    private static bool IsGarageContext(LiveRaceModels models)
    {
        var race = models.RaceEvents;
        var reference = models.Reference;
        return (race.HasData && (race.IsInGarage || race.IsGarageVisible))
            || reference.IsInGarage;
    }

    private static bool IsLocalActiveContext(LiveRaceModels models, bool allowPitContext)
    {
        var race = models.RaceEvents;
        var reference = models.Reference;
        if (IsPitContext(models))
        {
            return allowPitContext;
        }

        if ((race.HasData && race.IsOnTrack) || reference.IsOnTrack)
        {
            return true;
        }

        return false;
    }

    private static bool IsPitContext(LiveRaceModels models)
    {
        var reference = models.Reference;
        var race = models.RaceEvents;
        var pit = models.FuelPit;
        return (race.HasData && race.OnPitRoad)
            || reference.OnPitRoad == true
            || reference.PlayerOnPitRoad == true
            || reference.PlayerCarInPitStall
            || IsPitRoadTrackSurface(reference.TrackSurface)
            || IsPitRoadTrackSurface(reference.PlayerTrackSurface)
            || pit.OnPitRoad
            || pit.PitstopActive
            || pit.PlayerCarInPitStall
            || pit.TeamOnPitRoad == true;
    }

    private static bool IsPitRoadTrackSurface(int? trackSurface)
    {
        return trackSurface is 1 or 2;
    }

    private static bool HasReliableLocalFuel(LiveTelemetrySnapshot snapshot)
    {
        var models = snapshot.CompleteModels();
        var fuelSnapshot = snapshot.Fuel.HasValidFuel
            ? snapshot.Fuel
            : models.FuelPit.Fuel;
        return fuelSnapshot.HasValidFuel
            && fuelSnapshot.FuelLevelLiters is { } fuel
            && double.IsFinite(fuel)
            && fuel > 0d;
    }

    private static bool IsFactualLocalActiveContext(HistoricalTelemetrySample sample)
    {
        return !sample.IsInGarage
            && (sample.IsOnTrack
                || sample.OnPitRoad
                || sample.PitstopActive
                || sample.PlayerCarInPitStall
                || sample.TeamOnPitRoad == true);
    }

    private static bool IsProgressOnlyFocusGap(HistoricalTelemetrySample sample)
    {
        // A valid raw camera ID with absent timing/spatial progress is the
        // specific transient observed in live captures. A missing or invalid
        // camera is not an identity proof and must stay hidden.
        return string.Equals(
            sample.FocusUnavailableReason,
            "cam_car_progress_unavailable",
            StringComparison.Ordinal);
    }

    private static bool HasVerifiedSessionDriverCameraIdentity(
        HistoricalSessionContext context,
        HistoricalTelemetrySample sample)
    {
        var sessionDriverCarIdx = ValidCarIdx(context.DriverCarIdx);
        var rawCamCarIdx = ValidCarIdx(sample.RawCamCarIdx);
        if (sessionDriverCarIdx is null || rawCamCarIdx != sessionDriverCarIdx)
        {
            return false;
        }

        if (ValidCarIdx(sample.PlayerCarIdx) is { } playerCarIdx && playerCarIdx != sessionDriverCarIdx)
        {
            return false;
        }

        if (ValidCarIdx(sample.FocusCarIdx) is { } focusCarIdx && focusCarIdx != sessionDriverCarIdx)
        {
            return false;
        }

        var sessionDriver = context.Drivers.FirstOrDefault(driver => driver.CarIdx == sessionDriverCarIdx);
        return sessionDriver?.IsSpectator == false;
    }

    private static int? ValidCarIdx(int? carIdx)
    {
        // Player/focus IDs arrive through the normalized live model, whose
        // collector validates against the current session schema. Keeping a
        // legacy 64-slot cap here would reject legitimate expanded CarIdx IDs.
        return carIdx is >= 0 ? carIdx : null;
    }

    private static string ReasonCode(OverlayAvailabilityReason reason)
    {
        return reason switch
        {
            OverlayAvailabilityReason.Disconnected => "disconnected",
            OverlayAvailabilityReason.WaitingForTelemetry => "waiting_for_telemetry",
            OverlayAvailabilityReason.StaleTelemetry => "stale_telemetry",
            OverlayAvailabilityReason.HiddenForSession => "hidden_for_session",
            OverlayAvailabilityReason.NotInCar => "not_in_car",
            OverlayAvailabilityReason.NoData => "no_data",
            OverlayAvailabilityReason.Error => "error",
            _ => "available"
        };
    }

    private static LiveLocalStrategyContextSnapshot Unavailable(string reason, string statusText)
    {
        return new LiveLocalStrategyContextSnapshot(
            IsAvailable: false,
            Reason: reason,
            StatusText: statusText);
    }
}
