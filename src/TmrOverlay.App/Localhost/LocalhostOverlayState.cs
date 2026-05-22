namespace TmrOverlay.App.Localhost;

internal sealed class LocalhostOverlayState
{
    private static readonly TimeSpan RecentRequestWindow = TimeSpan.FromSeconds(10);
    private const int MaximumRecentRequestSamples = 25;

    private const string StatusDisabled = "disabled";
    private const string StatusFailed = "failed";
    private const string StatusListening = "listening";
    private const string StatusNotStarted = "not_started";
    private const string StatusStarting = "starting";
    private const string StatusStopped = "stopped";

    private readonly LocalhostOverlayOptions _options;
    private readonly object _sync = new();
    private readonly Dictionary<string, long> _routeCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _pathCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _clientCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _routeClientCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _pathClientCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _statusCodeCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _pathStatusCodeCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<LocalhostOverlayRequestSample> _recentRequests = new();
    private readonly Queue<LocalhostOverlayPageEventSample> _recentPageEvents = new();
    private readonly Dictionary<string, long> _pageEventCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _pageEventOverlayCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _pageEventClientCounts = new(StringComparer.OrdinalIgnoreCase);
    private string _status;
    private DateTimeOffset? _startAttemptedAtUtc;
    private DateTimeOffset? _startedAtUtc;
    private DateTimeOffset? _stoppedAtUtc;
    private string? _lastError;
    private DateTimeOffset? _lastErrorAtUtc;
    private long _totalRequests;
    private long _successfulRequests;
    private long _failedRequests;
    private long _requestErrorCount;
    private DateTimeOffset? _lastRequestAtUtc;
    private string? _lastRequestMethod;
    private string? _lastRequestPath;
    private string? _lastRequestRoute;
    private int? _lastRequestStatusCode;
    private double? _lastRequestDurationMs;
    private string? _lastRequestError;
    private string? _lastRequestUserAgent;
    private string? _lastRequestClientKind;
    private DateTimeOffset? _lastPageEventAtUtc;
    private string? _lastPageEventKind;
    private string? _lastPageEventOverlayId;
    private string? _lastPageEventClientId;
    private string? _lastPageEventClientKind;
    private bool? _lastPageEventShouldRender;
    private string? _lastPageEventStatus;
    private string? _lastPageEventError;

    public LocalhostOverlayState(LocalhostOverlayOptions options)
    {
        _options = options;
        _status = options.Enabled ? StatusNotStarted : StatusDisabled;
    }

    public void RecordDisabled()
    {
        lock (_sync)
        {
            _status = StatusDisabled;
            _stoppedAtUtc = null;
            _lastError = null;
            _lastErrorAtUtc = null;
        }
    }

    public void RecordStartAttempted()
    {
        lock (_sync)
        {
            _status = StatusStarting;
            _startAttemptedAtUtc = DateTimeOffset.UtcNow;
            _stoppedAtUtc = null;
            _lastError = null;
            _lastErrorAtUtc = null;
        }
    }

    public void RecordStarted()
    {
        lock (_sync)
        {
            _status = StatusListening;
            _startedAtUtc = DateTimeOffset.UtcNow;
            _stoppedAtUtc = null;
            _lastError = null;
            _lastErrorAtUtc = null;
        }
    }

