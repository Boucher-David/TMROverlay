using System.Globalization;
using TmrOverlay.App.Telemetry;

namespace TmrOverlay.App.Diagnostics;

internal enum AppDiagnosticsSeverity
{
    Neutral,
    Info,
    Success,
    Warning,
    Error
}

internal sealed record AppDiagnosticsStatusModel(
    AppDiagnosticsSeverity Severity,
    AppDiagnosticsSeverity SupportSeverity,
    string StatusText,
    string DetailText,
    string CaptureText,
    string MessageText,
    string SupportStatusText,
    string SessionStateText,
    string CurrentIssueText,
    bool IsConnected,
    bool IsCapturing,
    bool RawCaptureEnabled,
    bool RawCaptureActive,
    bool HasActiveIssue)
{
    public static AppDiagnosticsStatusModel From(
        TelemetryCaptureStatusSnapshot snapshot,
        DateTimeOffset? nowUtc = null)
    {
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var capturePath = snapshot.CurrentCaptureDirectory ?? snapshot.LastCaptureDirectory ?? snapshot.CaptureRoot;
        var captureText = snapshot.RawCaptureEnabled
            ? $"raw: {CompactPath(capturePath)}"
            : "raw: disabled; history ready";
        var frameAge = AgeSeconds(snapshot.LastFrameCapturedAtUtc, now);
        var diskAge = AgeSeconds(snapshot.LastDiskWriteAtUtc, now);
        var bytes = FormatBytes(snapshot.TelemetryFileBytes);
        var detail = snapshot.RawCaptureEnabled
            ? $"queued {snapshot.FrameCount,7:N0}  written {snapshot.WrittenFrameCount,7:N0}  drops {snapshot.DroppedFrameCount,4:N0}  file {bytes}"
            : $"frames {snapshot.FrameCount,7:N0}  history on  raw off";
        var appWarning = string.IsNullOrWhiteSpace(snapshot.AppWarning)
            ? null
            : $"warning: {Trim(snapshot.AppWarning)}";

        if (!string.IsNullOrWhiteSpace(snapshot.LastError))
        {
            return Build(
                snapshot,
                AppDiagnosticsSeverity.Error,
                AppDiagnosticsSeverity.Error,
                "Capture error",
                detail,
                captureText,
                $"error: {Trim(snapshot.LastError)}",
                "Error",
                hasActiveIssue: true);
        }

        if (!snapshot.IsConnected)
        {
            return Build(
                snapshot,
                appWarning is null ? AppDiagnosticsSeverity.Neutral : AppDiagnosticsSeverity.Warning,
                appWarning is null ? AppDiagnosticsSeverity.Neutral : AppDiagnosticsSeverity.Warning,
                "Waiting for iRacing",
                "collector idle",
                captureText,
                Combine(appWarning, "health: waiting is expected before iRacing is running"),
                appWarning is null ? "Waiting for iRacing" : "Warning",
                hasActiveIssue: appWarning is not null);
        }

        if (!snapshot.IsCapturing)
        {
            return Build(
                snapshot,
                appWarning is null ? AppDiagnosticsSeverity.Info : AppDiagnosticsSeverity.Warning,
                appWarning is null ? AppDiagnosticsSeverity.Info : AppDiagnosticsSeverity.Warning,
                "Connected, waiting for telemetry",
                "waiting for first telemetry frame",
                captureText,
                Combine(appWarning, "health: live telemetry starts after session data arrives"),
                appWarning is null ? "Connected" : "Warning",
                hasActiveIssue: appWarning is not null);
        }

        if (snapshot.RawCaptureEnabled && snapshot.FrameCount > 0 && snapshot.WrittenFrameCount == 0)
        {
            return Build(
                snapshot,
                AppDiagnosticsSeverity.Error,
                AppDiagnosticsSeverity.Error,
                "Frames queued, not written",
                detail,
                captureText,
                "error: telemetry frames arrived but disk writer has not confirmed writes",
                "Error",
                hasActiveIssue: true);
        }

        if (snapshot.RawCaptureEnabled && snapshot.WrittenFrameCount > snapshot.FrameCount + 2)
        {
            return Build(
                snapshot,
                AppDiagnosticsSeverity.Warning,
                AppDiagnosticsSeverity.Warning,
                "Capture counters inconsistent",
                detail,
                captureText,
                "warning: written frame count is ahead of queued frame count",
                "Warning",
                hasActiveIssue: true);
        }

        if (snapshot.DroppedFrameCount > 0)
        {
            return Build(
                snapshot,
                AppDiagnosticsSeverity.Warning,
                AppDiagnosticsSeverity.Warning,
                "Collecting with dropped frames",
                detail,
                captureText,
                "warning: capture queue overflowed; disk may be too slow",
                "Warning",
                hasActiveIssue: true);
        }

        if (frameAge is not null && frameAge > 5)
        {
            return Build(
                snapshot,
                AppDiagnosticsSeverity.Error,
                AppDiagnosticsSeverity.Error,
                "Telemetry frames stalled",
                detail,
                captureText,
                $"error: no SDK frame for {frameAge:N0}s; sim may be paused/disconnected",
                "Error",
                hasActiveIssue: true);
        }

        if (snapshot.RawCaptureEnabled && diskAge is not null && diskAge > 5)
        {
            return Build(
                snapshot,
                AppDiagnosticsSeverity.Error,
                AppDiagnosticsSeverity.Error,
                "Disk writes stalled",
                detail,
                captureText,
                $"error: no telemetry.bin write confirmation for {diskAge:N0}s",
                "Error",
                hasActiveIssue: true);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.LastWarning))
        {
            return Build(
                snapshot,
                AppDiagnosticsSeverity.Warning,
                AppDiagnosticsSeverity.Warning,
                "Collecting with warning",
                detail,
                captureText,
                $"warning: {Trim(snapshot.LastWarning)}",
                "Warning",
                hasActiveIssue: true);
        }

        if (appWarning is not null)
        {
            return Build(
                snapshot,
                AppDiagnosticsSeverity.Warning,
                AppDiagnosticsSeverity.Warning,
                "Build may be stale",
                detail,
                captureText,
                appWarning,
                "Warning",
                hasActiveIssue: true);
        }

        var healthMessage = snapshot.RawCaptureEnabled
            ? $"health: live frames ok; last frame {FormatAge(frameAge)}, disk {FormatAge(diskAge)}"
            : $"health: live analysis ok; last frame {FormatAge(frameAge)}";
        return Build(
            snapshot,
            AppDiagnosticsSeverity.Success,
            AppDiagnosticsSeverity.Success,
            snapshot.RawCaptureEnabled ? "Collecting raw telemetry" : "Analyzing live telemetry",
            detail,
            captureText,
            healthMessage,
            "Live telemetry",
            hasActiveIssue: false);
    }

    private static AppDiagnosticsStatusModel Build(
        TelemetryCaptureStatusSnapshot snapshot,
        AppDiagnosticsSeverity severity,
        AppDiagnosticsSeverity supportSeverity,
        string statusText,
        string detailText,
        string captureText,
        string messageText,
        string supportStatusText,
        bool hasActiveIssue)
    {
        return new AppDiagnosticsStatusModel(
            Severity: severity,
            SupportSeverity: supportSeverity,
            StatusText: statusText,
            DetailText: detailText,
            CaptureText: captureText,
            MessageText: messageText,
            SupportStatusText: supportStatusText,
            SessionStateText: BuildSessionStateText(snapshot),
            CurrentIssueText: BuildCurrentIssueText(snapshot),
            IsConnected: snapshot.IsConnected,
            IsCapturing: snapshot.IsCapturing,
            RawCaptureEnabled: snapshot.RawCaptureEnabled,
            RawCaptureActive: snapshot.RawCaptureActive,
            HasActiveIssue: hasActiveIssue);
    }

    private static string BuildSessionStateText(TelemetryCaptureStatusSnapshot snapshot)
    {
        if (snapshot.RawCaptureActive)
        {
            return $"Enhanced iRacing telemetry capture active ({FormatCount(snapshot.WrittenFrameCount)} frames written)";
        }

        if (snapshot.RawCaptureEnabled)
        {
            return "Enhanced iRacing telemetry capture requested; starts with live data";
        }

        if (snapshot.IsCapturing)
        {
            return $"Receiving live telemetry ({FormatCount(snapshot.FrameCount)} frames)";
        }

        if (snapshot.IsConnected)
        {
            return "iRacing connected; waiting for live session data";
        }

        return "Not connected; start iRacing when ready";
    }

    private static string BuildCurrentIssueText(TelemetryCaptureStatusSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.LastError))
        {
            return $"Error: {snapshot.LastError}";
        }

        if (!string.IsNullOrWhiteSpace(snapshot.LastWarning))
        {
            return $"Warning: {snapshot.LastWarning}";
        }

        if (!string.IsNullOrWhiteSpace(snapshot.AppWarning))
        {
            return $"App warning: {snapshot.AppWarning}";
        }

        if (!snapshot.IsConnected)
        {
            return "No active issue. Waiting is expected before iRacing is running.";
        }

        if (!snapshot.IsCapturing)
        {
            return "No active issue. Live telemetry starts after session data arrives.";
        }

        return "No active issue recorded.";
    }

    private static double? AgeSeconds(DateTimeOffset? timestampUtc, DateTimeOffset now)
    {
        return timestampUtc is null
            ? null
            : Math.Max(0d, (now - timestampUtc.Value).TotalSeconds);
    }

    private static string FormatAge(double? seconds)
    {
        return seconds is null ? "n/a" : $"{seconds.Value:N1}s ago";
    }

    private static string FormatBytes(long? bytes)
    {
        if (bytes is null)
        {
            return "n/a";
        }

        if (bytes < 1024)
        {
            return $"{bytes:N0} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes.Value / 1024d:N1} KB";
        }

        return $"{bytes.Value / 1024d / 1024d:N1} MB";
    }

    private static string CompactPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "not resolved";
        }

        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length <= 3)
        {
            return normalized;
        }

        return $".../{string.Join('/', segments.TakeLast(3))}";
    }

    private static string Trim(string value)
    {
        const int maxLength = 96;
        var normalized = value.ReplaceLineEndings(" ");
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..(maxLength - 1)] + "...";
    }

    private static string Combine(string? first, string second)
    {
        return string.IsNullOrWhiteSpace(first)
            ? second
            : $"{first} | {second}";
    }

    private static string FormatCount(int value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
