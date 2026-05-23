using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using TmrOverlay.App.Telemetry;
using TmrOverlay.Core.History;

namespace TmrOverlay.App.Replay;

internal sealed class RawCaptureReplayReader
{
    private const int FileHeaderSize = 32;
    private const int FrameHeaderSize = 32;
    private const string Magic = "TMRCAP01";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _telemetryPath;

    private RawCaptureReplayReader(
        string captureDirectory,
        CaptureManifest manifest,
        IReadOnlyDictionary<string, TelemetryVariableSchema> schema)
    {
        CaptureDirectory = captureDirectory;
        Manifest = manifest;
        Schema = schema;
        _telemetryPath = Path.Combine(captureDirectory, manifest.TelemetryFile);
    }

    public string CaptureDirectory { get; }

    public CaptureManifest Manifest { get; }

    public IReadOnlyDictionary<string, TelemetryVariableSchema> Schema { get; }

    public static RawCaptureReplayReader Open(string captureDirectory)
    {
        captureDirectory = Path.GetFullPath(captureDirectory);
        var manifestPath = Path.Combine(captureDirectory, "capture-manifest.json");
        var manifest = ReadJson<CaptureManifest>(manifestPath)
            ?? throw new InvalidOperationException($"Capture manifest could not be read: {manifestPath}");
        var schemaPath = Path.Combine(captureDirectory, manifest.SchemaFile);
        var schema = ReadJson<TelemetryVariableSchema[]>(schemaPath)
            ?? throw new InvalidOperationException($"Telemetry schema could not be read: {schemaPath}");

        return new RawCaptureReplayReader(
            captureDirectory,
            manifest,
            schema.ToDictionary(row => row.Name, StringComparer.OrdinalIgnoreCase));
    }

    public IEnumerable<TelemetryFrameEnvelope> ReadFrames(CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(_telemetryPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> fileHeader = stackalloc byte[FileHeaderSize];
        ReadExactly(stream, fileHeader);
        ValidateFileHeader(fileHeader);

        var frameHeader = new byte[FrameHeaderSize];
        while (stream.Position < stream.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadExactly(stream, frameHeader);

            var capturedUnixMs = BinaryPrimitives.ReadInt64LittleEndian(frameHeader.AsSpan(0, 8));
            var frameIndex = BinaryPrimitives.ReadInt32LittleEndian(frameHeader.AsSpan(8, 4));
            var sessionTick = BinaryPrimitives.ReadInt32LittleEndian(frameHeader.AsSpan(12, 4));
            var sessionInfoUpdate = BinaryPrimitives.ReadInt32LittleEndian(frameHeader.AsSpan(16, 4));
            var sessionTime = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(frameHeader.AsSpan(20, 8)));
            var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(frameHeader.AsSpan(28, 4));
            if (payloadLength < 0 || payloadLength > stream.Length - stream.Position)
            {
                throw new InvalidDataException($"Invalid raw telemetry payload length {payloadLength} at frame {frameIndex}.");
            }

            var payload = GC.AllocateUninitializedArray<byte>(payloadLength);
            ReadExactly(stream, payload);
            yield return new TelemetryFrameEnvelope(
                CapturedAtUtc: DateTimeOffset.FromUnixTimeMilliseconds(capturedUnixMs),
                FrameIndex: frameIndex,
                SessionTick: sessionTick,
                SessionInfoUpdate: sessionInfoUpdate,
                SessionTime: sessionTime,
                Payload: payload);
        }
    }

    private static T? ReadJson<T>(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, JsonOptions);
    }

    private static void ValidateFileHeader(ReadOnlySpan<byte> header)
    {
        var magic = Encoding.ASCII.GetString(header[..8]);
        if (!string.Equals(magic, Magic, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported raw capture magic '{magic}'.");
        }
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of raw telemetry capture.");
            }

            total += read;
        }
    }
}

internal sealed class RawCaptureSessionInfoProvider
{
    private readonly IReadOnlyList<int> _updates;
    private readonly Dictionary<int, string> _sessionInfoByUpdate;
    private readonly string? _latestSessionInfo;

    private RawCaptureSessionInfoProvider(
        IReadOnlyList<int> updates,
        Dictionary<int, string> sessionInfoByUpdate,
        string? latestSessionInfo)
    {
        _updates = updates;
        _sessionInfoByUpdate = sessionInfoByUpdate;
        _latestSessionInfo = latestSessionInfo;
    }

    public static RawCaptureSessionInfoProvider Open(string captureDirectory, CaptureManifest manifest)
    {
        var sessionInfoByUpdate = new Dictionary<int, string>();
        var sessionInfoDirectory = Path.Combine(captureDirectory, manifest.SessionInfoDirectory);
        if (Directory.Exists(sessionInfoDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(sessionInfoDirectory, "session-*.yaml"))
            {
                if (TryParseSessionInfoUpdate(path, out var update))
                {
                    sessionInfoByUpdate[update] = File.ReadAllText(path);
                }
            }
        }

        var latestPath = Path.Combine(captureDirectory, manifest.LatestSessionInfoFile);
        var latest = File.Exists(latestPath) ? File.ReadAllText(latestPath) : null;
        return new RawCaptureSessionInfoProvider(
            sessionInfoByUpdate.Keys.Order().ToArray(),
            sessionInfoByUpdate,
            latest);
    }

