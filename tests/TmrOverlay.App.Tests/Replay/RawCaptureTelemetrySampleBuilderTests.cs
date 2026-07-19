using System.Buffers.Binary;
using TmrOverlay.App.Replay;
using TmrOverlay.App.Telemetry;
using Xunit;

namespace TmrOverlay.App.Tests.Replay;

public sealed class RawCaptureTelemetrySampleBuilderTests
{
    [Fact]
    public void Build_IgnoresDefaultTimingAndSpatialPlaceholders()
    {
        var frame = RawFrameBuilder.Create()
            .AddInt("PlayerCarIdx")
            .AddInt("CamCarIdx")
            .AddIntArray("CarIdxClass", 64)
            .AddIntArray("CarIdxLapCompleted", 64)
            .AddDoubleArray("CarIdxLapDistPct", 64)
            .AddIntArray("CarIdxTrackSurface", 64)
            .AddIntArray("CarIdxPosition", 64)
            .AddIntArray("CarIdxClassPosition", 64)
            .AddDoubleArray("CarIdxF2Time", 64)
            .AddDoubleArray("CarIdxEstTime", 64)
            .AddDoubleArray("CarIdxLastLapTime", 64)
            .AddDoubleArray("CarIdxBestLapTime", 64)
            .Build();

        frame.WriteInt("PlayerCarIdx", 0);
        frame.WriteInt("CamCarIdx", 0);

        var sample = new RawCaptureTelemetrySampleBuilder(frame.Schema).Build(new TelemetryFrameEnvelope(
            CapturedAtUtc: DateTimeOffset.UtcNow,
            FrameIndex: 1,
            SessionTick: 1,
            SessionInfoUpdate: 1,
            SessionTime: 10d,
            Payload: frame.Payload));

        Assert.Null(sample.FocusCarIdx);
        Assert.Equal("cam_car_progress_unavailable", sample.FocusUnavailableReason);
        Assert.NotNull(sample.FocusClassCars);
        Assert.Empty(sample.FocusClassCars!);
        Assert.NotNull(sample.AllCars);
        Assert.Empty(sample.AllCars!);
    }

    [Fact]
    public void Build_PreservesSpatialLapDistanceWhenLapCompletedIsUnavailable()
    {
        var frame = RawFrameBuilder.Create()
            .AddInt("PlayerCarIdx")
            .AddInt("CamCarIdx")
            .AddIntArray("CarIdxClass", 64)
            .AddIntArray("CarIdxLapCompleted", 64)
            .AddDoubleArray("CarIdxLapDistPct", 64)
            .AddIntArray("CarIdxTrackSurface", 64)
            .Build();

        for (var carIdx = 0; carIdx < 64; carIdx++)
        {
            frame.WriteIntArray("CarIdxClass", carIdx, -1);
            frame.WriteIntArray("CarIdxLapCompleted", carIdx, -1);
            frame.WriteDoubleArray("CarIdxLapDistPct", carIdx, -1d);
            frame.WriteIntArray("CarIdxTrackSurface", carIdx, 0);
        }

        frame.WriteInt("PlayerCarIdx", 10);
        frame.WriteInt("CamCarIdx", 12);
        WriteSpatialCar(frame, carIdx: 10, carClass: 4098, lapDistPct: 0.15d);
        WriteSpatialCar(frame, carIdx: 12, carClass: 4098, lapDistPct: 0.42d);
        WriteSpatialCar(frame, carIdx: 14, carClass: 4098, lapDistPct: 0.70d);

        var sample = new RawCaptureTelemetrySampleBuilder(frame.Schema).Build(new TelemetryFrameEnvelope(
            CapturedAtUtc: DateTimeOffset.UtcNow,
            FrameIndex: 1,
            SessionTick: 1,
            SessionInfoUpdate: 1,
            SessionTime: 10d,
            Payload: frame.Payload));

        Assert.Equal(12, sample.FocusCarIdx);
        Assert.Equal(-1, sample.FocusLapCompleted);
        Assert.Equal(0.42d, sample.FocusLapDistPct);
        Assert.Equal(-1, sample.TeamLapCompleted);
        Assert.Equal(0.15d, sample.TeamLapDistPct);

        Assert.NotNull(sample.FocusClassCars);
        var focusClassCars = sample.FocusClassCars!;
        var focusCar = Assert.Single(focusClassCars, car => car.CarIdx == 12);
        Assert.Equal(-1, focusCar.LapCompleted);
        Assert.Equal(0.42d, focusCar.LapDistPct);

        Assert.NotNull(sample.AllCars);
        var allCars = sample.AllCars!;
        var spatialOpponent = Assert.Single(allCars, car => car.CarIdx == 14);
        Assert.Equal(-1, spatialOpponent.LapCompleted);
        Assert.Equal(0.70d, spatialOpponent.LapDistPct);
    }

