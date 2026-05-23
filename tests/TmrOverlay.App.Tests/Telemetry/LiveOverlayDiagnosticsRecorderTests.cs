using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TmrOverlay.App.Events;
using TmrOverlay.App.Storage;
using TmrOverlay.App.Telemetry;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Telemetry.EdgeCases;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Telemetry;

public sealed class LiveOverlayDiagnosticsRecorderTests
{
    [Fact]
    public void CompleteCollection_WritesGapRadarFuelAndPositionDiagnostics()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-live-overlay-diagnostics-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var captureDirectory = Path.Combine(storage.CaptureRoot, "capture-diagnostics");
            Directory.CreateDirectory(captureDirectory);
            var recorder = new LiveOverlayDiagnosticsRecorder(
                new LiveOverlayDiagnosticsOptions
                {
                    Enabled = true,
                    MinimumFrameSpacingSeconds = 0.1d,
                    MaxSampleFramesPerSession = 10,
                    MaxEventExamplesPerSession = 20,
                    LargeGapSeconds = 600d,
                    GapJumpSeconds = 300d
                },
                storage,
                new AppEventRecorder(storage),
                NullLogger<LiveOverlayDiagnosticsRecorder>.Instance);
            var context = CreateContext();
            var startedAtUtc = DateTimeOffset.Parse("2026-05-02T12:00:00Z");
            recorder.StartCollection("capture-diagnostics", startedAtUtc);

