namespace TmrOverlay.Core.Telemetry.Live;

internal interface ILiveTelemetrySource
{
    LiveTelemetrySnapshot Snapshot();

    LiveTelemetrySnapshot? LastActiveSnapshot()
    {
        return null;
    }
}
