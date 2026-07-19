using TmrOverlay.Core.OverlayBridge;
using Xunit;

namespace TmrOverlay.App.Tests.OverlayBridge.Fixtures;

public sealed class OverlayBridgePairedStreamFixturesTests
{
    [Fact]
    public void CreateAll_ReturnsDeterministicPriorityMatrixWithOffCarRaceReceivers()
    {
        var first = OverlayBridgePairedStreamFixtures.CreateAll();
        var second = OverlayBridgePairedStreamFixtures.CreateAll();

        Assert.Equal(
        [
            "remote-current-off-car-sector-sequence",
            "confirmed-local-direct-precedence",
            "remote-held-and-expired-by-receiver-receipt",
            "session-lease-and-replay-rejections",
            "tombstone-and-new-lease-handoff"
        ],
        first.Select(fixture => fixture.Name));

        Assert.Equal(
            first.Select(FixtureSignature),
            second.Select(FixtureSignature));
        Assert.All(first, fixture =>
        {
            Assert.False(fixture.ReceiverLocalContext.IsConfirmedInCar);
            Assert.Equal(OverlayBridgeSessionKind.Race, fixture.ReceiverLocalContext.SessionKind);
            Assert.Equal(OverlayBridgeRacePhase.Green, fixture.ReceiverLocalContext.RacePhase);
            Assert.False(fixture.ReceiverAdmissionContext.HasFreshDirectInCarTelemetry);
            Assert.False(fixture.ReceiverAdmissionContext.AllowsRawCaptureReplay);
            Assert.Equal(
                fixture.ReceiverLocalContext.Session,
                fixture.ReceiverAdmissionContext.ExpectedSession);
            Assert.NotEmpty(fixture.Steps);
            Assert.Equal(
                fixture.Steps.Select(step => step.Name).Distinct(StringComparer.Ordinal).Count(),
                fixture.Steps.Count);
        });
    }