    public string? FindForUpdate(int sessionInfoUpdate)
    {
        if (_sessionInfoByUpdate.TryGetValue(sessionInfoUpdate, out var exact))
        {
            return exact;
        }

        var index = UpperBound(_updates, sessionInfoUpdate) - 1;
        if (index >= 0 && _sessionInfoByUpdate.TryGetValue(_updates[index], out var previous))
        {
            return previous;
        }

        return _latestSessionInfo;
    }

    private static bool TryParseSessionInfoUpdate(string path, out int update)
    {
        update = 0;
        var name = Path.GetFileNameWithoutExtension(path);
        var dash = name.LastIndexOf('-');
        return dash >= 0 && int.TryParse(name[(dash + 1)..], out update);
    }

    private static int UpperBound(IReadOnlyList<int> values, int target)
    {
        var low = 0;
        var high = values.Count;
        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (values[mid] <= target)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }
}

internal sealed class RawCaptureTelemetrySampleBuilder
{
    private readonly IReadOnlyDictionary<string, TelemetryVariableSchema> _schema;

    public RawCaptureTelemetrySampleBuilder(IReadOnlyDictionary<string, TelemetryVariableSchema> schema)
    {
        _schema = schema;
    }

    public HistoricalTelemetrySample Build(TelemetryFrameEnvelope frame)
    {
        var reader = new RawCaptureTelemetryFrameReader(_schema, frame.Payload);
        var playerCarIdx = reader.ReadInt32("PlayerCarIdx");
        var focusSelection = ReadFocusCarSelection(reader);
        var focusCarIdx = focusSelection.FocusCarIdx;
        var focusProgress = focusCarIdx is { } focusProgressCarIdx
            ? ReadCarProgress(reader, focusProgressCarIdx, requireLapProgress: false)
            : null;
        var leaderProgress = ReadLeaderProgress(reader);
        var classLeaderProgress = ReadClassLeaderProgress(reader, playerCarIdx);
        var focusClassLeaderProgress = focusCarIdx is null
            ? null
            : focusCarIdx == playerCarIdx
                ? classLeaderProgress
                : ReadClassLeaderProgress(reader, focusCarIdx.Value);
        var nearbyCars = focusCarIdx is { } nearbyFocusCarIdx
            ? ReadNearbyCars(reader, nearbyFocusCarIdx)
            : [];
        var classCars = ReadClassCars(reader, playerCarIdx);
        var focusClassCars = focusCarIdx is null
            ? []
            : focusCarIdx == playerCarIdx
                ? classCars
                : ReadClassCars(reader, focusCarIdx.Value);
        var allCars = ReadAllTimingCars(reader);

        return new HistoricalTelemetrySample(
            CapturedAtUtc: frame.CapturedAtUtc,
            SessionTime: reader.ReadDouble("SessionTime"),
            SessionTick: reader.ReadInt32("SessionTick"),
            SessionInfoUpdate: frame.SessionInfoUpdate,
            IsOnTrack: reader.ReadBoolean("IsOnTrack"),
            IsInGarage: reader.ReadBoolean("IsInGarage"),
            OnPitRoad: reader.ReadBoolean("OnPitRoad"),
            PitstopActive: reader.ReadBoolean("PitstopActive"),
            PlayerCarInPitStall: reader.ReadBoolean("PlayerCarInPitStall"),
            FuelLevelLiters: reader.ReadDouble("FuelLevel"),
            FuelLevelPercent: reader.ReadDouble("FuelLevelPct"),
            FuelUsePerHourKg: reader.ReadDouble("FuelUsePerHour"),
            SpeedMetersPerSecond: reader.ReadDouble("Speed"),
            Lap: reader.ReadInt32("Lap"),
            LapCompleted: reader.ReadInt32("LapCompleted"),
            LapDistPct: reader.ReadDouble("LapDistPct"),
            LapLastLapTimeSeconds: reader.ReadNullableDouble("LapLastLapTime"),
            LapBestLapTimeSeconds: reader.ReadNullableDouble("LapBestLapTime"),
            AirTempC: reader.ReadDouble("AirTemp"),
            TrackTempCrewC: reader.ReadDouble("TrackTempCrew"),
            TrackWetness: reader.ReadInt32("TrackWetness"),
            WeatherDeclaredWet: reader.ReadNullableBoolean("WeatherDeclaredWet"),
            PlayerTireCompound: reader.ReadInt32("PlayerTireCompound"),
            Skies: reader.ReadNullableInt32("Skies"),
            PrecipitationPercent: reader.ReadNullableDouble("Precipitation"),
            WindVelocityMetersPerSecond: reader.ReadNullableDouble("WindVel"),
            WindDirectionRadians: reader.ReadNullableDouble("WindDir"),
            RelativeHumidityPercent: reader.ReadNullableDouble("RelativeHumidity"),
            FogLevelPercent: reader.ReadNullableDouble("FogLevel"),
            AirPressurePa: reader.ReadNullableDouble("AirPressure"),
            SolarAltitudeRadians: reader.ReadNullableFiniteDouble("SolarAltitude"),
            SolarAzimuthRadians: reader.ReadNullableFiniteDouble("SolarAzimuth"),
            IsGarageVisible: reader.ReadNullableBoolean("IsGarageVisible"),
            IsReplayPlaying: reader.ReadNullableBoolean("IsReplayPlaying"),
            SessionTimeRemain: reader.ReadNullableDouble("SessionTimeRemain"),
            SessionTimeTotal: reader.ReadNullableDouble("SessionTimeTotal"),
            SessionLapsRemainEx: reader.ReadInt32("SessionLapsRemainEx"),
            SessionLapsTotal: reader.ReadInt32("SessionLapsTotal"),
            SessionState: reader.ReadInt32("SessionState"),
            SessionFlags: reader.ReadNullableInt32("SessionFlags"),
            RaceLaps: reader.ReadInt32("RaceLaps"),
            PlayerCarIdx: playerCarIdx,
            RawCamCarIdx: focusSelection.RawCamCarIdx,
            FocusCarIdx: focusCarIdx,
            FocusUnavailableReason: focusSelection.UnavailableReason,
            FocusLapCompleted: focusProgress?.LapCompleted,
            FocusLapDistPct: focusProgress?.LapDistPct,
            FocusF2TimeSeconds: focusProgress?.F2TimeSeconds,
            FocusEstimatedTimeSeconds: focusProgress?.EstimatedTimeSeconds,
            FocusLastLapTimeSeconds: focusProgress?.LastLapTimeSeconds,
            FocusBestLapTimeSeconds: focusProgress?.BestLapTimeSeconds,
            FocusPosition: focusProgress?.Position,
            FocusClassPosition: focusProgress?.ClassPosition,
            FocusCarClass: focusProgress?.CarClass,
            FocusTireCompound: focusProgress?.TireCompound,
            FocusOnPitRoad: focusCarIdx is { } focusPitCarIdx
                ? reader.ReadBooleanArrayElement("CarIdxOnPitRoad", focusPitCarIdx)
                : null,
            FocusTrackSurface: focusCarIdx is { } focusSurfaceCarIdx
                ? reader.ReadInt32ArrayElement("CarIdxTrackSurface", focusSurfaceCarIdx)
                : null,
            TeamLapCompleted: reader.ReadInt32ArrayElement("CarIdxLapCompleted", playerCarIdx),
            TeamLapDistPct: reader.ReadDoubleArrayElement("CarIdxLapDistPct", playerCarIdx),
            TeamF2TimeSeconds: reader.ReadNullableDoubleArrayElement("CarIdxF2Time", playerCarIdx),
            TeamEstimatedTimeSeconds: reader.ReadNullableDoubleArrayElement("CarIdxEstTime", playerCarIdx),
            TeamLastLapTimeSeconds: reader.ReadNullableDoubleArrayElement("CarIdxLastLapTime", playerCarIdx),
            TeamBestLapTimeSeconds: reader.ReadNullableDoubleArrayElement("CarIdxBestLapTime", playerCarIdx),
            TeamPosition: reader.ReadInt32ArrayElement("CarIdxPosition", playerCarIdx),
            TeamClassPosition: reader.ReadInt32ArrayElement("CarIdxClassPosition", playerCarIdx),
            TeamCarClass: reader.ReadInt32ArrayElement("CarIdxClass", playerCarIdx),
            TeamTireCompound: reader.ReadInt32ArrayElement("CarIdxTireCompound", playerCarIdx),
            LeaderCarIdx: leaderProgress?.CarIdx,
            LeaderLapCompleted: leaderProgress?.LapCompleted,
            LeaderLapDistPct: leaderProgress?.LapDistPct,
            LeaderF2TimeSeconds: leaderProgress?.F2TimeSeconds,
            LeaderEstimatedTimeSeconds: leaderProgress?.EstimatedTimeSeconds,
            LeaderLastLapTimeSeconds: leaderProgress?.LastLapTimeSeconds,
            LeaderBestLapTimeSeconds: leaderProgress?.BestLapTimeSeconds,
            LeaderTireCompound: leaderProgress?.TireCompound,
            ClassLeaderCarIdx: classLeaderProgress?.CarIdx,
            ClassLeaderLapCompleted: classLeaderProgress?.LapCompleted,
            ClassLeaderLapDistPct: classLeaderProgress?.LapDistPct,
            ClassLeaderF2TimeSeconds: classLeaderProgress?.F2TimeSeconds,
            ClassLeaderEstimatedTimeSeconds: classLeaderProgress?.EstimatedTimeSeconds,
            ClassLeaderLastLapTimeSeconds: classLeaderProgress?.LastLapTimeSeconds,
            ClassLeaderBestLapTimeSeconds: classLeaderProgress?.BestLapTimeSeconds,
            ClassLeaderTireCompound: classLeaderProgress?.TireCompound,
            FocusClassLeaderCarIdx: focusClassLeaderProgress?.CarIdx,
            FocusClassLeaderLapCompleted: focusClassLeaderProgress?.LapCompleted,
            FocusClassLeaderLapDistPct: focusClassLeaderProgress?.LapDistPct,
            FocusClassLeaderF2TimeSeconds: focusClassLeaderProgress?.F2TimeSeconds,
            FocusClassLeaderEstimatedTimeSeconds: focusClassLeaderProgress?.EstimatedTimeSeconds,
            FocusClassLeaderLastLapTimeSeconds: focusClassLeaderProgress?.LastLapTimeSeconds,
            FocusClassLeaderBestLapTimeSeconds: focusClassLeaderProgress?.BestLapTimeSeconds,
            FocusClassLeaderTireCompound: focusClassLeaderProgress?.TireCompound,
            PlayerTrackSurface: reader.ReadNullableInt32("PlayerTrackSurface"),
            CarLeftRight: reader.ReadNullableInt32("CarLeftRight"),
            NearbyCars: nearbyCars,
            ClassCars: classCars,
            FocusClassCars: focusClassCars,
            AllCars: allCars,
            TeamOnPitRoad: reader.ReadBooleanArrayElement("CarIdxOnPitRoad", playerCarIdx),
            TeamFastRepairsUsed: reader.ReadInt32ArrayElement("CarIdxFastRepairsUsed", playerCarIdx),
            PitServiceStatus: reader.ReadNullableInt32("PlayerCarPitSvStatus"),
            PitServiceFlags: reader.ReadInt32("PitSvFlags"),
            PitServiceFuelLiters: reader.ReadNullableDouble("PitSvFuel"),
            PitRepairLeftSeconds: reader.ReadNullableDouble("PitRepairLeft"),
            PitOptRepairLeftSeconds: reader.ReadNullableDouble("PitOptRepairLeft"),
            PlayerCarDryTireSetLimit: reader.ReadInt32("PlayerCarDryTireSetLimit"),
            TireSetsUsed: reader.ReadInt32("TireSetsUsed"),
            TireSetsAvailable: reader.ReadInt32("TireSetsAvailable"),
            LeftTireSetsUsed: reader.ReadInt32("LeftTireSetsUsed"),
            RightTireSetsUsed: reader.ReadInt32("RightTireSetsUsed"),
            FrontTireSetsUsed: reader.ReadInt32("FrontTireSetsUsed"),
            RearTireSetsUsed: reader.ReadInt32("RearTireSetsUsed"),
            LeftTireSetsAvailable: reader.ReadInt32("LeftTireSetsAvailable"),
            RightTireSetsAvailable: reader.ReadInt32("RightTireSetsAvailable"),
            FrontTireSetsAvailable: reader.ReadInt32("FrontTireSetsAvailable"),
            RearTireSetsAvailable: reader.ReadInt32("RearTireSetsAvailable"),
            LeftFrontTiresUsed: reader.ReadInt32("LFTiresUsed"),
            RightFrontTiresUsed: reader.ReadInt32("RFTiresUsed"),
            LeftRearTiresUsed: reader.ReadInt32("LRTiresUsed"),
            RightRearTiresUsed: reader.ReadInt32("RRTiresUsed"),
            LeftFrontTiresAvailable: reader.ReadInt32("LFTiresAvailable"),
            RightFrontTiresAvailable: reader.ReadInt32("RFTiresAvailable"),
            LeftRearTiresAvailable: reader.ReadInt32("LRTiresAvailable"),
            RightRearTiresAvailable: reader.ReadInt32("RRTiresAvailable"),
            FastRepairUsed: reader.ReadInt32("FastRepairUsed"),
            FastRepairAvailable: reader.ReadInt32("FastRepairAvailable"),
            TireCondition: ReadTireCondition(reader),
            PitServiceTireRequest: ReadPitServiceTireRequest(reader),
            DriversSoFar: reader.ReadInt32("DCDriversSoFar"),
            DriverChangeLapStatus: reader.ReadInt32("DCLapStatus"),
            PlayerCarTeamIncidentCount: reader.ReadNullableInt32("PlayerCarTeamIncidentCount"),
            PlayerCarMyIncidentCount: reader.ReadNullableInt32("PlayerCarMyIncidentCount"),
            PlayerCarDriverIncidentCount: reader.ReadNullableInt32("PlayerCarDriverIncidentCount"),
            PlayerIncidents: reader.ReadNullableInt32("PlayerIncidents"),
            LapCurrentLapTimeSeconds: reader.ReadNullableDouble("LapCurrentLapTime"),
            LapDeltaToBestLapSeconds: reader.ReadNullableFiniteDouble("LapDeltaToBestLap"),
            LapDeltaToBestLapRate: reader.ReadNullableFiniteDouble("LapDeltaToBestLap_DD"),
            LapDeltaToBestLapOk: reader.ReadNullableBoolean("LapDeltaToBestLap_OK"),
            LapDeltaToOptimalLapSeconds: reader.ReadNullableFiniteDouble("LapDeltaToOptimalLap"),
            LapDeltaToOptimalLapRate: reader.ReadNullableFiniteDouble("LapDeltaToOptimalLap_DD"),
            LapDeltaToOptimalLapOk: reader.ReadNullableBoolean("LapDeltaToOptimalLap_OK"),
            LapDeltaToSessionBestLapSeconds: reader.ReadNullableFiniteDouble("LapDeltaToSessionBestLap"),
            LapDeltaToSessionBestLapRate: reader.ReadNullableFiniteDouble("LapDeltaToSessionBestLap_DD"),
            LapDeltaToSessionBestLapOk: reader.ReadNullableBoolean("LapDeltaToSessionBestLap_OK"),
            LapDeltaToSessionOptimalLapSeconds: reader.ReadNullableFiniteDouble("LapDeltaToSessionOptimalLap"),
            LapDeltaToSessionOptimalLapRate: reader.ReadNullableFiniteDouble("LapDeltaToSessionOptimalLap_DD"),
            LapDeltaToSessionOptimalLapOk: reader.ReadNullableBoolean("LapDeltaToSessionOptimalLap_OK"),
            LapDeltaToSessionLastLapSeconds: reader.ReadNullableFiniteDouble("LapDeltaToSessionLastlLap"),
            LapDeltaToSessionLastLapRate: reader.ReadNullableFiniteDouble("LapDeltaToSessionLastlLap_DD"),
            LapDeltaToSessionLastLapOk: reader.ReadNullableBoolean("LapDeltaToSessionLastlLap_OK"),
            Gear: reader.ReadNullableInt32("Gear"),
            Rpm: reader.ReadNullableFiniteDouble("RPM"),
            Throttle: reader.ReadNullableFiniteDouble("Throttle"),
            Brake: reader.ReadNullableFiniteDouble("Brake"),
            Clutch: reader.ReadNullableFiniteDouble("Clutch"),
            ClutchRaw: reader.ReadNullableFiniteDouble("ClutchRaw"),
            SteeringWheelAngle: reader.ReadNullableFiniteDouble("SteeringWheelAngle"),
            PlayerYawNorthRadians: reader.ReadNullableFiniteDouble("YawNorth"),
            BrakeAbsActive: reader.ReadNullableBoolean("BrakeABSactive"),
            EngineWarnings: reader.ReadNullableInt32("EngineWarnings"),
            Voltage: reader.ReadNullableFiniteDouble("Voltage"),
            WaterTempC: reader.ReadNullableFiniteDouble("WaterTemp"),
            FuelPressureBar: reader.ReadNullableFiniteDouble("FuelPress"),
            OilTempC: reader.ReadNullableFiniteDouble("OilTemp"),
            OilPressureBar: reader.ReadNullableFiniteDouble("OilPress"));
    }

