using System.Text.Json;
using TmrOverlay.Core.History;

namespace TmrOverlay.App.History;

internal sealed class SessionHistoryQueryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SessionHistoryOptions _options;
    private readonly CurrentSessionCarRadarCalibrationStore? _currentSessionCarRadarCalibration;

    public SessionHistoryQueryService(
        SessionHistoryOptions options,
        CurrentSessionCarRadarCalibrationStore? currentSessionCarRadarCalibration = null)
    {
        _options = options;
        _currentSessionCarRadarCalibration = currentSessionCarRadarCalibration;
    }

    public SessionHistoryLookupResult Lookup(HistoricalComboIdentity combo)
    {
        if (!_options.Enabled)
        {
            return SessionHistoryLookupResult.Empty(combo);
        }

        var userAggregate = ReadAggregate(_options.ResolvedUserHistoryRoot, combo);
        var baselineAggregate = _options.UseBaselineHistory
            ? ReadAggregate(_options.ResolvedBaselineHistoryRoot, combo)
            : null;
        return new SessionHistoryLookupResult(combo, userAggregate, baselineAggregate);
    }

    public CarRadarCalibrationLookupResult LookupCarRadarCalibration(HistoricalComboIdentity combo)
    {
        if (!_options.Enabled)
        {
            return CarRadarCalibrationLookupResult.Empty(combo);
        }

        var userAggregate = ReadCarRadarCalibrationAggregate(_options.ResolvedUserHistoryRoot, combo.CarKey);
        var baselineAggregate = _options.UseBaselineHistory
            ? ReadCarRadarCalibrationAggregate(_options.ResolvedBaselineHistoryRoot, combo.CarKey)
            : null;
        var currentSessionAggregate = _currentSessionCarRadarCalibration?.Lookup(combo);
        return new CarRadarCalibrationLookupResult(
            combo,
            userAggregate,
            baselineAggregate,
            currentSessionAggregate);
    }

    private static HistoricalSessionAggregate? ReadAggregate(string root, HistoricalComboIdentity combo)
    {
        var path = Path.Combine(
            root,
            "cars",
            combo.CarKey,
            "tracks",
            combo.TrackKey,
            "sessions",
            combo.SessionKey,
            "aggregate.json");

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var aggregate = JsonSerializer.Deserialize<HistoricalSessionAggregate>(stream, JsonOptions);
            return aggregate?.AggregateVersion == HistoricalDataVersions.AggregateVersion
                ? aggregate
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static HistoricalCarRadarCalibrationAggregate? ReadCarRadarCalibrationAggregate(
        string root,
        string carKey)
    {
        var path = Path.Combine(
            root,
            "cars",
            carKey,
            "radar-calibration.json");

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var aggregate = JsonSerializer.Deserialize<HistoricalCarRadarCalibrationAggregate>(stream, JsonOptions);
            return aggregate?.AggregateVersion == HistoricalDataVersions.CarRadarCalibrationAggregateVersion
                ? aggregate
                : null;
        }
        catch
        {
            return null;
        }
    }
}
