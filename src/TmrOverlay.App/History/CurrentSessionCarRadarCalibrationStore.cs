using TmrOverlay.Core.History;

namespace TmrOverlay.App.History;

// Holds completed radar calibration windows for the one live collection. This
// is deliberately an in-memory bridge: SessionHistoryStore remains the only
// path that writes learned radar calibration after collection finalization.
internal sealed class CurrentSessionCarRadarCalibrationStore
{
    private readonly object _sync = new();
    private string? _activeSourceId;
    private HistoricalSessionRadarCalibrationSnapshot? _snapshot;

    public void StartCollection(string sourceId)
    {
        lock (_sync)
        {
            _activeSourceId = sourceId;
            _snapshot = null;
        }
    }

    public void Publish(string sourceId, HistoricalSessionRadarCalibrationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_sync)
        {
            if (!string.Equals(_activeSourceId, sourceId, StringComparison.Ordinal))
            {
                return;
            }

            _snapshot = snapshot;
        }
    }

    public void CompleteCollection(string? sourceId)
    {
        lock (_sync)
        {
            if (sourceId is not null && !string.Equals(_activeSourceId, sourceId, StringComparison.Ordinal))
            {
                return;
            }

            _activeSourceId = null;
            _snapshot = null;
        }
    }

    public HistoricalCarRadarCalibrationAggregate? Lookup(HistoricalComboIdentity combo)
    {
        lock (_sync)
        {
            if (_snapshot is null
                || !string.Equals(_snapshot.Combo.CarKey, combo.CarKey, StringComparison.OrdinalIgnoreCase)
                || _snapshot.RadarCalibration is null)
            {
                return null;
            }

            var aggregate = new HistoricalCarRadarCalibrationAggregate
            {
                CarKey = _snapshot.Combo.CarKey,
                Car = _snapshot.Car,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            aggregate.RadarCalibration.Add(_snapshot.RadarCalibration);
            aggregate.SessionCount = aggregate.RadarCalibration.SourceSessionCount;
            return aggregate;
        }
    }
}