    private static HistoricalTireConditionSnapshot ReadTireCondition(RawCaptureTelemetryFrameReader reader)
    {
        return new HistoricalTireConditionSnapshot(
            LeftFront: ReadTireCornerCondition(reader, "LF"),
            RightFront: ReadTireCornerCondition(reader, "RF"),
            LeftRear: ReadTireCornerCondition(reader, "LR"),
            RightRear: ReadTireCornerCondition(reader, "RR"));
    }

    private static HistoricalTireCornerCondition ReadTireCornerCondition(RawCaptureTelemetryFrameReader reader, string prefix)
    {
        return new HistoricalTireCornerCondition(
            WearLeft: reader.ReadNullableFiniteDouble($"{prefix}wearL"),
            WearMiddle: reader.ReadNullableFiniteDouble($"{prefix}wearM"),
            WearRight: reader.ReadNullableFiniteDouble($"{prefix}wearR"),
            TemperatureCLeft: reader.ReadNullableFiniteDouble($"{prefix}tempCL"),
            TemperatureCMiddle: reader.ReadNullableFiniteDouble($"{prefix}tempCM"),
            TemperatureCRight: reader.ReadNullableFiniteDouble($"{prefix}tempCR"),
            ColdPressureKpa: reader.ReadNullableFiniteDouble($"{prefix}coldPressure"),
            OdometerMeters: reader.ReadNullableFiniteDouble($"{prefix}odometer"));
    }

