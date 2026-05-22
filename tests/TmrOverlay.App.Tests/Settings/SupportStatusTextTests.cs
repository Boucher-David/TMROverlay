using TmrOverlay.App.Overlays.SettingsPanel;
using TmrOverlay.App.Telemetry;
using TmrOverlay.Core.AppInfo;
using Xunit;

namespace TmrOverlay.App.Tests.Settings;

public sealed class SupportStatusTextTests
{
    [Fact]
    public void WaitingForIRacing_IsPresentedAsExpectedIdleState()
    {
        var snapshot = Snapshot();

        var status = SupportStatusText.AppStatus(snapshot);

        Assert.Equal("Waiting for iRacing", status.Text);
        Assert.Equal(SupportStatusLevel.Neutral, status.Level);
        Assert.Equal("Not connected; start iRacing when ready", SupportStatusText.SessionStateText(snapshot));
        Assert.Equal(
            "No active issue. Waiting is expected before iRacing is running.",
            SupportStatusText.CurrentIssueText(snapshot));
    }

    [Fact]
    public void ConnectedWithoutFrames_ExplainsTelemetryIsStillWaiting()
    {
        var snapshot = Snapshot(isConnected: true);

        var status = SupportStatusText.AppStatus(snapshot);

        Assert.Equal("Connected", status.Text);
        Assert.Equal(SupportStatusLevel.Info, status.Level);
        Assert.Equal("iRacing connected; waiting for live session data", SupportStatusText.SessionStateText(snapshot));
        Assert.Equal(
            "No active issue. Live telemetry starts after session data arrives.",
            SupportStatusText.CurrentIssueText(snapshot));
    }

    [Fact]
    public void LiveTelemetry_ReportsFrameCountWithoutRaisingIssue()
    {
        var snapshot = Snapshot(isConnected: true, isCapturing: true, frameCount: 1234);

        var status = SupportStatusText.AppStatus(snapshot);

        Assert.Equal("Live telemetry", status.Text);
        Assert.Equal(SupportStatusLevel.Success, status.Level);
        Assert.Equal("Receiving live telemetry (1,234 frames)", SupportStatusText.SessionStateText(snapshot));
        Assert.Equal("No active issue recorded.", SupportStatusText.CurrentIssueText(snapshot));
    }

    [Fact]
    public void DiagnosticsCaptureRequest_ExplainsWhenCaptureStarts()
    {
        var snapshot = Snapshot(rawCaptureEnabled: true);

        Assert.Equal(
            "Enhanced iRacing telemetry capture requested; starts with live data",
            SupportStatusText.SessionStateText(snapshot));
    }

    [Fact]
    public void LatestBundleText_CompactsLongFileNamesForSettingsUi()
    {
        const string path = @"C:\Users\driver\AppData\Local\TMR Overlay\diagnostics\bmw-m4-gt3-evo-gesamtstrecke-vln-history-analysis-20260520-142233-123.zip";

        Assert.Equal(
            "bmw-m4-gt3-e...-142233-123.zip",
            SupportStatusText.LatestBundleValueText(path));
        Assert.Equal(
            "Latest bundle: bmw-m4-gt3-e...-142233-123.zip",
            SupportStatusText.LatestBundleDisplayText(path));
    }

    [Fact]
    public void AppVersionText_IncludesInformationalBuildWhenDifferent()
    {
        var version = new AppVersionInfo
        {
            ProductName = "Tech Mates Racing Overlay",
            Version = "0.12.0",
            InformationalVersion = "0.12.0+abc123",
            RuntimeVersion = ".NET 8.0",
            OperatingSystem = "Windows",
            ProcessArchitecture = "X64"
        };

        Assert.Equal("v0.12.0+abc123", SupportStatusText.AppVersionText(version));
    }

    [Fact]
    public void AppVersionText_NormalizesFourPartAssemblyVersion()
    {
        var version = new AppVersionInfo
        {
            ProductName = "Tech Mates Racing Overlay",
            Version = "0.12.0.0",
            InformationalVersion = "0.12.0",
            RuntimeVersion = ".NET 8.0",
            OperatingSystem = "Windows",
            ProcessArchitecture = "X64"
        };

        Assert.Equal("v0.12.0", SupportStatusText.AppVersionText(version));
    }

    private static TelemetryCaptureStatusSnapshot Snapshot(
        bool isConnected = false,
        bool isCapturing = false,
        bool rawCaptureEnabled = false,
        bool rawCaptureActive = false,
        int frameCount = 0)
    {
        return new TelemetryCaptureStatusSnapshot(
            IsConnected: isConnected,
            IsCapturing: isCapturing,
            RawCaptureEnabled: rawCaptureEnabled,
            RawCaptureActive: rawCaptureActive,
            CaptureRoot: null,
            CurrentCaptureDirectory: null,
            LastCaptureDirectory: null,
            FrameCount: frameCount,
            WrittenFrameCount: 0,
            DroppedFrameCount: 0,
            TelemetryFileBytes: null,
            CaptureStartedAtUtc: null,
            LastFrameCapturedAtUtc: null,
            LastDiskWriteAtUtc: null,
            AppWarning: null,
            LastWarning: null,
            LastError: null,
            LastIssueAtUtc: null);
    }
}