    [Fact]
    public void PublishedSteps_AreLiveValidatedConfirmedInCarFactsWithoutRawTelemetryFixtures()
    {
        var publications = OverlayBridgePairedStreamFixtures.CreateAll()
            .SelectMany(fixture => fixture.Steps)
            .Where(step => step.Publication is not null)
            .Select(step => (Step: step, Publication: step.Publication!))
            .ToArray();

        Assert.NotEmpty(publications);
        Assert.All(publications, item =>
        {
            Assert.True(
                item.Publication.TryValidate(out var error),
                $"{item.Step.Name} must remain a valid synthetic Bridge publication: {error}.");
            Assert.Equal(OverlayBridgeSourceMode.Live, item.Publication.Header.SourceMode);
            Assert.DoesNotContain("capture", item.Publication.Header.RoomId, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("capture", item.Publication.Header.StreamId, StringComparison.OrdinalIgnoreCase);

            var active = item.Publication.ActiveTeamCar;
            if (active.Availability == OverlayBridgeFactGroupAvailability.Available)
            {
                var facts = Assert.IsType<OverlayBridgeActiveTeamCarFacts>(active.Facts);
                Assert.Equal(OverlayBridgeTeamCarSourceState.ConfirmedInCar, facts.SourceState);
                Assert.Equal(item.Publication.Header.Session.TeamCarKey, facts.TeamCarId);
                Assert.False(facts.IsDriverChangeInProgress);
                Assert.NotNull(facts.CurrentFuelLiters);
                Assert.NotNull(facts.TeamCarProgressLaps);
            }
            else
            {
                Assert.Equal(OverlayBridgeFactGroupUnavailableReason.Tombstoned, active.UnavailableReason);
                Assert.Null(active.Facts);
            }
        });
    }

    [Fact]
    public void Replay_ProvesEachFixtureExpectedAdmissionPriorityAndReceiverFreshnessState()
    {
        foreach (var fixture in OverlayBridgePairedStreamFixtures.CreateAll())
        {
            var replay = OverlayBridgePairedStreamFixtureRunner.Replay(fixture);

            Assert.Equal(fixture.Steps.Count, replay.Count);
            foreach (var observed in replay)
            {
                AssertExpectedState(observed);
            }
        }
    }

    [Fact]
    public void RejectedReplaySessionAndLeasePackets_KeepThePreviouslyAcceptedRemoteGroupCurrent()
    {
        var fixture = OverlayBridgePairedStreamFixtures.CreateSessionLeaseAndReplayRejections();
        var replay = OverlayBridgePairedStreamFixtureRunner.Replay(fixture);
        var rejected = replay.Where(item => item.Step.Expected.Decision is
            OverlayBridgePairedStreamDecision.RejectedSessionMismatch
            or OverlayBridgePairedStreamDecision.RejectedLeaseMismatch
            or OverlayBridgePairedStreamDecision.RejectedSameLeaseSequenceRegression
            or OverlayBridgePairedStreamDecision.RejectedReplay);

        Assert.NotEmpty(rejected);
        Assert.All(rejected, observed =>
        {
            Assert.NotEqual(OverlayBridgeReceiverAdmissionOutcome.Accepted, observed.Admission!.Outcome);
            Assert.Equal(100, observed.ReceiverState.LastObservedPublication!.Sequence);
            Assert.Equal(100, observed.ReceiverState.ActiveTeamCar.LastAcceptedPublication!.Sequence);
            Assert.Equal(67.5d, observed.ReceiverState.ActiveTeamCar.RetainedFacts!.CurrentFuelLiters);
            Assert.Equal(OverlayBridgeReceiverTerminalReason.None, observed.ReceiverState.ActiveTeamCar.TerminalReason);
            Assert.True(observed.ActiveTeamCar.IsUsableForCalculation);
        });
    }

    [Fact]
    public void Fanout_UsesIndependentReceiverStateForCurrentDirectAndHeldPriorityOutcomes()
    {
        var sectorFixture = OverlayBridgePairedStreamFixtures.CreateRemoteCurrentWithOffCarReceiver();
        var join = Assert.IsType<OverlayBridgeSectorPublication>(sectorFixture.Steps[0].Publication);
        var nextSector = Assert.IsType<OverlayBridgeSectorPublication>(sectorFixture.Steps[1].Publication);
        var rejectionFixture = OverlayBridgePairedStreamFixtures.CreateSessionLeaseAndReplayRejections();
        var wrongSession = Assert.IsType<OverlayBridgeSectorPublication>(
            Assert.Single(rejectionFixture.Steps, step => step.Name == "session-mismatch").Publication);
        var receivedAtUtc = DateTimeOffset.Parse("2026-07-19T13:00:00Z");
        var currentReceiver = new OverlayBridgeReceiverAdmissionStore();
        var directReceiver = new OverlayBridgeReceiverAdmissionStore();
        var delayedReceiver = new OverlayBridgeReceiverAdmissionStore();

        Assert.Equal(
            OverlayBridgeReceiverAdmissionOutcome.Accepted,
            currentReceiver.Admit(join, sectorFixture.ReceiverAdmissionContext, receivedAtUtc).Outcome);
        Assert.Equal(
            OverlayBridgeReceiverAdmissionOutcome.Accepted,
            directReceiver.Admit(join, sectorFixture.ReceiverAdmissionContext, receivedAtUtc).Outcome);
        Assert.Equal(
            OverlayBridgeReceiverAdmissionOutcome.Accepted,
            delayedReceiver.Admit(join, sectorFixture.ReceiverAdmissionContext, receivedAtUtc).Outcome);

        Assert.Equal(
            OverlayBridgeReceiverAdmissionOutcome.Accepted,
            currentReceiver.Admit(nextSector, sectorFixture.ReceiverAdmissionContext, receivedAtUtc.AddSeconds(4)).Outcome);
        Assert.Equal(
            OverlayBridgeReceiverAdmissionOutcome.SuppressedByDirectLocalPrecedence,
            directReceiver.ApplyLocalDirectPrecedence(true, receivedAtUtc.AddSeconds(1)).Outcome);
        Assert.Equal(
            OverlayBridgeReceiverAdmissionOutcome.RejectedSessionMismatch,
            delayedReceiver.Admit(wrongSession, sectorFixture.ReceiverAdmissionContext, receivedAtUtc.AddSeconds(2)).Outcome);

        var current = OverlayBridgeActiveTeamCarComposition.Resolve(
            currentReceiver.Snapshot(), receivedAtUtc.AddSeconds(5), sectorFixture.FreshnessPolicy);
        var direct = OverlayBridgeActiveTeamCarComposition.Resolve(
            directReceiver.Snapshot(), receivedAtUtc.AddSeconds(5), sectorFixture.FreshnessPolicy);
        var held = OverlayBridgeActiveTeamCarComposition.Resolve(
            delayedReceiver.Snapshot(), receivedAtUtc.AddSeconds(12), sectorFixture.FreshnessPolicy);

        Assert.Equal(OverlayBridgeActiveTeamCarGroupAvailability.Current, current.Availability);
        Assert.Equal(OverlayBridgeActiveTeamCarGroupSource.RemoteBridge, current.Source);
        Assert.Equal(71.6d, current.Facts!.CurrentFuelLiters);
        Assert.Equal(101, current.RemoteProvenance!.Publication.Sequence);

        Assert.Equal(OverlayBridgeActiveTeamCarGroupAvailability.Unavailable, direct.Availability);
        Assert.Equal(OverlayBridgeActiveTeamCarGroupSource.DirectLocalTelemetry, direct.Source);
        Assert.Null(direct.Facts);

        Assert.Equal(OverlayBridgeActiveTeamCarGroupAvailability.Held, held.Availability);
        Assert.Equal(OverlayBridgeActiveTeamCarGroupSource.RemoteBridge, held.Source);
        Assert.Null(held.Facts);
        Assert.Equal(100, held.RemoteProvenance!.Publication.Sequence);
        Assert.Equal(OverlayBridgeReceiverTerminalReason.None, held.ReceiverTerminalReason);
    }

    [Fact]
    public void SkewedPublisherClock_UsesExactReceiverReceiptFreshnessBoundaries()
    {
        var fixture = OverlayBridgePairedStreamFixtures.CreateHeldAndExpiredRemoteReceipt();
        var replay = OverlayBridgePairedStreamFixtureRunner.Replay(fixture);
        var join = replay[0];
        var currentBoundary = Assert.Single(replay, item => item.Step.Name == "receipt-aged-current-boundary");
        var currentPlusTick = Assert.Single(replay, item => item.Step.Name == "receipt-aged-current-boundary-plus-tick");
        var heldBoundary = Assert.Single(replay, item => item.Step.Name == "receipt-aged-held-boundary");
        var heldPlusTick = Assert.Single(replay, item => item.Step.Name == "receipt-aged-held-boundary-plus-tick");

        Assert.True(join.Step.Publication!.Header.PublishedAtUtc < join.Step.ReceiverObservedAtUtc.AddHours(-6));
        Assert.Equal(TimeSpan.FromSeconds(5), currentBoundary.ActiveTeamCar.Freshness.ReceiverObservedAge);
        Assert.Equal(OverlayBridgeActiveTeamCarGroupAvailability.Current, currentBoundary.ActiveTeamCar.Availability);
        Assert.Equal(TimeSpan.FromSeconds(5).Add(TimeSpan.FromTicks(1)), currentPlusTick.ActiveTeamCar.Freshness.ReceiverObservedAge);
        Assert.Equal(OverlayBridgeActiveTeamCarGroupAvailability.Held, currentPlusTick.ActiveTeamCar.Availability);
        Assert.Equal(TimeSpan.FromSeconds(15), heldBoundary.ActiveTeamCar.Freshness.ReceiverObservedAge);
        Assert.Equal(OverlayBridgeActiveTeamCarGroupAvailability.Held, heldBoundary.ActiveTeamCar.Availability);
        Assert.Equal(TimeSpan.FromSeconds(15).Add(TimeSpan.FromTicks(1)), heldPlusTick.ActiveTeamCar.Freshness.ReceiverObservedAge);
        Assert.Equal(OverlayBridgeActiveTeamCarGroupAvailability.Unavailable, heldPlusTick.ActiveTeamCar.Availability);
        Assert.Equal(
            OverlayBridgeActiveTeamCarGroupUnavailableReason.ReceiverFreshnessExpired,
            heldPlusTick.ActiveTeamCar.UnavailableReason);
    }

    [Fact]
    public void SectorAndHandoffVariants_CarryCompleteConflictingFactGroups()
    {
        var sectorFixture = OverlayBridgePairedStreamFixtures.CreateRemoteCurrentWithOffCarReceiver();
        var sectorOne = Publication(sectorFixture, "join-snapshot");
        var sectorTwo = Publication(sectorFixture, "lap-41-sector-2");
        var sectorThree = Publication(sectorFixture, "lap-41-sector-3");
        var handoffFixture = OverlayBridgePairedStreamFixtures.CreateTombstoneAndNewLeaseHandoff();
        var handoff = Publication(handoffFixture, "new-lease-handoff");

        AssertTeamCarFacts(
            sectorOne,
            fuelLiters: 72.4d,
            progressLaps: 41.12d,
            physicalTankCapacityLiters: 110d,
            effectiveSessionCapacityLiters: 110d,
            cleanBurnConfidence: OverlayBridgeEvidenceConfidence.Measured,
            cleanBurnSampleCount: 2,
            firstCleanBurnFuelLiters: 2.31d,
            isDriverChangeInProgress: false,
            isOnPitRoad: false,
            isInPitStall: false,
            isPitstopActive: false,
            repairServiceState: OverlayBridgePitServiceState.None,
            fastRepairUsed: false);
        AssertTeamCarFacts(
            sectorTwo,
            fuelLiters: 71.6d,
            progressLaps: 41.42d,
            physicalTankCapacityLiters: 110d,
            effectiveSessionCapacityLiters: 105d,
            cleanBurnConfidence: OverlayBridgeEvidenceConfidence.High,
            cleanBurnSampleCount: 2,
            firstCleanBurnFuelLiters: 2.42d,
            isDriverChangeInProgress: false,
            isOnPitRoad: true,
            isInPitStall: true,
            isPitstopActive: true,
            repairServiceState: OverlayBridgePitServiceState.Servicing,
            fastRepairUsed: false);
        AssertTeamCarFacts(
            sectorThree,
            fuelLiters: 70.8d,
            progressLaps: 41.74d,
            physicalTankCapacityLiters: 110d,
            effectiveSessionCapacityLiters: 108d,
            cleanBurnConfidence: OverlayBridgeEvidenceConfidence.Measured,
            cleanBurnSampleCount: 3,
            firstCleanBurnFuelLiters: 2.42d,
            isDriverChangeInProgress: false,
            isOnPitRoad: false,
            isInPitStall: false,
            isPitstopActive: false,
            repairServiceState: OverlayBridgePitServiceState.Complete,
            fastRepairUsed: false);
        AssertTeamCarFacts(
            handoff,
            fuelLiters: 55.2d,
            progressLaps: 44.76d,
            physicalTankCapacityLiters: 100d,
            effectiveSessionCapacityLiters: 100d,
            cleanBurnConfidence: OverlayBridgeEvidenceConfidence.High,
            cleanBurnSampleCount: 2,
            firstCleanBurnFuelLiters: 2.08d,
            isDriverChangeInProgress: false,
            isOnPitRoad: false,
            isInPitStall: false,
            isPitstopActive: false,
            repairServiceState: OverlayBridgePitServiceState.Complete,
            fastRepairUsed: true);

        Assert.Equal(100d, sectorOne.FuelCapacity.MaximumFuelPercent);
        Assert.Equal(0.75d, sectorOne.FuelCapacity.FuelKgPerLiter);
        Assert.Equal(95.45d, sectorTwo.FuelCapacity.MaximumFuelPercent);
        Assert.Equal(15d, sectorTwo.RepairService.RequestedFuelLiters);
        Assert.True(sectorTwo.RepairService.FastRepairAvailable);
        Assert.Equal(98.18d, sectorThree.FuelCapacity.MaximumFuelPercent);
        Assert.Equal(0.76d, handoff.FuelCapacity.FuelKgPerLiter);
        Assert.True(handoff.RepairService.FastRepairAvailable);
    }

    [Fact]
    public void ScalarMergeCounterexamples_RequireOneAtomicSourceAndNeverExposeMixedFacts()
    {
        foreach (var fixture in OverlayBridgePairedStreamFixtures.CreateAll())
        {
            var counterexample = fixture.ScalarMergeCounterexample;
            if (counterexample is null)
            {
                continue;
            }

            Assert.NotEqual(counterexample.LocalCandidate.FuelLiters, counterexample.RemoteCandidate.FuelLiters);
            Assert.NotEqual(counterexample.LocalCandidate.ProgressLaps, counterexample.RemoteCandidate.ProgressLaps);
            Assert.NotEqual(
                counterexample.LocalCandidate.EffectiveSessionCapacityLiters,
                counterexample.RemoteCandidate.EffectiveSessionCapacityLiters);
            Assert.NotEqual(
                counterexample.LocalCandidate.CleanBurnSampleCount,
                counterexample.RemoteCandidate.CleanBurnSampleCount);
            Assert.NotEqual(
                counterexample.LocalCandidate.IsDriverChangeInProgress,
                counterexample.RemoteCandidate.IsDriverChangeInProgress);
            Assert.NotEqual(
                counterexample.LocalCandidate.RepairServiceState,
                counterexample.RemoteCandidate.RepairServiceState);

            var observed = Assert.Single(
                OverlayBridgePairedStreamFixtureRunner.Replay(fixture),
                item => string.Equals(item.Step.Name, counterexample.StepName, StringComparison.Ordinal));

            Assert.Equal(counterexample.ExpectedAuthoritativeSource, observed.ActiveTeamCar.Source);
            if (counterexample.ExpectedAuthoritativeSource == OverlayBridgeActiveTeamCarGroupSource.RemoteBridge)
            {
                var facts = Assert.IsType<OverlayBridgeActiveTeamCarFacts>(observed.ActiveTeamCar.Facts);
                AssertTeamCarSentinel(facts, counterexample.RemoteCandidate);
                Assert.NotEqual(counterexample.LocalCandidate.FuelLiters, facts.CurrentFuelLiters);
                Assert.NotEqual(counterexample.LocalCandidate.ProgressLaps, facts.TeamCarProgressLaps);
                Assert.NotEqual(
                    counterexample.LocalCandidate.EffectiveSessionCapacityLiters,
                    facts.FuelCapacity.EffectiveSessionCapacityLiters);
                Assert.NotEqual(
                    counterexample.LocalCandidate.RepairServiceState,
                    facts.RepairService.ServiceState);
            }
            else
            {
                Assert.Equal(OverlayBridgeActiveTeamCarGroupSource.DirectLocalTelemetry, observed.ActiveTeamCar.Source);
                Assert.False(observed.ActiveTeamCar.IsUsableForCalculation);
                Assert.Null(observed.ActiveTeamCar.Facts);
                AssertTeamCarSentinel(
                    observed.ReceiverState.ActiveTeamCar.RetainedFacts!,
                    counterexample.RemoteCandidate);
                Assert.NotEqual(
                    counterexample.LocalCandidate.FuelLiters,
                    observed.ReceiverState.ActiveTeamCar.RetainedFacts!.CurrentFuelLiters);
                Assert.NotEqual(
                    counterexample.LocalCandidate.ProgressLaps,
                    observed.ReceiverState.ActiveTeamCar.RetainedFacts.TeamCarProgressLaps);
                Assert.NotEqual(
                    counterexample.LocalCandidate.CleanBurnSampleCount,
                    observed.ReceiverState.ActiveTeamCar.RetainedFacts.CleanBurnEvidence.Samples.Count);
            }
        }
    }

    private static OverlayBridgeActiveTeamCarFacts Publication(
        OverlayBridgePairedStreamFixture fixture,
        string stepName)
    {
        var step = Assert.Single(fixture.Steps, candidate => candidate.Name == stepName);
        return Assert.IsType<OverlayBridgeActiveTeamCarFacts>(step.Publication!.ActiveTeamCar.Facts);
    }

    private static void AssertTeamCarFacts(
        OverlayBridgeActiveTeamCarFacts facts,
        double fuelLiters,
        double progressLaps,
        double physicalTankCapacityLiters,
        double effectiveSessionCapacityLiters,
        OverlayBridgeEvidenceConfidence cleanBurnConfidence,
        int cleanBurnSampleCount,
        double firstCleanBurnFuelLiters,
        bool isDriverChangeInProgress,
        bool isOnPitRoad,
        bool isInPitStall,
        bool isPitstopActive,
        OverlayBridgePitServiceState repairServiceState,
        bool fastRepairUsed)
    {
        Assert.Equal(OverlayBridgeTeamCarSourceState.ConfirmedInCar, facts.SourceState);
        Assert.Equal(fuelLiters, facts.CurrentFuelLiters);
        Assert.Equal(progressLaps, facts.TeamCarProgressLaps);
        Assert.Equal(physicalTankCapacityLiters, facts.FuelCapacity.PhysicalTankCapacityLiters);
        Assert.Equal(effectiveSessionCapacityLiters, facts.FuelCapacity.EffectiveSessionCapacityLiters);
        Assert.Equal(cleanBurnConfidence, facts.CleanBurnEvidence.Confidence);
        Assert.Equal(cleanBurnSampleCount, facts.CleanBurnEvidence.Samples.Count);
        Assert.Equal(firstCleanBurnFuelLiters, facts.CleanBurnEvidence.Samples[0].FuelUsedLiters);
        Assert.Equal(isDriverChangeInProgress, facts.IsDriverChangeInProgress);
        Assert.Equal(isOnPitRoad, facts.IsOnPitRoad);
        Assert.Equal(isInPitStall, facts.IsInPitStall);
        Assert.Equal(isPitstopActive, facts.IsPitstopActive);
        Assert.Equal(repairServiceState, facts.RepairService.ServiceState);
        Assert.Equal(fastRepairUsed, facts.RepairService.FastRepairUsed);
    }

    private static void AssertTeamCarSentinel(
        OverlayBridgeActiveTeamCarFacts facts,
        OverlayBridgePairedStreamFactSentinel expected)
    {
        Assert.Equal(expected.FuelLiters, facts.CurrentFuelLiters);
        Assert.Equal(expected.ProgressLaps, facts.TeamCarProgressLaps);
        Assert.Equal(expected.PhysicalTankCapacityLiters, facts.FuelCapacity.PhysicalTankCapacityLiters);
        Assert.Equal(expected.EffectiveSessionCapacityLiters, facts.FuelCapacity.EffectiveSessionCapacityLiters);
        Assert.Equal(expected.MaximumFuelPercent, facts.FuelCapacity.MaximumFuelPercent);
        Assert.Equal(expected.FuelKgPerLiter, facts.FuelCapacity.FuelKgPerLiter);
        Assert.Equal(expected.CleanBurnConfidence, facts.CleanBurnEvidence.Confidence);
        Assert.Equal(expected.CleanBurnSampleCount, facts.CleanBurnEvidence.Samples.Count);
        Assert.Equal(expected.FirstCleanBurnFuelLiters, facts.CleanBurnEvidence.Samples[0].FuelUsedLiters);
        Assert.Equal(expected.IsDriverChangeInProgress, facts.IsDriverChangeInProgress);
        Assert.Equal(expected.IsOnPitRoad, facts.IsOnPitRoad);
        Assert.Equal(expected.IsInPitStall, facts.IsInPitStall);
        Assert.Equal(expected.IsPitstopActive, facts.IsPitstopActive);
        Assert.Equal(expected.RepairServiceState, facts.RepairService.ServiceState);
        Assert.Equal(expected.RequestedFuelLiters, facts.RepairService.RequestedFuelLiters);
        Assert.Equal(expected.FastRepairAvailable, facts.RepairService.FastRepairAvailable);
        Assert.Equal(expected.FastRepairUsed, facts.RepairService.FastRepairUsed);
    }

    private static void AssertExpectedState(OverlayBridgePairedStreamObservedStep observed)
    {
        var expected = observed.Step.Expected;
        if (expected.AdmissionOutcome is { } expectedOutcome)
        {
            Assert.Equal(expectedOutcome, observed.Admission!.Outcome);
        }
        else
        {
            Assert.Null(observed.Admission);
        }

        var receiverActive = observed.ReceiverState.ActiveTeamCar;
        Assert.Equal(expected.ActiveTeamCarAcceptedSequence, receiverActive.LastAcceptedPublication!.Sequence);
        Assert.Equal(expected.LastObservedSequence, observed.ReceiverState.LastObservedPublication!.Sequence);
        Assert.Equal(expected.RetainedRemoteFuelLiters, receiverActive.RetainedFacts!.CurrentFuelLiters);
        Assert.Equal(expected.ReceiverTerminalReason, receiverActive.TerminalReason);

        var active = observed.ActiveTeamCar;
        Assert.Equal(expected.ActiveTeamCarAvailability, active.Availability);
        Assert.Equal(expected.ActiveTeamCarSource, active.Source);
        Assert.Equal(expected.UnavailableReason, active.UnavailableReason);
        Assert.Equal(expected.ReceiverTerminalReason, active.ReceiverTerminalReason);
        Assert.Equal(expected.IsUsableForCalculation, active.IsUsableForCalculation);
        Assert.Equal(expected.ActiveTeamCarFactsVisible, active.Facts is not null);

        if (expected.ActiveTeamCarFactsVisible)
        {
            Assert.Equal(expected.VisibleRemoteFuelLiters, active.Facts!.CurrentFuelLiters);
        }
        else
        {
            Assert.Null(expected.VisibleRemoteFuelLiters);
            Assert.Null(active.Facts);
        }
    }

    private static string FixtureSignature(OverlayBridgePairedStreamFixture fixture) => string.Join(
        '|',
        fixture.Name,
        fixture.ReceiverLocalContext.Session.SessionId,
        fixture.ReceiverLocalContext.Session.SessionEpoch,
        fixture.FreshnessPolicy.CurrentMaximumAge.Ticks,
        fixture.FreshnessPolicy.HeldMaximumAge.Ticks,
        string.Join(
            ';',
            fixture.Steps.Select(step => string.Join(
                ',',
                step.Name,
                (int)step.Kind,
                step.ReceiverObservedAtUtc.UtcTicks,
                step.Publication?.Header.SnapshotId,
                step.Publication?.Header.Sequence,
                (int)step.Expected.Decision,
                (int?)step.Expected.AdmissionOutcome,
                step.Expected.ActiveTeamCarAcceptedSequence,
                step.Expected.LastObservedSequence,
                step.Expected.RetainedRemoteFuelLiters,
                step.Expected.VisibleRemoteFuelLiters))));
}