    private static HistoricalPitServiceTireRequest ReadPitServiceTireRequest(RawCaptureTelemetryFrameReader reader)
    {
        return new HistoricalPitServiceTireRequest(
            RequestedTireCompound: reader.ReadNullableInt32("PitSvTireCompound"),
            LeftFrontServicePressureKpa: reader.ReadNullableFiniteDouble("PitSvLFP"),
            RightFrontServicePressureKpa: reader.ReadNullableFiniteDouble("PitSvRFP"),
            LeftRearServicePressureKpa: reader.ReadNullableFiniteDouble("PitSvLRP"),
            RightRearServicePressureKpa: reader.ReadNullableFiniteDouble("PitSvRRP"),
            LeftFrontColdPressurePa: reader.ReadNullableFiniteDouble("dpLFTireColdPress"),
            RightFrontColdPressurePa: reader.ReadNullableFiniteDouble("dpRFTireColdPress"),
            LeftRearColdPressurePa: reader.ReadNullableFiniteDouble("dpLRTireColdPress"),
            RightRearColdPressurePa: reader.ReadNullableFiniteDouble("dpRRTireColdPress"),
            LeftFrontChangeRequested: reader.ReadNullableBoolean("dpLFTireChange"),
            RightFrontChangeRequested: reader.ReadNullableBoolean("dpRFTireChange"),
            LeftRearChangeRequested: reader.ReadNullableBoolean("dpLRTireChange"),
            RightRearChangeRequested: reader.ReadNullableBoolean("dpRRTireChange"));
    }

