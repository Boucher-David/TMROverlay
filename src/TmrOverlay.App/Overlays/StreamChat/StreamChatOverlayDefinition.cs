using TmrOverlay.Core.Overlays;

namespace TmrOverlay.App.Overlays.StreamChat;

internal static class StreamChatOverlayDefinition
{
    public static OverlayDefinition Definition { get; } = new(
        Id: "stream-chat",
        DisplayName: "Stream Chat",
        DefaultWidth: 380,
        DefaultHeight: 520,
        ShowSessionFilters: false,
        ShowScaleControl: true,
        ShowOpacityControl: true);
}