    public void RecordStartFailed(Exception exception)
    {
        lock (_sync)
        {
            _status = StatusFailed;
            _lastError = exception.Message;
            _lastErrorAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public void RecordStopped()
    {
        lock (_sync)
        {
            _stoppedAtUtc = DateTimeOffset.UtcNow;
            if (string.Equals(_status, StatusListening, StringComparison.Ordinal)
                || string.Equals(_status, StatusStarting, StringComparison.Ordinal))
            {
                _status = StatusStopped;
            }
        }
    }

    public void RecordRequest(
        string route,
        string method,
        string path,
        int statusCode,
        TimeSpan duration,
        string? userAgent = null,
        Exception? exception = null)
    {
        var routeKey = string.IsNullOrWhiteSpace(route) ? "unknown" : route.Trim();
        var statusKey = statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var failed = statusCode >= 400 || exception is not null;
        var clientKind = ClassifyClientKind(userAgent);
        var requestAtUtc = DateTimeOffset.UtcNow;
        var durationMs = Math.Round(duration.TotalMilliseconds, 3);
        lock (_sync)
        {
            _totalRequests++;
            if (failed)
            {
                _failedRequests++;
            }
            else
            {
                _successfulRequests++;
            }

            if (exception is not null)
            {
                _requestErrorCount++;
            }

            var pathKey = NormalizePathKey(path);
            var pathStatusKey = PathStatusKey(pathKey, statusKey);
            var routeClientKey = $"{routeKey}|{clientKind}";
            var pathClientKey = $"{pathKey}|{clientKind}";
            _routeCounts[routeKey] = _routeCounts.GetValueOrDefault(routeKey) + 1;
            _pathCounts[pathKey] = _pathCounts.GetValueOrDefault(pathKey) + 1;
            _clientCounts[clientKind] = _clientCounts.GetValueOrDefault(clientKind) + 1;
            _routeClientCounts[routeClientKey] = _routeClientCounts.GetValueOrDefault(routeClientKey) + 1;
            _pathClientCounts[pathClientKey] = _pathClientCounts.GetValueOrDefault(pathClientKey) + 1;
            _statusCodeCounts[statusKey] = _statusCodeCounts.GetValueOrDefault(statusKey) + 1;
            _pathStatusCodeCounts[pathStatusKey] = _pathStatusCodeCounts.GetValueOrDefault(pathStatusKey) + 1;
            _lastRequestAtUtc = requestAtUtc;
            _lastRequestMethod = method;
            _lastRequestPath = pathKey;
            _lastRequestRoute = routeKey;
            _lastRequestStatusCode = statusCode;
            _lastRequestDurationMs = durationMs;
            _lastRequestError = exception?.Message;
            _lastRequestUserAgent = NormalizeUserAgent(userAgent);
            _lastRequestClientKind = clientKind;
            _recentRequests.Enqueue(new LocalhostOverlayRequestSample(
                AtUtc: requestAtUtc,
                Method: method,
                Path: pathKey,
                Route: routeKey,
                StatusCode: statusCode,
                DurationMs: durationMs,
                ClientKind: clientKind,
                UserAgent: _lastRequestUserAgent,
                Error: exception?.Message));
            while (_recentRequests.Count > MaximumRecentRequestSamples)
            {
                _recentRequests.Dequeue();
            }
        }
    }

    public LocalhostOverlaySnapshot Snapshot()
    {
        lock (_sync)
        {
            var lastRequestAgeSeconds = _lastRequestAtUtc is { } lastRequestAtUtc
                ? Math.Max(0d, (DateTimeOffset.UtcNow - lastRequestAtUtc).TotalSeconds)
                : (double?)null;
            return new LocalhostOverlaySnapshot(
                Enabled: _options.Enabled,
                Port: _options.Port,
                Prefix: _options.Prefix,
                Status: _status,
                StartAttemptedAtUtc: _startAttemptedAtUtc,
                StartedAtUtc: _startedAtUtc,
                StoppedAtUtc: _stoppedAtUtc,
                LastError: _lastError,
                LastErrorAtUtc: _lastErrorAtUtc,
                TotalRequests: _totalRequests,
                SuccessfulRequests: _successfulRequests,
                FailedRequests: _failedRequests,
                RequestErrorCount: _requestErrorCount,
                LastRequestAtUtc: _lastRequestAtUtc,
                LastRequestMethod: _lastRequestMethod,
                LastRequestPath: _lastRequestPath,
                LastRequestRoute: _lastRequestRoute,
                LastRequestStatusCode: _lastRequestStatusCode,
                LastRequestDurationMs: _lastRequestDurationMs,
                LastRequestError: _lastRequestError,
                LastRequestUserAgent: _lastRequestUserAgent,
                LastRequestClientKind: _lastRequestClientKind,
                LastRequestAgeSeconds: lastRequestAgeSeconds,
                HasRecentRequests: lastRequestAgeSeconds is not null && lastRequestAgeSeconds <= RecentRequestWindow.TotalSeconds,
                RouteCounts: CopyCounts(_routeCounts),
                PathCounts: CopyCounts(_pathCounts),
                ClientCounts: CopyCounts(_clientCounts),
                RouteClientCounts: CopyCounts(_routeClientCounts),
                PathClientCounts: CopyCounts(_pathClientCounts),
                PathStatusCodeCounts: CopyCounts(_pathStatusCodeCounts),
                StatusCodeCounts: CopyCounts(_statusCodeCounts),
                RecentRequests: _recentRequests.ToArray(),
                LastPageEventAtUtc: _lastPageEventAtUtc,
                LastPageEventKind: _lastPageEventKind,
                LastPageEventOverlayId: _lastPageEventOverlayId,
                LastPageEventClientId: _lastPageEventClientId,
                LastPageEventClientKind: _lastPageEventClientKind,
                LastPageEventShouldRender: _lastPageEventShouldRender,
                LastPageEventStatus: _lastPageEventStatus,
                LastPageEventError: _lastPageEventError,
                PageEventCounts: CopyCounts(_pageEventCounts),
                PageEventOverlayCounts: CopyCounts(_pageEventOverlayCounts),
                PageEventClientCounts: CopyCounts(_pageEventClientCounts),
                RecentPageEvents: _recentPageEvents.ToArray());
        }
    }

    public void RecordPageEvent(LocalhostOverlayPageEvent pageEvent)
    {
        var eventKind = NormalizeKey(pageEvent.Event, "unknown");
        var overlayId = NormalizeKey(pageEvent.OverlayId, "unknown");
        var clientKind = NormalizeClientKind(pageEvent.ClientKind);
        var recordedAtUtc = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            _lastPageEventAtUtc = recordedAtUtc;
            _lastPageEventKind = eventKind;
            _lastPageEventOverlayId = overlayId;
            _lastPageEventClientId = NormalizeOptional(pageEvent.ClientId);
            _lastPageEventClientKind = clientKind;
            _lastPageEventShouldRender = pageEvent.ShouldRender;
            _lastPageEventStatus = NormalizeOptional(pageEvent.Status);
            _lastPageEventError = NormalizeOptional(pageEvent.Error);
            var overlayEventKey = $"{overlayId}|{eventKind}";
            var clientEventKey = $"{clientKind}|{eventKind}";
            _pageEventCounts[eventKind] = _pageEventCounts.GetValueOrDefault(eventKind) + 1;
            _pageEventOverlayCounts[overlayEventKey] = _pageEventOverlayCounts.GetValueOrDefault(overlayEventKey) + 1;
            _pageEventClientCounts[clientEventKey] = _pageEventClientCounts.GetValueOrDefault(clientEventKey) + 1;
            _recentPageEvents.Enqueue(new LocalhostOverlayPageEventSample(
                AtUtc: recordedAtUtc,
                Event: eventKind,
                OverlayId: overlayId,
                ClientId: _lastPageEventClientId,
                ClientKind: clientKind,
                ShouldRender: pageEvent.ShouldRender,
                Status: _lastPageEventStatus,
                Error: _lastPageEventError));
            while (_recentPageEvents.Count > MaximumRecentRequestSamples)
            {
                _recentPageEvents.Dequeue();
            }
        }
    }

    public static string PathStatusKey(string path, string statusCode)
    {
        return $"{NormalizePathKey(path)}|{statusCode}";
    }

    private static string NormalizePathKey(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? "/" : path.Trim();
    }

    private static string? NormalizeUserAgent(string? userAgent)
    {
        return string.IsNullOrWhiteSpace(userAgent) ? null : userAgent.Trim();
    }

    private static string NormalizeKey(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeClientKind(string? clientKind)
    {
        var normalized = NormalizeKey(clientKind, "unknown").ToLowerInvariant();
        if (normalized.StartsWith("obs", StringComparison.Ordinal))
        {
            return "obs";
        }

        return normalized switch
        {
            "obs" or "chrome" or "edge" or "firefox" or "other" or "unknown" => normalized,
            _ => "other"
        };
    }

    private static string ClassifyClientKind(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return "unknown";
        }

        if (userAgent.Contains("OBS", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("obs-browser", StringComparison.OrdinalIgnoreCase))
        {
            return "obs";
        }

        if (userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase))
        {
            return "edge";
        }

        if (userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("Chromium/", StringComparison.OrdinalIgnoreCase))
        {
            return "chrome";
        }

        if (userAgent.Contains("Firefox/", StringComparison.OrdinalIgnoreCase))
        {
            return "firefox";
        }

        return "other";
    }

    private static IReadOnlyDictionary<string, long> CopyCounts(Dictionary<string, long> counts)
    {
        return counts
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
    }
}

internal sealed record LocalhostOverlaySnapshot(
    bool Enabled,
    int Port,
    string Prefix,
    string Status,
    DateTimeOffset? StartAttemptedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? StoppedAtUtc,
    string? LastError,
    DateTimeOffset? LastErrorAtUtc,
    long TotalRequests,
    long SuccessfulRequests,
    long FailedRequests,
    long RequestErrorCount,
    DateTimeOffset? LastRequestAtUtc,
    string? LastRequestMethod,
    string? LastRequestPath,
    string? LastRequestRoute,
    int? LastRequestStatusCode,
    double? LastRequestDurationMs,
    string? LastRequestError,
    string? LastRequestUserAgent,
    string? LastRequestClientKind,
    double? LastRequestAgeSeconds,
    bool HasRecentRequests,
    IReadOnlyDictionary<string, long> RouteCounts,
    IReadOnlyDictionary<string, long> PathCounts,
    IReadOnlyDictionary<string, long> ClientCounts,
    IReadOnlyDictionary<string, long> RouteClientCounts,
    IReadOnlyDictionary<string, long> PathClientCounts,
    IReadOnlyDictionary<string, long> PathStatusCodeCounts,
    IReadOnlyDictionary<string, long> StatusCodeCounts,
    IReadOnlyList<LocalhostOverlayRequestSample> RecentRequests,
    DateTimeOffset? LastPageEventAtUtc,
    string? LastPageEventKind,
    string? LastPageEventOverlayId,
    string? LastPageEventClientId,
    string? LastPageEventClientKind,
    bool? LastPageEventShouldRender,
    string? LastPageEventStatus,
    string? LastPageEventError,
    IReadOnlyDictionary<string, long> PageEventCounts,
    IReadOnlyDictionary<string, long> PageEventOverlayCounts,
    IReadOnlyDictionary<string, long> PageEventClientCounts,
    IReadOnlyList<LocalhostOverlayPageEventSample> RecentPageEvents);

internal sealed record LocalhostOverlayRequestSample(
    DateTimeOffset AtUtc,
    string Method,
    string Path,
    string Route,
    int StatusCode,
    double DurationMs,
    string ClientKind,
    string? UserAgent,
    string? Error);

internal sealed record LocalhostOverlayPageEvent(
    string Event,
    string OverlayId,
    string? ClientId,
    string? ClientKind,
    bool? ShouldRender,
    string? Status,
    string? Error);

internal sealed record LocalhostOverlayPageEventSample(
    DateTimeOffset AtUtc,
    string Event,
    string OverlayId,
    string? ClientId,
    string ClientKind,
    bool? ShouldRender,
    string? Status,
    string? Error);