    private static CarProgress? ReadLeaderProgress(RawCaptureTelemetryFrameReader reader)
    {
        CarProgress? bestProgress = null;
        for (var carIdx = 0; carIdx < 64; carIdx++)
        {
            var progress = ReadCarProgress(reader, carIdx, requireLapProgress: false);
            if (progress is null)
            {
                continue;
            }

            if (progress.Position == 1)
            {
                return progress;
            }

            if (!progress.HasLapProgress)
            {
                continue;
            }

            if (bestProgress is null || progress.TotalLaps > bestProgress.TotalLaps)
            {
                bestProgress = progress;
            }
        }

        return bestProgress;
    }

    private static CarProgress? ReadClassLeaderProgress(RawCaptureTelemetryFrameReader reader, int referenceCarIdx)
    {
        var referenceClass = reader.ReadInt32ArrayElement("CarIdxClass", referenceCarIdx);
        if (referenceClass is null)
        {
            return null;
        }

        CarProgress? bestClassProgress = null;
        for (var carIdx = 0; carIdx < 64; carIdx++)
        {
            var carClass = reader.ReadInt32ArrayElement("CarIdxClass", carIdx);
            if (carClass != referenceClass)
            {
                continue;
            }

            var progress = ReadCarProgress(reader, carIdx, requireLapProgress: false);
            if (progress is null)
            {
                continue;
            }

            if (progress.ClassPosition == 1)
            {
                return progress;
            }

            if (!progress.HasLapProgress)
            {
                continue;
            }

            if (bestClassProgress is null || progress.TotalLaps > bestClassProgress.TotalLaps)
            {
                bestClassProgress = progress;
            }
        }

        return bestClassProgress;
    }

