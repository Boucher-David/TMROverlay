using TmrOverlay.Core.PitService;
using Xunit;

namespace TmrOverlay.App.Tests.PitService;

public sealed class PitServiceStationaryServiceTrackerTests
{
    [Theory]
    [InlineData(PitServiceRequestChangeClassifications.None, true)]
    [InlineData(PitServiceRequestChangeClassifications.CompletionClear, true)]
    [InlineData(PitServiceRequestChangeClassifications.MaterialMutation, true)]
    [InlineData(PitServiceRequestChangeClassifications.LegacyUnspecified, false)]
    [InlineData("unexpected", false)]
    public void RequestTransitionClassification_RequiresAnExplicitFormatSevenValue(
        string classification,
        bool expected)
    {
        Assert.Equal(
            expected,
            PitServiceRequestChangeClassifications.IsExplicitRequestTransitionClassification(classification));
    }

    [Fact]
    public void FuelIncreaseTracker_DetectsSmoothCumulativeRefuelWithDiagnosticsEventSemantics()
    {
        var tracker = new PitServiceFuelIncreaseTracker(5d);

        Assert.False(tracker.Track(5d));
        Assert.False(tracker.Track(5.2d));
        Assert.True(tracker.Track(5.4d));
        Assert.False(tracker.Track(5.6d));

        Assert.True(tracker.SawFuelIncrease);
        Assert.Equal(0.6d, tracker.MaxFuelIncreaseLiters);
        Assert.Equal(0.6d, tracker.LastFuelIncreaseLiters);
    }

    [Fact]
    public void FuelIncreaseTracker_UsesPitWindowLowWaterMarkForGradualRefuelAfterSmallBurn()
    {
        var tracker = new PitServiceFuelIncreaseTracker(5d);

        Assert.False(tracker.Track(5d));
        Assert.False(tracker.Track(4.8d));
        Assert.False(tracker.Track(5d));
        Assert.True(tracker.Track(5.2d));

        Assert.True(tracker.SawFuelIncrease);
        Assert.Equal(0.4d, tracker.MaxFuelIncreaseLiters);
        Assert.Equal(0.4d, tracker.LastFuelIncreaseLiters);
    }

    [Fact]
    public void Track_ExcludesPitLaneTravelAndPreservesStationaryFuelFlowEvidence()
    {
        var tracker = new PitServiceStationaryServiceTracker();
        var start = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

        Assert.Null(tracker.Track(Frame(start, inStall: false, serviceActive: false, fuelLiters: 20d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(10), inStall: true, serviceActive: true, fuelLiters: 20d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(12), inStall: true, serviceActive: true, fuelLiters: 22d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(15), inStall: true, serviceActive: true, fuelLiters: 25d)));

        var observation = tracker.Track(Frame(start.AddSeconds(20), inStall: false, serviceActive: false, fuelLiters: 25d));

        Assert.NotNull(observation);
        Assert.Equal(5d, observation.DurationSeconds);
        Assert.Equal(5d, observation.NetFuelDeltaLiters);
        Assert.Equal(5d, observation.PositiveFuelAddedLiters);
        Assert.Equal(3d, observation.FuelFlowDurationSeconds);
        Assert.True(observation.SawPitStall);
        Assert.True(observation.SawServiceActive);
        Assert.DoesNotContain("no-observed-fuel-flow", observation.QualificationFlags);
    }

    [Fact]
    public void Track_RecordsRequestChangesAndDoesNotInventAFlowRateFromOneSample()
    {
        var tracker = new PitServiceStationaryServiceTracker();
        var start = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

        Assert.Null(tracker.Track(Frame(start, inStall: true, serviceActive: true, fuelLiters: 20d, tires: 0)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(2), inStall: true, serviceActive: true, fuelLiters: 24d, tires: 4)));

        var observation = tracker.Finish();