            recorder.RecordFrame(CreateSnapshot(
                context,
                CreateSample(
                    startedAtUtc,
                    sessionTime: 0d,
                    focusCarIdx: 12,
                    carLeftRight: 2,
                    focusF2TimeSeconds: 700d,
                    classPosition: 2,
                    observedPosition: 25,
                    observedClassPosition: 10,
                    observedLapDistPct: 0.9748d),
                sequence: 1));
            recorder.RecordFrame(CreateSnapshot(
                context,
                CreateSample(
                    startedAtUtc.AddSeconds(1),
                    sessionTime: 1d,
                    focusCarIdx: 10,
                    carLeftRight: 2,
                    focusF2TimeSeconds: 1100d,
                    classPosition: 3,
                    observedPosition: 26,
                    observedClassPosition: 11,
                    observedLapDistPct: 0.9752d),
                sequence: 2));

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(2), captureDirectory);

            Assert.Equal(Path.Combine(captureDirectory, "live-overlay-diagnostics.json"), path);
            using var document = JsonDocument.Parse(File.ReadAllText(path!));
            var rootElement = document.RootElement;
            Assert.Equal(2, rootElement.GetProperty("totals").GetProperty("frameCount").GetInt32());
            Assert.True(rootElement.GetProperty("gap").GetProperty("nonRaceFramesWithData").GetInt32() >= 1);
            Assert.True(rootElement.GetProperty("gap").GetProperty("classLargeGapFrames").GetInt32() >= 1);
            Assert.Equal(1, rootElement.GetProperty("gap").GetProperty("classJumpFrames").GetInt32());
            Assert.Equal(1, rootElement.GetProperty("radar").GetProperty("nonPlayerFocusFrames").GetInt32());
            Assert.Equal(1, rootElement.GetProperty("radar").GetProperty("localSuppressedNonPlayerFocusFrames").GetInt32());
            Assert.Equal(1, rootElement.GetProperty("radar").GetProperty("rawSideSuppressedForFocusFrames").GetInt32());
            Assert.Equal(1, rootElement.GetProperty("radar").GetProperty("sideSignalWithoutPlacementFrames").GetInt32());
            Assert.Equal(2, rootElement.GetProperty("fuel").GetProperty("framesWithInstantaneousBurn").GetInt32());
            Assert.Equal(2, rootElement.GetProperty("fuel").GetProperty("instantaneousBurnWithoutFuelLevelFrames").GetInt32());
            Assert.Equal(1, rootElement.GetProperty("positionCadence").GetProperty("intraLapOverallPositionChanges").GetInt32());
            Assert.Equal(1, rootElement.GetProperty("positionCadence").GetProperty("intraLapClassPositionChanges").GetInt32());
            Assert.Equal(2, rootElement.GetProperty("lapDelta").GetProperty("framesWithAnyValue").GetInt32());
            Assert.Equal(2, rootElement.GetProperty("lapDelta").GetProperty("framesWithAnyUsableValue").GetInt32());
            Assert.Equal(2, rootElement.GetProperty("lapDelta").GetProperty("valueFrameCounts").GetProperty("toBestLap").GetInt32());
            Assert.Equal(2, rootElement.GetProperty("lapDelta").GetProperty("usableFrameCounts").GetProperty("toBestLap").GetInt32());
            Assert.Equal(2, rootElement.GetProperty("relativeLapRelationship").GetProperty("observedFrames").GetInt32());
            Assert.Equal(2, rootElement.GetProperty("sectorTiming").GetProperty("metadataFrames").GetInt32());
            Assert.Equal(2, rootElement.GetProperty("trackMap").GetProperty("framesWithSectors").GetInt32());

            var eventKinds = rootElement
                .GetProperty("eventSamples")
                .EnumerateArray()
                .Select(item => item.GetProperty("kind").GetString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains("gap.non-race-data", eventKinds);
            Assert.Contains("gap.large-jump", eventKinds);
            Assert.Contains("radar.local-suppressed-non-player-focus", eventKinds);
            Assert.Contains("radar.side-suppressed-focus", eventKinds);
            Assert.Contains("fuel.instantaneous-without-level", eventKinds);
            Assert.Contains("position.overall-intra-lap", eventKinds);
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
    public void CompleteCollection_SamplesFramesPerSessionKind()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-live-overlay-diagnostics-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var captureDirectory = Path.Combine(storage.CaptureRoot, "capture-diagnostics");
            Directory.CreateDirectory(captureDirectory);
            var recorder = new LiveOverlayDiagnosticsRecorder(
                new LiveOverlayDiagnosticsOptions
                {
                    Enabled = true,
                    MinimumFrameSpacingSeconds = 0.1d,
                    MaxSampleFramesPerSession = 2,
                    MaxEventExamplesPerSession = 20
                },
                storage,
                new AppEventRecorder(storage),
                NullLogger<LiveOverlayDiagnosticsRecorder>.Instance);
            var warmupContext = CreateContext(sessionType: "Warmup", eventType: "Race");
            var raceContext = CreateContext(sessionType: "Race", eventType: "Race");
            var startedAtUtc = DateTimeOffset.Parse("2026-05-02T12:00:00Z");
            recorder.StartCollection("capture-diagnostics", startedAtUtc);

            for (var index = 0; index < 5; index++)
            {
                recorder.RecordFrame(CreateSnapshot(
                    warmupContext,
                    CreateSample(
                        startedAtUtc.AddSeconds(index),
                        sessionTime: index,
                        focusCarIdx: 10,
                        carLeftRight: 0,
                        focusF2TimeSeconds: index,
                        classPosition: 1,
                        observedPosition: 1,
                        observedClassPosition: 1,
                        observedLapDistPct: 0.1d),
                    sequence: index + 1));
            }

            for (var index = 0; index < 2; index++)
            {
                recorder.RecordFrame(CreateSnapshot(
                    raceContext,
                    CreateSample(
                        startedAtUtc.AddSeconds(10 + index),
                        sessionTime: 10 + index,
                        focusCarIdx: 10,
                        carLeftRight: 0,
                        focusF2TimeSeconds: 10 + index,
                        classPosition: 1,
                        observedPosition: 1,
                        observedClassPosition: 1,
                        observedLapDistPct: 0.2d),
                    sequence: 10 + index));
            }

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(12), captureDirectory);

            using var document = JsonDocument.Parse(File.ReadAllText(path!));
            var sampleSessionKinds = document.RootElement
                .GetProperty("sampleFrames")
                .EnumerateArray()
                .Select(item => item.GetProperty("sessionKind").GetString())
                .ToArray();
            Assert.Equal(4, sampleSessionKinds.Length);
            Assert.Equal(2, sampleSessionKinds.Count(kind => string.Equals(kind, "Warmup", StringComparison.OrdinalIgnoreCase)));
            Assert.Equal(2, sampleSessionKinds.Count(kind => string.Equals(kind, "Race", StringComparison.OrdinalIgnoreCase)));
            Assert.Equal(4, document.RootElement.GetProperty("totals").GetProperty("sampledFrameCount").GetInt32());
            Assert.Equal(3, document.RootElement.GetProperty("totals").GetProperty("droppedFrameSampleCount").GetInt32());
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
    public void CompleteCollection_SummarizesFlagsTelemetry()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-live-overlay-diagnostics-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var captureDirectory = Path.Combine(storage.CaptureRoot, "capture-diagnostics");
            Directory.CreateDirectory(captureDirectory);
            var recorder = new LiveOverlayDiagnosticsRecorder(
                new LiveOverlayDiagnosticsOptions
                {
                    Enabled = true,
                    MinimumFrameSpacingSeconds = 0.1d,
                    MaxSampleFramesPerSession = 10,
                    MaxEventExamplesPerSession = 20
                },
                storage,
                new AppEventRecorder(storage),
                NullLogger<LiveOverlayDiagnosticsRecorder>.Instance);
            var context = CreateRaceGridContext();
            var startedAtUtc = DateTimeOffset.Parse("2026-05-02T12:00:00Z");
            recorder.StartCollection("capture-diagnostics", startedAtUtc);

            recorder.RecordFrame(CreateSnapshot(
                context,
                CreateSample(
                    startedAtUtc,
                    sessionTime: 0d,
                    focusCarIdx: 10,
                    carLeftRight: 0,
                    focusF2TimeSeconds: 0d,
                    classPosition: 1,
                    observedPosition: 1,
                    observedClassPosition: 1,
                    observedLapDistPct: 0.1d,
                    sessionState: 3,
                    sessionFlags: 0x00000008 | 0x00000020),
                sequence: 1));
            recorder.RecordFrame(CreateSnapshot(
                context,
                CreateSample(
                    startedAtUtc.AddSeconds(1),
                    sessionTime: 1d,
                    focusCarIdx: 10,
                    carLeftRight: 0,
                    focusF2TimeSeconds: 0d,
                    classPosition: 1,
                    observedPosition: 1,
                    observedClassPosition: 1,
                    observedLapDistPct: 0.1d,
                    sessionState: 5,
                    sessionFlags: 0),
                sequence: 2));

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(2), captureDirectory);

            using var document = JsonDocument.Parse(File.ReadAllText(path!));
            var flags = document.RootElement.GetProperty("flags");
            Assert.Equal(2, flags.GetProperty("framesWithSessionState").GetInt32());
            Assert.Equal(2, flags.GetProperty("framesWithRawFlags").GetInt32());
            Assert.Equal(1, flags.GetProperty("framesWithActiveRawFlags").GetInt32());
            Assert.Equal(2, flags.GetProperty("framesWithDisplayFlags").GetInt32());
            Assert.Equal(1, flags.GetProperty("stateOnlyDisplayFrames").GetInt32());
            Assert.Equal(2, flags.GetProperty("maxDisplayFlags").GetInt32());
            Assert.Equal(1, flags.GetProperty("displayTransitionFrames").GetInt32());
            Assert.Equal(0, flags.GetProperty("displayClearedTransitionFrames").GetInt32());
            Assert.Equal(1, flags.GetProperty("longestDisplayDurationFrames").GetInt32());
            Assert.Equal("Yellow:Yellow+Blue:Blue", flags.GetProperty("longestDisplayState").GetString());
            Assert.Equal(1, flags.GetProperty("rawFlagCounts").GetProperty("0x00000028").GetInt32());
            Assert.Equal(1, flags.GetProperty("framesWithYellowFamilyRawFlags").GetInt32());
            Assert.Equal(1, flags.GetProperty("yellowFamilyBitCounts").GetProperty("Yellow").GetInt32());
            Assert.Equal(1, flags.GetProperty("yellowFamilyStateCounts").GetProperty("Yellow").GetInt32());
            Assert.Equal(1, flags.GetProperty("rawToDisplayCounts").GetProperty("0x00000028 -> Yellow:Yellow+Blue:Blue").GetInt32());
            Assert.Equal(1, flags.GetProperty("rawToDisplayCounts").GetProperty("0x00000000 -> Finish:Checkered").GetInt32());
            Assert.Equal(1, flags.GetProperty("rawToDisplayLabelCounts").GetProperty("0x00000028 -> Yellow:Yellow:Yellow+Blue:Blue:Blue").GetInt32());
            Assert.Equal(1, flags.GetProperty("rawToDisplayLabelCounts").GetProperty("0x00000000 -> Finish:Checkered:Checkered (session complete)").GetInt32());
            Assert.Equal(1, flags.GetProperty("displayStateCounts").GetProperty("Yellow:Yellow+Blue:Blue").GetInt32());
            Assert.Equal(1, flags.GetProperty("displayStateCounts").GetProperty("Finish:Checkered").GetInt32());
            Assert.Equal(1, flags.GetProperty("displayLabelStateCounts").GetProperty("Yellow:Yellow:Yellow+Blue:Blue:Blue").GetInt32());
            Assert.Equal(1, flags.GetProperty("displayLabelStateCounts").GetProperty("Finish:Checkered:Checkered (session complete)").GetInt32());
            Assert.Equal(1, flags.GetProperty("displayTransitionCounts").GetProperty("Yellow:Yellow+Blue:Blue -> Finish:Checkered").GetInt32());
            Assert.Equal(1, flags.GetProperty("displayKindCounts").GetProperty("Yellow").GetInt32());
            Assert.Equal(1, flags.GetProperty("displayKindCounts").GetProperty("Blue").GetInt32());
            Assert.Equal(1, flags.GetProperty("displayKindCounts").GetProperty("Checkered").GetInt32());
            Assert.Equal(1, flags.GetProperty("displayCategoryCounts").GetProperty("Yellow").GetInt32());
            Assert.Equal(1, flags.GetProperty("displayCategoryCounts").GetProperty("Blue").GetInt32());
            Assert.Equal(1, flags.GetProperty("displayCategoryCounts").GetProperty("Finish").GetInt32());
            Assert.Equal(1, flags.GetProperty("displayLabelCounts").GetProperty("Yellow:Yellow:Yellow").GetInt32());
            Assert.Equal(1, flags.GetProperty("displayLabelCounts").GetProperty("Blue:Blue:Blue").GetInt32());
            Assert.Equal(1, flags.GetProperty("displayLabelCounts").GetProperty("Finish:Checkered:Checkered (session complete)").GetInt32());

            var sample = document.RootElement.GetProperty("sampleFrames").EnumerateArray().First();
            Assert.Equal("0x00000028", sample.GetProperty("sessionFlagsHex").GetString());
            Assert.Equal("Yellow + Blue", sample.GetProperty("flagStatus").GetString());
            Assert.Equal(2, sample.GetProperty("flagDisplayCount").GetInt32());
            var roleContext = sample.GetProperty("roleContext");
            Assert.Equal("driver", roleContext.GetProperty("localRole").GetString());
            Assert.Equal("DriverInfo.Drivers[].IsSpectator", roleContext.GetProperty("roleSource").GetString());
            Assert.False(roleContext.GetProperty("playerIsSpectator").GetBoolean());
            Assert.True(roleContext.TryGetProperty("isSpotting", out var isSpotting));
            Assert.Equal(JsonValueKind.Null, isSpotting.ValueKind);
            Assert.Equal("not-observed", roleContext.GetProperty("spottingSignalStatus").GetString());
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
    public void CompleteCollection_DecodesYellowFamilyFlagTypes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tmr-overlay-live-diagnostics-{Guid.NewGuid():N}");
        try
        {
            var storage = CreateStorage(root);
            var recorder = CreateRecorder(storage);
            var captureDirectory = Path.Combine(root, "capture");
            Directory.CreateDirectory(captureDirectory);
            var context = CreateContext();
            var startedAtUtc = DateTimeOffset.Parse("2026-05-02T12:00:00Z");
            recorder.StartCollection("capture-diagnostics", startedAtUtc);

            const int sessionYellowFamilyFlags = 0x00000008
                | 0x00000040
                | 0x00000100
                | 0x00000200
                | 0x00002000
                | 0x00004000
                | 0x00008000;

            recorder.RecordFrame(CreateSnapshot(
                context,
                CreateSample(
                    startedAtUtc,
                    sessionTime: 0d,
                    focusCarIdx: 10,
                    carLeftRight: 0,
                    focusF2TimeSeconds: 0d,
                    classPosition: 1,
                    observedPosition: 1,
                    observedClassPosition: 1,
                    observedLapDistPct: 0.1d,
                    sessionState: 4,
                    sessionFlags: sessionYellowFamilyFlags,
                    nearbyCars:
                    [
                        ProximityCar(11, sessionFlags: 0x00000040),
                        ProximityCar(12, sessionFlags: 0x00000200),
                        ProximityCar(13, sessionFlags: 0x00004000 | 0x00008000)
                    ]),
                sequence: 1));

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(1), captureDirectory);

            using var document = JsonDocument.Parse(File.ReadAllText(path!));
            var flags = document.RootElement.GetProperty("flags");
            Assert.Equal(1, flags.GetProperty("framesWithYellowFamilyRawFlags").GetInt32());
            Assert.Equal(1, flags.GetProperty("framesWithCarIdxYellowFamilyFlags").GetInt32());
            var bitCounts = flags.GetProperty("yellowFamilyBitCounts");
            Assert.Equal(1, bitCounts.GetProperty("Yellow").GetInt32());
            Assert.Equal(1, bitCounts.GetProperty("Debris").GetInt32());
            Assert.Equal(1, bitCounts.GetProperty("WavingYellow").GetInt32());
            Assert.Equal(1, bitCounts.GetProperty("OneToGreen").GetInt32());
            Assert.Equal(1, bitCounts.GetProperty("RandomWaving").GetInt32());
            Assert.Equal(1, bitCounts.GetProperty("Caution").GetInt32());
            Assert.Equal(1, bitCounts.GetProperty("WavingCaution").GetInt32());
            Assert.Equal(
                1,
                flags.GetProperty("yellowFamilyStateCounts")
                    .GetProperty("Yellow+Debris+WavingYellow+OneToGreen+RandomWaving+Caution+WavingCaution")
                    .GetInt32());

            var carIdxBitCounts = flags.GetProperty("carIdxYellowFamilyBitCounts");
            Assert.Equal(1, carIdxBitCounts.GetProperty("Debris").GetInt32());
            Assert.Equal(1, carIdxBitCounts.GetProperty("OneToGreen").GetInt32());
            Assert.Equal(1, carIdxBitCounts.GetProperty("Caution").GetInt32());
            Assert.Equal(1, carIdxBitCounts.GetProperty("WavingCaution").GetInt32());
            Assert.Equal(1, flags.GetProperty("carIdxYellowFamilyStateCounts").GetProperty("Debris").GetInt32());
            Assert.Equal(1, flags.GetProperty("carIdxYellowFamilyStateCounts").GetProperty("OneToGreen").GetInt32());
            Assert.Equal(1, flags.GetProperty("carIdxYellowFamilyStateCounts").GetProperty("Caution+WavingCaution").GetInt32());
            Assert.Equal(
                1,
                flags.GetProperty("rawToDisplayLabelCounts")
                    .GetProperty("0x0000E348 -> Yellow:Caution:Caution (waving)")
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
    public void CompleteCollection_SummarizesRadarOppositeSideTransitions()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-live-overlay-diagnostics-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var captureDirectory = Path.Combine(storage.CaptureRoot, "capture-diagnostics");
            Directory.CreateDirectory(captureDirectory);
            var recorder = CreateRecorder(storage);
            var context = CreateContext();
            var startedAtUtc = DateTimeOffset.Parse("2026-05-02T12:00:00Z");
            recorder.StartCollection("capture-diagnostics", startedAtUtc);

            recorder.RecordFrame(CreateSnapshot(
                context,
                CreateSample(
                    startedAtUtc,
                    sessionTime: 0d,
                    focusCarIdx: 10,
                    carLeftRight: 2,
                    focusF2TimeSeconds: 500d,
                    classPosition: 2,
                    observedPosition: 25,
                    observedClassPosition: 10,
                    observedLapDistPct: 0.50d,
                    nearbyCars: []),
                sequence: 1));
            recorder.RecordFrame(CreateSnapshot(
                context,
                CreateSample(
                    startedAtUtc.AddSeconds(0.3d),
                    sessionTime: 0.3d,
                    focusCarIdx: 10,
                    carLeftRight: 3,
                    focusF2TimeSeconds: 500.3d,
                    classPosition: 2,
                    observedPosition: 25,
                    observedClassPosition: 10,
                    observedLapDistPct: 0.51d,
                    nearbyCars: []),
                sequence: 2));

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(1), captureDirectory);

            using var document = JsonDocument.Parse(File.ReadAllText(path!));
            var radar = document.RootElement.GetProperty("radar");
            Assert.Equal(1, radar.GetProperty("sideTransitionFrames").GetInt32());
            Assert.Equal(1, radar.GetProperty("oppositeSideFlipFrames").GetInt32());
            Assert.Equal(1, radar.GetProperty("sideTransitionWithoutPlacementFrames").GetInt32());
            Assert.Equal(1, radar.GetProperty("sideTransitionCounts").GetProperty("left -> right").GetInt32());

            var eventKinds = document.RootElement
                .GetProperty("eventSamples")
                .EnumerateArray()
                .Select(item => item.GetProperty("kind").GetString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains("radar.side-transition", eventKinds);
            Assert.Contains("radar.opposite-side-flip", eventKinds);
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
    public void CompleteCollection_DoesNotTreatAllZeroLapDeltaPlaceholdersAsUsable()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-live-overlay-diagnostics-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var captureDirectory = Path.Combine(storage.CaptureRoot, "capture-diagnostics");
            Directory.CreateDirectory(captureDirectory);
            var recorder = CreateRecorder(storage);
            var context = CreateContext();
            var capturedAtUtc = DateTimeOffset.Parse("2026-05-02T12:00:00Z");
            recorder.StartCollection("capture-diagnostics", capturedAtUtc);
            var sample = CreateSample(
                capturedAtUtc,
                sessionTime: 0d,
                focusCarIdx: 10,
                carLeftRight: 1,
                focusF2TimeSeconds: 500d,
                classPosition: 3,
                observedPosition: 25,
                observedClassPosition: 10,
                observedLapDistPct: 0.5d) with
            {
                LapDeltaToBestLapSeconds = 0d,
                LapDeltaToBestLapRate = 0d,
                LapDeltaToBestLapOk = null,
                LapDeltaToOptimalLapSeconds = 0d,
                LapDeltaToOptimalLapRate = 0d,
                LapDeltaToOptimalLapOk = null,
                LapDeltaToSessionBestLapSeconds = 0d,
                LapDeltaToSessionBestLapRate = 0d,
                LapDeltaToSessionBestLapOk = null,
                LapDeltaToSessionOptimalLapSeconds = 0d,
                LapDeltaToSessionOptimalLapRate = 0d,
                LapDeltaToSessionOptimalLapOk = null,
                LapDeltaToSessionLastLapSeconds = 0d,
                LapDeltaToSessionLastLapRate = 0d,
                LapDeltaToSessionLastLapOk = null
            };

            recorder.RecordFrame(CreateSnapshot(context, sample, sequence: 1));

            var path = recorder.CompleteCollection(capturedAtUtc.AddSeconds(1), captureDirectory);

            using var document = JsonDocument.Parse(File.ReadAllText(path!));
            var lapDelta = document.RootElement.GetProperty("lapDelta");
            Assert.Equal(1, lapDelta.GetProperty("observedFrames").GetInt32());
            Assert.Equal(1, lapDelta.GetProperty("framesWithAnyValue").GetInt32());
            Assert.Equal(0, lapDelta.GetProperty("framesWithAnyUsableValue").GetInt32());
            Assert.Equal("values_present_without_usable_quality_all_zero", lapDelta.GetProperty("classification").GetString());
            Assert.True(lapDelta.GetProperty("valuesPresentWithoutUsableQuality").GetBoolean());
            Assert.True(lapDelta.GetProperty("allObservedValuesZero").GetBoolean());
            Assert.Contains("all-zero placeholders", lapDelta.GetProperty("interpretation").GetString(), StringComparison.Ordinal);
            Assert.Equal(1, lapDelta.GetProperty("valueFrameCounts").GetProperty("toBestLap").GetInt32());
            Assert.Equal(1, lapDelta.GetProperty("valueFrameCounts").GetProperty("toOptimalLap").GetInt32());
            Assert.Equal(1, lapDelta.GetProperty("valueFrameCounts").GetProperty("toSessionBestLap").GetInt32());
            Assert.Equal(1, lapDelta.GetProperty("valueFrameCounts").GetProperty("toSessionOptimalLap").GetInt32());
            Assert.Equal(1, lapDelta.GetProperty("valueFrameCounts").GetProperty("toSessionLastLap").GetInt32());
            Assert.False(lapDelta.GetProperty("usableFrameCounts").TryGetProperty("toBestLap", out _));
            Assert.False(lapDelta.GetProperty("usableFrameCounts").TryGetProperty("toOptimalLap", out _));
            Assert.False(lapDelta.GetProperty("usableFrameCounts").TryGetProperty("toSessionBestLap", out _));
            Assert.False(lapDelta.GetProperty("usableFrameCounts").TryGetProperty("toSessionOptimalLap", out _));
            Assert.False(lapDelta.GetProperty("usableFrameCounts").TryGetProperty("toSessionLastLap", out _));
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
    public void CompleteCollection_SummarizesLapProfileReadiness()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-live-overlay-diagnostics-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var captureDirectory = Path.Combine(storage.CaptureRoot, "capture-diagnostics");
            Directory.CreateDirectory(captureDirectory);
            var recorder = CreateRecorder(storage);
            var context = CreateContext();
            var capturedAtUtc = DateTimeOffset.Parse("2026-05-02T12:00:00Z");
            recorder.StartCollection("capture-diagnostics", capturedAtUtc);
            var snapshot = CreateSnapshot(
                context,
                CreateSample(
                    capturedAtUtc,
                    sessionTime: 0d,
                    focusCarIdx: 10,
                    carLeftRight: 1,
                    focusF2TimeSeconds: 500d,
                    classPosition: 3,
                    observedPosition: 25,
                    observedClassPosition: 10,
                    observedLapDistPct: 0.5d),
                sequence: 1);
            var timingRows = new[]
            {
                TimingRow(10, carClass: 12, className: "GT3", classPosition: 1, bestLap: 92.4d, lastLap: 92.4d),
                TimingRow(11, carClass: 12, className: "GT3", classPosition: 2, bestLap: 93.1d, lastLap: 93.1d),
                TimingRow(12, carClass: 12, className: "GT3", classPosition: 3, bestLap: 94.2d, lastLap: null)
            };
            var scoringRows = new[]
            {
                ScoringRow(10, carClass: 12, className: "GT3", classPosition: 1, bestLap: 92.4d, lastLap: 92.4d),
                ScoringRow(11, carClass: 12, className: "GT3", classPosition: 2, bestLap: 93.1d, lastLap: 93.1d),
                ScoringRow(12, carClass: 12, className: "GT3", classPosition: 3, bestLap: 94.2d, lastLap: null)
            };
            snapshot = snapshot with
            {
                Models = snapshot.Models with
                {
                    Timing = LiveTimingModel.Empty with
                    {
                        HasData = true,
                        Quality = LiveModelQuality.Reliable,
                        PlayerCarIdx = 10,
                        FocusCarIdx = 10,
                        OverallRows = timingRows,
                        ClassRows = timingRows
                    },
                    Scoring = LiveScoringModel.Empty with
                    {
                        HasData = true,
                        Quality = LiveModelQuality.Reliable,
                        Source = LiveScoringSource.SessionResults,
                        ReferenceCarIdx = 10,
                        ReferenceCarClass = 12,
                        Rows = scoringRows,
                        ClassGroups =
                        [
                            new LiveScoringClassGroup(
                                CarClass: 12,
                                ClassName: "GT3",
                                CarClassColorHex: "#ff0000",
                                IsReferenceClass: true,
                                RowCount: scoringRows.Length,
                                Rows: scoringRows)
                        ]
                    }
                }
            };

            recorder.RecordFrame(snapshot);

            var path = recorder.CompleteCollection(capturedAtUtc.AddSeconds(1), captureDirectory);

            using var document = JsonDocument.Parse(File.ReadAllText(path!));
            var lapProfile = document.RootElement.GetProperty("lapProfile");
            Assert.Equal(1, lapProfile.GetProperty("observedFrames").GetInt32());
            Assert.Equal(1, lapProfile.GetProperty("framesWithTimingRows").GetInt32());
            Assert.Equal(1, lapProfile.GetProperty("framesWithScoringRows").GetInt32());
            Assert.Equal(1, lapProfile.GetProperty("framesWithAnyRows").GetInt32());
            Assert.Equal(1, lapProfile.GetProperty("framesWithAnyBestLap").GetInt32());
            Assert.Equal(1, lapProfile.GetProperty("framesWithAnyLastLap").GetInt32());
            Assert.Equal(1, lapProfile.GetProperty("framesWithBestAndLastLap").GetInt32());
            Assert.Equal(1, lapProfile.GetProperty("framesWithRecentPersonalBest").GetInt32());
            Assert.Equal(1, lapProfile.GetProperty("framesWithClassFastestBestLap").GetInt32());
            Assert.Equal(1, lapProfile.GetProperty("framesWithClassFastestLastLap").GetInt32());
            Assert.Equal(3, lapProfile.GetProperty("maxRows").GetInt32());
            Assert.Equal(2, lapProfile.GetProperty("maxRowsWithBestAndLastLap").GetInt32());
            Assert.Equal(1, lapProfile.GetProperty("maxRowsWithRecentPersonalBest").GetInt32());
            Assert.Equal(2, lapProfile.GetProperty("sourceRowCounts").GetProperty("merged:best-and-last-lap").GetInt32());
            Assert.Equal(1, lapProfile.GetProperty("sourceRowCounts").GetProperty("merged:recent-personal-best").GetInt32());
            Assert.Equal(1, lapProfile.GetProperty("sourceRowCounts").GetProperty("merged:class-fastest-last-lap").GetInt32());
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
    public void CompleteCollection_SummarizesUnavailableFocusContext()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-live-overlay-diagnostics-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var captureDirectory = Path.Combine(storage.CaptureRoot, "capture-diagnostics");
            Directory.CreateDirectory(captureDirectory);
            var recorder = new LiveOverlayDiagnosticsRecorder(
                new LiveOverlayDiagnosticsOptions
                {
                    Enabled = true,
                    MinimumFrameSpacingSeconds = 0.1d,
                    MaxSampleFramesPerSession = 10,
                    MaxEventExamplesPerSession = 20
                },
                storage,
                new AppEventRecorder(storage),
                NullLogger<LiveOverlayDiagnosticsRecorder>.Instance);
            var context = CreateContext();
            var startedAtUtc = DateTimeOffset.Parse("2026-05-02T12:00:00Z");
            recorder.StartCollection("capture-diagnostics", startedAtUtc);

            recorder.RecordFrame(CreateSnapshot(
                context,
                CreateSample(
                    startedAtUtc,
                    sessionTime: 0d,
                    focusCarIdx: null,
                    carLeftRight: 0,
                    focusF2TimeSeconds: 0d,
                    classPosition: 2,
                    observedPosition: 25,
                    observedClassPosition: 10,
                    observedLapDistPct: 0.5d,
                    rawCamCarIdx: 70,
                    focusUnavailableReason: "cam_car_idx_invalid",
                    isOnTrack: false,
                    isInGarage: true,
                    isGarageVisible: true),
                sequence: 1));

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(1), captureDirectory);

            using var document = JsonDocument.Parse(File.ReadAllText(path!));
            var focus = document.RootElement.GetProperty("focus");
            Assert.Equal(1, focus.GetProperty("unavailableFrames").GetInt32());
            Assert.Equal(1, focus.GetProperty("unavailableWithPlayerCarFrames").GetInt32());
            Assert.Equal(1, focus.GetProperty("unavailableWithRawCamCarFrames").GetInt32());
            Assert.Equal(1, focus.GetProperty("unavailableOffTrackFrames").GetInt32());
            Assert.Equal(1, focus.GetProperty("unavailableGarageFrames").GetInt32());
            Assert.Equal(1, focus.GetProperty("unavailableReasonCounts").GetProperty("cam_car_idx_invalid").GetInt32());

            var eventKinds = document.RootElement
                .GetProperty("eventSamples")
                .EnumerateArray()
                .Select(item => item.GetProperty("kind").GetString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains("focus.unavailable", eventKinds);
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
    public void CompleteCollection_SummarizesScoringSourceAndCoverage()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-live-overlay-diagnostics-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var captureDirectory = Path.Combine(storage.CaptureRoot, "capture-diagnostics");
            Directory.CreateDirectory(captureDirectory);
            var recorder = new LiveOverlayDiagnosticsRecorder(
                new LiveOverlayDiagnosticsOptions
                {
                    Enabled = true,
                    MinimumFrameSpacingSeconds = 0.1d,
                    MaxSampleFramesPerSession = 10,
                    MaxEventExamplesPerSession = 20
                },
                storage,
                new AppEventRecorder(storage),
                NullLogger<LiveOverlayDiagnosticsRecorder>.Instance);
            var context = CreateRaceGridContext();
            var startedAtUtc = DateTimeOffset.Parse("2026-05-02T12:00:00Z");
            recorder.StartCollection("capture-diagnostics", startedAtUtc);

            recorder.RecordFrame(CreateSnapshot(
                context,
                CreateSample(
                    startedAtUtc,
                    sessionTime: 0d,
                    focusCarIdx: 10,
                    carLeftRight: 0,
                    focusF2TimeSeconds: 0d,
                    classPosition: 0,
                    observedPosition: 0,
                    observedClassPosition: 0,
                    observedLapDistPct: 0.02d,
                    focusLapDistPct: 0.02d,
                    focusLapCompleted: 0,
                    nearbyCars:
                    [
                        new HistoricalCarProximity(
                            CarIdx: 11,
                            LapCompleted: 0,
                            LapDistPct: 0.03d,
                            F2TimeSeconds: 0d,
                            EstimatedTimeSeconds: 0d,
                            Position: 0,
                            ClassPosition: 0,
                            CarClass: 4098,
                            TrackSurface: 3,
                            OnPitRoad: false)
                    ]),
                sequence: 1));

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(1), captureDirectory);

            using var document = JsonDocument.Parse(File.ReadAllText(path!));
            var scoring = document.RootElement.GetProperty("scoring");
            Assert.Equal(1, scoring.GetProperty("framesWithData").GetInt32());
            Assert.Equal(1, scoring.GetProperty("startingGridFrames").GetInt32());
            Assert.Equal(2, scoring.GetProperty("maxRows").GetInt32());
            Assert.Equal(1, scoring.GetProperty("maxClassGroups").GetInt32());
            Assert.Equal(2, scoring.GetProperty("maxCoverageResultRows").GetInt32());
            Assert.True(scoring.GetProperty("maxCoverageLiveTimingRows").GetInt32() >= 1);
            Assert.Equal(1, scoring.GetProperty("sourceCounts").GetProperty("StartingGrid").GetInt32());

            var sample = document.RootElement.GetProperty("sampleFrames").EnumerateArray().Single();
            Assert.Equal("StartingGrid", sample.GetProperty("scoringSource").GetString());
            Assert.Equal(2, sample.GetProperty("scoringRowCount").GetInt32());
            Assert.True(sample.GetProperty("coverageLiveTimingRowCount").GetInt32() >= 1);
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
    public void CompleteCollection_SummarizesPitServiceSignalsAcrossNonPlayerFocus()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-live-overlay-diagnostics-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var captureDirectory = Path.Combine(storage.CaptureRoot, "capture-diagnostics");
            Directory.CreateDirectory(captureDirectory);
            var recorder = new LiveOverlayDiagnosticsRecorder(
                new LiveOverlayDiagnosticsOptions
                {
                    Enabled = true,
                    MinimumFrameSpacingSeconds = 0.1d,
                    MaxSampleFramesPerSession = 10,
                    MaxEventExamplesPerSession = 20
                },
                storage,
                new AppEventRecorder(storage),
                NullLogger<LiveOverlayDiagnosticsRecorder>.Instance);
            var context = CreateContext();
            var startedAtUtc = DateTimeOffset.Parse("2026-05-02T12:00:00Z");
            recorder.StartCollection("capture-diagnostics", startedAtUtc);

            recorder.RecordFrame(CreateSnapshot(
                context,
                CreateSample(
                    startedAtUtc,
                    sessionTime: 0d,
                    focusCarIdx: 12,
                    carLeftRight: 0,
                    focusF2TimeSeconds: 700d,
                    classPosition: 2,
                    observedPosition: 25,
                    observedClassPosition: 10,
                    observedLapDistPct: 0.5d,
                    pitServiceFlags: 0x10,
                    pitServiceFuelLiters: 25d,
                    fastRepairUsed: 0,
                    teamFastRepairsUsed: 0),
                sequence: 1));
            recorder.RecordFrame(CreateSnapshot(
                context,
                CreateSample(
                    startedAtUtc.AddSeconds(1),
                    sessionTime: 1d,
                    focusCarIdx: 12,
                    carLeftRight: 0,
                    focusF2TimeSeconds: 701d,
                    classPosition: 2,
                    observedPosition: 25,
                    observedClassPosition: 10,
                    observedLapDistPct: 0.5d,
                    pitServiceFlags: 0x1f,
                    pitServiceFuelLiters: 45d,
                    fastRepairUsed: 0,
                    teamFastRepairsUsed: 0),
                sequence: 2));

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(2), captureDirectory);

            using var document = JsonDocument.Parse(File.ReadAllText(path!));
            var fuel = document.RootElement.GetProperty("fuel");
            Assert.Equal(2, fuel.GetProperty("pitServiceSignalFrames").GetInt32());
            Assert.Equal(2, fuel.GetProperty("pitServiceRequestFrames").GetInt32());
            Assert.Equal(1, fuel.GetProperty("pitServiceChangeFrames").GetInt32());
            Assert.Equal(2, fuel.GetProperty("pitServiceNonPlayerFocusFrames").GetInt32());
            Assert.Equal(2, fuel.GetProperty("fuelLocalStrategyUnavailableFrames").GetInt32());
            Assert.Equal(2, fuel.GetProperty("pitServiceLocalStrategyUnavailableFrames").GetInt32());
            Assert.Equal(2, fuel.GetProperty("fuelLocalStrategyUnavailableReasonCounts").GetProperty("focus_on_another_car").GetInt32());
            Assert.Equal(2, fuel.GetProperty("pitServiceLocalStrategyUnavailableReasonCounts").GetProperty("focus_on_another_car").GetInt32());

            var eventKinds = document.RootElement
                .GetProperty("eventSamples")
                .EnumerateArray()
                .Select(item => item.GetProperty("kind").GetString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains("pit-service.changed", eventKinds);
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
    public void CompleteCollection_TreatsCarLeftRightOneAsClearNotSideSignal()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-live-overlay-diagnostics-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var captureDirectory = Path.Combine(storage.CaptureRoot, "capture-diagnostics");
            Directory.CreateDirectory(captureDirectory);
            var recorder = CreateRecorder(storage);
            var context = CreateContext();
            var capturedAtUtc = DateTimeOffset.Parse("2026-05-02T12:00:00Z");
            recorder.StartCollection("capture-diagnostics", capturedAtUtc);

            recorder.RecordFrame(CreateSnapshot(
                context,
                CreateSample(
                    capturedAtUtc,
                    sessionTime: 0d,
                    focusCarIdx: 10,
                    carLeftRight: 1,
                    focusF2TimeSeconds: 500d,
                    classPosition: 3,
                    observedPosition: 25,
                    observedClassPosition: 10,
                    observedLapDistPct: 0.5d),
                sequence: 1));

            var path = recorder.CompleteCollection(capturedAtUtc.AddSeconds(1), captureDirectory);

            using var document = JsonDocument.Parse(File.ReadAllText(path!));
            var radar = document.RootElement.GetProperty("radar");
            Assert.Equal(0, radar.GetProperty("sideSignalFrames").GetInt32());
            Assert.Equal(0, radar.GetProperty("sideSignalWithoutPlacementFrames").GetInt32());
            Assert.Equal(1, radar.GetProperty("sideStateCounts").GetProperty("clear").GetInt32());

            var sample = document.RootElement.GetProperty("sampleFrames").EnumerateArray().Single();
            Assert.Equal(1, sample.GetProperty("rawCarLeftRight").GetInt32());
            Assert.Equal("clear", sample.GetProperty("sideStatus").GetString());
            Assert.False(sample.GetProperty("hasSideSignal").GetBoolean());
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
    public void CompleteCollection_CapturesPitWindowFuelBlackFlagAndRawControlEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-live-overlay-diagnostics-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var captureDirectory = Path.Combine(storage.CaptureRoot, "capture-diagnostics");
            Directory.CreateDirectory(captureDirectory);
            var recorder = CreateRecorder(storage);
            var context = CreateContext();
            var startedAtUtc = DateTimeOffset.Parse("2026-05-02T12:00:00Z");
            recorder.StartCollection("capture-diagnostics", startedAtUtc);

            recorder.RecordFrame(
                CreateSnapshot(
                    context,
                    CreateSample(
                        startedAtUtc,
                        sessionTime: 0d,
                        focusCarIdx: 10,
                        carLeftRight: 1,
                        focusF2TimeSeconds: 500d,
                        classPosition: 3,
                        observedPosition: 25,
                        observedClassPosition: 10,
                        observedLapDistPct: 0.5d,
                        fuelLevelLiters: 20d,
                        onPitRoad: true,
                        playerCarInPitStall: true,
                        pitServiceStatus: 1,
                        pitServiceFlags: 0x10,
                        pitServiceFuelLiters: 20d,
                        sessionFlags: 0x00010000),
                    sequence: 1),
                new RawTelemetryWatchSnapshot(new Dictionary<string, double>
                {
                    ["dcFrontARB"] = 1d,
                    ["dpFuelFill"] = 0d,
                    ["PitSvFuel"] = 20d
                }));
            recorder.RecordFrame(
                CreateSnapshot(
                    context,
                    CreateSample(
                        startedAtUtc.AddSeconds(1),
                        sessionTime: 1d,
                        focusCarIdx: 10,
                        carLeftRight: 1,
                        focusF2TimeSeconds: 501d,
                        classPosition: 3,
                        observedPosition: 25,
                        observedClassPosition: 10,
                        observedLapDistPct: 0.5d,
                        fuelLevelLiters: 45d,
                        onPitRoad: true,
                        playerCarInPitStall: true,
                        pitServiceStatus: 2,
                        pitServiceFlags: 0x1f,
                        pitServiceFuelLiters: 45d,
                        sessionFlags: 0x00010000),
                    sequence: 2),
                new RawTelemetryWatchSnapshot(new Dictionary<string, double>
                {
                    ["dcFrontARB"] = 2d,
                    ["dpFuelFill"] = 1d,
                    ["PitSvFuel"] = 45d
                }));

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(2), captureDirectory);

            using var document = JsonDocument.Parse(File.ReadAllText(path!));
            var fuel = document.RootElement.GetProperty("fuel");
            Assert.Equal(1, fuel.GetProperty("pitWindowCount").GetInt32());
            Assert.Equal(1, fuel.GetProperty("pitWindowsWithFuelIncrease").GetInt32());
            Assert.Equal(1, fuel.GetProperty("pitWindowsWithBlackFlag").GetInt32());
            Assert.Equal(1, fuel.GetProperty("fuelIncreaseEventFrames").GetInt32());
            Assert.Equal(1, fuel.GetProperty("pitServiceChangeFrames").GetInt32());
            var pitWindow = fuel.GetProperty("pitWindows").EnumerateArray().Single();
            Assert.True(pitWindow.GetProperty("sawFuelIncrease").GetBoolean());
            Assert.True(pitWindow.GetProperty("sawBlackFlag").GetBoolean());
            Assert.True(pitWindow.GetProperty("sawPlayerPitStall").GetBoolean());
            Assert.True(pitWindow.GetProperty("sawPitServiceChange").GetBoolean());
            Assert.Equal(25d, pitWindow.GetProperty("netFuelDeltaLiters").GetDouble(), 3);
            Assert.Equal("0x00010000", pitWindow.GetProperty("entrySessionFlagsHex").GetString());

            var raw = document.RootElement.GetProperty("rawTelemetry");
            Assert.Equal(2, raw.GetProperty("driverControlSignalFrames").GetInt32());
            Assert.Equal(1, raw.GetProperty("driverControlChangeFrames").GetInt32());
            Assert.Equal(2, raw.GetProperty("pitCommandSignalFrames").GetInt32());
            Assert.Equal(1, raw.GetProperty("pitCommandChangeFrames").GetInt32());
            Assert.Equal(2, raw.GetProperty("driverControlFieldCounts").GetProperty("dcFrontARB").GetInt32());
            Assert.Equal(1, raw.GetProperty("driverControlChangeCounts").GetProperty("dcFrontARB").GetInt32());
            Assert.Equal(2, raw.GetProperty("pitCommandFieldCounts").GetProperty("dpFuelFill").GetInt32());
            Assert.Equal(1, raw.GetProperty("pitCommandChangeCounts").GetProperty("dpFuelFill").GetInt32());
            Assert.Equal(1, raw.GetProperty("pitCommandChangeCounts").GetProperty("PitSvFuel").GetInt32());

            var eventKinds = document.RootElement
                .GetProperty("eventSamples")
                .EnumerateArray()
                .Select(item => item.GetProperty("kind").GetString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains("pit-window.fuel-increase", eventKinds);
            Assert.Contains("pit-service.changed", eventKinds);
            Assert.Contains("raw.driver-control.changed", eventKinds);
            Assert.Contains("raw.pit-command.changed", eventKinds);
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
    public void CompleteCollection_DetectsPitWindowFuelIncreaseFromNetFuelDelta()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-live-overlay-diagnostics-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var captureDirectory = Path.Combine(storage.CaptureRoot, "capture-diagnostics");
            Directory.CreateDirectory(captureDirectory);
            var recorder = CreateRecorder(storage);
            var context = CreateContext();
            var startedAtUtc = DateTimeOffset.Parse("2026-05-02T12:00:00Z");
            recorder.StartCollection("capture-diagnostics", startedAtUtc);

            foreach (var (offsetSeconds, fuelLevelLiters) in new[] { (0d, 5.0d), (1d, 5.2d), (2d, 5.4d), (3d, 5.6d) })
            {
                recorder.RecordFrame(
                    CreateSnapshot(
                        context,
                        CreateSample(
                            startedAtUtc.AddSeconds(offsetSeconds),
                            sessionTime: offsetSeconds,
                            focusCarIdx: 10,
                            carLeftRight: 1,
                            focusF2TimeSeconds: 500d + offsetSeconds,
                            classPosition: 3,
                            observedPosition: 25,
                            observedClassPosition: 10,
                            observedLapDistPct: 0.5d,
                            fuelLevelLiters: fuelLevelLiters,
                            onPitRoad: true,
                            playerCarInPitStall: true,
                            pitServiceStatus: 1,
                            pitServiceFlags: 0x10,
                            pitServiceFuelLiters: 30d),
                        sequence: (long)offsetSeconds + 1));
            }

            recorder.RecordFrame(
                CreateSnapshot(
                    context,
                    CreateSample(
                        startedAtUtc.AddSeconds(4),
                        sessionTime: 4d,
                        focusCarIdx: 10,
                        carLeftRight: 1,
                        focusF2TimeSeconds: 504d,
                        classPosition: 3,
                        observedPosition: 25,
                        observedClassPosition: 10,
                        observedLapDistPct: 0.5d,
                        fuelLevelLiters: 5.6d),
                    sequence: 5));

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(5), captureDirectory);

            using var document = JsonDocument.Parse(File.ReadAllText(path!));
            var fuel = document.RootElement.GetProperty("fuel");
            Assert.Equal(1, fuel.GetProperty("pitWindowCount").GetInt32());
            Assert.Equal(1, fuel.GetProperty("pitWindowsWithFuelIncrease").GetInt32());
            Assert.Equal(1, fuel.GetProperty("fuelIncreaseEventFrames").GetInt32());
            var pitWindow = fuel.GetProperty("pitWindows").EnumerateArray().Single();
            Assert.True(pitWindow.GetProperty("sawFuelIncrease").GetBoolean());
            Assert.Equal(0.6d, pitWindow.GetProperty("netFuelDeltaLiters").GetDouble(), 3);
            Assert.Equal(0.6d, pitWindow.GetProperty("maxFuelIncreaseLiters").GetDouble(), 3);
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
    public void CompleteCollection_FlagsNonRaceRaceProjectionWhenModelStillContainsIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-live-overlay-diagnostics-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var captureDirectory = Path.Combine(storage.CaptureRoot, "capture-diagnostics");
            Directory.CreateDirectory(captureDirectory);
            var recorder = CreateRecorder(storage);
            var context = CreateContext();
            var capturedAtUtc = DateTimeOffset.Parse("2026-05-02T12:00:00Z");
            recorder.StartCollection("capture-diagnostics", capturedAtUtc);
            var snapshot = CreateSnapshot(
                context,
                CreateSample(
                    capturedAtUtc,
                    sessionTime: 0d,
                    focusCarIdx: 10,
                    carLeftRight: 1,
                    focusF2TimeSeconds: 500d,
                    classPosition: 3,
                    observedPosition: 25,
                    observedClassPosition: 10,
                    observedLapDistPct: 0.5d),
                sequence: 1);
            snapshot = snapshot with
            {
                Models = snapshot.Models with
                {
                    Session = snapshot.Models.Session with
                    {
                        RaceLaps = 12
                    },
                    RaceProgress = snapshot.Models.RaceProgress with
                    {
                        RaceLapsRemaining = 12d,
                        RaceLapsRemainingSource = "test-non-race-signal"
                    },
                    RaceProjection = LiveRaceProjectionModel.Empty with
                    {
                        HasData = true,
                        Quality = LiveModelQuality.Partial,
                        EstimatedTeamLapsRemaining = 12d,
                        EstimatedTeamLapsRemainingSource = "test-non-race-projection"
                    }
                }
            };

            recorder.RecordFrame(snapshot);

            var path = recorder.CompleteCollection(capturedAtUtc.AddSeconds(1), captureDirectory);

            using var document = JsonDocument.Parse(File.ReadAllText(path!));
            var raceProjection = document.RootElement.GetProperty("raceProjection");
            Assert.Equal(1, raceProjection.GetProperty("nonRaceRaceProjectionFrames").GetInt32());
            Assert.Equal(1, raceProjection.GetProperty("nonRaceRaceLapSignalFrames").GetInt32());
            Assert.Equal(1, raceProjection.GetProperty("sourceCounts").GetProperty("test-non-race-signal").GetInt32());
            Assert.Equal(1, raceProjection.GetProperty("sourceCounts").GetProperty("test-non-race-projection").GetInt32());

            var eventKinds = document.RootElement
                .GetProperty("eventSamples")
                .EnumerateArray()
                .Select(item => item.GetProperty("kind").GetString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains("race-projection.non-race-race-laps-signal", eventKinds);
            Assert.Contains("race-projection.non-race-projection", eventKinds);
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
    public void CompleteCollection_SummarizesRelativeLapRelationshipProbe()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-live-overlay-diagnostics-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var captureDirectory = Path.Combine(storage.CaptureRoot, "capture-diagnostics");
            Directory.CreateDirectory(captureDirectory);
            var recorder = new LiveOverlayDiagnosticsRecorder(
                new LiveOverlayDiagnosticsOptions
                {
                    Enabled = true,
                    MinimumFrameSpacingSeconds = 0.1d,
                    MaxSampleFramesPerSession = 10,
                    MaxEventExamplesPerSession = 20,
                    MaxEventExamplesPerKind = 10
                },
                storage,
                new AppEventRecorder(storage),
                NullLogger<LiveOverlayDiagnosticsRecorder>.Instance);
            var context = CreateContext();
            var startedAtUtc = DateTimeOffset.Parse("2026-05-02T12:00:00Z");
            recorder.StartCollection("capture-diagnostics", startedAtUtc);

            recorder.RecordFrame(CreateSnapshot(
                context,
                CreateSample(
                    startedAtUtc,
                    sessionTime: 0d,
                    focusCarIdx: 10,
                    carLeftRight: 0,
                    focusF2TimeSeconds: 500d,
                    classPosition: 3,
                    observedPosition: 25,
                    observedClassPosition: 10,
                    observedLapDistPct: 0.86d,
                    focusLapDistPct: 0.5d,
                    nearbyCars:
                    [
                        new HistoricalCarProximity(
                            CarIdx: 44,
                            LapCompleted: 109,
                            LapDistPct: 0.60d,
                            F2TimeSeconds: 420d,
                            EstimatedTimeSeconds: 420d,
                            Position: 5,
                            ClassPosition: 2,
                            CarClass: 4098,
                            TrackSurface: 3,
                            OnPitRoad: false),
                        new HistoricalCarProximity(
                            CarIdx: 45,
                            LapCompleted: 106,
                            LapDistPct: 0.40d,
                            F2TimeSeconds: 650d,
                            EstimatedTimeSeconds: 650d,
                            Position: 40,
                            ClassPosition: 16,
                            CarClass: 4098,
                            TrackSurface: 1,
                            OnPitRoad: true),
                        new HistoricalCarProximity(
                            CarIdx: 46,
                            LapCompleted: 108,
                            LapDistPct: 0.86d,
                            F2TimeSeconds: 520d,
                            EstimatedTimeSeconds: 520d,
                            Position: 18,
                            ClassPosition: 8,
                            CarClass: 4098,
                            TrackSurface: 3,
                            OnPitRoad: false),
                        new HistoricalCarProximity(
                            CarIdx: 47,
                            LapCompleted: 108,
                            LapDistPct: 0.14d,
                            F2TimeSeconds: 540d,
                            EstimatedTimeSeconds: 540d,
                            Position: 19,
                            ClassPosition: 9,
                            CarClass: 4098,
                            TrackSurface: 3,
                            OnPitRoad: false)
                    ]),
                sequence: 1));

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(1), captureDirectory);

            using var document = JsonDocument.Parse(File.ReadAllText(path!));
            var summary = document.RootElement.GetProperty("relativeLapRelationship");
            Assert.Equal(1, summary.GetProperty("framesWithReferenceProgress").GetInt32());
            Assert.Equal(1, summary.GetProperty("framesWithNearbyCars").GetInt32());
            Assert.Equal(1, summary.GetProperty("framesWithOfficialLapDelta").GetInt32());
            Assert.Equal(1, summary.GetProperty("framesWithPendingRelationship").GetInt32());
            Assert.Equal(4, summary.GetProperty("maxNearbyCars").GetInt32());
            Assert.Equal(1, summary.GetProperty("officialRelationshipCounts").GetProperty("one-lap-ahead").GetInt32());
            Assert.Equal(1, summary.GetProperty("officialRelationshipCounts").GetProperty("two-plus-laps-behind").GetInt32());
            Assert.Equal(1, summary.GetProperty("pitRelationshipCounts").GetProperty("two-plus-laps-behind").GetInt32());
            Assert.Equal(1, summary.GetProperty("pendingRelationshipCounts").GetProperty("same-lap-car-ahead-near-lapping-reference").GetInt32());
            Assert.Equal(1, summary.GetProperty("pendingRelationshipCounts").GetProperty("same-lap-reference-catching-car-to-lap").GetInt32());

            var eventKinds = document.RootElement
                .GetProperty("eventSamples")
                .EnumerateArray()
                .Select(item => item.GetProperty("kind").GetString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains("relative-lap.same-lap-car-ahead-near-lapping-reference", eventKinds);
            Assert.Contains("relative-lap.same-lap-reference-catching-car-to-lap", eventKinds);
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
    public void CompleteCollection_SummarizesTrackMapSectorHighlights()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-live-overlay-diagnostics-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var captureDirectory = Path.Combine(storage.CaptureRoot, "capture-diagnostics");
            Directory.CreateDirectory(captureDirectory);
            var recorder = new LiveOverlayDiagnosticsRecorder(
                new LiveOverlayDiagnosticsOptions
                {
                    Enabled = true,
                    MinimumFrameSpacingSeconds = 0.1d,
                    MaxSampleFramesPerSession = 10,
                    MaxEventExamplesPerSession = 20
                },
                storage,
                new AppEventRecorder(storage),
                NullLogger<LiveOverlayDiagnosticsRecorder>.Instance);
            var context = CreateContext();
            var startedAtUtc = DateTimeOffset.Parse("2026-05-02T12:00:00Z");
            recorder.StartCollection("capture-diagnostics", startedAtUtc);

            var trackMap = new LiveTrackMapModel(
                HasSectors: true,
                HasLiveTiming: true,
                Quality: LiveModelQuality.Reliable,
                Sectors:
                [
                    new LiveTrackSectorSegment(0, 0d, 0.5d, LiveTrackSectorHighlights.BestLap),
                    new LiveTrackSectorSegment(1, 0.5d, 0.75d, LiveTrackSectorHighlights.BestLap),
                    new LiveTrackSectorSegment(2, 0.75d, 1d, LiveTrackSectorHighlights.BestLap)
                ]);
            recorder.RecordFrame(CreateSnapshot(
                context,
                CreateSample(
                    startedAtUtc,
                    sessionTime: 0d,
                    focusCarIdx: 10,
                    carLeftRight: 1,
                    focusF2TimeSeconds: 700d,
                    classPosition: 2,
                    observedPosition: 25,
                    observedClassPosition: 10,
                    observedLapDistPct: 0.1d),
                sequence: 1,
                trackMap: trackMap));

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(1), captureDirectory);

            using var document = JsonDocument.Parse(File.ReadAllText(path!));
            var trackMapElement = document.RootElement.GetProperty("trackMap");
            Assert.Equal(1, trackMapElement.GetProperty("framesWithLiveTiming").GetInt32());
            Assert.Equal(1, trackMapElement.GetProperty("bestLapSectorFrames").GetInt32());
            Assert.Equal(1, trackMapElement.GetProperty("fullLapHighlightFrames").GetInt32());
            Assert.Equal(3, trackMapElement.GetProperty("sectorHighlightCounts").GetProperty("best-lap").GetInt32());
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
    public void CompleteCollection_DerivesSectorIntervalsFromFocusProgress()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-live-overlay-diagnostics-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var captureDirectory = Path.Combine(storage.CaptureRoot, "capture-diagnostics");
            Directory.CreateDirectory(captureDirectory);
            var recorder = new LiveOverlayDiagnosticsRecorder(
                new LiveOverlayDiagnosticsOptions
                {
                    Enabled = true,
                    MinimumFrameSpacingSeconds = 0.1d,
                    MaxSampleFramesPerSession = 10,
                    MaxEventExamplesPerSession = 20,
                    LargeGapSeconds = 600d,
                    GapJumpSeconds = 300d
                },
                storage,
                new AppEventRecorder(storage),
                NullLogger<LiveOverlayDiagnosticsRecorder>.Instance);
            var context = CreateContext();
            var startedAtUtc = DateTimeOffset.Parse("2026-05-02T12:00:00Z");
            recorder.StartCollection("capture-diagnostics", startedAtUtc);

            var progress = new[] { 0.49d, 0.51d, 0.61d, 0.70d, 0.76d };
            for (var index = 0; index < progress.Length; index++)
            {
                recorder.RecordFrame(CreateSnapshot(
                    context,
                    CreateSample(
                        startedAtUtc.AddSeconds(index),
                        sessionTime: index,
                        focusCarIdx: 10,
                        carLeftRight: 1,
                        focusF2TimeSeconds: 700d + index,
                        classPosition: 2,
                        observedPosition: 25,
                        observedClassPosition: 10,
                        observedLapDistPct: 0.1d,
                        focusLapDistPct: progress[index]),
                    sequence: index + 1));
            }

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(3), captureDirectory);

            using var document = JsonDocument.Parse(File.ReadAllText(path!));
            var sectorTiming = document.RootElement.GetProperty("sectorTiming");
            Assert.Equal(3, sectorTiming.GetProperty("sectorCount").GetInt32());
            Assert.Equal(progress.Length, sectorTiming.GetProperty("metadataFrames").GetInt32());
            Assert.Equal(progress.Length, sectorTiming.GetProperty("focusTrackedFrames").GetInt32());
            Assert.Equal(2, sectorTiming.GetProperty("crossingCount").GetInt32());
            Assert.Equal(1, sectorTiming.GetProperty("completedIntervalCount").GetInt32());

            var eventKinds = document.RootElement
                .GetProperty("eventSamples")
                .EnumerateArray()
                .Select(item => item.GetProperty("kind").GetString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains("sector.interval-derived", eventKinds);
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
    public void CompleteCollection_DerivesSectorIntervalsWhenLapCountersAreInvalid()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-live-overlay-diagnostics-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var captureDirectory = Path.Combine(storage.CaptureRoot, "capture-diagnostics");
            Directory.CreateDirectory(captureDirectory);
            var recorder = new LiveOverlayDiagnosticsRecorder(
                new LiveOverlayDiagnosticsOptions
                {
                    Enabled = true,
                    MinimumFrameSpacingSeconds = 0.1d,
                    MaxSampleFramesPerSession = 10,
                    MaxEventExamplesPerSession = 20
                },
                storage,
                new AppEventRecorder(storage),
                NullLogger<LiveOverlayDiagnosticsRecorder>.Instance);
            var context = CreateContext();
            var startedAtUtc = DateTimeOffset.Parse("2026-05-02T12:00:00Z");
            recorder.StartCollection("capture-diagnostics", startedAtUtc);

            var progress = new[] { 0.99d, 0.01d, 0.10d, 0.20d, 0.30d, 0.40d, 0.51d };
            for (var index = 0; index < progress.Length; index++)
            {
                recorder.RecordFrame(CreateSnapshot(
                    context,
                    CreateSample(
                        startedAtUtc.AddSeconds(index),
                        sessionTime: index,
                        focusCarIdx: 10,
                        carLeftRight: 1,
                        focusF2TimeSeconds: 700d + index,
                        classPosition: 2,
                        observedPosition: 25,
                        observedClassPosition: 10,
                        observedLapDistPct: 0.1d,
                        focusLapDistPct: progress[index],
                        focusLapCompleted: -1),
                    sequence: index + 1));
            }

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(8), captureDirectory);

            using var document = JsonDocument.Parse(File.ReadAllText(path!));
            var sectorTiming = document.RootElement.GetProperty("sectorTiming");
            Assert.Equal(progress.Length, sectorTiming.GetProperty("lapCounterUnavailableFrames").GetInt32());
            Assert.Equal(1, sectorTiming.GetProperty("syntheticLapWrapFrames").GetInt32());
            Assert.True(sectorTiming.GetProperty("crossingCount").GetInt32() >= 2);
            Assert.True(sectorTiming.GetProperty("completedIntervalCount").GetInt32() >= 1);

            var eventKinds = document.RootElement
                .GetProperty("eventSamples")
                .EnumerateArray()
                .Select(item => item.GetProperty("kind").GetString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains("sector.interval-derived", eventKinds);
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
    public void CompleteCollection_DeduplicatesAndCapsEventSamplesByKind()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-live-overlay-diagnostics-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var captureDirectory = Path.Combine(storage.CaptureRoot, "capture-diagnostics");
            Directory.CreateDirectory(captureDirectory);
            var recorder = new LiveOverlayDiagnosticsRecorder(
                new LiveOverlayDiagnosticsOptions
                {
                    Enabled = true,
                    MinimumFrameSpacingSeconds = 0.1d,
                    MaxSampleFramesPerSession = 10,
                    MaxEventExamplesPerSession = 20,
                    MaxEventExamplesPerKind = 2,
                    LargeGapSeconds = 600d,
                    GapJumpSeconds = 300d
                },
                storage,
                new AppEventRecorder(storage),
                NullLogger<LiveOverlayDiagnosticsRecorder>.Instance);
            var context = CreateContext();
            var startedAtUtc = DateTimeOffset.Parse("2026-05-02T12:00:00Z");
            recorder.StartCollection("capture-diagnostics", startedAtUtc);

            for (var index = 0; index < 5; index++)
            {
                recorder.RecordFrame(CreateSnapshot(
                    context,
                    CreateSample(
                        startedAtUtc.AddSeconds(index),
                        sessionTime: index,
                        focusCarIdx: 10,
                        carLeftRight: 1,
                        focusF2TimeSeconds: 700d + index,
                        classPosition: 2,
                        observedPosition: 25,
                        observedClassPosition: 10,
                        observedLapDistPct: 0.9748d),
                    sequence: index + 1));
            }

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(6), captureDirectory);

            using var document = JsonDocument.Parse(File.ReadAllText(path!));
            var rootElement = document.RootElement;
            Assert.Equal(2, rootElement.GetProperty("options").GetProperty("maxEventExamplesPerKind").GetInt32());
            Assert.True(rootElement.GetProperty("totals").GetProperty("droppedEventSampleCount").GetInt32() > 0);

            var eventKinds = rootElement
                .GetProperty("eventSamples")
                .EnumerateArray()
                .Select(item => item.GetProperty("kind").GetString())
                .ToArray();
            Assert.Equal(2, eventKinds.Count(kind => string.Equals(kind, "gap.large-seconds", StringComparison.OrdinalIgnoreCase)));
            Assert.Contains(eventKinds, kind => string.Equals(kind, "fuel.instantaneous-without-level", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static LiveTelemetrySnapshot CreateSnapshot(
        HistoricalSessionContext context,
        HistoricalTelemetrySample sample,
        long sequence,
        LiveTrackMapModel? trackMap = null)
    {
        var fuel = LiveFuelSnapshot.From(context, sample);
        var proximity = LiveProximitySnapshot.From(context, sample);
        var leaderGap = LiveLeaderGapSnapshot.From(sample);
        return new LiveTelemetrySnapshot(
            IsConnected: true,
            IsCollecting: true,
            SourceId: "capture-diagnostics",
            StartedAtUtc: DateTimeOffset.Parse("2026-05-02T12:00:00Z"),
            LastUpdatedAtUtc: sample.CapturedAtUtc,
            Sequence: sequence,
            Context: context,
            Combo: HistoricalComboIdentity.From(context),
            LatestSample: sample,
            Fuel: fuel,
            Proximity: proximity,
            LeaderGap: leaderGap)
        {
            Models = LiveRaceModelBuilder.From(context, sample, fuel, proximity, leaderGap, trackMap)
        };
    }

    private static LiveOverlayDiagnosticsRecorder CreateRecorder(AppStorageOptions storage)
    {
        return new LiveOverlayDiagnosticsRecorder(
            new LiveOverlayDiagnosticsOptions
            {
                Enabled = true,
                MinimumFrameSpacingSeconds = 0.1d,
                MaxSampleFramesPerSession = 10,
                MaxEventExamplesPerSession = 40,
                MaxEventExamplesPerKind = 10
            },
            storage,
            new AppEventRecorder(storage),
            NullLogger<LiveOverlayDiagnosticsRecorder>.Instance);
    }

    private static HistoricalTelemetrySample CreateSample(
        DateTimeOffset capturedAtUtc,
        double sessionTime,
        int? focusCarIdx,
        int carLeftRight,
        double focusF2TimeSeconds,
        int classPosition,
        int observedPosition,
        int observedClassPosition,
        double observedLapDistPct,
        double focusLapDistPct = 0.5d,
        int focusLapCompleted = 108,
        IReadOnlyList<HistoricalCarProximity>? nearbyCars = null,
        int? rawCamCarIdx = null,
        string? focusUnavailableReason = null,
        bool isOnTrack = true,
        bool isInGarage = false,
        bool? isGarageVisible = null,
        double fuelLevelLiters = 0d,
        bool onPitRoad = false,
        bool playerCarInPitStall = false,
        int? pitServiceStatus = null,
        int? pitServiceFlags = null,
        double? pitServiceFuelLiters = null,
        double? pitRepairLeftSeconds = null,
        double? pitOptRepairLeftSeconds = null,
        int? fastRepairUsed = null,
        int? teamFastRepairsUsed = null,
        int? sessionState = null,
        int? sessionFlags = null)
    {
        return new HistoricalTelemetrySample(
            CapturedAtUtc: capturedAtUtc,
            SessionTime: sessionTime,
            SessionTick: (int)sessionTime + 1,
            SessionInfoUpdate: 1,
            IsOnTrack: isOnTrack,
            IsInGarage: isInGarage,
            OnPitRoad: onPitRoad,
            PitstopActive: false,
            PlayerCarInPitStall: playerCarInPitStall,
            FuelLevelLiters: fuelLevelLiters,
            FuelLevelPercent: 0d,
            FuelUsePerHourKg: 60d,
            SpeedMetersPerSecond: 50d,
            Lap: focusLapCompleted + 1,
            LapCompleted: focusLapCompleted,
            LapDistPct: focusLapDistPct,
            LapLastLapTimeSeconds: 500d,
            LapBestLapTimeSeconds: 490d,
            AirTempC: 20d,
            TrackTempCrewC: 24d,
            TrackWetness: 1,
            WeatherDeclaredWet: false,
            PlayerTireCompound: 0,
            IsGarageVisible: isGarageVisible,
            SessionState: sessionState,
            SessionFlags: sessionFlags,
            PlayerCarIdx: 10,
            RawCamCarIdx: rawCamCarIdx,
            FocusCarIdx: focusCarIdx,
            FocusUnavailableReason: focusUnavailableReason,
            FocusLapCompleted: focusLapCompleted,
            FocusLapDistPct: focusLapDistPct,
            FocusF2TimeSeconds: focusF2TimeSeconds,
            FocusEstimatedTimeSeconds: focusF2TimeSeconds,
            FocusLastLapTimeSeconds: 500d,
            FocusBestLapTimeSeconds: 490d,
            FocusPosition: observedPosition,
            FocusClassPosition: classPosition,
            FocusCarClass: 4098,
            FocusOnPitRoad: false,
            TeamLapCompleted: focusLapCompleted,
            TeamLapDistPct: focusLapDistPct,
            TeamF2TimeSeconds: focusF2TimeSeconds,
            TeamEstimatedTimeSeconds: focusF2TimeSeconds,
            TeamPosition: observedPosition,
            TeamClassPosition: classPosition,
            TeamCarClass: 4098,
            LeaderCarIdx: 11,
            LeaderLapCompleted: 109,
            LeaderLapDistPct: 0.1d,
            LeaderF2TimeSeconds: 0d,
            ClassLeaderCarIdx: 11,
            ClassLeaderLapCompleted: 109,
            ClassLeaderLapDistPct: 0.1d,
            ClassLeaderF2TimeSeconds: 0d,
            FocusClassLeaderCarIdx: 11,
            FocusClassLeaderLapCompleted: 109,
            FocusClassLeaderLapDistPct: 0.1d,
            FocusClassLeaderF2TimeSeconds: 0d,
            CarLeftRight: carLeftRight,
            ClassCars:
            [
                new HistoricalCarProximity(
                    CarIdx: 43,
                    LapCompleted: 108,
                    LapDistPct: observedLapDistPct,
                    F2TimeSeconds: 900d,
                    EstimatedTimeSeconds: 900d,
                    Position: observedPosition,
                    ClassPosition: observedClassPosition,
                    CarClass: 4098,
                    TrackSurface: 3,
                    OnPitRoad: false)
            ],
            NearbyCars: nearbyCars,
            TeamOnPitRoad: false,
            PitServiceStatus: pitServiceStatus,
            PitServiceFlags: pitServiceFlags,
            PitServiceFuelLiters: pitServiceFuelLiters,
            PitRepairLeftSeconds: pitRepairLeftSeconds,
            PitOptRepairLeftSeconds: pitOptRepairLeftSeconds,
            FastRepairUsed: fastRepairUsed,
            TeamFastRepairsUsed: teamFastRepairsUsed,
            DriversSoFar: 1,
            LapDeltaToBestLapSeconds: -0.2d,
            LapDeltaToBestLapRate: 0.01d,
            LapDeltaToBestLapOk: true);
    }

    private static HistoricalCarProximity ProximityCar(int carIdx, int? sessionFlags = null)
    {
        return new HistoricalCarProximity(
            CarIdx: carIdx,
            LapCompleted: 108,
            LapDistPct: 0.5d,
            F2TimeSeconds: 900d,
            EstimatedTimeSeconds: 900d,
            Position: carIdx,
            ClassPosition: carIdx,
            CarClass: 4098,
            TrackSurface: 3,
            OnPitRoad: false,
            SessionFlags: sessionFlags);
    }

    private static LiveTimingRow TimingRow(
        int carIdx,
        int? carClass,
        string? className,
        int? classPosition,
        double? bestLap,
        double? lastLap)
    {
        var evidence = LiveSignalEvidence.Reliable("unit-test");
        return new LiveTimingRow(
            CarIdx: carIdx,
            Quality: LiveModelQuality.Reliable,
            Source: "unit-test",
            IsPlayer: carIdx == 10,
            IsFocus: carIdx == 10,
            IsOverallLeader: classPosition == 1,
            IsClassLeader: classPosition == 1,
            HasTiming: true,
            HasSpatialProgress: true,
            CanUseForRadarPlacement: true,
            TimingEvidence: evidence,
            SpatialEvidence: evidence,
            RadarPlacementEvidence: evidence,
            GapEvidence: evidence,
            DriverName: $"Driver {carIdx}",
            TeamName: null,
            CarNumber: carIdx.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CarClassName: className,
            CarClassColorHex: "#ff0000",
            OverallPosition: classPosition,
            ClassPosition: classPosition,
            CarClass: carClass,
            LapCompleted: 12,
            LapDistPct: 0.5d,
            ProgressLaps: 12.5d,
            F2TimeSeconds: 100d + carIdx,
            EstimatedTimeSeconds: 100d + carIdx,
            LastLapTimeSeconds: lastLap,
            BestLapTimeSeconds: bestLap,
            GapSecondsToClassLeader: classPosition is { } position && position > 1 ? (double)position : 0d,
            GapLapsToClassLeader: null,
            IntervalSecondsToPreviousClassRow: null,
            IntervalLapsToPreviousClassRow: null,
            DeltaSecondsToFocus: null,
            TrackSurface: 3,
            OnPitRoad: false);
    }

    private static LiveScoringRow ScoringRow(
        int carIdx,
        int? carClass,
        string? className,
        int? classPosition,
        double? bestLap,
        double? lastLap)
    {
        return new LiveScoringRow(
            CarIdx: carIdx,
            OverallPositionRaw: classPosition,
            ClassPositionRaw: classPosition,
            OverallPosition: classPosition,
            ClassPosition: classPosition,
            CarClass: carClass,
            DriverName: $"Driver {carIdx}",
            TeamName: null,
            CarNumber: carIdx.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CarClassName: className,
            CarClassColorHex: "#ff0000",
            IsPlayer: carIdx == 10,
            IsFocus: carIdx == 10,
            IsReferenceClass: true,
            Lap: 13,
            LapsComplete: 12,
            LastLapTimeSeconds: lastLap,
            BestLapTimeSeconds: bestLap,
            ReasonOut: null,
            HasTakenGrid: true);
    }

    private static HistoricalSessionContext CreateContext(
        string sessionType = "Offline Testing",
        string eventType = "Test")
    {
        return new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity
            {
                CarId = 156,
                CarScreenName = "Mercedes-AMG GT3 2020",
                CarClassId = 4098,
                DriverCarFuelKgPerLiter = 0.75d,
                DriverCarEstLapTimeSeconds = 500d
            },
            Track = new HistoricalTrackIdentity
            {
                TrackId = 262,
                TrackDisplayName = "Nurburgring Combined",
                TrackLengthKm = 24.3d
            },
            Session = new HistoricalSessionIdentity
            {
                SessionType = sessionType,
                EventType = eventType
            },
            Conditions = new HistoricalSessionInfoConditions(),
            Drivers =
            [
                new HistoricalSessionDriver
                {
                    CarIdx = 10,
                    UserName = "Player",
                    CarClassId = 4098,
                    IsSpectator = false
                },
                new HistoricalSessionDriver
                {
                    CarIdx = 12,
                    UserName = "Focused Driver",
                    CarClassId = 4098,
                    IsSpectator = false
                }
            ],
            Sectors =
            [
                new HistoricalTrackSector
                {
                    SectorNum = 0,
                    SectorStartPct = 0d
                },
                new HistoricalTrackSector
                {
                    SectorNum = 1,
                    SectorStartPct = 0.5d
                },
                new HistoricalTrackSector
                {
                    SectorNum = 2,
                    SectorStartPct = 0.75d
                }
            ]
        };
    }

    private static HistoricalSessionContext CreateRaceGridContext()
    {
        return new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity
            {
                CarId = 156,
                CarScreenName = "Mercedes-AMG GT3 2020",
                CarClassId = 4098,
                DriverCarFuelKgPerLiter = 0.75d,
                DriverCarEstLapTimeSeconds = 90d
            },
            Track = new HistoricalTrackIdentity
            {
                TrackId = 1,
                TrackDisplayName = "Test Circuit",
                TrackLengthKm = 5.1d
            },
            Session = new HistoricalSessionIdentity
            {
                SessionType = "Race",
                EventType = "Race"
            },
            Conditions = new HistoricalSessionInfoConditions(),
            Drivers =
            [
                new HistoricalSessionDriver
                {
                    CarIdx = 10,
                    UserName = "Player",
                    CarNumber = "10",
                    CarClassId = 4098,
                    CarClassShortName = "GT3",
                    IsSpectator = false
                },
                new HistoricalSessionDriver
                {
                    CarIdx = 11,
                    UserName = "Grid Leader",
                    CarNumber = "11",
                    CarClassId = 4098,
                    CarClassShortName = "GT3",
                    IsSpectator = false
                }
            ],
            StartingGridPositions =
            [
                new HistoricalSessionResultPosition
                {
                    Position = 0,
                    ClassPosition = 0,
                    CarIdx = 11
                },
                new HistoricalSessionResultPosition
                {
                    Position = 1,
                    ClassPosition = 1,
                    CarIdx = 10
                }
            ]
        };
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