    private static FocusCarSelection ReadFocusCarSelection(RawCaptureTelemetryFrameReader reader)
    {
        var camCarIdx = reader.ReadNullableInt32("CamCarIdx");
        if (camCarIdx is null)
        {
            return new FocusCarSelection(null, null, "cam_car_idx_missing");
        }

        if (camCarIdx is < 0 or >= 64)
        {
            return new FocusCarSelection(camCarIdx, null, "cam_car_idx_invalid");
        }

        if (ReadCarProgress(reader, camCarIdx.Value, requireLapProgress: false) is not null)
        {
            return new FocusCarSelection(camCarIdx, camCarIdx, null);
        }

        return new FocusCarSelection(camCarIdx, null, "cam_car_progress_unavailable");
    }

    private static CarProgress? ReadCarProgress(RawCaptureTelemetryFrameReader reader, int carIdx, bool requireLapProgress = true)
    {
        var lapCompleted = reader.ReadInt32ArrayElement("CarIdxLapCompleted", carIdx);
        var lapDistPct = reader.ReadDoubleArrayElement("CarIdxLapDistPct", carIdx);
        var f2TimeSeconds = reader.ReadNullableDoubleArrayElement("CarIdxF2Time", carIdx);
        var estimatedTimeSeconds = reader.ReadNullableDoubleArrayElement("CarIdxEstTime", carIdx);
        var position = reader.ReadInt32ArrayElement("CarIdxPosition", carIdx);
        var classPosition = reader.ReadInt32ArrayElement("CarIdxClassPosition", carIdx);
        var hasLapProgress = HasLapProgress(lapCompleted, lapDistPct);
        if (!hasLapProgress && requireLapProgress)
        {
            return null;
        }

        if (!hasLapProgress && !HasStandingOrTiming(position, classPosition, f2TimeSeconds, estimatedTimeSeconds))
        {
            return null;
        }

        return new CarProgress(
            CarIdx: carIdx,
            LapCompleted: hasLapProgress ? lapCompleted!.Value : -1,
            LapDistPct: hasLapProgress ? Math.Clamp(lapDistPct!.Value, 0d, 1d) : -1d,
            F2TimeSeconds: f2TimeSeconds,
            EstimatedTimeSeconds: estimatedTimeSeconds,
            LastLapTimeSeconds: reader.ReadNullableDoubleArrayElement("CarIdxLastLapTime", carIdx),
            BestLapTimeSeconds: reader.ReadNullableDoubleArrayElement("CarIdxBestLapTime", carIdx),
            Position: position,
            ClassPosition: classPosition,
            CarClass: reader.ReadInt32ArrayElement("CarIdxClass", carIdx),
            TireCompound: reader.ReadInt32ArrayElement("CarIdxTireCompound", carIdx));
    }

    private static IReadOnlyList<HistoricalCarProximity> ReadNearbyCars(RawCaptureTelemetryFrameReader reader, int referenceCarIdx)
    {
        if (referenceCarIdx < 0)
        {
            return [];
        }

        var cars = new List<HistoricalCarProximity>();
        for (var carIdx = 0; carIdx < 64; carIdx++)
        {
            if (carIdx == referenceCarIdx)
            {
                continue;
            }

            var lapCompleted = reader.ReadInt32ArrayElement("CarIdxLapCompleted", carIdx);
            var lapDistPct = reader.ReadDoubleArrayElement("CarIdxLapDistPct", carIdx);
            if (lapDistPct is null
                || double.IsNaN(lapDistPct.Value)
                || double.IsInfinity(lapDistPct.Value)
                || lapDistPct < 0d)
            {
                continue;
            }

            cars.Add(new HistoricalCarProximity(
                CarIdx: carIdx,
                LapCompleted: lapCompleted is >= 0 ? lapCompleted.Value : -1,
                LapDistPct: Math.Clamp(lapDistPct.Value, 0d, 1d),
                F2TimeSeconds: reader.ReadNullableDoubleArrayElement("CarIdxF2Time", carIdx),
                EstimatedTimeSeconds: reader.ReadNullableDoubleArrayElement("CarIdxEstTime", carIdx),
                Position: reader.ReadInt32ArrayElement("CarIdxPosition", carIdx),
                ClassPosition: reader.ReadInt32ArrayElement("CarIdxClassPosition", carIdx),
                CarClass: reader.ReadInt32ArrayElement("CarIdxClass", carIdx),
                TrackSurface: reader.ReadInt32ArrayElement("CarIdxTrackSurface", carIdx),
                OnPitRoad: reader.ReadBooleanArrayElement("CarIdxOnPitRoad", carIdx),
                TireCompound: reader.ReadInt32ArrayElement("CarIdxTireCompound", carIdx),
                SessionFlags: reader.ReadInt32ArrayElement("CarIdxSessionFlags", carIdx)));
        }

        return cars;
    }

