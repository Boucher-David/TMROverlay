using TmrOverlay.App.OverlayBridge;
using TmrOverlay.App.Overlays.SettingsPanel;
using TmrOverlay.Core.OverlayBridge;
using Xunit;

namespace TmrOverlay.App.Tests.Settings;

public sealed class OverlayBridgeSupportViewModelTests
{
    [Fact]
    public void UnavailableDefault_IsExplicitlyDisabledAndDoesNotImplyNetworking()
    {
        var state = new OverlayBridgeSupportState();

        var view = OverlayBridgeSupportViewModel.From(state.Snapshot());

        Assert.Equal("Unavailable", view.AvailabilityText);
        Assert.Equal("Disabled", view.EnabledText);
        Assert.Equal("Not started — transport not implemented", view.PairingTransportText);
        Assert.Equal("Not available", view.SchemaText);
        Assert.Equal("0 connected", view.ConnectedPairedClientsText);
        Assert.Equal("No frames", view.LatestFrameAgeText);
        Assert.Equal("None reported", view.LastSafeErrorText);
    }

    [Fact]
    public void AvailableHealth_FormatsFutureMultiClientFieldsWithoutUnsafeErrorFormatting()
    {
        var view = OverlayBridgeSupportViewModel.From(new OverlayBridgeSupportSnapshot(
            Availability: OverlayBridgeAvailability.Available,
            Enabled: true,
            TransportStatus: OverlayBridgeTransportStatus.Connected,
            SchemaVersion: "bridge/v1",
            SchemaHash: "sha256:abcd1234",
            ConnectedPairedClientCount: 2,
            LatestFrameAge: TimeSpan.FromSeconds(12),
            LastSafeError: "Peer closed\r\nconnection"));

        Assert.Equal("Available", view.AvailabilityText);
        Assert.Equal("Enabled", view.EnabledText);
        Assert.Equal("Connected", view.PairingTransportText);
        Assert.Equal("bridge/v1 · sha256:abcd1234", view.SchemaText);
        Assert.Equal("2 connected", view.ConnectedPairedClientsText);
        Assert.Equal("12 seconds ago", view.LatestFrameAgeText);
        Assert.Equal("Peer closed connection", view.LastSafeErrorText);
    }
}