        Assert.NotNull(observation);
        Assert.Equal(2d, observation.DurationSeconds);
        Assert.True(observation.RequestChangedDuringService);
        Assert.Equal(PitServiceRequestChangeClassifications.MaterialMutation, observation.RequestChangeClassification);
        Assert.Equal(4, observation.LastRequest.RequestedTireCount);
        Assert.Equal(4d, observation.PositiveFuelAddedLiters);
        Assert.Null(observation.FuelFlowDurationSeconds);
        Assert.Contains("fuel-flow-duration-unresolved", observation.QualificationFlags);
    }

    [Fact]
    public void Track_QualifiesFuelRegressionAndTransientRequestChanges()
    {
        var tracker = new PitServiceStationaryServiceTracker();
        var start = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

        Assert.Null(tracker.Track(Frame(start, true, true, 20d, tires: 0)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(1), true, true, 22d, tires: 4)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(2), true, true, 21d, tires: 0)));
        var observation = tracker.Finish();

        Assert.NotNull(observation);
        Assert.True(observation.RequestChangedDuringService);
        Assert.Equal(PitServiceRequestChangeClassifications.MaterialMutation, observation.RequestChangeClassification);
        Assert.Null(observation.PositiveFuelAddedLiters);
        Assert.Contains("fuel-nonmonotonic", observation.QualificationFlags);
    }

    [Fact]
    public void Track_ClassifiesPostRefuelSelectionClearAsCompletionRatherThanMutation()
    {
        var tracker = new PitServiceStationaryServiceTracker();
        var start = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

        Assert.Null(tracker.Track(Frame(start, true, true, 5d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(1), true, true, 10d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(2), true, true, 15d)));
        Assert.Null(tracker.Track(Frame(
            start.AddSeconds(3),
            inStall: true,
            serviceActive: false,
            fuelLiters: 15d,
            request: ClearedRequest())));

        var observation = tracker.Finish();

        Assert.NotNull(observation);
        Assert.Equal(10d, observation.PositiveFuelAddedLiters);
        Assert.Equal(PitServiceRequestChangeClassifications.CompletionClear, observation.RequestChangeClassification);
        Assert.False(observation.RequestChangedDuringService);
        Assert.Contains("request-cleared-at-completion", observation.QualificationFlags);
        Assert.DoesNotContain("request-changed-during-service", observation.QualificationFlags);
    }

    [Fact]
    public void Track_DoesNotCloseOnAnUnavailableTelemetryFrame()
    {
        var tracker = new PitServiceStationaryServiceTracker();
        var start = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

        Assert.Null(tracker.Track(Frame(start, true, true, 20d)));
        Assert.Null(tracker.Track(Frame(start.AddSeconds(1), false, false, null, reliable: false)));

        var observation = tracker.Track(Frame(start.AddSeconds(2), false, false, 20d));

        Assert.NotNull(observation);
        Assert.Equal(0d, observation.DurationSeconds);
        Assert.Contains("telemetry-interrupted", observation.QualificationFlags);
    }

    [Theory]
    [MemberData(nameof(ConfirmedTireShapes))]
    public void Classify_ConfirmsExactSingleAxleSideAndFourTireOutcomes(
        bool leftFront,
        bool rightFront,
        bool leftRear,
        bool rightRear,
        string expectedLabel)
    {
        var tracker = new PitServiceStationaryServiceTracker();
        var start = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        var request = Request(leftFront, rightFront, leftRear, rightRear);

        Assert.Null(tracker.Track(Frame(
            start,
            inStall: true,
            serviceActive: true,
            fuelLiters: 20d,
            request: request,
            tireCounters: Counters(10, 20, 30, 40))));
        Assert.Null(tracker.Track(Frame(
            start.AddSeconds(3),
            inStall: true,
            serviceActive: true,
            fuelLiters: 20d,
            request: request,
            tireCounters: Counters(
                leftFront ? 11 : 10,
                rightFront ? 21 : 20,
                leftRear ? 31 : 30,
                rightRear ? 41 : 40))));

        var observation = tracker.Finish();

        Assert.NotNull(observation);
        var assessment = PitServiceTireChangeClassifier.Classify(observation);
        Assert.Equal(expectedLabel, assessment.RequestedShape.DisplayLabel);
        Assert.Equal(PitServiceTireExecutionState.Confirmed, assessment.ExecutionState);
        Assert.True(assessment.IsCleanForTireTimingLearning);
        Assert.NotNull(observation.EntryTireCounters);
        Assert.NotNull(observation.ExitTireCounters);
        Assert.NotNull(observation.TireCounterDelta);
    }

    [Fact]
    public void Classify_LeavesIncompleteOrContaminatedCounterEvidenceOutOfTimingLearning()
    {
        var start = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        var request = Request(leftFront: true, rightFront: false, leftRear: false, rightRear: false);

        var incompleteTracker = new PitServiceStationaryServiceTracker();
        Assert.Null(incompleteTracker.Track(Frame(start, true, true, 20d, request: request)));
        Assert.Null(incompleteTracker.Track(Frame(start.AddSeconds(2), true, true, 20d, request: request)));
        var incomplete = PitServiceTireChangeClassifier.Classify(Assert.IsType<PitServiceStationaryServiceObservation>(incompleteTracker.Finish()));

        Assert.Equal(PitServiceTireExecutionState.RequestedOnly, incomplete.ExecutionState);
        Assert.False(incomplete.IsCleanForTireTimingLearning);
        Assert.Contains("exact-corner-counters-unavailable", incomplete.QualificationFlags);

        var repairTracker = new PitServiceStationaryServiceTracker();
        Assert.Null(repairTracker.Track(Frame(
            start,
            true,
            true,
            20d,
            request: request,
            tireCounters: Counters(10, 20, 30, 40),
            hasRepair: true)));
        Assert.Null(repairTracker.Track(Frame(
            start.AddSeconds(2),
            true,
            true,
            20d,
            request: request,
            tireCounters: Counters(11, 20, 30, 40),
            hasRepair: true)));
        var repair = PitServiceTireChangeClassifier.Classify(Assert.IsType<PitServiceStationaryServiceObservation>(repairTracker.Finish()));

        Assert.Equal(PitServiceTireExecutionState.Confirmed, repair.ExecutionState);
        Assert.False(repair.IsCleanForTireTimingLearning);
        Assert.Contains("repair-active", repair.QualificationFlags);

        var interruptedTracker = new PitServiceStationaryServiceTracker();
        Assert.Null(interruptedTracker.Track(Frame(
            start,
            true,
            true,
            20d,
            request: request,
            tireCounters: Counters(10, 20, 30, 40))));
        Assert.Null(interruptedTracker.Track(Frame(
            start.AddSeconds(1),
            false,
            false,
            null,
            reliable: false)));
        Assert.Null(interruptedTracker.Track(Frame(
            start.AddSeconds(2),
            true,
            true,
            20d,
            request: request,
            tireCounters: Counters(11, 20, 30, 40))));
        var interrupted = PitServiceTireChangeClassifier.Classify(
            Assert.IsType<PitServiceStationaryServiceObservation>(interruptedTracker.Finish()));

        Assert.Equal(PitServiceTireExecutionState.Ambiguous, interrupted.ExecutionState);
        Assert.False(interrupted.IsCleanForTireTimingLearning);
        Assert.Contains("telemetry-interrupted", interrupted.QualificationFlags);
    }

    public static IEnumerable<object[]> ConfirmedTireShapes()
    {
        yield return [true, false, false, false, "LF"];
        yield return [false, true, false, false, "RF"];
        yield return [false, false, true, false, "LR"];
        yield return [false, false, false, true, "RR"];
        yield return [true, true, false, false, "Front tires"];
        yield return [false, false, true, true, "Rear tires"];
        yield return [true, false, true, false, "Left tires"];
        yield return [false, true, false, true, "Right tires"];
        yield return [true, true, true, true, "4 tires"];
    }

    private static PitServiceObservationFrame Frame(
        DateTimeOffset capturedAtUtc,
        bool inStall,
        bool serviceActive,
        double? fuelLiters,
        int tires = 0,
        bool reliable = true,
        PitServiceRequestShape? request = null,
        PitServiceTireCounterSnapshot? tireCounters = null,
        bool hasRepair = false)
    {
        return new PitServiceObservationFrame(
            CapturedAtUtc: capturedAtUtc,
            SessionTimeSeconds: null,
            IsReliable: reliable,
            PlayerCarInPitStall: inStall,
            PitstopActive: serviceActive,
            FuelLiters: fuelLiters,
            Request: request ?? Request(
                leftFront: tires > 0,
                rightFront: tires > 1,
                leftRear: tires > 2,
                rightRear: tires > 3),
            TireSetsUsed: 1,
            TeamOrLocalFastRepairsUsed: 0,
            RawStatus: null,
            RawFlags: null,
            HasRepair: hasRepair,
            TireCounters: tireCounters);
    }

    private static PitServiceRequestShape Request(
        bool leftFront,
        bool rightFront,
        bool leftRear,
        bool rightRear)
    {
        return new PitServiceRequestShape(
            LeftFrontTire: leftFront,
            RightFrontTire: rightFront,
            LeftRearTire: leftRear,
            RightRearTire: rightRear,
            Fuel: true,
            Tearoff: false,
            FastRepair: false,
            FuelLiters: 10d,
            RequestedTireCompoundIndex: 1);
    }

    private static PitServiceRequestShape ClearedRequest()
    {
        return new PitServiceRequestShape(
            LeftFrontTire: false,
            RightFrontTire: false,
            LeftRearTire: false,
            RightRearTire: false,
            Fuel: false,
            Tearoff: false,
            FastRepair: false,
            FuelLiters: 15d,
            RequestedTireCompoundIndex: 1);
    }

    private static PitServiceTireCounterSnapshot Counters(int leftFront, int rightFront, int leftRear, int rightRear)
    {
        return new PitServiceTireCounterSnapshot(
            TireSetsUsed: null,
            TireSetsAvailable: null,
            LeftTireSetsUsed: null,
            RightTireSetsUsed: null,
            FrontTireSetsUsed: null,
            RearTireSetsUsed: null,
            LeftTireSetsAvailable: null,
            RightTireSetsAvailable: null,
            FrontTireSetsAvailable: null,
            RearTireSetsAvailable: null,
            LeftFrontTiresUsed: leftFront,
            RightFrontTiresUsed: rightFront,
            LeftRearTiresUsed: leftRear,
            RightRearTiresUsed: rightRear,
            LeftFrontTiresAvailable: null,
            RightFrontTiresAvailable: null,
            LeftRearTiresAvailable: null,
            RightRearTiresAvailable: null);
    }
}
