using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.Core.Overlays;

internal enum OverlayAvailabilityReason
{
    Available,
    Disconnected,
    WaitingForTelemetry,
    StaleTelemetry,
    HiddenForSession,
    NotInCar,
    NoData,
    Error
}

internal enum OverlaySessionKind
{
    Test,
    Practice,
    Qualifying,
    Race
}

internal sealed record OverlayAvailabilitySnapshot(
    OverlayAvailabilityReason Reason,
    bool IsAvailable,
    bool IsFresh,
    string StatusText,
    double? TelemetryAgeSeconds,
    DateTimeOffset? LastUpdatedAtUtc,
    OverlaySessionKind? SessionKind)
{
    public OverlayAvailabilitySnapshot WithUnavailable(
        OverlayAvailabilityReason reason,
        string statusText)
    {
        return this with
        {
            Reason = reason,
            IsAvailable = false,
            StatusText = statusText
        };
    }
}

internal static class OverlayAvailabilityEvaluator
{
    public static readonly TimeSpan DefaultFreshTelemetryWindow = TimeSpan.FromSeconds(1.5d);

    public static OverlayAvailabilitySnapshot FromSnapshot(
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now,
        TimeSpan? freshTelemetryWindow = null,
        string staleStatusText = "waiting for fresh telemetry")
    {
        var sessionKind = CurrentSessionKind(snapshot);
        var window = freshTelemetryWindow ?? DefaultFreshTelemetryWindow;
        if (!snapshot.IsConnected)
        {
            return Unavailable(
                OverlayAvailabilityReason.Disconnected,
                "waiting for iRacing",
                snapshot,
                sessionKind,
                ageSeconds: null);
        }

        if (!snapshot.IsCollecting)
        {
            return Unavailable(
                OverlayAvailabilityReason.WaitingForTelemetry,
                "waiting for telemetry",
                snapshot,
                sessionKind,
                ageSeconds: null);
        }

        var ageSeconds = TelemetryAgeSeconds(snapshot.LastUpdatedAtUtc, now);
        if (snapshot.LastUpdatedAtUtc is null
            || ageSeconds is null
            || Math.Abs(ageSeconds.Value) > window.TotalSeconds)
        {
            return Unavailable(
                OverlayAvailabilityReason.StaleTelemetry,
                staleStatusText,
                snapshot,
                sessionKind,
                ageSeconds);
        }

        return new OverlayAvailabilitySnapshot(
            Reason: OverlayAvailabilityReason.Available,
            IsAvailable: true,
            IsFresh: true,
            StatusText: "live",
            TelemetryAgeSeconds: Math.Max(0d, ageSeconds.Value),
            LastUpdatedAtUtc: snapshot.LastUpdatedAtUtc,
            SessionKind: sessionKind);
    }

    public static OverlaySessionKind? CurrentSessionKind(LiveTelemetrySnapshot snapshot)
    {
        var session = snapshot.Models.Session;
        var context = snapshot.Context.Session;
        return ClassifySession(
            session.SessionType
            ?? session.SessionName
            ?? session.EventType
            ?? context.SessionType
            ?? context.SessionName
            ?? context.EventType);
    }

    public static OverlaySessionKind? ClassifySession(string? sessionType)
    {
        if (string.IsNullOrWhiteSpace(sessionType))
        {
            return null;
        }

        var normalized = sessionType.ToLowerInvariant();
        if (normalized.Contains("test", StringComparison.Ordinal)
            || normalized.Contains("practice", StringComparison.Ordinal))
        {
            return OverlaySessionKind.Practice;
        }

        if (normalized.Contains("qual", StringComparison.Ordinal))
        {
            return OverlaySessionKind.Qualifying;
        }

        if (normalized.Contains("race", StringComparison.Ordinal))
        {
            return OverlaySessionKind.Race;
        }

        return null;
    }

    public static bool IsAllowedForSession(
        OverlaySettings settings,
        OverlaySessionKind? sessionKind)
    {
        // These persisted fields predate descriptor-owned session behavior.
        // Retain them for compatible settings reads, but do not let them
        // silently diverge the native, browser, and localhost render paths.
        return true;
    }

    public static OverlaySessionKind? NormalizeSessionKind(OverlaySessionKind? sessionKind)
    {
        return sessionKind == OverlaySessionKind.Test
            ? OverlaySessionKind.Practice
            : sessionKind;
    }

    private static OverlayAvailabilitySnapshot Unavailable(
        OverlayAvailabilityReason reason,
        string statusText,
        LiveTelemetrySnapshot snapshot,
        OverlaySessionKind? sessionKind,
        double? ageSeconds)
    {
        return new OverlayAvailabilitySnapshot(
            Reason: reason,
            IsAvailable: false,
            IsFresh: false,
            StatusText: statusText,
            TelemetryAgeSeconds: ageSeconds is null ? null : Math.Max(0d, ageSeconds.Value),
            LastUpdatedAtUtc: snapshot.LastUpdatedAtUtc,
            SessionKind: sessionKind);
    }

    private static double? TelemetryAgeSeconds(DateTimeOffset? lastUpdatedAtUtc, DateTimeOffset now)
    {
        return lastUpdatedAtUtc is null
            ? null
            : (now - lastUpdatedAtUtc.Value).TotalSeconds;
    }
}

internal static class OverlaySessionPolicyEvaluator
{
    public static bool IsAllowed(string overlayId, OverlaySessionKind? sessionKind)
    {
        if (!OverlayBehaviorDescriptorCatalog.TryGet(overlayId, out var descriptor))
        {
            return true;
        }

        return descriptor.SessionPolicy switch
        {
            OverlaySessionPolicy.RaceOnly =>
                OverlayAvailabilityEvaluator.NormalizeSessionKind(sessionKind) == OverlaySessionKind.Race,
            OverlaySessionPolicy.KnownSessionRequired => sessionKind is not null,
            OverlaySessionPolicy.QualifyingUnsupported =>
                OverlayAvailabilityEvaluator.NormalizeSessionKind(sessionKind) != OverlaySessionKind.Qualifying,
            _ => true
        };
    }

    public static string? HiddenStatus(string overlayId, OverlaySessionKind? sessionKind)
    {
        if (IsAllowed(overlayId, sessionKind)
            || !OverlayBehaviorDescriptorCatalog.TryGet(overlayId, out var descriptor))
        {
            return null;
        }

        return descriptor.SessionPolicy switch
        {
            OverlaySessionPolicy.RaceOnly => "race only",
            OverlaySessionPolicy.KnownSessionRequired => "waiting for session",
            OverlaySessionPolicy.QualifyingUnsupported => "qualifying unsupported",
            _ => "session unavailable"
        };
    }
}
