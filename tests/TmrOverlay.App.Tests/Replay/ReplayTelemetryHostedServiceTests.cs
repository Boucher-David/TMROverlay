using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TmrOverlay.App.Events;
using TmrOverlay.App.Performance;
using TmrOverlay.App.Replay;
using TmrOverlay.App.Storage;
using TmrOverlay.App.Telemetry;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Replay;

public sealed class ReplayTelemetryHostedServiceTests
{
    [Fact]
    public void FromConfiguration_ParsesReplayFilters()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Replay:Enabled"] = "true",
                ["Replay:CaptureDirectory"] = ".",
                ["Replay:SpeedMultiplier"] = "25",
                ["Replay:StartFrameIndex"] = "10",
                ["Replay:EndFrameIndex"] = "20",
                ["Replay:StartSessionTimeSeconds"] = "30.5",
                ["Replay:EndSessionTimeSeconds"] = "40.25",
                ["Replay:SessionTypes"] = "Race,Lone Qualify,Offline Testing",
                ["Replay:FocusCarIdx"] = "17"
            })
            .Build();

        var options = ReplayOptions.FromConfiguration(configuration);

        Assert.True(options.Enabled);
        Assert.Equal(25d, options.SpeedMultiplier);
        Assert.Equal(10, options.StartFrameIndex);
        Assert.Equal(20, options.EndFrameIndex);
        Assert.Equal(30.5d, options.StartSessionTimeSeconds);
        Assert.Equal(40.25d, options.EndSessionTimeSeconds);
        Assert.Equal(17, options.FocusCarIdx);
        Assert.Equal(
            new[] { "practice", "qualifying", "race" },
            options.SessionTypes.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    [Fact]
    public async Task StartAsync_DecodesRawCaptureFramesIntoLiveTelemetrySink()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-replay-service-test", Guid.NewGuid().ToString("N"));
        var capture = TinyRawCapture.Write(root);
        var sink = new RecordingLiveTelemetrySink();
        var service = new ReplayTelemetryHostedService(
            new ReplayOptions
            {
                Enabled = true,
                CaptureDirectory = capture.DirectoryPath,
                SpeedMultiplier = 1000d
            },
            new TelemetryCaptureState(),
            sink,
            new AppPerformanceState(),
            new AppEventRecorder(CreateStorage(root)),
            NullLogger<ReplayTelemetryHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntil(() => sink.Samples.Count >= 2, TimeSpan.FromSeconds(3));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        var samples = sink.Samples;
        Assert.Equal("capture-replay-service-test", sink.CollectionSourceId);
        Assert.Contains(sink.SessionInfoUpdates, yaml => yaml.Contains("SessionInfo:", StringComparison.Ordinal));
        Assert.Equal(2, samples.Count);
        Assert.Equal(1, samples[0].SessionInfoUpdate);
        Assert.Equal(17, samples[0].PlayerCarIdx);
        Assert.Equal(17, samples[0].FocusCarIdx);
        Assert.Equal(17, samples[0].TeamCarClass);
        Assert.Equal(2, samples[0].TeamLapCompleted);
        Assert.Equal(0.12d, samples[0].TeamLapDistPct);
        Assert.Equal(17, samples[0].FocusClassLeaderCarIdx);
        Assert.Contains(samples[0].AllCars ?? [], car => car.CarIdx == 19);
        Assert.Equal(42.5d, samples[0].FuelLevelLiters);
        Assert.Equal(0.12d, samples[0].LapDistPct);
        Assert.Equal(41.9d, samples[1].FuelLevelLiters);
        Assert.Equal(0.25d, samples[1].LapDistPct);
        Assert.All(samples, sample =>
            Assert.InRange((DateTimeOffset.UtcNow - sample.CapturedAtUtc).TotalSeconds, 0d, 5d));
    }

    [Fact]
    public async Task StartAsync_AppliesReplayFrameFilter()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-replay-service-test", Guid.NewGuid().ToString("N"));
        var capture = TinyRawCapture.Write(root);
        var sink = new RecordingLiveTelemetrySink();
        var service = new ReplayTelemetryHostedService(
            new ReplayOptions
            {
                Enabled = true,
                CaptureDirectory = capture.DirectoryPath,
                SpeedMultiplier = 1000d,
                StartFrameIndex = 1
            },
            new TelemetryCaptureState(),
            sink,
            new AppPerformanceState(),
            new AppEventRecorder(CreateStorage(root)),
            NullLogger<ReplayTelemetryHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntil(() => sink.Samples.Count >= 1, TimeSpan.FromSeconds(3));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        var sample = Assert.Single(sink.Samples);
        Assert.Equal(41.9d, sample.FuelLevelLiters);
        Assert.Equal(0.25d, sample.LapDistPct);
    }

    [Fact]
    public void Inspect_ReportsImportShapeWithoutReadingDecodedSamples()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-replay-inspection-test", Guid.NewGuid().ToString("N"));
        var capture = TinyRawCapture.Write(root);

        var inspection = RawCaptureSemanticReplayReader.Open(capture.DirectoryPath).Inspect();

        Assert.Equal("capture-replay-service-test", inspection.CaptureId);
        Assert.Equal(2, inspection.ManifestFrameCount);
        Assert.Equal(2, inspection.ObservedFrameCount);
        Assert.Equal(0, inspection.FirstFrameIndex);
        Assert.Equal(1, inspection.LastFrameIndex);
        Assert.Equal(10d, inspection.FirstSessionTimeSeconds);
        Assert.Equal(10.016d, inspection.LastSessionTimeSeconds);
        Assert.Equal(new[] { 1 }, inspection.SessionInfoUpdates);
        Assert.Equal(new[] { 1 }, inspection.SessionInfoSnapshotUpdates);
        Assert.Empty(inspection.UnsupportedSchemaTypes);
        Assert.Empty(inspection.Warnings);
        Assert.Empty(inspection.Errors);
    }

    [Fact]
    public void ReadFrames_RecomputesFocusScopedFieldsForFocusOverride()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-replay-focus-test", Guid.NewGuid().ToString("N"));
        var capture = TinyRawCapture.Write(root);
        var replay = RawCaptureSemanticReplayReader.Open(capture.DirectoryPath);

        var frame = Assert.Single(replay.ReadFrames(new RawCaptureSemanticReplayFilter(
            EndFrameIndex: 0,
            SessionTypes: new HashSet<string>(new[] { "race" }, StringComparer.OrdinalIgnoreCase),
            FocusCarIdx: 19)));
        var sample = frame.Sample;

        Assert.Equal(17, sample.RawCamCarIdx);
        Assert.Equal(19, sample.FocusCarIdx);
        Assert.Null(sample.FocusUnavailableReason);
        Assert.Equal(22, sample.FocusCarClass);
        Assert.Equal(0.44d, sample.FocusLapDistPct);
        Assert.Equal(3, sample.FocusPosition);
        Assert.Equal(2, sample.FocusClassPosition);
        Assert.Equal(21, sample.FocusClassLeaderCarIdx);
        Assert.Equal(0.60d, sample.FocusClassLeaderLapDistPct);
        Assert.DoesNotContain(sample.FocusClassCars ?? [], car => car.CarIdx == 17);
        Assert.Contains(sample.FocusClassCars ?? [], car => car.CarIdx == 21);
        Assert.Contains(sample.NearbyCars ?? [], car => car.CarIdx == 17);
        Assert.DoesNotContain(sample.NearbyCars ?? [], car => car.CarIdx == 19);
    }

    [Fact]
    public void ReadFrames_FiltersByParsedSessionType()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-replay-session-type-test", Guid.NewGuid().ToString("N"));
        var capture = TinyRawCapture.Write(root);
        var replay = RawCaptureSemanticReplayReader.Open(capture.DirectoryPath);

        Assert.Equal(2, replay.ReadFrames(new RawCaptureSemanticReplayFilter(
            SessionTypes: new HashSet<string>(new[] { "race" }, StringComparer.OrdinalIgnoreCase))).Count());
        Assert.Empty(replay.ReadFrames(new RawCaptureSemanticReplayFilter(
            SessionTypes: new HashSet<string>(new[] { "qualifying" }, StringComparer.OrdinalIgnoreCase))));
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
            ForensicsRoot = Path.Combine(root, "forensics"),
            TrackMapRoot = Path.Combine(root, "track-maps", "user"),
            EventsRoot = Path.Combine(root, "logs", "events"),
            RuntimeStatePath = Path.Combine(root, "runtime-state.json")
        };
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.True(condition(), "Timed out waiting for replay telemetry samples.");
    }

    private sealed class RecordingLiveTelemetrySink : ILiveTelemetrySink
    {
        private readonly object _sync = new();
        private readonly List<HistoricalTelemetrySample> _samples = [];
        private readonly List<string> _sessionInfoUpdates = [];

        public string? CollectionSourceId { get; private set; }

        public IReadOnlyList<HistoricalTelemetrySample> Samples
        {
            get
            {
                lock (_sync)
                {
                    return _samples.ToArray();
                }
            }
        }

        public IReadOnlyList<string> SessionInfoUpdates
        {
            get
            {
                lock (_sync)
                {
                    return _sessionInfoUpdates.ToArray();
                }
            }
        }

        public void MarkConnected()
        {
        }

        public void MarkCollectionStarted(string sourceId, DateTimeOffset startedAtUtc)
        {
            CollectionSourceId = sourceId;
        }

        public void MarkDisconnected()
        {
        }

        public void ApplySessionInfo(string sessionInfoYaml)
        {
            lock (_sync)
            {
                _sessionInfoUpdates.Add(sessionInfoYaml);
            }
        }

        public void RecordFrame(HistoricalTelemetrySample sample)
        {
            lock (_sync)
            {
                _samples.Add(sample);
            }
        }
    }

    private sealed record TinyRawCapture(string DirectoryPath)
    {
        public static TinyRawCapture Write(string root)
        {
            var captureDirectory = Path.Combine(root, "capture-replay-service-test");
            var sessionInfoDirectory = Path.Combine(captureDirectory, "session-info");
            Directory.CreateDirectory(sessionInfoDirectory);

            var layout = RawPayloadLayout.Create();
            var startedAtUtc = new DateTimeOffset(2026, 5, 24, 18, 0, 0, TimeSpan.Zero);
            var manifest = new CaptureManifest
            {
                CaptureId = "capture-replay-service-test",
                StartedAtUtc = startedAtUtc,
                TelemetryFile = "telemetry.bin",
                SchemaFile = "telemetry-schema.json",
                LatestSessionInfoFile = "latest-session.yaml",
                SessionInfoDirectory = "session-info",
                SdkVersion = 1,
                TickRate = 60,
                BufferLength = layout.BufferLength,
                VariableCount = layout.Schema.Count,
                FrameCount = 2,
                SessionInfoSnapshotCount = 1
            };

            File.WriteAllText(
                Path.Combine(captureDirectory, "capture-manifest.json"),
                JsonSerializer.Serialize(manifest));
            File.WriteAllText(
                Path.Combine(captureDirectory, "telemetry-schema.json"),
                JsonSerializer.Serialize(layout.Schema));

            const string sessionInfoYaml = """
SessionInfo:
 CurrentSessionNum: 0
 Sessions:
 - SessionNum: 0
   SessionName: Race
   SessionType: Race
DriverInfo:
 DriverCarIdx: 17
""";
            File.WriteAllText(Path.Combine(captureDirectory, "latest-session.yaml"), sessionInfoYaml);
            File.WriteAllText(Path.Combine(sessionInfoDirectory, "session-0001.yaml"), sessionInfoYaml);

            var firstPayload = layout.Payload(payload =>
            {
                payload.WriteDouble("SessionTime", 10d);
                payload.WriteInt32("SessionTick", 600);
                payload.WriteBoolean("IsOnTrack", true);
                payload.WriteBoolean("IsInGarage", false);
                payload.WriteBoolean("OnPitRoad", false);
                payload.WriteBoolean("PitstopActive", false);
                payload.WriteBoolean("PlayerCarInPitStall", false);
                payload.WriteDouble("FuelLevel", 42.5d);
                payload.WriteDouble("FuelLevelPct", 0.5d);
                payload.WriteDouble("FuelUsePerHour", 38.2d);
                payload.WriteDouble("Speed", 52d);
                payload.WriteInt32("Lap", 3);
                payload.WriteInt32("LapCompleted", 2);
                payload.WriteDouble("LapDistPct", 0.12d);
                payload.WriteDouble("AirTemp", 20d);
                payload.WriteDouble("TrackTempCrew", 30d);
                payload.WriteInt32("TrackWetness", 0);
                payload.WriteInt32("PlayerTireCompound", 0);
                payload.WriteInt32("SessionLapsRemainEx", 4);
                payload.WriteInt32("SessionLapsTotal", 6);
                payload.WriteInt32("SessionState", 4);
                payload.WriteInt32("RaceLaps", 6);
                payload.WriteInt32("PlayerCarIdx", 17);
                payload.WriteInt32("CamCarIdx", 17);
                payload.InitializeCarArrays();
                payload.WriteCarProgress(17, carClass: 17, lapCompleted: 2, lapDistPct: 0.12d, position: 4, classPosition: 1);
                payload.WriteCarProgress(19, carClass: 22, lapCompleted: 2, lapDistPct: 0.44d, position: 3, classPosition: 2);
                payload.WriteCarProgress(21, carClass: 22, lapCompleted: 2, lapDistPct: 0.60d, position: 2, classPosition: 1);
            });
            var secondPayload = layout.Payload(payload =>
            {
                payload.WriteDouble("SessionTime", 10.016d);
                payload.WriteInt32("SessionTick", 601);
                payload.WriteBoolean("IsOnTrack", true);
                payload.WriteBoolean("IsInGarage", false);
                payload.WriteBoolean("OnPitRoad", false);
                payload.WriteBoolean("PitstopActive", false);
                payload.WriteBoolean("PlayerCarInPitStall", false);
                payload.WriteDouble("FuelLevel", 41.9d);
                payload.WriteDouble("FuelLevelPct", 0.49d);
                payload.WriteDouble("FuelUsePerHour", 39.1d);
                payload.WriteDouble("Speed", 54d);
                payload.WriteInt32("Lap", 3);
                payload.WriteInt32("LapCompleted", 2);
                payload.WriteDouble("LapDistPct", 0.25d);
                payload.WriteDouble("AirTemp", 20d);
                payload.WriteDouble("TrackTempCrew", 30d);
                payload.WriteInt32("TrackWetness", 0);
                payload.WriteInt32("PlayerTireCompound", 0);
                payload.WriteInt32("SessionLapsRemainEx", 4);
                payload.WriteInt32("SessionLapsTotal", 6);
                payload.WriteInt32("SessionState", 4);
                payload.WriteInt32("RaceLaps", 6);
                payload.WriteInt32("PlayerCarIdx", 17);
                payload.WriteInt32("CamCarIdx", 17);
                payload.InitializeCarArrays();
                payload.WriteCarProgress(17, carClass: 17, lapCompleted: 2, lapDistPct: 0.25d, position: 4, classPosition: 1);
                payload.WriteCarProgress(19, carClass: 22, lapCompleted: 2, lapDistPct: 0.50d, position: 3, classPosition: 2);
                payload.WriteCarProgress(21, carClass: 22, lapCompleted: 2, lapDistPct: 0.65d, position: 2, classPosition: 1);
            });

            using var writer = new BinaryWriter(
                File.Open(Path.Combine(captureDirectory, "telemetry.bin"), FileMode.Create, FileAccess.Write),
                Encoding.UTF8);
            writer.Write(Encoding.ASCII.GetBytes("TMRCAP01"));
            writer.Write(manifest.SdkVersion);
            writer.Write(manifest.TickRate);
            writer.Write(manifest.BufferLength);
            writer.Write(manifest.VariableCount);
            writer.Write(startedAtUtc.ToUnixTimeMilliseconds());
            WriteFrame(writer, startedAtUtc, frameIndex: 0, sessionTick: 600, sessionInfoUpdate: 1, sessionTime: 10d, firstPayload);
            WriteFrame(writer, startedAtUtc.AddMilliseconds(16), frameIndex: 1, sessionTick: 601, sessionInfoUpdate: 1, sessionTime: 10.016d, secondPayload);

            return new TinyRawCapture(captureDirectory);
        }

        private static void WriteFrame(
            BinaryWriter writer,
            DateTimeOffset capturedAtUtc,
            int frameIndex,
            int sessionTick,
            int sessionInfoUpdate,
            double sessionTime,
            byte[] payload)
        {
            writer.Write(capturedAtUtc.ToUnixTimeMilliseconds());
            writer.Write(frameIndex);
            writer.Write(sessionTick);
            writer.Write(sessionInfoUpdate);
            writer.Write(sessionTime);
            writer.Write(payload.Length);
            writer.Write(payload);
        }
    }

    private sealed class RawPayloadLayout
    {
        private readonly List<Field> _fields = [];
        private int _offset;

        private RawPayloadLayout()
        {
        }

        public IReadOnlyList<TelemetryVariableSchema> Schema => _fields
            .Select(field => new TelemetryVariableSchema(
                field.Name,
                field.TypeName,
                TypeCode: 0,
                field.Count,
                field.Offset,
                field.ByteSize,
                Length: field.ByteSize * field.Count,
                field.Unit,
                Description: string.Empty))
            .ToArray();

        public int BufferLength => _offset;

        public static RawPayloadLayout Create()
        {
            var layout = new RawPayloadLayout();
            return layout
                .AddDouble("SessionTime", "s")
                .AddInt32("SessionTick")
                .AddBoolean("IsOnTrack")
                .AddBoolean("IsInGarage")
                .AddBoolean("OnPitRoad")
                .AddBoolean("PitstopActive")
                .AddBoolean("PlayerCarInPitStall")
                .AddDouble("FuelLevel", "l")
                .AddDouble("FuelLevelPct", "%")
                .AddDouble("FuelUsePerHour", "kg/h")
                .AddDouble("Speed", "m/s")
                .AddInt32("Lap")
                .AddInt32("LapCompleted")
                .AddDouble("LapDistPct", "%")
                .AddDouble("AirTemp", "C")
                .AddDouble("TrackTempCrew", "C")
                .AddInt32("TrackWetness")
                .AddInt32("PlayerTireCompound")
                .AddInt32("SessionLapsRemainEx")
                .AddInt32("SessionLapsTotal")
                .AddInt32("SessionState")
                .AddInt32("RaceLaps")
                .AddInt32("PlayerCarIdx")
                .AddInt32("CamCarIdx")
                .AddInt32Array("CarIdxClass", 64)
                .AddInt32Array("CarIdxLapCompleted", 64)
                .AddDoubleArray("CarIdxLapDistPct", 64, "%")
                .AddDoubleArray("CarIdxF2Time", 64, "s")
                .AddDoubleArray("CarIdxEstTime", 64, "s")
                .AddDoubleArray("CarIdxLastLapTime", 64, "s")
                .AddDoubleArray("CarIdxBestLapTime", 64, "s")
                .AddInt32Array("CarIdxPosition", 64)
                .AddInt32Array("CarIdxClassPosition", 64)
                .AddInt32Array("CarIdxTrackSurface", 64)
                .AddBooleanArray("CarIdxOnPitRoad", 64)
                .AddInt32Array("CarIdxTireCompound", 64)
                .AddInt32Array("CarIdxSessionFlags", 64);
        }

        public byte[] Payload(Action<RawPayload> configure)
        {
            var payload = new byte[BufferLength];
            configure(new RawPayload(_fields.ToDictionary(field => field.Name, StringComparer.OrdinalIgnoreCase), payload));
            return payload;
        }

        private RawPayloadLayout AddBoolean(string name) => Add(name, "irBool", 1, 1, string.Empty);

        private RawPayloadLayout AddBooleanArray(string name, int count) => Add(name, "irBool", count, 1, string.Empty);

        private RawPayloadLayout AddInt32(string name) => Add(name, "irInt", 1, 4, string.Empty);

        private RawPayloadLayout AddInt32Array(string name, int count) => Add(name, "irInt", count, 4, string.Empty);

        private RawPayloadLayout AddDouble(string name, string unit) => Add(name, "irDouble", 1, 8, unit);

        private RawPayloadLayout AddDoubleArray(string name, int count, string unit) => Add(name, "irDouble", count, 8, unit);

        private RawPayloadLayout Add(string name, string typeName, int count, int byteSize, string unit)
        {
            _fields.Add(new Field(name, typeName, count, _offset, byteSize, unit));
            _offset += byteSize * count;
            return this;
        }

        public sealed record Field(string Name, string TypeName, int Count, int Offset, int ByteSize, string Unit);
    }

    private sealed class RawPayload
    {
        private readonly IReadOnlyDictionary<string, RawPayloadLayout.Field> _fields;
        private readonly byte[] _payload;

        public RawPayload(IReadOnlyDictionary<string, RawPayloadLayout.Field> fields, byte[] payload)
        {
            _fields = fields;
            _payload = payload;
        }

        public void WriteBoolean(string name, bool value)
        {
            var field = _fields[name];
            _payload[field.Offset] = value ? (byte)1 : (byte)0;
        }

        public void WriteInt32(string name, int value)
        {
            var field = _fields[name];
            BitConverter.GetBytes(value).CopyTo(_payload, field.Offset);
        }

        public void WriteDouble(string name, double value)
        {
            var field = _fields[name];
            BitConverter.GetBytes(value).CopyTo(_payload, field.Offset);
        }

        public void InitializeCarArrays()
        {
            WriteInt32Array("CarIdxClass", -1);
            WriteInt32Array("CarIdxLapCompleted", -1);
            WriteDoubleArray("CarIdxLapDistPct", -1d);
            WriteDoubleArray("CarIdxF2Time", -1d);
            WriteDoubleArray("CarIdxEstTime", -1d);
            WriteDoubleArray("CarIdxLastLapTime", -1d);
            WriteDoubleArray("CarIdxBestLapTime", -1d);
            WriteInt32Array("CarIdxPosition", -1);
            WriteInt32Array("CarIdxClassPosition", -1);
            WriteInt32Array("CarIdxTrackSurface", -1);
            WriteBooleanArray("CarIdxOnPitRoad", false);
            WriteInt32Array("CarIdxTireCompound", -1);
            WriteInt32Array("CarIdxSessionFlags", 0);
        }

        public void WriteCarProgress(
            int carIdx,
            int carClass,
            int lapCompleted,
            double lapDistPct,
            int position,
            int classPosition)
        {
            WriteInt32ArrayElement("CarIdxClass", carIdx, carClass);
            WriteInt32ArrayElement("CarIdxLapCompleted", carIdx, lapCompleted);
            WriteDoubleArrayElement("CarIdxLapDistPct", carIdx, lapDistPct);
            WriteDoubleArrayElement("CarIdxF2Time", carIdx, position * 0.5d);
            WriteDoubleArrayElement("CarIdxEstTime", carIdx, lapDistPct * 100d);
            WriteDoubleArrayElement("CarIdxLastLapTime", carIdx, 95d + carIdx);
            WriteDoubleArrayElement("CarIdxBestLapTime", carIdx, 94d + carIdx);
            WriteInt32ArrayElement("CarIdxPosition", carIdx, position);
            WriteInt32ArrayElement("CarIdxClassPosition", carIdx, classPosition);
            WriteInt32ArrayElement("CarIdxTrackSurface", carIdx, 3);
            WriteBooleanArrayElement("CarIdxOnPitRoad", carIdx, false);
            WriteInt32ArrayElement("CarIdxTireCompound", carIdx, 0);
        }

        private void WriteBooleanArray(string name, bool value)
        {
            var field = _fields[name];
            for (var index = 0; index < field.Count; index++)
            {
                _payload[field.Offset + index] = value ? (byte)1 : (byte)0;
            }
        }

        private void WriteInt32Array(string name, int value)
        {
            var field = _fields[name];
            for (var index = 0; index < field.Count; index++)
            {
                BitConverter.GetBytes(value).CopyTo(_payload, field.Offset + (index * field.ByteSize));
            }
        }

        private void WriteDoubleArray(string name, double value)
        {
            var field = _fields[name];
            for (var index = 0; index < field.Count; index++)
            {
                BitConverter.GetBytes(value).CopyTo(_payload, field.Offset + (index * field.ByteSize));
            }
        }

        private void WriteBooleanArrayElement(string name, int index, bool value)
        {
            var field = _fields[name];
            _payload[field.Offset + index] = value ? (byte)1 : (byte)0;
        }

        private void WriteInt32ArrayElement(string name, int index, int value)
        {
            var field = _fields[name];
            BitConverter.GetBytes(value).CopyTo(_payload, field.Offset + (index * field.ByteSize));
        }

        private void WriteDoubleArrayElement(string name, int index, double value)
        {
            var field = _fields[name];
            BitConverter.GetBytes(value).CopyTo(_payload, field.Offset + (index * field.ByteSize));
        }
    }
}