    [Fact]
    public void Build_UsesCapturedSeventyTwoSlotCarIdxSchemaInsteadOfLegacySixtyFourSlotLimit()
    {
        // `capture-20260714-193157-308` is a real Windows capture whose
        // 27 CarIdx arrays all expose 72 elements. Populate its last valid
        // index so this regression proves replay uses captured schema shape.
        const int capturedCarIdxSlotCount = 72;
        const int expandedCarIdx = capturedCarIdxSlotCount - 1;
        var frame = RawFrameBuilder.Create()
            .AddInt("PlayerCarIdx")
            .AddInt("CamCarIdx")
            .AddIntArray("CarIdxClass", capturedCarIdxSlotCount)
            .AddIntArray("CarIdxLapCompleted", capturedCarIdxSlotCount)
            .AddDoubleArray("CarIdxLapDistPct", capturedCarIdxSlotCount)
            .AddIntArray("CarIdxTrackSurface", capturedCarIdxSlotCount)
            .AddIntArray("CarIdxPosition", capturedCarIdxSlotCount)
            .AddIntArray("CarIdxClassPosition", capturedCarIdxSlotCount)
            .AddDoubleArray("CarIdxF2Time", capturedCarIdxSlotCount)
            .AddDoubleArray("CarIdxEstTime", capturedCarIdxSlotCount)
            .AddDoubleArray("CarIdxLastLapTime", capturedCarIdxSlotCount)
            .AddDoubleArray("CarIdxBestLapTime", capturedCarIdxSlotCount)
            .Build();

        for (var carIdx = 0; carIdx <= expandedCarIdx; carIdx++)
        {
            frame.WriteIntArray("CarIdxClass", carIdx, -1);
            frame.WriteIntArray("CarIdxLapCompleted", carIdx, -1);
            frame.WriteDoubleArray("CarIdxLapDistPct", carIdx, -1d);
            frame.WriteIntArray("CarIdxTrackSurface", carIdx, 0);
            frame.WriteIntArray("CarIdxPosition", carIdx, -1);
            frame.WriteIntArray("CarIdxClassPosition", carIdx, -1);
            frame.WriteDoubleArray("CarIdxF2Time", carIdx, -1d);
            frame.WriteDoubleArray("CarIdxEstTime", carIdx, -1d);
            frame.WriteDoubleArray("CarIdxLastLapTime", carIdx, -1d);
            frame.WriteDoubleArray("CarIdxBestLapTime", carIdx, -1d);
        }

        frame.WriteInt("PlayerCarIdx", expandedCarIdx);
        frame.WriteInt("CamCarIdx", expandedCarIdx);
        frame.WriteIntArray("CarIdxClass", expandedCarIdx, 4098);
        frame.WriteIntArray("CarIdxLapCompleted", expandedCarIdx, 7);
        frame.WriteDoubleArray("CarIdxLapDistPct", expandedCarIdx, 0.25d);
        frame.WriteIntArray("CarIdxTrackSurface", expandedCarIdx, 3);
        frame.WriteIntArray("CarIdxPosition", expandedCarIdx, 1);
        frame.WriteIntArray("CarIdxClassPosition", expandedCarIdx, 1);

        var sample = new RawCaptureTelemetrySampleBuilder(frame.Schema).Build(new TelemetryFrameEnvelope(
            CapturedAtUtc: DateTimeOffset.UtcNow,
            FrameIndex: 1,
            SessionTick: 1,
            SessionInfoUpdate: 1,
            SessionTime: 10d,
            Payload: frame.Payload));

        Assert.Equal(expandedCarIdx, sample.FocusCarIdx);
        Assert.Equal(expandedCarIdx, sample.LeaderCarIdx);
        Assert.Contains(sample.AllCars ?? [], car => car.CarIdx == expandedCarIdx);
    }

    private static void WriteSpatialCar(RawFrame frame, int carIdx, int carClass, double lapDistPct)
    {
        frame.WriteIntArray("CarIdxClass", carIdx, carClass);
        frame.WriteIntArray("CarIdxLapCompleted", carIdx, -1);
        frame.WriteDoubleArray("CarIdxLapDistPct", carIdx, lapDistPct);
        frame.WriteIntArray("CarIdxTrackSurface", carIdx, 3);
    }

    private sealed class RawFrameBuilder
    {
        private readonly List<Field> _fields = [];
        private int _offset;

        public static RawFrameBuilder Create() => new();

        public RawFrameBuilder AddInt(string name) => Add(name, "irInt", 1, 4);

        public RawFrameBuilder AddIntArray(string name, int count) => Add(name, "irInt", count, 4);

        public RawFrameBuilder AddDoubleArray(string name, int count) => Add(name, "irDouble", count, 8);

        public RawFrame Build()
        {
            return new RawFrame(
                _fields.ToDictionary(
                    field => field.Name,
                    field => new TelemetryVariableSchema(
                        Name: field.Name,
                        TypeName: field.TypeName,
                        TypeCode: 0,
                        Count: field.Count,
                        Offset: field.Offset,
                        ByteSize: field.ByteSize,
                        Length: field.ByteSize * field.Count,
                        Unit: string.Empty,
                        Description: string.Empty),
                    StringComparer.OrdinalIgnoreCase),
                new byte[_offset]);
        }

        private RawFrameBuilder Add(string name, string typeName, int count, int byteSize)
        {
            _fields.Add(new Field(name, typeName, count, _offset, byteSize));
            _offset += count * byteSize;
            return this;
        }

        private sealed record Field(string Name, string TypeName, int Count, int Offset, int ByteSize);
    }

    private sealed class RawFrame(
        IReadOnlyDictionary<string, TelemetryVariableSchema> schema,
        byte[] payload)
    {
        public IReadOnlyDictionary<string, TelemetryVariableSchema> Schema { get; } = schema;

        public byte[] Payload { get; } = payload;

        public void WriteInt(string name, int value)
        {
            var field = Schema[name];
            BinaryPrimitives.WriteInt32LittleEndian(Payload.AsSpan(field.Offset, field.ByteSize), value);
        }

        public void WriteIntArray(string name, int index, int value)
        {
            var field = Schema[name];
            BinaryPrimitives.WriteInt32LittleEndian(Payload.AsSpan(field.Offset + index * field.ByteSize, field.ByteSize), value);
        }

        public void WriteDoubleArray(string name, int index, double value)
        {
            var field = Schema[name];
            BinaryPrimitives.WriteInt64LittleEndian(
                Payload.AsSpan(field.Offset + index * field.ByteSize, field.ByteSize),
                BitConverter.DoubleToInt64Bits(value));
        }
    }
}
