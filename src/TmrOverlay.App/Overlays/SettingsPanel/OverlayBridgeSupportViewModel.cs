using System.Text;
using TmrOverlay.Core.OverlayBridge;

namespace TmrOverlay.App.Overlays.SettingsPanel;

/// <summary>
/// Maps safe Overlay Bridge runtime health to precise, non-actionable settings-tab copy.
/// </summary>
internal sealed record OverlayBridgeSupportViewModel(
    string AvailabilityText,
    string EnabledText,
    string PairingTransportText,
    string SchemaText,
    string ConnectedPairedClientsText,
    string LatestFrameAgeText,
    string LastSafeErrorText)
{
    private const int MaximumSafeErrorLength = 96;

    public static OverlayBridgeSupportViewModel From(OverlayBridgeSupportSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new OverlayBridgeSupportViewModel(
            AvailabilityText: snapshot.Availability == OverlayBridgeAvailability.Available
                ? "Available"
                : "Unavailable",
            EnabledText: snapshot.Enabled ? "Enabled" : "Disabled",
            PairingTransportText: PairingTransportText(snapshot),
            SchemaText: SchemaText(snapshot),
            ConnectedPairedClientsText: $"{Math.Max(0, snapshot.ConnectedPairedClientCount)} connected",
            LatestFrameAgeText: LatestFrameAgeText(snapshot.LatestFrameAge),
            LastSafeErrorText: SafeErrorText(snapshot.LastSafeError));
    }

    private static string PairingTransportText(OverlayBridgeSupportSnapshot snapshot)
    {
        if (snapshot.Availability == OverlayBridgeAvailability.Unavailable)
        {
            return "Not started — transport not implemented";
        }

        return snapshot.TransportStatus switch
        {
            OverlayBridgeTransportStatus.Disabled => "Disabled",
            OverlayBridgeTransportStatus.AwaitingPairing => "Awaiting approved pairing",
            OverlayBridgeTransportStatus.Ready => "Ready; no paired clients connected",
            OverlayBridgeTransportStatus.Connected => "Connected",
            OverlayBridgeTransportStatus.Degraded => "Degraded",
            _ => "Unavailable"
        };
    }

    private static string SchemaText(OverlayBridgeSupportSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.SchemaVersion) && string.IsNullOrWhiteSpace(snapshot.SchemaHash))
        {
            return "Not available";
        }

        if (string.IsNullOrWhiteSpace(snapshot.SchemaHash))
        {
            return snapshot.SchemaVersion!.Trim();
        }

        if (string.IsNullOrWhiteSpace(snapshot.SchemaVersion))
        {
            return snapshot.SchemaHash.Trim();
        }

        return $"{snapshot.SchemaVersion.Trim()} · {snapshot.SchemaHash.Trim()}";
    }

    private static string LatestFrameAgeText(TimeSpan? age)
    {
        if (age is null)
        {
            return "No frames";
        }

        var safeAge = age.Value < TimeSpan.Zero ? TimeSpan.Zero : age.Value;
        if (safeAge < TimeSpan.FromSeconds(1))
        {
            return "Less than 1 second ago";
        }

        if (safeAge < TimeSpan.FromMinutes(1))
        {
            return $"{Math.Floor(safeAge.TotalSeconds)} seconds ago";
        }

        if (safeAge < TimeSpan.FromHours(1))
        {
            return $"{Math.Floor(safeAge.TotalMinutes)} minutes ago";
        }

        return $"{Math.Floor(safeAge.TotalHours)} hours ago";
    }

    private static string SafeErrorText(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return "None reported";
        }

        var compact = new StringBuilder(error.Length);
        foreach (var character in error.Trim())
        {
            compact.Append(char.IsControl(character) ? ' ' : character);
        }

        var normalized = string.Join(' ', compact.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= MaximumSafeErrorLength
            ? normalized
            : normalized[..(MaximumSafeErrorLength - 3)] + "...";
    }
}
