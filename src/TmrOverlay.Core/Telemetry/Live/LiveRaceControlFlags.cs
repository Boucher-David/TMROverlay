namespace TmrOverlay.Core.Telemetry.Live;

// Keep Fuel V2 live qualification and captured-history qualification on the
// same race-control vocabulary. A green session state alone does not prove a
// clean racing lap while a yellow-family flag is active.
internal static class LiveRaceControlFlags
{
    private const int YellowFamilyMask = 0x00000008
        | 0x00000040
        | 0x00000100
        | 0x00000200
        | 0x00002000
        | 0x00004000
        | 0x00008000;

    public static bool HasYellowFamily(int? flags) => (flags.GetValueOrDefault() & YellowFamilyMask) != 0;
}
