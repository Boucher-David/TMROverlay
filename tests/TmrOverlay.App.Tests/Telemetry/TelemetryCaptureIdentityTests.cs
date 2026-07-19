using TmrOverlay.App.Telemetry;
using Xunit;

namespace TmrOverlay.App.Tests.Telemetry;

public sealed class TelemetryCaptureIdentityTests
{
    [Fact]
    public void CreateUniqueCaptureId_ReservesReconnectSuffixesInsteadOfReusingTimestampDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-capture-identity-test", Guid.NewGuid().ToString("N"));
        const string timestampStem = "capture-20260713-123456-789";
        try
        {
            Directory.CreateDirectory(Path.Combine(root, timestampStem));

            Assert.Equal(
                $"{timestampStem}-r001",
                TelemetryCaptureSession.CreateUniqueCaptureId(root, timestampStem));

            Directory.CreateDirectory(Path.Combine(root, $"{timestampStem}-r001"));
            Assert.Equal(
                $"{timestampStem}-r002",
                TelemetryCaptureSession.CreateUniqueCaptureId(root, timestampStem));
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
    public void BuildCollectionSourceId_DistinguishesCollectionsWithTheSameTimestampStem()
    {
        const string sourceStem = "session-20260713-123456-789";

        var first = TelemetryCaptureHostedService.BuildCollectionSourceId(sourceStem, 1);
        var reconnect = TelemetryCaptureHostedService.BuildCollectionSourceId(sourceStem, 2);

        Assert.Equal("session-20260713-123456-789-g000001", first);
        Assert.Equal("session-20260713-123456-789-g000002", reconnect);
        Assert.NotEqual(first, reconnect);
    }

    [Fact]
    public void BuildRollingCollectionSourceStem_UsesNonceToPreventCrossProcessTimestampCollision()
    {
        var startedAtUtc = DateTimeOffset.Parse("2026-07-13T12:34:56.789Z");

        var first = TelemetryCaptureHostedService.BuildRollingCollectionSourceStem(
            startedAtUtc,
            Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var restarted = TelemetryCaptureHostedService.BuildRollingCollectionSourceStem(
            startedAtUtc,
            Guid.Parse("22222222-2222-2222-2222-222222222222"));

        Assert.Equal("session-20260713-123456-789-11111111111111111111111111111111", first);
        Assert.Equal("session-20260713-123456-789-22222222222222222222222222222222", restarted);
        Assert.NotEqual(first, restarted);
    }
}
