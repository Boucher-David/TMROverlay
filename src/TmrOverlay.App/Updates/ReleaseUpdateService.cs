using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TmrOverlay.App.Events;
using TmrOverlay.Core.AppInfo;
using Velopack;
using Velopack.Sources;

namespace TmrOverlay.App.Updates;

internal sealed class ReleaseUpdateService : IHostedService, IDisposable
{
    private readonly ReleaseUpdateOptions _options;
    private readonly AppEventRecorder _events;
    private readonly ILogger<ReleaseUpdateService> _logger;
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private readonly object _sync = new();
    private CancellationTokenSource? _startupCheckCancellation;
    private Task? _startupCheckTask;
    private UpdateManager? _updateManager;
    private bool _updateManagerCreated;
    private bool _disposed;
    private ReleaseUpdateSnapshot _snapshot;

    public ReleaseUpdateService(
        ReleaseUpdateOptions options,
        AppEventRecorder events,
        ILogger<ReleaseUpdateService> logger)
    {
        _options = options;
        _events = events;
        _logger = logger;
        _snapshot = options.Enabled
            ? IdleSnapshot()
            : ReleaseUpdateSnapshot.Disabled(options.RepositoryUrl);
    }

    public event EventHandler? StateChanged;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return Task.CompletedTask;
        }

        _startupCheckCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _startupCheckTask = CheckOnStartupAsync(_startupCheckCancellation.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancelStartupCheck();
        var startupCheckTask = _startupCheckTask;
        if (startupCheckTask is null)
        {
            return;
        }

        try
        {
            await startupCheckTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public ReleaseUpdateSnapshot Snapshot()
    {
        lock (_sync)
        {
            return _snapshot;
        }
    }

    public async Task<ReleaseUpdateSnapshot> CheckForUpdatesAsync(
        ReleaseUpdateCheckSource source,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return SetSnapshot(ReleaseUpdateSnapshot.Disabled(_options.RepositoryUrl));
        }

        await _checkLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manager = CreateUpdateManager();
            if (manager is null)
            {
                return Snapshot();
            }

            if (!manager.IsInstalled)
            {
                return SetSnapshot(NotInstalledSnapshot(manager));
            }

            var pendingRestart = manager.UpdatePendingRestart;
            if (pendingRestart is not null)
            {
                return SetSnapshot(PendingRestartSnapshot(manager, pendingRestart));
            }

            var checking = InstalledSnapshot(
                ReleaseUpdateStatus.Checking,
                manager,
                checkInProgress: true,
                latestVersion: Snapshot().LatestVersion,
                latestFileName: Snapshot().LatestFileName,
                deltaCount: Snapshot().DeltaCount,
                lastCheckedAtUtc: Snapshot().LastCheckedAtUtc,
                lastFailedAtUtc: Snapshot().LastFailedAtUtc,
                lastError: Snapshot().LastError,
                releasePageUrl: Snapshot().ReleasePageUrl);
            SetSnapshot(checking);

            _events.Record("update_check_started", new Dictionary<string, string?>
            {
                ["source"] = source.ToString().ToLowerInvariant(),
                ["repositoryUrl"] = _options.RepositoryUrl
            });

            try
            {
                var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
                var checkedAtUtc = DateTimeOffset.UtcNow;
                if (update is null)
                {
                    _events.Record("update_check_succeeded", new Dictionary<string, string?>
                    {
                        ["source"] = source.ToString().ToLowerInvariant(),
                        ["result"] = "up_to_date"
                    });
                    return SetSnapshot(InstalledSnapshot(
                        ReleaseUpdateStatus.UpToDate,
                        manager,
                        checkInProgress: false,
                        latestVersion: manager.CurrentVersion?.ToString(),
                        latestFileName: null,
                        deltaCount: 0,
                        lastCheckedAtUtc: checkedAtUtc,
                        lastFailedAtUtc: null,
                        lastError: null,
                        releasePageUrl: ReleasePageUrl(null)));
                }

                var target = update.TargetFullRelease;
                var latestVersion = target.Version?.ToString();
                _events.Record("update_check_succeeded", new Dictionary<string, string?>
                {
                    ["source"] = source.ToString().ToLowerInvariant(),
                    ["result"] = "available",
                    ["latestVersion"] = latestVersion
                });
                return SetSnapshot(InstalledSnapshot(
                    ReleaseUpdateStatus.Available,
                    manager,
                    checkInProgress: false,
                    latestVersion: latestVersion,
                    latestFileName: target.FileName,
                    deltaCount: update.DeltasToTarget?.Length ?? 0,
                    lastCheckedAtUtc: checkedAtUtc,
                    lastFailedAtUtc: null,
                    lastError: null,
                    releasePageUrl: ReleasePageUrl(latestVersion)));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "Failed to check for TmrOverlay updates.");
                _events.Record("update_check_failed", new Dictionary<string, string?>
                {
                    ["source"] = source.ToString().ToLowerInvariant(),
                    ["error"] = exception.GetType().Name
                });
                return SetSnapshot(InstalledSnapshot(
                    ReleaseUpdateStatus.Failed,
                    manager,
                    checkInProgress: false,
                    latestVersion: null,
                    latestFileName: null,
                    deltaCount: 0,
                    lastCheckedAtUtc: Snapshot().LastCheckedAtUtc,
                    lastFailedAtUtc: DateTimeOffset.UtcNow,
                    lastError: exception.Message,
                    releasePageUrl: ReleasePageUrl(null)));
            }
        }
        finally
        {
            _checkLock.Release();
        }
    }

    public async Task<ReleaseUpdateSnapshot> DownloadAndPrepareUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return SetSnapshot(ReleaseUpdateSnapshot.Disabled(_options.RepositoryUrl));
        }

        await _checkLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manager = CreateUpdateManager();
            if (manager is null)
            {
                return Snapshot();
            }

            if (!manager.IsInstalled)
            {
                return SetSnapshot(NotInstalledSnapshot(manager));
            }

            var pendingRestart = manager.UpdatePendingRestart;
            if (pendingRestart is not null)
            {
                return SetSnapshot(PendingRestartSnapshot(manager, pendingRestart));
            }

            var operationStartedAtUtc = DateTimeOffset.UtcNow;
            _events.Record("update_download_started", new Dictionary<string, string?>
            {
                ["repositoryUrl"] = _options.RepositoryUrl
            });

            try
            {
                var previous = Snapshot();
                SetSnapshot(InstalledSnapshot(
                    ReleaseUpdateStatus.Checking,
                    manager,
                    checkInProgress: true,
                    latestVersion: previous.LatestVersion,
                    latestFileName: previous.LatestFileName,
                    deltaCount: previous.DeltaCount,
                    lastCheckedAtUtc: previous.LastCheckedAtUtc,
                    lastFailedAtUtc: previous.LastFailedAtUtc,
                    lastError: previous.LastError,
                    releasePageUrl: previous.ReleasePageUrl,
                    lastDownloadStartedAtUtc: previous.LastDownloadStartedAtUtc,
                    lastDownloadedAtUtc: previous.LastDownloadedAtUtc,
                    downloadProgressPercent: previous.DownloadProgressPercent,
                    lastApplyStartedAtUtc: previous.LastApplyStartedAtUtc));

                var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
                var checkedAtUtc = DateTimeOffset.UtcNow;
                if (update is null)
                {
                    _events.Record("update_download_skipped", new Dictionary<string, string?>
                    {
                        ["result"] = "up_to_date"
                    });
                    return SetSnapshot(InstalledSnapshot(
                        ReleaseUpdateStatus.UpToDate,
                        manager,
                        checkInProgress: false,
                        latestVersion: manager.CurrentVersion?.ToString(),
                        latestFileName: null,
                        deltaCount: 0,
                        lastCheckedAtUtc: checkedAtUtc,
                        lastFailedAtUtc: null,
                        lastError: null,
                        releasePageUrl: ReleasePageUrl(null)));
                }

                var target = update.TargetFullRelease;
                var latestVersion = target.Version?.ToString();
                var latestFileName = target.FileName;
                var deltaCount = update.DeltasToTarget?.Length ?? 0;
                var releasePageUrl = ReleasePageUrl(latestVersion);
                SetSnapshot(InstalledSnapshot(
                    ReleaseUpdateStatus.Downloading,
                    manager,
                    checkInProgress: false,
                    latestVersion: latestVersion,
                    latestFileName: latestFileName,
                    deltaCount: deltaCount,
                    lastCheckedAtUtc: checkedAtUtc,
                    lastFailedAtUtc: null,
                    lastError: null,
                    releasePageUrl: releasePageUrl,
                    lastDownloadStartedAtUtc: operationStartedAtUtc,
                    downloadProgressPercent: 0));

                await manager.DownloadUpdatesAsync(
                        update,
                        progress =>
                        {
                            SetSnapshot(InstalledSnapshot(
                                ReleaseUpdateStatus.Downloading,
                                manager,
                                checkInProgress: false,
                                latestVersion: latestVersion,
                                latestFileName: latestFileName,
                                deltaCount: deltaCount,
                                lastCheckedAtUtc: checkedAtUtc,
                                lastFailedAtUtc: null,
                                lastError: null,
                                releasePageUrl: releasePageUrl,
                                lastDownloadStartedAtUtc: operationStartedAtUtc,
                                downloadProgressPercent: Math.Clamp(progress, 0, 100)));
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

                var downloadedAtUtc = DateTimeOffset.UtcNow;
                pendingRestart = manager.UpdatePendingRestart;
                if (pendingRestart is null)
                {
                    _events.Record("update_download_failed", new Dictionary<string, string?>
                    {
                        ["latestVersion"] = latestVersion,
                        ["latestFileName"] = latestFileName,
                        ["error"] = "PendingRestartMissing"
                    });
                    return SetSnapshot(InstalledSnapshot(
                        ReleaseUpdateStatus.Failed,
                        manager,
                        checkInProgress: false,
                        latestVersion: latestVersion,
                        latestFileName: latestFileName,
                        deltaCount: deltaCount,
                        lastCheckedAtUtc: checkedAtUtc,
                        lastFailedAtUtc: DateTimeOffset.UtcNow,
                        lastError: "Update downloaded, but Velopack did not report a pending restart.",
                        releasePageUrl: releasePageUrl,
                        lastDownloadStartedAtUtc: operationStartedAtUtc,
                        lastDownloadedAtUtc: downloadedAtUtc,
                        downloadProgressPercent: 100));
                }

                _events.Record("update_download_succeeded", new Dictionary<string, string?>
                {
                    ["latestVersion"] = latestVersion,
                    ["latestFileName"] = latestFileName,
                    ["deltaCount"] = deltaCount.ToString()
                });
                return SetSnapshot(PendingRestartSnapshot(
                    manager,
                    pendingRestart,
                    lastCheckedAtUtc: checkedAtUtc,
                    lastDownloadStartedAtUtc: operationStartedAtUtc,
                    lastDownloadedAtUtc: downloadedAtUtc,
                    downloadProgressPercent: 100));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "Failed to download TmrOverlay update.");
                _events.Record("update_download_failed", new Dictionary<string, string?>
                {
                    ["error"] = exception.GetType().Name
                });
                var previous = Snapshot();
                return SetSnapshot(InstalledSnapshot(
                    ReleaseUpdateStatus.Failed,
                    manager,
                    checkInProgress: false,
                    latestVersion: previous.LatestVersion,
                    latestFileName: previous.LatestFileName,
                    deltaCount: previous.DeltaCount,
                    lastCheckedAtUtc: previous.LastCheckedAtUtc,
                    lastFailedAtUtc: DateTimeOffset.UtcNow,
                    lastError: exception.Message,
                    releasePageUrl: previous.ReleasePageUrl ?? ReleasePageUrl(previous.LatestVersion),
                    lastDownloadStartedAtUtc: previous.LastDownloadStartedAtUtc,
                    lastDownloadedAtUtc: previous.LastDownloadedAtUtc,
                    downloadProgressPercent: previous.DownloadProgressPercent,
                    lastApplyStartedAtUtc: previous.LastApplyStartedAtUtc));
            }
        }
        finally
        {
            _checkLock.Release();
        }
    }

    public ReleaseUpdateSnapshot BeginApplyUpdateAndRestart()
    {
        if (!_options.Enabled)
        {
            return SetSnapshot(ReleaseUpdateSnapshot.Disabled(_options.RepositoryUrl));
        }

        if (!_checkLock.Wait(0))
        {
            return Snapshot();
        }

        try
        {
            var manager = CreateUpdateManager();
            if (manager is null)
            {
                return Snapshot();
            }

            if (!manager.IsInstalled)
            {
                return SetSnapshot(NotInstalledSnapshot(manager));
            }

            var pendingRestart = manager.UpdatePendingRestart;
            if (pendingRestart is null)
            {
                var previous = Snapshot();
                return SetSnapshot(InstalledSnapshot(
                    ReleaseUpdateStatus.Failed,
                    manager,
                    checkInProgress: false,
                    latestVersion: previous.LatestVersion,
                    latestFileName: previous.LatestFileName,
                    deltaCount: previous.DeltaCount,
                    lastCheckedAtUtc: previous.LastCheckedAtUtc,
                    lastFailedAtUtc: DateTimeOffset.UtcNow,
                    lastError: "No downloaded update is pending restart.",
                    releasePageUrl: previous.ReleasePageUrl ?? ReleasePageUrl(previous.LatestVersion),
                    lastDownloadStartedAtUtc: previous.LastDownloadStartedAtUtc,
                    lastDownloadedAtUtc: previous.LastDownloadedAtUtc,
                    downloadProgressPercent: previous.DownloadProgressPercent,
                    lastApplyStartedAtUtc: previous.LastApplyStartedAtUtc));
            }

            var applyStartedAtUtc = DateTimeOffset.UtcNow;
            var previousPending = Snapshot();
            SetSnapshot(InstalledSnapshot(
                ReleaseUpdateStatus.Applying,
                manager,
                checkInProgress: false,
                latestVersion: pendingRestart.Version?.ToString(),
                latestFileName: pendingRestart.FileName,
                deltaCount: previousPending.DeltaCount,
                lastCheckedAtUtc: previousPending.LastCheckedAtUtc,
                lastFailedAtUtc: null,
                lastError: null,
                releasePageUrl: ReleasePageUrl(pendingRestart.Version?.ToString()),
                lastDownloadStartedAtUtc: previousPending.LastDownloadStartedAtUtc,
                lastDownloadedAtUtc: previousPending.LastDownloadedAtUtc,
                downloadProgressPercent: previousPending.DownloadProgressPercent,
                lastApplyStartedAtUtc: applyStartedAtUtc));

            _events.Record("update_apply_started", new Dictionary<string, string?>
            {
                ["latestVersion"] = pendingRestart.Version?.ToString(),
                ["latestFileName"] = pendingRestart.FileName
            });
            manager.WaitExitThenApplyUpdates(pendingRestart, false, true, Array.Empty<string>());
            return Snapshot();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to start TmrOverlay update apply.");
            _events.Record("update_apply_failed", new Dictionary<string, string?>
            {
                ["error"] = exception.GetType().Name
            });
            var manager = CreateUpdateManager();
            if (manager is null)
            {
                return SetSnapshot(FailedSnapshot(exception));
            }

            var previous = Snapshot();
            return SetSnapshot(InstalledSnapshot(
                ReleaseUpdateStatus.Failed,
                manager,
                checkInProgress: false,
                latestVersion: previous.LatestVersion,
                latestFileName: previous.LatestFileName,
                deltaCount: previous.DeltaCount,
                lastCheckedAtUtc: previous.LastCheckedAtUtc,
                lastFailedAtUtc: DateTimeOffset.UtcNow,
                lastError: exception.Message,
                releasePageUrl: previous.ReleasePageUrl ?? ReleasePageUrl(previous.LatestVersion),
                lastDownloadStartedAtUtc: previous.LastDownloadStartedAtUtc,
                lastDownloadedAtUtc: previous.LastDownloadedAtUtc,
                downloadProgressPercent: previous.DownloadProgressPercent,
                lastApplyStartedAtUtc: previous.LastApplyStartedAtUtc));
        }
        finally
        {
            _checkLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelStartupCheck();
        _startupCheckCancellation?.Dispose();
        _startupCheckCancellation = null;
        _checkLock.Dispose();
    }

    private void CancelStartupCheck()
    {
        try
        {
            _startupCheckCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task CheckOnStartupAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_options.StartupDelaySeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.StartupDelaySeconds), cancellationToken)
                    .ConfigureAwait(false);
            }

            await CheckForUpdatesAsync(ReleaseUpdateCheckSource.Startup, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private UpdateManager? CreateUpdateManager()
    {
        lock (_sync)
        {
            if (_updateManagerCreated)
            {
                return _updateManager;
            }

            _updateManagerCreated = true;
            try
            {
                var source = new GithubSource(
                    _options.RepositoryUrl,
                    accessToken: string.Empty,
                    prerelease: _options.IncludePrerelease,
                    downloader: new HttpClientFileDownloader());
                _updateManager = new UpdateManager(source, new UpdateOptions(), locator: null!);
                _snapshot = _updateManager.IsInstalled
                    ? InstalledSnapshot(
                        ReleaseUpdateStatus.Idle,
                        _updateManager,
                        checkInProgress: false,
                        latestVersion: null,
                        latestFileName: null,
                        deltaCount: 0,
                        lastCheckedAtUtc: null,
                        lastFailedAtUtc: null,
                        lastError: null,
                        releasePageUrl: ReleasePageUrl(null))
                    : NotInstalledSnapshot(_updateManager);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to initialize Velopack update manager.");
                _snapshot = FailedSnapshot(exception);
            }

            return _updateManager;
        }
    }

    private ReleaseUpdateSnapshot SetSnapshot(ReleaseUpdateSnapshot snapshot)
    {
        lock (_sync)
        {
            _snapshot = snapshot;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return snapshot;
    }

    private ReleaseUpdateSnapshot IdleSnapshot() => new(
        ReleaseUpdateStatus.Idle,
        Enabled: true,
        IsInstalled: false,
        IsPortable: false,
        CheckInProgress: false,
        SourceName: "GitHub Releases",
        RepositoryUrl: _options.RepositoryUrl,
        CurrentVersion: AppVersionInfo.Current.Version,
        LatestVersion: null,
        LatestFileName: null,
        DeltaCount: 0,
        LastCheckedAtUtc: null,
        LastDownloadStartedAtUtc: null,
        LastDownloadedAtUtc: null,
        DownloadProgressPercent: null,
        LastApplyStartedAtUtc: null,
        LastFailedAtUtc: null,
        LastError: null,
        ReleasePageUrl: ReleasePageUrl(null));

    private ReleaseUpdateSnapshot NotInstalledSnapshot(UpdateManager manager) => new(
        ReleaseUpdateStatus.NotInstalled,
        Enabled: _options.Enabled,
        IsInstalled: false,
        IsPortable: manager.IsPortable,
        CheckInProgress: false,
        SourceName: "GitHub Releases",
        RepositoryUrl: _options.RepositoryUrl,
        CurrentVersion: AppVersionInfo.Current.Version,
        LatestVersion: null,
        LatestFileName: null,
        DeltaCount: 0,
        LastCheckedAtUtc: null,
        LastDownloadStartedAtUtc: null,
        LastDownloadedAtUtc: null,
        DownloadProgressPercent: null,
        LastApplyStartedAtUtc: null,
        LastFailedAtUtc: null,
        LastError: null,
        ReleasePageUrl: ReleasePageUrl(null));

    private ReleaseUpdateSnapshot PendingRestartSnapshot(
        UpdateManager manager,
        VelopackAsset pendingRestart,
        DateTimeOffset? lastCheckedAtUtc = null,
        DateTimeOffset? lastDownloadStartedAtUtc = null,
        DateTimeOffset? lastDownloadedAtUtc = null,
        int? downloadProgressPercent = null) => new(
        ReleaseUpdateStatus.PendingRestart,
        Enabled: _options.Enabled,
        IsInstalled: true,
        IsPortable: manager.IsPortable,
        CheckInProgress: false,
        SourceName: "GitHub Releases",
        RepositoryUrl: _options.RepositoryUrl,
        CurrentVersion: manager.CurrentVersion?.ToString() ?? AppVersionInfo.Current.Version,
        LatestVersion: pendingRestart.Version?.ToString(),
        LatestFileName: pendingRestart.FileName,
        DeltaCount: 0,
        LastCheckedAtUtc: lastCheckedAtUtc ?? Snapshot().LastCheckedAtUtc,
        LastDownloadStartedAtUtc: lastDownloadStartedAtUtc ?? Snapshot().LastDownloadStartedAtUtc,
        LastDownloadedAtUtc: lastDownloadedAtUtc ?? Snapshot().LastDownloadedAtUtc,
        DownloadProgressPercent: downloadProgressPercent ?? Snapshot().DownloadProgressPercent,
        LastApplyStartedAtUtc: Snapshot().LastApplyStartedAtUtc,
        LastFailedAtUtc: null,
        LastError: null,
        ReleasePageUrl: ReleasePageUrl(pendingRestart.Version?.ToString()));

    private ReleaseUpdateSnapshot InstalledSnapshot(
        ReleaseUpdateStatus status,
        UpdateManager manager,
        bool checkInProgress,
        string? latestVersion,
        string? latestFileName,
        int deltaCount,
        DateTimeOffset? lastCheckedAtUtc,
        DateTimeOffset? lastFailedAtUtc,
        string? lastError,
        string? releasePageUrl,
        DateTimeOffset? lastDownloadStartedAtUtc = null,
        DateTimeOffset? lastDownloadedAtUtc = null,
        int? downloadProgressPercent = null,
        DateTimeOffset? lastApplyStartedAtUtc = null) => new(
        status,
        Enabled: _options.Enabled,
        IsInstalled: true,
        IsPortable: manager.IsPortable,
        CheckInProgress: checkInProgress,
        SourceName: "GitHub Releases",
        RepositoryUrl: _options.RepositoryUrl,
        CurrentVersion: manager.CurrentVersion?.ToString() ?? AppVersionInfo.Current.Version,
        LatestVersion: latestVersion,
        LatestFileName: latestFileName,
        DeltaCount: deltaCount,
        LastCheckedAtUtc: lastCheckedAtUtc,
        LastDownloadStartedAtUtc: lastDownloadStartedAtUtc,
        LastDownloadedAtUtc: lastDownloadedAtUtc,
        DownloadProgressPercent: downloadProgressPercent,
        LastApplyStartedAtUtc: lastApplyStartedAtUtc,
        LastFailedAtUtc: lastFailedAtUtc,
        LastError: lastError,
        ReleasePageUrl: releasePageUrl);

    private ReleaseUpdateSnapshot FailedSnapshot(Exception exception) => new(
        ReleaseUpdateStatus.Failed,
        Enabled: _options.Enabled,
        IsInstalled: false,
        IsPortable: false,
        CheckInProgress: false,
        SourceName: "GitHub Releases",
        RepositoryUrl: _options.RepositoryUrl,
        CurrentVersion: AppVersionInfo.Current.Version,
        LatestVersion: null,
        LatestFileName: null,
        DeltaCount: 0,
        LastCheckedAtUtc: null,
        LastDownloadStartedAtUtc: null,
        LastDownloadedAtUtc: null,
        DownloadProgressPercent: null,
        LastApplyStartedAtUtc: null,
        LastFailedAtUtc: DateTimeOffset.UtcNow,
        LastError: exception.Message,
        ReleasePageUrl: ReleasePageUrl(null));

    private string ReleasePageUrl(string? version)
    {
        var baseUrl = _options.RepositoryUrl.TrimEnd('/');
        return string.IsNullOrWhiteSpace(version)
            ? $"{baseUrl}/releases"
            : $"{baseUrl}/releases/tag/v{version}";
    }
}