    private static IReadOnlyList<HistoricalCarProximity> ReadClassCars(RawCaptureTelemetryFrameReader reader, int referenceCarIdx)
    {
        if (referenceCarIdx < 0)
        {
            return [];
        }

        var referenceClass = reader.ReadInt32ArrayElement("CarIdxClass", referenceCarIdx);
        if (referenceClass is null)
        {
            return [];
        }

        var cars = new List<HistoricalCarProximity>();
        for (var carIdx = 0; carIdx < 64; carIdx++)
        {
            var carClass = reader.ReadInt32ArrayElement("CarIdxClass", carIdx);
            if (carClass != referenceClass)
            {
                continue;
            }

            var lapCompleted = reader.ReadInt32ArrayElement("CarIdxLapCompleted", carIdx);
            var lapDistPct = reader.ReadDoubleArrayElement("CarIdxLapDistPct", carIdx);
            var f2TimeSeconds = reader.ReadNullableDoubleArrayElement("CarIdxF2Time", carIdx);
            var estimatedTimeSeconds = reader.ReadNullableDoubleArrayElement("CarIdxEstTime", carIdx);
            var position = reader.ReadInt32ArrayElement("CarIdxPosition", carIdx);
            var classPosition = reader.ReadInt32ArrayElement("CarIdxClassPosition", carIdx);
            var hasLapProgress = HasLapProgress(lapCompleted, lapDistPct);
            if (!hasLapProgress && !HasStandingOrTiming(position, classPosition, f2TimeSeconds, estimatedTimeSeconds))
            {
                continue;
            }

            cars.Add(new HistoricalCarProximity(
                CarIdx: carIdx,
                LapCompleted: hasLapProgress ? lapCompleted!.Value : -1,
                LapDistPct: hasLapProgress ? Math.Clamp(lapDistPct!.Value, 0d, 1d) : -1d,
                F2TimeSeconds: f2TimeSeconds,
                EstimatedTimeSeconds: estimatedTimeSeconds,
                Position: position,
                ClassPosition: classPosition,
                CarClass: carClass,
                TrackSurface: reader.ReadInt32ArrayElement("CarIdxTrackSurface", carIdx),
                OnPitRoad: reader.ReadBooleanArrayElement("CarIdxOnPitRoad", carIdx),
                TireCompound: reader.ReadInt32ArrayElement("CarIdxTireCompound", carIdx),
                SessionFlags: reader.ReadInt32ArrayElement("CarIdxSessionFlags", carIdx)));
        }

        return cars;
    }

    private static IReadOnlyList<HistoricalCarProximity> ReadAllTimingCars(RawCaptureTelemetryFrameReader reader)
    {
        var cars = new List<HistoricalCarProximity>();
        for (var carIdx = 0; carIdx < 64; carIdx++)
        {
            var lapCompleted = reader.ReadInt32ArrayElement("CarIdxLapCompleted", carIdx);
            var lapDistPct = reader.ReadDoubleArrayElement("CarIdxLapDistPct", carIdx);
            var f2TimeSeconds = reader.ReadNullableDoubleArrayElement("CarIdxF2Time", carIdx);
            var estimatedTimeSeconds = reader.ReadNullableDoubleArrayElement("CarIdxEstTime", carIdx);
            var position = reader.ReadInt32ArrayElement("CarIdxPosition", carIdx);
            var classPosition = reader.ReadInt32ArrayElement("CarIdxClassPosition", carIdx);
            var hasLapProgress = HasLapProgress(lapCompleted, lapDistPct);
            if (!hasLapProgress && !HasStandingOrTiming(position, classPosition, f2TimeSeconds, estimatedTimeSeconds))
            {
                continue;
            }

            cars.Add(new HistoricalCarProximity(
                CarIdx: carIdx,
                LapCompleted: hasLapProgress ? lapCompleted!.Value : -1,
                LapDistPct: hasLapProgress ? Math.Clamp(lapDistPct!.Value, 0d, 1d) : -1d,
                F2TimeSeconds: f2TimeSeconds,
                EstimatedTimeSeconds: estimatedTimeSeconds,
                Position: position,
                ClassPosition: classPosition,
                CarClass: reader.ReadInt32ArrayElement("CarIdxClass", carIdx),
                TrackSurface: reader.ReadInt32ArrayElement("CarIdxTrackSurface", carIdx),
                OnPitRoad: reader.ReadBooleanArrayElement("CarIdxOnPitRoad", carIdx),
                TireCompound: reader.ReadInt32ArrayElement("CarIdxTireCompound", carIdx),
                SessionFlags: reader.ReadInt32ArrayElement("CarIdxSessionFlags", carIdx)));
        }

        return cars;
    }

