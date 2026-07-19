using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TmrOverlay.App.Events;
using TmrOverlay.App.History;
using TmrOverlay.App.Storage;
using TmrOverlay.App.Telemetry;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Telemetry;

public sealed class FuelV2CaptureRecorderTests
{
    [Fact]
    public void CompleteCollection_WritesCaptureSidecarUnderConfiguredDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-fuel-v2-capture-recorder-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var captureDirectory = Path.Combine(storage.CaptureRoot, "capture-fuel-v2");
            Directory.CreateDirectory(captureDirectory);
            var recorder = new FuelV2CaptureRecorder(
                new FuelV2CaptureOptions
                {
                    Enabled = true,
                    OutputFileName = "custom-fuel-v2.json",
                    CaptureDirectoryName = "custom-fuel-v2-sidecar",
                    LogDirectoryName = "custom-fuel-v2-logs"
                },
                storage,
                new AppEventRecorder(storage),
                NullLogger<FuelV2CaptureRecorder>.Instance);
            var startedAtUtc = DateTimeOffset.Parse("2026-05-10T12:00:00Z");
            recorder.StartCollection("capture-fuel-v2", startedAtUtc);
            var context = CapacityContext(sessionNum: 0, sessionType: "Race", capPercent: 1d, dcRuleSet: "IMSA");
            recorder.RecordFrame(CapacitySnapshot(context, 70d, startedAtUtc.AddSeconds(1), sequence: 1), captureDirectory: captureDirectory);

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(5), captureDirectory);

            var expectedPath = Path.Combine(
                captureDirectory,
                "custom-fuel-v2-sidecar",
                "capture-fuel-v2-fuel-v2-s001-race-custom-fuel-v2.json");
            Assert.Equal(expectedPath, path);
            Assert.Equal(expectedPath, recorder.LastArtifactPath);
            Assert.True(File.Exists(expectedPath));
            Assert.Empty(Directory.EnumerateFiles(
                Path.GetDirectoryName(expectedPath)!,
                "*.tmp",
                SearchOption.TopDirectoryOnly));
            Assert.Equal(Path.Combine(storage.LogsRoot, "custom-fuel-v2-logs"), recorder.DiagnosticsLogRoot);

            using var document = JsonDocument.Parse(File.ReadAllText(expectedPath));
            Assert.Equal(6, document.RootElement.GetProperty("formatVersion").GetInt32());
            Assert.Equal("capture-fuel-v2-fuel-v2-s001-race", document.RootElement.GetProperty("sourceId").GetString());
            Assert.Equal("raw-capture-sidecar", document.RootElement.GetProperty("output").GetProperty("mode").GetString());
            Assert.True(document.RootElement.GetProperty("output").GetProperty("rawTelemetryExcluded").GetBoolean());
            Assert.False(document.RootElement.GetProperty("output").GetProperty("durableHistoryMutated").GetBoolean());
            Assert.Equal(1, document.RootElement.GetProperty("totals").GetProperty("frameCount").GetInt32());
            Assert.Equal(0, document.RootElement.GetProperty("stationaryServiceObservations").GetArrayLength());
            Assert.Equal(0, document.RootElement.GetProperty("pitRouteObservations").GetArrayLength());
            var pitService = document.RootElement.GetProperty("pitService");
            Assert.Equal(0, pitService.GetProperty("stationaryServiceObservationCount").GetInt32());
            Assert.Equal(0, pitService.GetProperty("retainedStationaryServiceObservationCount").GetInt32());
            Assert.Equal(0, pitService.GetProperty("droppedStationaryServiceObservationCount").GetInt32());
            Assert.Equal(0, pitService.GetProperty("pitRouteObservationCount").GetInt32());
            Assert.Equal(0, pitService.GetProperty("retainedPitRouteObservationCount").GetInt32());
            Assert.Equal(0, pitService.GetProperty("droppedPitRouteObservationCount").GetInt32());
            Assert.Equal("race", document.RootElement.GetProperty("sessionLineage").GetProperty("sessionFamily").GetString());
            Assert.Equal(1, document.RootElement.GetProperty("sessionLineage").GetProperty("segmentOrdinal").GetInt32());
            Assert.Equal("IMSA", document.RootElement.GetProperty("sessionScope").GetProperty("session").GetProperty("dcRuleSet").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CompleteCollection_WithoutCaptureDirectoryWritesSanitizedDiagnosticsLogArtifact()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-fuel-v2-capture-recorder-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var recorder = new FuelV2CaptureRecorder(
                new FuelV2CaptureOptions
                {
                    Enabled = true,
                    OutputFileName = "diagnostics.json",
                    LogDirectoryName = "fuel-v2-log-root"
                },
                storage,
                new AppEventRecorder(storage),
                NullLogger<FuelV2CaptureRecorder>.Instance);
            var startedAtUtc = DateTimeOffset.Parse("2026-05-10T12:00:00Z");
            recorder.StartCollection("session/with/slashes", startedAtUtc);
            var context = CapacityContext(sessionNum: 0, sessionType: "Race", capPercent: 1d);
            recorder.RecordFrame(CapacitySnapshot(context, 70d, startedAtUtc.AddSeconds(1), sequence: 1));

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(5), captureDirectory: null);

            var expectedPath = Path.Combine(
                storage.LogsRoot,
                "fuel-v2-log-root",
                "session-with-slashes-fuel-v2-s001-race-diagnostics.json");
            Assert.Equal(expectedPath, path);
            Assert.Equal(expectedPath, recorder.LastArtifactPath);
            Assert.True(File.Exists(expectedPath));
            using var document = JsonDocument.Parse(File.ReadAllText(expectedPath));
            Assert.Equal("rolling-log", document.RootElement.GetProperty("output").GetProperty("mode").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CompleteCollection_RecordsRaceBurnSelectorOnlyAsBoundedShadowEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-fuel-v2-capture-recorder-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var recorder = new FuelV2CaptureRecorder(
                new FuelV2CaptureOptions { Enabled = true },
                storage,
                new AppEventRecorder(storage),
                NullLogger<FuelV2CaptureRecorder>.Instance);
            var startedAtUtc = DateTimeOffset.Parse("2026-07-14T12:00:00Z");
            var context = CapacityContext(sessionNum: 0, sessionType: "Race", capPercent: 1d);
            recorder.StartCollection("selector-shadow", startedAtUtc);

            recorder.RecordFrame(RaceBurnSelectorSnapshot(
                context,
                startedAtUtc.AddSeconds(1),
                sequence: 1,
                Enumerable.Repeat(14.8d, 10).ToArray()));
            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(2), captureDirectory: null);

            Assert.NotNull(path);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var shadow = document.RootElement.GetProperty("raceBurnSelectorShadow");
            Assert.True(shadow.GetProperty("shadowOnly").GetBoolean());
            Assert.Equal(1, shadow.GetProperty("framesEvaluated").GetInt32());
            var latest = shadow.GetProperty("latest");
            Assert.True(latest.GetProperty("shadowOnly").GetBoolean());
            Assert.Equal("LiveConfirmed", latest.GetProperty("state").GetString());
            Assert.Equal("TenLapAverage", latest.GetProperty("selectedBucket").GetString());
            Assert.Equal("history-unavailable", latest.GetProperty("conflict").GetString());
            Assert.True(latest.GetProperty("wouldSeedPlanIfPromoted").GetBoolean());
            Assert.True(latest.GetProperty("wouldDriveAdviceIfPromoted").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CompleteCollection_RetainsOnlyStationaryServiceEvidenceSeparateFromPitLaneTravel()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-fuel-v2-capture-recorder-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var recorder = new FuelV2CaptureRecorder(
                new FuelV2CaptureOptions
                {
                    Enabled = true,
                    MaxStationaryServiceObservations = 5
                },
                storage,
                new AppEventRecorder(storage),
                NullLogger<FuelV2CaptureRecorder>.Instance);
            var startedAtUtc = DateTimeOffset.Parse("2026-05-10T12:00:00Z");
            var context = CapacityContext(sessionNum: 0, sessionType: "Race", capPercent: 1d);
            recorder.StartCollection("stationary-service", startedAtUtc);

            recorder.RecordFrame(StationaryServiceSnapshot(
                context,
                fuelLevelLiters: 20d,
                capturedAtUtc: startedAtUtc.AddSeconds(1),
                sequence: 1,
                inStall: true,
                serviceActive: true,
                requestFlags: 0x10,
                tireSetsUsed: 2,
                leftFrontTiresUsed: 10,
                rightFrontTiresUsed: 20,
                leftRearTiresUsed: 30,
                rightRearTiresUsed: 40));
            recorder.RecordFrame(StationaryServiceSnapshot(
                context,
                fuelLevelLiters: 22d,
                capturedAtUtc: startedAtUtc.AddSeconds(3),
                sequence: 2,
                inStall: true,
                serviceActive: true,
                requestFlags: 0x1F,
                tireSetsUsed: 3,
                leftFrontTiresUsed: 11,
                rightFrontTiresUsed: 21,
                leftRearTiresUsed: 31,
                rightRearTiresUsed: 41));
            recorder.RecordFrame(StationaryServiceSnapshot(
                context,
                fuelLevelLiters: 25d,
                capturedAtUtc: startedAtUtc.AddSeconds(5),
                sequence: 3,
                inStall: true,
                serviceActive: true,
                requestFlags: 0x1F,
                tireSetsUsed: 3,
                leftFrontTiresUsed: 11,
                rightFrontTiresUsed: 21,
                leftRearTiresUsed: 31,
                rightRearTiresUsed: 41));
            recorder.RecordFrame(StationaryServiceSnapshot(
                context,
                fuelLevelLiters: 25d,
                capturedAtUtc: startedAtUtc.AddSeconds(8),
                sequence: 4,
                inStall: false,
                serviceActive: false,
                requestFlags: 0,
                tireSetsUsed: 3,
                leftFrontTiresUsed: 11,
                rightFrontTiresUsed: 21,
                leftRearTiresUsed: 31,
                rightRearTiresUsed: 41));

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(9), captureDirectory: null);

            Assert.NotNull(path);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var observations = document.RootElement.GetProperty("stationaryServiceObservations");
            var observation = Assert.Single(observations.EnumerateArray());
            Assert.Equal(4d, observation.GetProperty("durationSeconds").GetDouble());
            Assert.Equal(5d, observation.GetProperty("positiveFuelAddedLiters").GetDouble());
            Assert.True(observation.GetProperty("requestChangedDuringService").GetBoolean());
            Assert.Equal(4, observation.GetProperty("lastRequest").GetProperty("requestedTireCount").GetInt32());
            Assert.Equal(10, observation.GetProperty("entryTireCounters").GetProperty("leftFrontTiresUsed").GetInt32());
            Assert.Equal(11, observation.GetProperty("exitTireCounters").GetProperty("leftFrontTiresUsed").GetInt32());
            Assert.Equal(1, observation.GetProperty("tireCounterDelta").GetProperty("leftFrontTiresUsed").GetInt32());
            var pitService = document.RootElement.GetProperty("pitService");
            Assert.Equal(1, pitService.GetProperty("stationaryServiceObservationCount").GetInt32());
            Assert.Equal(1, pitService.GetProperty("retainedStationaryServiceObservationCount").GetInt32());
            Assert.Equal(0, pitService.GetProperty("droppedStationaryServiceObservationCount").GetInt32());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CurrentSessionEvidence_UpdatesModelReadinessOnlyAfterStationaryServiceCloses()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-fuel-v2-current-session-readiness-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var recorder = new FuelV2CaptureRecorder(
                new FuelV2CaptureOptions
                {
                    Enabled = true,
                    MaxStationaryServiceObservations = 5
                },
                storage,
                new AppEventRecorder(storage),
                NullLogger<FuelV2CaptureRecorder>.Instance);
            var historyOptions = new FuelV2HistoryOptions
            {
                Enabled = true,
                UseForStrategy = false,
                ResolvedHistoryRoot = Path.Combine(storage.UserHistoryRoot, "fuel-v2")
            };
            var readiness = new FuelV2ModelReadinessQueryService(
                historyOptions,
                new FuelV2HistoryStore(historyOptions),
                recorder);
            var startedAtUtc = DateTimeOffset.Parse("2026-07-15T23:27:00Z");
            var context = CapacityContext(sessionNum: 0, sessionType: "Practice", capPercent: 1d);
            recorder.StartCollection("current-session-readiness", startedAtUtc);

            recorder.RecordFrame(StationaryServiceSnapshot(
                context,
                fuelLevelLiters: 20d,
                capturedAtUtc: startedAtUtc.AddSeconds(1),
                sequence: 1,
                inStall: true,
                serviceActive: true,
                requestFlags: 0x10,
                tireSetsUsed: 2,
                leftFrontTiresUsed: 10,
                rightFrontTiresUsed: 20,
                leftRearTiresUsed: 30,
                rightRearTiresUsed: 40));
            recorder.RecordFrame(StationaryServiceSnapshot(
                context,
                fuelLevelLiters: 25d,
                capturedAtUtc: startedAtUtc.AddSeconds(3),
                sequence: 2,
                inStall: true,
                serviceActive: true,
                requestFlags: 0x10,
                tireSetsUsed: 2,
                leftFrontTiresUsed: 10,
                rightFrontTiresUsed: 20,
                leftRearTiresUsed: 30,
                rightRearTiresUsed: 40));

            var activeReadiness = readiness.Lookup(context);
            Assert.Empty(activeReadiness.EvidenceSources ?? []);
            Assert.Equal(
                FuelV2ModelReadinessState.Missing,
                activeReadiness.Rows.Single(row => row.Label == "Refuel")
                    .Cells.Single(cell => cell.Label == "Small fill").State);

            recorder.RecordFrame(StationaryServiceSnapshot(
                context,
                fuelLevelLiters: 30d,
                capturedAtUtc: startedAtUtc.AddSeconds(5),
                sequence: 3,
                inStall: true,
                serviceActive: true,
                requestFlags: 0x10,
                tireSetsUsed: 2,
                leftFrontTiresUsed: 10,
                rightFrontTiresUsed: 20,
                leftRearTiresUsed: 30,
                rightRearTiresUsed: 40));
            recorder.RecordFrame(StationaryServiceSnapshot(
                context,
                fuelLevelLiters: 30d,
                capturedAtUtc: startedAtUtc.AddSeconds(6),
                sequence: 4,
                inStall: false,
                serviceActive: false,
                requestFlags: 0,
                tireSetsUsed: 2,
                leftFrontTiresUsed: 10,
                rightFrontTiresUsed: 20,
                leftRearTiresUsed: 30,
                rightRearTiresUsed: 40));

            var closedReadiness = readiness.Lookup(context);
            Assert.Contains(FuelV2ModelReadinessEvidenceSource.CurrentSession, closedReadiness.EvidenceSources!);
            Assert.Equal(startedAtUtc.AddSeconds(5), closedReadiness.CurrentSessionEvidenceUpdatedAtUtc);
            Assert.Equal(
                FuelV2ModelReadinessState.Confirmed,
                closedReadiness.Rows.Single(row => row.Label == "Refuel")
                    .Cells.Single(cell => cell.Label == "Small fill").State);
            Assert.Equal(
                FuelV2ModelReadinessState.Confirmed,
                closedReadiness.Rows.Single(row => row.Label == "Refuel")
                    .Cells.Single(cell => cell.Label == "Fuel flow").State);

            recorder.CompleteCollection(startedAtUtc.AddSeconds(7), captureDirectory: null);

            var resetReadiness = readiness.Lookup(context);
            Assert.Empty(resetReadiness.EvidenceSources ?? []);
            Assert.Equal(
                FuelV2ModelReadinessState.Missing,
                resetReadiness.Rows.Single(row => row.Label == "Refuel")
                    .Cells.Single(cell => cell.Label == "Small fill").State);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CompleteCollection_DetectsSmoothPitWindowRefuelFromCumulativeFuelIncrease()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-fuel-v2-capture-recorder-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var recorder = new FuelV2CaptureRecorder(
                new FuelV2CaptureOptions { Enabled = true },
                storage,
                new AppEventRecorder(storage),
                NullLogger<FuelV2CaptureRecorder>.Instance);
            var startedAtUtc = DateTimeOffset.Parse("2026-07-14T12:00:00Z");
            var context = CapacityContext(sessionNum: 0, sessionType: "Race", capPercent: 1d);
            recorder.StartCollection("smooth-pit-refuel", startedAtUtc);

            foreach (var (offsetSeconds, fuelLevelLiters) in new[]
            {
                (0d, 5.0d),
                (1d, 5.2d),
                (2d, 5.4d),
                (3d, 5.6d)
            })
            {
                recorder.RecordFrame(StationaryServiceSnapshot(
                    context,
                    fuelLevelLiters,
                    startedAtUtc.AddSeconds(offsetSeconds),
                    sequence: (long)offsetSeconds + 1,
                    inStall: true,
                    serviceActive: true,
                    requestFlags: 0x10,
                    tireSetsUsed: 2));
            }

            recorder.RecordFrame(StationaryServiceSnapshot(
                context,
                fuelLevelLiters: 5.6d,
                capturedAtUtc: startedAtUtc.AddSeconds(4),
                sequence: 5,
                inStall: false,
                serviceActive: false,
                requestFlags: 0,
                tireSetsUsed: 2));

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(5), captureDirectory: null);

            Assert.NotNull(path);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var pitWindow = document.RootElement.GetProperty("pitWindows").EnumerateArray().Single();
            Assert.True(pitWindow.GetProperty("sawFuelIncrease").GetBoolean());
            Assert.Equal(0.6d, pitWindow.GetProperty("maxFuelIncreaseLiters").GetDouble(), 3);
            Assert.Equal(1, document.RootElement.GetProperty("pitService").GetProperty("pitWindowsWithFuelIncrease").GetInt32());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CompleteCollection_RecordsAConfirmedLocalPitRouteFromNormalizedLiveFrames()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-fuel-v2-capture-recorder-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var recorder = new FuelV2CaptureRecorder(
                new FuelV2CaptureOptions { Enabled = true },
                storage,
                new AppEventRecorder(storage),
                NullLogger<FuelV2CaptureRecorder>.Instance);
            var startedAtUtc = DateTimeOffset.Parse("2026-07-14T12:00:00Z");
            var context = CapacityContext(sessionNum: 0, sessionType: "Practice", capPercent: 1d);
            recorder.StartCollection("confirmed-local-pit-route", startedAtUtc);

            var frames = new[]
            {
                (onPitRoad: false, inStall: false, fuel: 40d),
                (onPitRoad: true, inStall: false, fuel: 39.9d),
                (onPitRoad: true, inStall: false, fuel: 39.8d),
                (onPitRoad: true, inStall: true, fuel: 39.6d),
                (onPitRoad: true, inStall: true, fuel: 39.5d),
                (onPitRoad: true, inStall: false, fuel: 44d),
                (onPitRoad: true, inStall: false, fuel: 43.9d),
                (onPitRoad: false, inStall: false, fuel: 43.7d),
                (onPitRoad: false, inStall: false, fuel: 43.6d)
            };
            for (var index = 0; index < frames.Length; index++)
            {
                var frame = frames[index];
                recorder.RecordFrame(PitRouteSnapshot(
                    context,
                    frame.fuel,
                    startedAtUtc.AddSeconds(index + 1),
                    sequence: index + 1,
                    onPitRoad: frame.onPitRoad,
                    inStall: frame.inStall));
            }

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(11), captureDirectory: null);

            Assert.NotNull(path);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var route = Assert.Single(document.RootElement.GetProperty("pitRouteObservations").EnumerateArray());
            Assert.Equal(2d, route.GetProperty("entryToBoxSeconds").GetDouble());
            Assert.Equal(2d, route.GetProperty("boxToExitSeconds").GetDouble());
            Assert.Equal(0.3d, route.GetProperty("entryToBoxFuelUsedLiters").GetDouble(), precision: 6);
            Assert.Equal(0.3d, route.GetProperty("boxToExitFuelUsedLiters").GetDouble(), precision: 6);
            Assert.True(route.GetProperty("hasCompleteRoute").GetBoolean());
            Assert.Equal(
                "driver-pit-track-percent:0.068197",
                route.GetProperty("assignment").GetProperty("pitBoxIdentity").GetString());
            Assert.Equal(1, document.RootElement
                .GetProperty("pitService")
                .GetProperty("pitRouteObservationCount")
                .GetInt32());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CompleteCollection_PreservesProgressOnlyCameraFallbackPitRouteCollectionWhenFuelV2DisplayWaitsForCurrentData()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-fuel-v2-capture-recorder-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var recorder = new FuelV2CaptureRecorder(
                new FuelV2CaptureOptions { Enabled = true },
                storage,
                new AppEventRecorder(storage),
                NullLogger<FuelV2CaptureRecorder>.Instance);
            var startedAtUtc = DateTimeOffset.Parse("2026-07-14T12:00:00Z");
            var context = CapacityContext(sessionNum: 0, sessionType: "Practice", capPercent: 1d);
            recorder.StartCollection("display-wait-local-route", startedAtUtc);

            var frames = new[]
            {
                (onPitRoad: false, inStall: false, fuel: 40d),
                (onPitRoad: true, inStall: false, fuel: 39.9d),
                (onPitRoad: true, inStall: false, fuel: 39.8d),
                (onPitRoad: true, inStall: true, fuel: 39.6d),
                (onPitRoad: true, inStall: true, fuel: 39.5d),
                (onPitRoad: true, inStall: false, fuel: 44d),
                (onPitRoad: true, inStall: false, fuel: 43.9d),
                (onPitRoad: false, inStall: false, fuel: 43.7d),
                (onPitRoad: false, inStall: false, fuel: 43.6d)
            };
            for (var index = 0; index < frames.Length; index++)
            {
                var frame = frames[index];
                var baseSnapshot = PitRouteSnapshot(
                    context,
                    frame.fuel,
                    startedAtUtc.AddSeconds(index + 1),
                    sequence: index + 1,
                    onPitRoad: frame.onPitRoad,
                    inStall: frame.inStall);
                var snapshot = baseSnapshot with
                {
                    // The presenter is intentionally ineligible, but the
                    // normalized FuelPit model still has valid local route
                    // evidence that the capture collector must retain. This
                    // models the exact narrow fallback observed in capture:
                    // session driver and raw camera agree, but focus has no
                    // usable timing/spatial progress yet.
                    Fuel = LiveFuelSnapshot.Unavailable,
                    HasFrameForCurrentContext = false,
                    HasSessionInfoForCurrentCollection = false,
                    LatestSample = baseSnapshot.LatestSample! with
                    {
                        FocusCarIdx = null,
                        FocusUnavailableReason = "cam_car_progress_unavailable"
                    },
                    Models = baseSnapshot.Models with
                    {
                        DriverDirectory = baseSnapshot.Models.DriverDirectory with
                        {
                            FocusCarIdx = null
                        },
                        Reference = baseSnapshot.Models.Reference with
                        {
                            FocusCarIdx = null,
                            FocusIsPlayer = false,
                            FocusUnavailableReason = "cam_car_progress_unavailable"
                        }
                    }
                };
                recorder.RecordFrame(snapshot);
            }

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(11), captureDirectory: null);

            Assert.NotNull(path);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var route = Assert.Single(document.RootElement.GetProperty("pitRouteObservations").EnumerateArray());
            Assert.True(route.GetProperty("hasCompleteRoute").GetBoolean());
            Assert.Equal(
                "session-driver-camera-fallback",
                route.GetProperty("pitEntry").GetProperty("localIdentityProvenance").GetString());
            Assert.Equal(1, document.RootElement
                .GetProperty("pitService")
                .GetProperty("pitRouteObservationCount")
                .GetInt32());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CompleteCollection_ConfirmsAndAnnotatesTeamStintsOnlyAfterAStableSingleStepDriverSwap()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-fuel-v2-driver-swap-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var recorder = new FuelV2CaptureRecorder(
                new FuelV2CaptureOptions { Enabled = true },
                storage,
                new AppEventRecorder(storage),
                NullLogger<FuelV2CaptureRecorder>.Instance);
            var startedAtUtc = DateTimeOffset.Parse("2026-07-17T12:00:00Z");
            var context = CapacityContext(sessionNum: 0, sessionType: "Race", capPercent: 1d, teamRacing: true);
            recorder.StartCollection("confirmed-driver-swap", startedAtUtc);

            recorder.RecordFrame(TeamStintSnapshot(context, 70d, startedAtUtc.AddSeconds(1), 1, 2, 10, 0.10d));
            recorder.RecordFrame(TeamStintSnapshot(context, 69d, startedAtUtc.AddSeconds(2), 2, 2, 11, 0.20d));
            recorder.RecordFrame(TeamStintSnapshot(context, 68d, startedAtUtc.AddSeconds(3), 3, 3, 11, 0.30d));
            recorder.RecordFrame(TeamStintSnapshot(context, 67d, startedAtUtc.AddSeconds(4), 4, 3, 11, 0.40d));
            recorder.RecordFrame(TeamStintSnapshot(context, 66d, startedAtUtc.AddSeconds(5), 5, 3, 11, 0.50d));

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(6), captureDirectory: null);

            Assert.NotNull(path);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var rootElement = document.RootElement;
            var team = rootElement.GetProperty("team");
            Assert.Equal(2, team.GetProperty("initialDriversSoFar").GetInt32());
            Assert.Equal(3, team.GetProperty("finalDriversSoFar").GetInt32());
            Assert.Equal(10, team.GetProperty("initialDriverChangeLapStatus").GetInt32());
            Assert.Equal(11, team.GetProperty("finalDriverChangeLapStatus").GetInt32());
            Assert.Equal(1, team.GetProperty("driverChangeEventCount").GetInt32());
            Assert.Equal(1, team.GetProperty("confirmedDriverSwapCount").GetInt32());
            Assert.Equal(0, team.GetProperty("unconfirmedDriverChangeEventCount").GetInt32());
            Assert.Equal(1, team.GetProperty("driverChangeLapStatusChangeCount").GetInt32());

            var stints = rootElement.GetProperty("teamStints").EnumerateArray().ToArray();
            Assert.Equal(2, stints.Length);
            Assert.Equal(2, stints[0].GetProperty("driversSoFarAtStart").GetInt32());
            Assert.Equal(3, stints[0].GetProperty("driversSoFarAtEnd").GetInt32());
            Assert.True(stints[0].GetProperty("endsAtConfirmedDriverSwap").GetBoolean());
            Assert.False(stints[0].GetProperty("startsAfterConfirmedDriverSwap").GetBoolean());
            Assert.Equal(3, stints[1].GetProperty("driversSoFarAtStart").GetInt32());
            Assert.True(stints[1].GetProperty("startsAfterConfirmedDriverSwap").GetBoolean());
            Assert.False(stints[1].GetProperty("endsAtConfirmedDriverSwap").GetBoolean());
            Assert.Contains(
                rootElement.GetProperty("eventSamples").EnumerateArray(),
                sample => sample.GetProperty("kind").GetString() == "driver-control.swap-confirmed");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CompleteCollection_DoesNotSplitTeamStintsForLateJoinLapStatusOrMultiDriverCounterMovement()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-fuel-v2-driver-swap-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var recorder = new FuelV2CaptureRecorder(
                new FuelV2CaptureOptions { Enabled = true },
                storage,
                new AppEventRecorder(storage),
                NullLogger<FuelV2CaptureRecorder>.Instance);
            var startedAtUtc = DateTimeOffset.Parse("2026-07-17T12:00:00Z");
            var context = CapacityContext(sessionNum: 0, sessionType: "Race", capPercent: 1d, teamRacing: true);
            recorder.StartCollection("unconfirmed-driver-swap", startedAtUtc);

            // A late connection can first see a non-one count. It is retained
            // as baseline provenance only. DCLapStatus then changes, but it
            // cannot prove a swap. A 2-to-4 counter jump also stays factual
            // and unconfirmed because the collector cannot identify/split two
            // missing driver transitions safely.
            recorder.RecordFrame(TeamStintSnapshot(context, 70d, startedAtUtc.AddSeconds(1), 1, 2, 10, 0.10d));
            recorder.RecordFrame(TeamStintSnapshot(context, 69d, startedAtUtc.AddSeconds(2), 2, 2, 11, 0.20d));
            recorder.RecordFrame(TeamStintSnapshot(context, 68d, startedAtUtc.AddSeconds(3), 3, 4, 11, 0.30d));
            recorder.RecordFrame(TeamStintSnapshot(context, 67d, startedAtUtc.AddSeconds(4), 4, 4, 11, 0.40d));

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(5), captureDirectory: null);

            Assert.NotNull(path);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var rootElement = document.RootElement;
            var team = rootElement.GetProperty("team");
            Assert.Equal(2, team.GetProperty("initialDriversSoFar").GetInt32());
            Assert.Equal(4, team.GetProperty("finalDriversSoFar").GetInt32());
            Assert.Equal(1, team.GetProperty("driverChangeEventCount").GetInt32());
            Assert.Equal(0, team.GetProperty("confirmedDriverSwapCount").GetInt32());
            Assert.Equal(1, team.GetProperty("unconfirmedDriverChangeEventCount").GetInt32());
            Assert.Equal(1, team.GetProperty("driverChangeLapStatusChangeCount").GetInt32());
            Assert.Single(rootElement.GetProperty("teamStints").EnumerateArray());
            Assert.DoesNotContain(
                rootElement.GetProperty("eventSamples").EnumerateArray(),
                sample => sample.GetProperty("kind").GetString() == "driver-control.swap-confirmed");
            Assert.Contains(
                rootElement.GetProperty("eventSamples").EnumerateArray(),
                sample => sample.GetProperty("kind").GetString() == "driver-control.lap-status-changed");
            Assert.Contains(
                rootElement.GetProperty("eventSamples").EnumerateArray(),
                sample => sample.GetProperty("kind").GetString() == "driver-control.change-unconfirmed");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CompleteCollection_WhenDisabledReturnsNullAndDoesNotWriteArtifact()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-fuel-v2-capture-recorder-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var captureDirectory = Path.Combine(storage.CaptureRoot, "capture-fuel-v2");
            Directory.CreateDirectory(captureDirectory);
            var recorder = new FuelV2CaptureRecorder(
                new FuelV2CaptureOptions
                {
                    Enabled = false,
                    OutputFileName = "disabled.json",
                    CaptureDirectoryName = "fuel-v2-disabled"
                },
                storage,
                new AppEventRecorder(storage),
                NullLogger<FuelV2CaptureRecorder>.Instance);
            var startedAtUtc = DateTimeOffset.Parse("2026-05-10T12:00:00Z");
            recorder.StartCollection("capture-fuel-v2-disabled", startedAtUtc);

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(5), captureDirectory);

            Assert.Null(path);
            Assert.Null(recorder.LastArtifactPath);
            Assert.False(File.Exists(Path.Combine(captureDirectory, "fuel-v2-disabled", "disabled.json")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void RecordFrame_ResolvesExistingCapacityScopeFieldsFromLiveSessionContext()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-fuel-v2-capture-recorder-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var recorder = new FuelV2CaptureRecorder(
                new FuelV2CaptureOptions { Enabled = true },
                storage,
                new AppEventRecorder(storage),
                NullLogger<FuelV2CaptureRecorder>.Instance);
            var startedAtUtc = DateTimeOffset.Parse("2026-05-10T12:00:00Z");
            recorder.StartCollection("capture-fuel-v2-capacity", startedAtUtc);

            var context = new HistoricalSessionContext
            {
                Car = new HistoricalCarIdentity
                {
                    CarId = 1,
                    CarPath = "test-car",
                    DriverCarFuelMaxLiters = 75d,
                    DriverCarFuelKgPerLiter = 0.75d
                },
                Track = new HistoricalTrackIdentity { TrackId = 2, TrackName = "test-track", TrackConfigName = "Full" },
                Session = new HistoricalSessionIdentity { CurrentSessionNum = 0, SessionNum = 0, SessionType = "Race" },
                Conditions = new HistoricalSessionInfoConditions(),
                FuelCapacityRules = new HistoricalFuelCapacityRules
                {
                    DriverCarMaxFuelPercent = 0.8d,
                    CarClassMaxFuelPercent = 0.8d
                }
            };
            var snapshot = LiveTelemetrySnapshot.Empty with
            {
                IsConnected = true,
                IsCollecting = true,
                SourceId = "capture-fuel-v2-capacity",
                StartedAtUtc = startedAtUtc,
                LastUpdatedAtUtc = startedAtUtc.AddSeconds(1),
                Sequence = 1,
                Context = context,
                Combo = HistoricalComboIdentity.From(context),
                Fuel = LiveFuel(58d)
            };

            recorder.RecordFrame(snapshot);
            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(2), captureDirectory: null);

            Assert.NotNull(path);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var capacity = document.RootElement.GetProperty("sessionScope").GetProperty("fuelCapacity");
            Assert.Equal(75d, capacity.GetProperty("physicalTankCapacityLiters").GetDouble());
            Assert.Equal(60d, capacity.GetProperty("effectiveSessionCapacityLiters").GetDouble());
            Assert.Equal("matching_driver_and_class_caps", capacity.GetProperty("effectiveSessionCapacitySource").GetString());
            Assert.Equal(0.8d, capacity.GetProperty("driverCarMaxFuelPercent").GetDouble());
            Assert.Equal(0.8d, capacity.GetProperty("carClassMaxFuelPercent").GetDouble());
            Assert.Equal(string.Empty, capacity.GetProperty("limitation").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void RecordFrame_SplitsSessionEvidenceBeforeRestrictedRaceCapIsObserved()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-fuel-v2-capture-recorder-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var recorder = new FuelV2CaptureRecorder(
                new FuelV2CaptureOptions { Enabled = true },
                storage,
                new AppEventRecorder(storage),
                NullLogger<FuelV2CaptureRecorder>.Instance);
            var startedAtUtc = DateTimeOffset.Parse("2026-05-10T12:00:00Z");
            recorder.StartCollection("capture-fuel-v2-session-transition", startedAtUtc);

            var warmup = CapacityContext(sessionNum: 0, sessionType: "Warmup", capPercent: 1d);
            var race = CapacityContext(sessionNum: 1, sessionType: "Race", capPercent: 0.68d);
            recorder.RecordFrame(CapacitySnapshot(warmup, 74d, startedAtUtc.AddSeconds(1), sequence: 1));
            var completedPaths = recorder.RecordFrame(CapacitySnapshot(race, 50d, startedAtUtc.AddSeconds(2), sequence: 2));

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(3), captureDirectory: null);

            var warmupPath = Assert.Single(completedPaths);
            Assert.NotNull(path);
            using var warmupDocument = JsonDocument.Parse(File.ReadAllText(warmupPath));
            using var raceDocument = JsonDocument.Parse(File.ReadAllText(path));
            var warmupRoot = warmupDocument.RootElement;
            var raceRoot = raceDocument.RootElement;
            Assert.Equal("warmup", warmupRoot.GetProperty("sessionLineage").GetProperty("sessionFamily").GetString());
            Assert.Equal(74d, warmupRoot.GetProperty("fuel").GetProperty("maxFuelLiters").GetDouble());
            Assert.Equal(75d, warmupRoot.GetProperty("sessionScope").GetProperty("fuelCapacity").GetProperty("effectiveSessionCapacityLiters").GetDouble());
            Assert.Equal("race", raceRoot.GetProperty("sessionLineage").GetProperty("sessionFamily").GetString());
            Assert.Equal(50d, raceRoot.GetProperty("fuel").GetProperty("maxFuelLiters").GetDouble());
            Assert.Equal(51d, raceRoot.GetProperty("sessionScope").GetProperty("fuelCapacity").GetProperty("effectiveSessionCapacityLiters").GetDouble());
            Assert.Equal("session-transition", warmupRoot.GetProperty("sessionLineage").GetProperty("endedByBoundaryKind").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void RecordFrame_QuarantinesUnverifiedIdentityUntilExactCarLayoutAndOccurrenceAreKnown()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-fuel-v2-capture-recorder-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var recorder = new FuelV2CaptureRecorder(
                new FuelV2CaptureOptions { Enabled = true },
                storage,
                new AppEventRecorder(storage),
                NullLogger<FuelV2CaptureRecorder>.Instance);
            var startedAtUtc = DateTimeOffset.Parse("2026-05-10T12:00:00Z");
            recorder.StartCollection("capture-fuel-v2-identity", startedAtUtc);
            var incomplete = CapacityContext(sessionNum: 0, sessionType: "Race", capPercent: 1d, trackConfigName: null);
            var complete = CapacityContext(sessionNum: 0, sessionType: "Race", capPercent: 1d);

            var incompleteCompleted = recorder.RecordFrame(CapacitySnapshot(incomplete, 74d, startedAtUtc.AddSeconds(1), sequence: 1));
            recorder.RecordFrame(CapacitySnapshot(complete, 50d, startedAtUtc.AddSeconds(2), sequence: 2));
            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(3), captureDirectory: null);

            Assert.Empty(incompleteCompleted);
            Assert.NotNull(path);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(1, document.RootElement.GetProperty("totals").GetProperty("frameCount").GetInt32());
            Assert.Equal(50d, document.RootElement.GetProperty("fuel").GetProperty("maxFuelLiters").GetDouble());
            Assert.True(document.RootElement.GetProperty("sessionLineage").GetProperty("exactTrackLayoutVerified").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void RecordFrame_SplitsWhenExactTrackConfigurationChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-fuel-v2-capture-recorder-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var recorder = new FuelV2CaptureRecorder(
                new FuelV2CaptureOptions { Enabled = true },
                storage,
                new AppEventRecorder(storage),
                NullLogger<FuelV2CaptureRecorder>.Instance);
            var startedAtUtc = DateTimeOffset.Parse("2026-05-10T12:00:00Z");
            recorder.StartCollection("capture-fuel-v2-layout", startedAtUtc);
            var full = CapacityContext(sessionNum: 0, sessionType: "Race", capPercent: 1d, trackConfigName: "GP-Short");
            var spaced = CapacityContext(sessionNum: 0, sessionType: "Race", capPercent: 1d, trackConfigName: "GP Short");

            recorder.RecordFrame(CapacitySnapshot(full, 70d, startedAtUtc.AddSeconds(1), sequence: 1));
            var completedPaths = recorder.RecordFrame(CapacitySnapshot(spaced, 50d, startedAtUtc.AddSeconds(2), sequence: 2));
            var currentPath = recorder.CompleteCollection(startedAtUtc.AddSeconds(3), captureDirectory: null);

            var firstPath = Assert.Single(completedPaths);
            Assert.NotNull(currentPath);
            using var first = JsonDocument.Parse(File.ReadAllText(firstPath));
            using var second = JsonDocument.Parse(File.ReadAllText(currentPath));
            Assert.Equal(
                "track-id-2-config-47502D53686F7274",
                first.RootElement.GetProperty("sessionLineage").GetProperty("trackLayoutKey").GetString());
            Assert.Equal(
                "track-id-2-config-47502053686F7274",
                second.RootElement.GetProperty("sessionLineage").GetProperty("trackLayoutKey").GetString());
            Assert.Equal(70d, first.RootElement.GetProperty("fuel").GetProperty("maxFuelLiters").GetDouble());
            Assert.Equal(50d, second.RootElement.GetProperty("fuel").GetProperty("maxFuelLiters").GetDouble());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static HistoricalSessionContext CapacityContext(
        int sessionNum,
        string sessionType,
        double capPercent,
        string? trackConfigName = "Full",
        string? dcRuleSet = null)
    {
        return new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity
            {
                CarId = 1,
                CarPath = "test-car",
                DriverCarFuelMaxLiters = 75d,
                DriverCarFuelKgPerLiter = 0.75d
            },
            Track = new HistoricalTrackIdentity { TrackId = 2, TrackName = "test-track", TrackConfigName = trackConfigName },
            Session = new HistoricalSessionIdentity
            {
                CurrentSessionNum = sessionNum,
                SessionNum = sessionNum,
                SessionType = sessionType,
                DCRuleSet = dcRuleSet,
                SubSessionId = 4
            },
            Conditions = new HistoricalSessionInfoConditions(),
            PitRouteAssignment = new HistoricalPitRouteAssignment
            {
                DriverPitTrackPct = 0.068197d,
                TrackPitSpeedLimitKph = 80d,
                TrackNumPitStalls = 39
            },
            DriverCarIdx = 10,
            Drivers =
            [
                new HistoricalSessionDriver { CarIdx = 10, IsSpectator = false }
            ],
            FuelCapacityRules = new HistoricalFuelCapacityRules
            {
                DriverCarMaxFuelPercent = capPercent,
                CarClassMaxFuelPercent = capPercent
            }
        };
    }

    private static LiveTelemetrySnapshot CapacitySnapshot(
        HistoricalSessionContext context,
        double fuelLevelLiters,
        DateTimeOffset capturedAtUtc,
        long sequence)
    {
        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            SourceId = "capture-fuel-v2-session-transition",
            StartedAtUtc = capturedAtUtc.AddSeconds(-1),
            LastUpdatedAtUtc = capturedAtUtc,
            Sequence = sequence,
            Context = context,
            Combo = HistoricalComboIdentity.From(context),
            Fuel = LiveFuel(fuelLevelLiters)
        };
    }

    private static LiveTelemetrySnapshot RaceBurnSelectorSnapshot(
        HistoricalSessionContext context,
        DateTimeOffset capturedAtUtc,
        long sequence,
        IReadOnlyList<double> fuelPerLapLiters)
    {
        var samples = fuelPerLapLiters
            .Select((burn, index) => new LiveFuelPerLapAcceptedSample(
                FuelPerLapLiters: burn,
                ProgressDeltaLaps: 1d,
                FuelUsedLiters: burn,
                ElapsedSeconds: 90d,
                StartedAtSessionTimeSeconds: index * 90d,
                CompletedAtSessionTimeSeconds: (index + 1) * 90d))
            .ToArray();
        return CapacitySnapshot(context, 50d, capturedAtUtc, sequence) with
        {
            HasFrameForCurrentContext = true,
            HasSessionInfoForCurrentCollection = true,
            FuelPerLapWindow = new LiveFuelPerLapWindow(
                Last: LiveFuelPerLapWindowValue.Live(samples[^1].FuelPerLapLiters, samples.Length),
                FiveLapAverage: null,
                TenLapAverage: null,
                Max: null,
                AcceptedSampleCount: samples.Length,
                CleanSamples: samples,
                FormationFuelUsedLiters: 0d,
                PitOrEdgeFuelUsedLiters: 0d)
        };
    }

    private static LiveTelemetrySnapshot StationaryServiceSnapshot(
        HistoricalSessionContext context,
        double fuelLevelLiters,
        DateTimeOffset capturedAtUtc,
        long sequence,
        bool inStall,
        bool serviceActive,
        int requestFlags,
        int tireSetsUsed,
        int? leftFrontTiresUsed = null,
        int? rightFrontTiresUsed = null,
        int? leftRearTiresUsed = null,
        int? rightRearTiresUsed = null)
    {
        var request = LivePitServiceRequest.FromFlags(
            requestFlags,
            fuelLiters: 12.5d,
            requestedTireCompoundIndex: 1,
            requestedTireCompoundLabel: "Dry",
            requestedTireCompoundShortLabel: "D");
        var pitService = LivePitServiceModel.Empty with
        {
            HasData = true,
            Quality = LiveModelQuality.Reliable,
            OnPitRoad = true,
            PitstopActive = serviceActive,
            PlayerCarInPitStall = inStall,
            Status = serviceActive ? 1 : 0,
            Flags = requestFlags,
            Request = request,
            Tires = LivePitServiceTireState.Empty with
            {
                RequestedTireCount = request.RequestedTireCount,
                TireSetsUsed = tireSetsUsed,
                LeftFrontTiresUsed = leftFrontTiresUsed,
                RightFrontTiresUsed = rightFrontTiresUsed,
                LeftRearTiresUsed = leftRearTiresUsed,
                RightRearTiresUsed = rightRearTiresUsed
            }
        };
        return CapacitySnapshot(context, fuelLevelLiters, capturedAtUtc, sequence) with
        {
            Models = LiveRaceModels.Empty with { PitService = pitService }
        };
    }

    private static LiveTelemetrySnapshot PitRouteSnapshot(
        HistoricalSessionContext context,
        double fuelLevelLiters,
        DateTimeOffset capturedAtUtc,
        long sequence,
        bool onPitRoad,
        bool inStall)
    {
        var sample = new HistoricalTelemetrySample(
            CapturedAtUtc: capturedAtUtc,
            SessionTime: (capturedAtUtc - DateTimeOffset.Parse("2026-07-14T12:00:00Z")).TotalSeconds,
            SessionTick: checked((int)sequence * 60),
            SessionInfoUpdate: 1,
            IsOnTrack: !onPitRoad,
            IsInGarage: false,
            OnPitRoad: onPitRoad,
            PitstopActive: inStall,
            PlayerCarInPitStall: inStall,
            FuelLevelLiters: fuelLevelLiters,
            FuelLevelPercent: fuelLevelLiters / 75d,
            FuelUsePerHourKg: 0d,
            SpeedMetersPerSecond: onPitRoad ? 20d : 45d,
            Lap: 4,
            LapCompleted: 3,
            LapDistPct: 0.8d,
            LapLastLapTimeSeconds: null,
            LapBestLapTimeSeconds: null,
            AirTempC: 20d,
            TrackTempCrewC: 24d,
            TrackWetness: 0,
            WeatherDeclaredWet: false,
            PlayerTireCompound: 0,
            PlayerCarIdx: 10,
            RawCamCarIdx: 10,
            FocusCarIdx: 10,
            PlayerTrackSurface: onPitRoad ? 1 : 3);
        var fuelPit = LiveFuelPitModel.Empty with
        {
            HasData = true,
            Quality = LiveModelQuality.Reliable,
            Fuel = LiveFuel(fuelLevelLiters),
            OnPitRoad = onPitRoad,
            PitstopActive = inStall,
            PlayerCarInPitStall = inStall
        };
        return CapacitySnapshot(context, fuelLevelLiters, capturedAtUtc, sequence) with
        {
            LatestSample = sample,
            HasFrameForCurrentContext = true,
            HasSessionInfoForCurrentCollection = true,
            Models = LiveRaceModels.Empty with
            {
                IsLiveSampleModel = true,
                DriverDirectory = LiveDriverDirectoryModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 10
                },
                Reference = LiveReferenceModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 10,
                    FocusIsPlayer = true,
                    IsOnTrack = !onPitRoad,
                    OnPitRoad = onPitRoad,
                    PlayerOnPitRoad = onPitRoad,
                    PlayerCarInPitStall = inStall
                },
                RaceEvents = LiveRaceEventModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    IsOnTrack = !onPitRoad,
                    OnPitRoad = onPitRoad
                },
                FuelPit = fuelPit
            }
        };
    }

    private static LiveFuelSnapshot LiveFuel(double fuelLevelLiters)
    {
        return new LiveFuelSnapshot(
            HasValidFuel: true,
            Source: "local-driver-scalar",
            FuelLevelLiters: fuelLevelLiters,
            FuelLevelPercent: null,
            FuelUsePerHourKg: null,
            FuelUsePerHourLiters: null,
            FuelPerLapLiters: null,
            MeasuredFuelPerLapMinimumLiters: null,
            MeasuredFuelPerLapAverageLiters: null,
            MeasuredFuelPerLapMaximumLiters: null,
            MeasuredFuelPerLapSampleCount: 0,
            LapTimeSeconds: null,
            LapTimeSource: "unavailable",
            EstimatedMinutesRemaining: null,
            EstimatedLapsRemaining: null,
            Confidence: "none");
    }

    private static AppStorageOptions CreateStorage(string root)
    {
        return new AppStorageOptions
        {
            AppDataRoot = root,
            CaptureRoot = Path.Combine(root, "captures"),
            UserHistoryRoot = Path.Combine(root, "history", "user"),
            BaselineHistoryRoot = Path.Combine(root, "history", "baseline"),
            LogsRoot = Path.Combine(root, "logs"),
            SettingsRoot = Path.Combine(root, "settings"),
            DiagnosticsRoot = Path.Combine(root, "diagnostics"),
            TrackMapRoot = Path.Combine(root, "track-maps", "user"),
            EventsRoot = Path.Combine(root, "logs", "events"),
            RuntimeStatePath = Path.Combine(root, "runtime-state.json")
        };
    }
}
