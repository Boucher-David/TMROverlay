using TmrOverlay.Core.PitService;
using Xunit;

namespace TmrOverlay.App.Tests.PitService;

public sealed class PitServiceRouteTrackerTests
{
    [Fact]
    public void Track_CollectsTwoSampleConfirmedLocalRouteWithFuelAndAssignedStall()
    {
        var tracker = new PitServiceRouteTracker();
        var start = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

        Assert.Null(tracker.Track(Frame(start, 1, onPitRoad: false, inStall: false, fuel: 40d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(1), 2, onPitRoad: true, inStall: false, fuel: 39.9d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(2), 3, onPitRoad: true, inStall: false, fuel: 39.8d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(3), 4, onPitRoad: true, inStall: true, fuel: 39.6d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(4), 5, onPitRoad: true, inStall: true, fuel: 39.5d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(5), 6, onPitRoad: true, inStall: false, fuel: 44d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(6), 7, onPitRoad: true, inStall: false, fuel: 43.9d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(7), 8, onPitRoad: false, inStall: false, fuel: 43.7d)));

        var observation = tracker.Track(Frame(start.AddSeconds(8), 9, onPitRoad: false, inStall: false, fuel: 43.6d));

        Assert.NotNull(observation);
        Assert.Equal(2d, observation.EntryToBoxSeconds);
        Assert.Equal(2d, observation.BoxToExitSeconds);
        Assert.NotNull(observation.EntryToBoxFuelUsedLiters);
        Assert.NotNull(observation.BoxToExitFuelUsedLiters);
        Assert.Equal(0.3d, observation.EntryToBoxFuelUsedLiters.Value, precision: 6);
        Assert.Equal(0.3d, observation.BoxToExitFuelUsedLiters.Value, precision: 6);
        Assert.Equal("driver-pit-track-percent:0.068197", observation.Assignment.PitBoxIdentity);
        Assert.Equal("pit-speed-kph:80", observation.Assignment.PitSpeedRuleIdentity);
        Assert.True(observation.HasObservedBoxEntry);
        Assert.True(observation.HasObservedBoxExit);
        Assert.True(observation.HasQualifiedEntryToBoxLeg);
        Assert.True(observation.HasQualifiedBoxToExitLeg);
        Assert.True(observation.HasCompleteRoute);
        Assert.Empty(observation.QualificationFlags);

        var withoutStaticAssignment = observation with
        {
            Assignment = new PitServiceRouteAssignment(
                DriverPitTrackPct: null,
                TrackPitSpeedLimitKph: null,
                TrackNumPitStalls: null,
                DCRuleSet: null)
        };
        Assert.True(withoutStaticAssignment.HasCompleteRoute);
        Assert.False(withoutStaticAssignment.Assignment.IsComplete);
    }

    [Fact]
    public void Track_DoesNotTreatAnAlreadyOnPitRoadFirstFrameAsDirectEntry()
    {
        var tracker = new PitServiceRouteTracker();
        var start = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

        Assert.Null(tracker.Track(Frame(start, 1, onPitRoad: true, inStall: false, fuel: 40d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(1), 2, onPitRoad: true, inStall: true, fuel: 39.5d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(2), 3, onPitRoad: true, inStall: false, fuel: 44d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(3), 4, onPitRoad: false, inStall: false, fuel: 43.7d)));

        Assert.Null(tracker.Finish());
    }

    [Fact]
    public void Track_QualifiesAContinuousNoStallPitLanePassSeparatelyFromAStoppedRoute()
    {
        var tracker = new PitServiceRouteTracker();
        var start = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

        Assert.Null(tracker.Track(Frame(start, 1, onPitRoad: false, inStall: false, fuel: 40d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(1), 2, onPitRoad: true, inStall: false, fuel: 39.9d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(2), 3, onPitRoad: true, inStall: false, fuel: 39.8d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(3), 4, onPitRoad: true, inStall: false, fuel: 39.7d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(4), 5, onPitRoad: false, inStall: false, fuel: 39.5d)));

        var observation = tracker.Track(Frame(start.AddSeconds(5), 6, onPitRoad: false, inStall: false, fuel: 39.4d));

        Assert.NotNull(observation);
        Assert.Null(observation.BoxEntry);
        Assert.Null(observation.BoxExit);
        Assert.True(observation.HasCompletePitLanePass);
        Assert.False(observation.HasCompleteRoute);
        Assert.Contains("box-entry-unobserved", observation.QualificationFlags);
        Assert.Contains("box-exit-unobserved", observation.QualificationFlags);
    }