    private static bool HasLapProgress(int? lapCompleted, double? lapDistPct)
    {
        return lapCompleted is >= 0
            && lapDistPct is { } pct
            && !double.IsNaN(pct)
            && !double.IsInfinity(pct)
            && pct >= 0d;
    }

    private static bool HasStandingOrTiming(
        int? position,
        int? classPosition,
        double? f2TimeSeconds,
        double? estimatedTimeSeconds)
    {
        return position is > 0
            || classPosition is > 0
            || f2TimeSeconds is not null
            || estimatedTimeSeconds is not null;
    }

    private sealed record FocusCarSelection(
        int? RawCamCarIdx,
        int? FocusCarIdx,
        string? UnavailableReason);

    private sealed record CarProgress(
        int CarIdx,
        int LapCompleted,
        double LapDistPct,
        double? F2TimeSeconds,
        double? EstimatedTimeSeconds,
        double? LastLapTimeSeconds,
        double? BestLapTimeSeconds,
        int? Position,
        int? ClassPosition,
        int? CarClass,
        int? TireCompound)
    {
        public bool HasLapProgress => LapCompleted >= 0 && LapDistPct >= 0d;

        public double TotalLaps => LapCompleted + LapDistPct;
    }
}

internal sealed class RawCaptureTelemetryFrameReader
{
    private readonly IReadOnlyDictionary<string, TelemetryVariableSchema> _schema;
    private readonly ReadOnlyMemory<byte> _payload;

    public RawCaptureTelemetryFrameReader(
        IReadOnlyDictionary<string, TelemetryVariableSchema> schema,
        byte[] payload)
    {
        _schema = schema;
        _payload = payload;
    }

    public int ReadInt32(string variableName)
    {
        return ReadValue(variableName) switch
        {
            int value => value,
            uint value => unchecked((int)value),
            _ => 0
        };
    }

    public int? ReadNullableInt32(string variableName)
    {
        return ReadValue(variableName) switch
        {
            int value => value,
            uint value => unchecked((int)value),
            _ => null
        };
    }

    public double ReadDouble(string variableName)
    {
        return ReadValue(variableName) switch
        {
            double value => value,
            float value => value,
            int value => value,
            uint value => value,
            _ => double.NaN
        };
    }

    public double? ReadNullableDouble(string variableName)
    {
        var value = ReadDouble(variableName);
        return double.IsNaN(value) || double.IsInfinity(value) || value < 0d ? null : value;
    }

    public double? ReadNullableFiniteDouble(string variableName)
    {
        var value = ReadDouble(variableName);
        return double.IsNaN(value) || double.IsInfinity(value) ? null : value;
    }

    public bool ReadBoolean(string variableName)
    {
        return ReadValue(variableName) switch
        {
            bool value => value,
            int value => value != 0,
            uint value => value != 0,
            _ => false
        };
    }

    public bool? ReadNullableBoolean(string variableName)
    {
        return ReadValue(variableName) switch
        {
            bool value => value,
            int value => value != 0,
            uint value => value != 0,
            _ => null
        };
    }

    public int? ReadInt32ArrayElement(string variableName, int index)
    {
        if (index < 0)
        {
            return null;
        }

        return ReadValue(variableName, index) switch
        {
            int value => value,
            uint value => unchecked((int)value),
            _ => null
        };
    }

    public double? ReadDoubleArrayElement(string variableName, int index)
    {
        if (index < 0)
        {
            return null;
        }

        return ReadValue(variableName, index) switch
        {
            double value => value,
            float value => value,
            int value => value,
            uint value => value,
            _ => null
        };
    }

    public double? ReadNullableDoubleArrayElement(string variableName, int index)
    {
        var value = ReadDoubleArrayElement(variableName, index);
        return value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value.Value < 0d
            ? null
            : value;
    }

    public bool? ReadBooleanArrayElement(string variableName, int index)
    {
        if (index < 0)
        {
            return null;
        }

        return ReadValue(variableName, index) switch
        {
            bool value => value,
            int value => value != 0,
            uint value => value != 0,
            _ => null
        };
    }

    private object? ReadValue(string variableName, int index = 0)
    {
        if (!_schema.TryGetValue(variableName, out var field) || index < 0 || index >= Math.Max(1, field.Count))
        {
            return null;
        }

        var byteSize = field.ByteSize > 0 ? field.ByteSize : ByteSizeFor(field.TypeName);
        if (byteSize <= 0)
        {
            return null;
        }

        var offset = field.Offset + (index * byteSize);
        if (offset < 0 || offset + byteSize > _payload.Length)
        {
            return null;
        }

        var span = _payload.Span.Slice(offset, byteSize);
        return field.TypeName switch
        {
            "irBool" => span[0] != 0,
            "irInt" => BinaryPrimitives.ReadInt32LittleEndian(span),
            "irBitField" => BinaryPrimitives.ReadUInt32LittleEndian(span),
            "irFloat" => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(span)),
            "irDouble" => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(span)),
            _ => null
        };
    }

    private static int ByteSizeFor(string typeName)
    {
        return typeName switch
        {
            "irBool" => 1,
            "irInt" => 4,
            "irBitField" => 4,
            "irFloat" => 4,
            "irDouble" => 8,
            _ => 0
        };
    }
}
