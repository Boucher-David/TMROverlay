using TmrOverlay.Core.Overlays;
using OverlaySizes = TmrOverlay.App.Overlays.OverlayGeometryContractValues.OverlaySizes;

namespace TmrOverlay.App.Overlays.StreamChat;

internal static class StreamChatOverlayDefinition
{
    public static OverlayDefinition Definition { get; } = new(
        Id: "stream-chat",
        DisplayName: "Stream Chat",
        DefaultWidth: OverlaySizes.StreamChatWidth,
        DefaultHeight: OverlaySizes.StreamChatHeight,
        ShowSessionFilters: false,
        ShowScaleControl: true,
        ShowOpacityControl: true);
}
