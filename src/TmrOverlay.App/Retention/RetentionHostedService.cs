using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TmrOverlay.App.Storage;
using TmrOverlay.App.Telemetry;

namespace TmrOverlay.App.Retention;

internal sealed class RetentionHostedService : IHostedService, IDisposable
{
    private readonly AppStorageOptions _storageOptions;
    private readonly RetentionOptions _options;
    private readonly LiveModelParityOptions _liveModelParityOptions;
    private readonly LiveOverlayDiagnosticsOptions _liveOverlayDiagnosticsOptions;
    private readonly FuelV2CaptureOptions _fuelV2CaptureOptions;
    private readonly ILogger<RetentionHostedService> _logger;
    private readonly CancellationTokenSource _cleanupCancellation = new();
    private Task _cleanupTask = Task.CompletedTask;

    public RetentionHostedService(
        AppStorageOptions storageOptions,
        RetentionOptions options,
        LiveModelParityOptions liveModelParityOptions,
        LiveOverlayDiagnosticsOptions liveOverlayDiagnosticsOptions,
        ILogger<RetentionHostedService> logger,
        FuelV2CaptureOptions? fuelV2CaptureOptions = null)
    {
        _storageOptions = storageOptions;
        _options = options;
        _liveModelParityOptions = liveModelParityOptions;
        _liveOverlayDiagnosticsOptions = liveOverlayDiagnosticsOptions;
        _fuelV2CaptureOptions = fuelV2CaptureOptions ?? new FuelV2CaptureOptions();
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return Task.CompletedTask;
        }

        _cleanupTask = Task.Run(
            () => RunStartupCleanupAsync(_cleanupCancellation.Token),
            CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cleanupCancellation.Cancel();
        try
        {
            await _cleanupTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _cleanupCancellation.IsCancellationRequested)
        {
        }
    }

    public void Dispose()
    {
        _cleanupCancellation.Cancel();
        _cleanupCancellation.Dispose();
    }

    public Task RunAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return Task.CompletedTask;
        }

        CleanupDirectories(_storageOptions.CaptureRoot, "capture-*", _options.CaptureRetentionDays, _options.MaxCaptureDirectories, cancellationToken);
        CleanupFiles(_storageOptions.DiagnosticsRoot, "*.zip", _options.DiagnosticsRetentionDays, _options.MaxDiagnosticsBundles, cancellationToken);
        CleanupFiles(
            Path.Combine(_storageOptions.LogsRoot, "performance"),
            "performance-*.jsonl",
            _options.PerformanceLogRetentionDays,
            _options.MaxPerformanceLogFiles,
            cancellationToken);
        CleanupFiles(
            Path.Combine(_storageOptions.LogsRoot, "edge-cases"),
            "*-edge-cases.json",
            _options.EdgeCaseRetentionDays,
            _options.MaxEdgeCaseFiles,
            cancellationToken);
        CleanupFiles(
            Path.Combine(_storageOptions.LogsRoot, _liveModelParityOptions.LogDirectoryName),
            $"*{_liveModelParityOptions.OutputFileName}",
            _options.EdgeCaseRetentionDays,
            _options.MaxEdgeCaseFiles,
            cancellationToken);
        CleanupFiles(
            Path.Combine(_storageOptions.LogsRoot, _liveOverlayDiagnosticsOptions.LogDirectoryName),
            $"*{_liveOverlayDiagnosticsOptions.OutputFileName}",
            _options.EdgeCaseRetentionDays,
            _options.MaxEdgeCaseFiles,
            cancellationToken);
        CleanupFiles(
            Path.Combine(_storageOptions.LogsRoot, _fuelV2CaptureOptions.LogDirectoryName),
            $"*{_fuelV2CaptureOptions.OutputFileName}",
            _options.EdgeCaseRetentionDays,
            _options.MaxEdgeCaseFiles,
            cancellationToken);
        return Task.CompletedTask;
    }

    private async Task RunStartupCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Retention cleanup failed.");
        }
    }

    private void CleanupDirectories(string root, string searchPattern, int retentionDays, int maxCount, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var cutoffUtc = DateTime.UtcNow.AddDays(-retentionDays);
        var directories = Directory
            .EnumerateDirectories(root, searchPattern)
            .Select(path => new DirectoryInfo(path))
            .OrderByDescending(directory => directory.LastWriteTimeUtc)
            .ToArray();

        foreach (var directory in directories.Skip(maxCount).Concat(directories.Where(directory => directory.LastWriteTimeUtc < cutoffUtc)).DistinctBy(directory => directory.FullName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryDeleteDirectory(directory.FullName);
        }
    }

    private void CleanupFiles(string root, string searchPattern, int retentionDays, int maxCount, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var cutoffUtc = DateTime.UtcNow.AddDays(-retentionDays);
        var files = Directory
            .EnumerateFiles(root, searchPattern)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();

        foreach (var file in files.Skip(maxCount).Concat(files.Where(file => file.LastWriteTimeUtc < cutoffUtc)).DistinctBy(file => file.FullName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryDeleteFile(file.FullName);
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
            _logger.LogInformation("Deleted old retained directory {DirectoryPath}.", path);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to delete retained directory {DirectoryPath}.", path);
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
            _logger.LogInformation("Deleted old retained file {FilePath}.", path);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to delete retained file {FilePath}.", path);
        }
    }
}
