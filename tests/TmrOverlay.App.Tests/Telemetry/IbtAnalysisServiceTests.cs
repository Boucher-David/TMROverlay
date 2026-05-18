using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TmrOverlay.App.Telemetry;
using Xunit;

namespace TmrOverlay.App.Tests.Telemetry;

public sealed class IbtAnalysisServiceTests
{
    [Fact]
    public void FromConfiguration_DefaultsToOptInAnalysisAndTelemetryLogging()
    {
        var configuration = new ConfigurationBuilder().Build();

        var options = IbtAnalysisOptions.FromConfiguration(configuration);

        Assert.True(options.Enabled);
        Assert.True(options.TelemetryLoggingEnabled);
        Assert.EndsWith(Path.Combine("iRacing", "telemetry"), options.TelemetryRoot);
        Assert.Equal(60, options.MaxIRacingExitWaitSeconds);
        Assert.False(options.CopyIbtIntoCaptureDirectory);
    }

    [Fact]
    public void IbtTelemetryFile_Read_ParsesSyntheticHeaderSchemaAndSessionInfo()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-ibt-parser-test", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var ibtPath = Path.Combine(root, "sample.ibt");
            WriteSyntheticIbt(ibtPath, DateTimeOffset.UtcNow);

            var ibt = IbtTelemetryFile.Read(ibtPath, CancellationToken.None);

