namespace TmrOverlay.App.Telemetry;

// iRacing's CarIdx arrays can grow with the active entry table. Keep the
// fields that establish the timing-row cardinality in one App-owned list so
// live collection and raw replay cannot silently drift apart.
internal static class CarIdxTelemetrySchema
{
    public static readonly IReadOnlyList<string> TimingArrayNames =
    [
        "CarIdxLapCompleted",
        "CarIdxLapDistPct",
        "CarIdxTrackSurface",
        "CarIdxPosition",
        "CarIdxClassPosition",
        "CarIdxClass",
        "CarIdxF2Time",
        "CarIdxEstTime"
    ];
}
