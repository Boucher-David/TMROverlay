namespace TmrOverlay.Core.History;

internal sealed record SessionHistoryLookupResult(
    HistoricalComboIdentity Combo,
    HistoricalSessionAggregate? UserAggregate,
    HistoricalSessionAggregate? BaselineAggregate)
{
    public HistoricalSessionAggregate? PreferredAggregate => UserAggregate ?? BaselineAggregate;

    public string? PreferredAggregateSource => UserAggregate is not null
        ? "user"
        : BaselineAggregate is not null
            ? "baseline"
            : null;

    public bool HasAnyData => PreferredAggregate is not null;

    public static SessionHistoryLookupResult Empty(HistoricalComboIdentity combo)
    {
        return new SessionHistoryLookupResult(combo, UserAggregate: null, BaselineAggregate: null);
    }
}

internal sealed record CarRadarCalibrationLookupResult(
    HistoricalComboIdentity Combo,
    HistoricalCarRadarCalibrationAggregate? UserAggregate,
    HistoricalCarRadarCalibrationAggregate? BaselineAggregate,
    HistoricalCarRadarCalibrationAggregate? CurrentSessionAggregate = null)
{
    public string CarKey => Combo.CarKey;

    public HistoricalCarRadarCalibrationAggregate? PreferredAggregate => CurrentSessionAggregate ?? UserAggregate ?? BaselineAggregate;

    public string? PreferredAggregateSource => CurrentSessionAggregate is not null
        ? "current-session"
        : UserAggregate is not null
        ? "user"
        : BaselineAggregate is not null
            ? "baseline"
            : null;

    public bool HasAnyData => PreferredAggregate is not null;

    public static CarRadarCalibrationLookupResult Empty(HistoricalComboIdentity combo)
    {
        return new CarRadarCalibrationLookupResult(
            combo,
            UserAggregate: null,
            BaselineAggregate: null,
            CurrentSessionAggregate: null);
    }
}