            Assert.Equal(2, ibt.Header.Version);
            Assert.Equal(60, ibt.Header.TickRate);
            Assert.Equal(9, ibt.Fields.Count);
            Assert.Equal(4, ibt.DiskHeader.RecordCount);
            Assert.Contains(ibt.Fields, field => field.Name == "Lat" && field.TypeName == "irDouble");
            Assert.Contains(ibt.Fields, field => field.Name == "Alt" && field.TypeName == "irDouble");
            Assert.Contains("WeekendInfo:", ibt.SessionInfoYaml);
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
    public async Task WriteAsync_WritesCompactSidecarsAndComparesLiveSchema()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-ibt-analysis-test", Guid.NewGuid().ToString("N"));
        try
        {
            var telemetryRoot = Path.Combine(root, "ibt");
            var captureDirectory = Path.Combine(root, "captures", "capture-test");
            Directory.CreateDirectory(telemetryRoot);
            Directory.CreateDirectory(captureDirectory);

            var startedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
            WriteSyntheticIbt(Path.Combine(telemetryRoot, "sample.ibt"), startedAtUtc);
            WriteCaptureManifest(captureDirectory, startedAtUtc);
            WriteLiveSchema(captureDirectory);

            var service = new IbtAnalysisService(
                new IbtAnalysisOptions
                {
                    Enabled = true,
                    TelemetryRoot = telemetryRoot,
                    MaxCandidateAgeMinutes = 60,
                    MaxAnalysisMilliseconds = 10_000,
                    MaxSampledRecords = 100,
                    MinStableAgeSeconds = 0
                },
                NullLogger<IbtAnalysisService>.Instance);

            var result = await service.WriteAsync(captureDirectory);

            Assert.Equal(IbtAnalysisStatus.Succeeded, result.Status);
            Assert.Equal(9, result.FieldCount);
            Assert.True(File.Exists(Path.Combine(captureDirectory, "ibt-analysis", "status.json")));
            Assert.True(File.Exists(Path.Combine(captureDirectory, "ibt-analysis", "ibt-schema-summary.json")));
            Assert.True(File.Exists(Path.Combine(captureDirectory, "ibt-analysis", "ibt-local-car-summary.json")));
            var comparisonJson = File.ReadAllText(Path.Combine(captureDirectory, "ibt-analysis", "ibt-vs-live-schema.json"));
            using var comparison = JsonDocument.Parse(comparisonJson);
            var ibtOnly = comparison.RootElement.GetProperty("onlyInIbtFieldNames").EnumerateArray().Select(item => item.GetString()).ToArray();
            Assert.Contains("Lat", ibtOnly);
            Assert.Contains("Lon", ibtOnly);

            var localCarJson = File.ReadAllText(Path.Combine(captureDirectory, "ibt-analysis", "ibt-local-car-summary.json"));
            using var localCar = JsonDocument.Parse(localCarJson);
            Assert.True(localCar.RootElement.GetProperty("trackMapReadiness").GetProperty("isCandidate").GetBoolean());
            Assert.Contains(
                localCar.RootElement.GetProperty("missingOpponentContextFields").EnumerateArray(),
                item => item.GetString() == "CarIdxF2Time");
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
    public async Task WriteAsync_WritesSkippedStatusWhenTelemetryRootIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-ibt-missing-root-test", Guid.NewGuid().ToString("N"));
        try
        {
            var captureDirectory = Path.Combine(root, "captures", "capture-test");
            Directory.CreateDirectory(captureDirectory);
            WriteCaptureManifest(captureDirectory, DateTimeOffset.UtcNow);
            WriteLiveSchema(captureDirectory);

            var service = new IbtAnalysisService(
                new IbtAnalysisOptions
                {
                    Enabled = true,
                    TelemetryRoot = Path.Combine(root, "missing"),
                    MinStableAgeSeconds = 0
                },
                NullLogger<IbtAnalysisService>.Instance);

            var result = await service.WriteAsync(captureDirectory);

            Assert.Equal(IbtAnalysisStatus.Skipped, result.Status);
            Assert.Equal("telemetry_root_missing", result.Reason);
            Assert.True(File.Exists(Path.Combine(captureDirectory, "ibt-analysis", "status.json")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void WriteSyntheticIbt(string path, DateTimeOffset startedAtUtc)
    {
        var fields = new[]
        {
            new TestIbtField("SessionTime", 5, 0, 1, "s", "Seconds since session start"),
            new TestIbtField("LapDist", 4, 8, 1, "m", "Meters traveled from S/F this lap"),
            new TestIbtField("LapDistPct", 4, 12, 1, "%", "Percentage distance around lap"),
            new TestIbtField("Lat", 5, 16, 1, "deg", "Latitude in decimal degrees"),
            new TestIbtField("Lon", 5, 24, 1, "deg", "Longitude in decimal degrees"),
            new TestIbtField("Alt", 5, 32, 1, "m", "Altitude in meters"),
            new TestIbtField("FuelLevel", 4, 40, 1, "l", "Fuel level"),
            new TestIbtField("Speed", 4, 44, 1, "m/s", "Vehicle speed"),
            new TestIbtField("Gear", 2, 48, 1, "", "Selected gear")
        };
        const int telemetryHeaderBytes = 112;
        const int diskHeaderBytes = 32;
        const int varHeaderBytes = 144;
        const int bufferLength = 52;
        const int recordCount = 4;
        var varHeaderOffset = telemetryHeaderBytes + diskHeaderBytes;
        var sessionInfo = Encoding.UTF8.GetBytes("""
            WeekendInfo:
              TrackName: test_track
            SessionInfo:
              CurrentSessionNum: 0
            """);
        var sessionInfoOffset = varHeaderOffset + fields.Length * varHeaderBytes;
        var bufferOffset = sessionInfoOffset + sessionInfo.Length;

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(2);
        writer.Write(1);
        writer.Write(60);
        writer.Write(0);
        writer.Write(sessionInfo.Length);
        writer.Write(sessionInfoOffset);
        writer.Write(fields.Length);
        writer.Write(varHeaderOffset);
        writer.Write(1);
        writer.Write(bufferLength);
        writer.Write(new byte[12]);
        writer.Write(bufferOffset);
        writer.Write(new byte[telemetryHeaderBytes - 56]);

        writer.Write(startedAtUtc.ToUnixTimeSeconds());
        writer.Write(0d);
        writer.Write(3d);
        writer.Write(1);
        writer.Write(recordCount);

        foreach (var field in fields)
        {
            writer.Write(field.TypeCode);
            writer.Write(field.Offset);
            writer.Write(field.Count);
            writer.Write(0);
            WriteFixedString(writer, field.Name, 32);
            WriteFixedString(writer, field.Description, 64);
            WriteFixedString(writer, field.Unit, 32);
        }

        writer.Write(sessionInfo);
        for (var record = 0; record < recordCount; record++)
        {
            writer.Write(record * 0.5d);
            writer.Write(100f + record);
            writer.Write(0.1f + record * 0.01f);
            writer.Write(35.1d + record * 0.001d);
            writer.Write(-80.2d - record * 0.001d);
            writer.Write(280d + record);
            writer.Write(42f - record);
            writer.Write(20f + record);
            writer.Write(record);
        }
    }

    private static void WriteFixedString(BinaryWriter writer, string value, int byteCount)
    {
        var bytes = new byte[byteCount];
        var encoded = Encoding.UTF8.GetBytes(value);
        Array.Copy(encoded, bytes, Math.Min(encoded.Length, bytes.Length));
        writer.Write(bytes);
    }

    private static void WriteCaptureManifest(string captureDirectory, DateTimeOffset startedAtUtc)
    {
        File.WriteAllText(
            Path.Combine(captureDirectory, "capture-manifest.json"),
            JsonSerializer.Serialize(new CaptureManifest
            {
                CaptureId = "capture-test",
                StartedAtUtc = startedAtUtc,
                FinishedAtUtc = startedAtUtc.AddMinutes(10),
                TelemetryFile = "telemetry.bin",
                SchemaFile = "telemetry-schema.json",
                LatestSessionInfoFile = "latest-session.yaml",
                SessionInfoDirectory = "session-info",
                SdkVersion = 2,
                TickRate = 60,
                BufferLength = 32,
                VariableCount = 2,
                FrameCount = 10
            }));
    }

    private static void WriteLiveSchema(string captureDirectory)
    {
        var schema = new[]
        {
            new TelemetryVariableSchema("SessionTime", "irDouble", 5, 1, 0, 8, 8, "s", "Seconds since session start"),
            new TelemetryVariableSchema("LapDist", "irFloat", 4, 1, 8, 4, 4, "m", "Meters traveled from S/F this lap")
        };
        File.WriteAllText(
            Path.Combine(captureDirectory, "telemetry-schema.json"),
            JsonSerializer.Serialize(schema));
        File.WriteAllBytes(Path.Combine(captureDirectory, "telemetry.bin"), new byte[32]);
    }

    private sealed record TestIbtField(
        string Name,
        int TypeCode,
        int Offset,
        int Count,
        string Unit,
        string Description);
}