    [Fact]
    public void Track_RetainsInterruptedPartialObservationButNeverQualifiesItAsACompleteRoute()
    {
        var tracker = new PitServiceRouteTracker();
        var start = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

        Assert.Null(tracker.Track(Frame(start, 1, onPitRoad: false, inStall: false, fuel: 40d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(1), 2, onPitRoad: true, inStall: false, fuel: 39.9d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(2), 3, onPitRoad: true, inStall: false, fuel: 39.8d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(3), 4, onPitRoad: true, inStall: true, fuel: 39.6d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(4), 5, onPitRoad: true, inStall: true, fuel: 39.5d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(5), 6, onPitRoad: true, inStall: true, fuel: null, reliable: false)));

        var observation = tracker.Finish();

        Assert.NotNull(observation);
        Assert.True(observation.HasObservedBoxEntry);
        Assert.Contains("telemetry-interrupted", observation.QualificationFlags);
        Assert.Contains("pit-exit-unobserved", observation.QualificationFlags);
        Assert.False(observation.HasCompleteRoute);
    }

    [Fact]
    public void Track_RejectsRouteFuelRegressionFromCompleteQualification()
    {
        var tracker = new PitServiceRouteTracker();
        var start = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

        Assert.Null(tracker.Track(Frame(start, 1, onPitRoad: false, inStall: false, fuel: 40d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(1), 2, onPitRoad: true, inStall: false, fuel: 39.9d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(2), 3, onPitRoad: true, inStall: false, fuel: 39.8d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(3), 4, onPitRoad: true, inStall: true, fuel: 40.2d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(4), 5, onPitRoad: true, inStall: true, fuel: 40.3d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(5), 6, onPitRoad: true, inStall: false, fuel: 44d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(6), 7, onPitRoad: true, inStall: false, fuel: 43.9d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(7), 8, onPitRoad: false, inStall: false, fuel: 43.7d)));

        var observation = tracker.Track(Frame(start.AddSeconds(8), 9, onPitRoad: false, inStall: false, fuel: 43.6d));

        Assert.NotNull(observation);
        Assert.Contains("entry-to-box-fuel-nonmonotonic", observation.QualificationFlags);
        Assert.False(observation.HasQualifiedEntryToBoxLeg);
        Assert.True(observation.HasQualifiedBoxToExitLeg);
        Assert.False(observation.HasCompleteRoute);
    }

    [Fact]
    public void Track_RejectsGarageTransitionFromCompleteQualification()
    {
        var tracker = new PitServiceRouteTracker();
        var start = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

        Assert.Null(tracker.Track(Frame(start, 1, onPitRoad: false, inStall: false, fuel: 40d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(1), 2, onPitRoad: true, inStall: false, fuel: 39.9d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(2), 3, onPitRoad: true, inStall: false, fuel: 39.8d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(3), 4, onPitRoad: true, inStall: true, fuel: 39.6d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(4), 5, onPitRoad: true, inStall: true, fuel: 39.5d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(5), 6, onPitRoad: true, inStall: true, fuel: 39.5d, isInGarage: true)));

        var observation = tracker.Finish();

        Assert.NotNull(observation);
        Assert.Contains("garage-during-route", observation.QualificationFlags);
        Assert.False(observation.HasCompleteRoute);
    }

    private static PitServiceRouteObservationFrame Frame(
        DateTimeOffset capturedAtUtc,
        long sequence,
        bool onPitRoad,
        bool inStall,
        double? fuel,
        bool reliable = true,
        bool isInGarage = false)
    {
        return new PitServiceRouteObservationFrame(
            CapturedAtUtc: capturedAtUtc,
            SessionTimeSeconds: (capturedAtUtc - new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero)).TotalSeconds,
            SessionTick: checked((int)sequence * 60),
            Sequence: sequence,
            IsReliable: reliable,
            OnPitRoad: onPitRoad,
            PlayerCarInPitStall: inStall,
            IsInGarage: isInGarage,
            FuelLiters: fuel,
            LapDistPct: 0.8d,
            LocalIdentityProvenance: "strict-local",
            Assignment: new PitServiceRouteAssignment(0.068197d, 80d, 39, "default"));
    }
}
