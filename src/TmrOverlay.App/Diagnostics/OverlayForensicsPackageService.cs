using System.Text.Json;
using Microsoft.Extensions.Logging;
using TmrOverlay.App.Events;
using TmrOverlay.App.Storage;

namespace TmrOverlay.App.Diagnostics;

internal sealed class OverlayForensicsPackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly AppStorageOptions _storageOptions;
    private readonly AppEventRecorder _events;
    private readonly ILogger<OverlayForensicsPackageService> _logger;

    public OverlayForensicsPackageService(
        AppStorageOptions storageOptions,
        AppEventRecorder events,
        ILogger<OverlayForensicsPackageService> logger)
    {
        _storageOptions = storageOptions;
        _events = events;
        _logger = logger;
    }

    public string CreateInitialPackage(
        string captureDirectory,
        string? captureId,
        string? diagnosticsBundlePath,
        string source)
    {
        if (string.IsNullOrWhiteSpace(captureDirectory) || !Directory.Exists(captureDirectory))
        {
            throw new DirectoryNotFoundException($"Capture directory not found: {captureDirectory}");
        }

        var resolvedCaptureId = ResolveCaptureId(captureDirectory, captureId);
        var outputDirectory = Path.Combine(ForensicsRoot(), SanitizePathSegment(resolvedCaptureId));
        Directory.CreateDirectory(outputDirectory);

        if (HasEnrichedForensicsReport(outputDirectory))
        {
            _events.Record("overlay_forensics_package_preserved", new Dictionary<string, string?>
            {
                ["captureId"] = resolvedCaptureId,
                ["captureDirectory"] = captureDirectory,
                ["outputDirectory"] = outputDirectory,
                ["diagnosticsBundlePath"] = diagnosticsBundlePath,
                ["source"] = source
            });
            _logger.LogInformation(
                "Preserved enriched overlay forensics package {ForensicsDirectory} for {CaptureId}.",
                outputDirectory,
                resolvedCaptureId);
            return outputDirectory;
        }

        var boundary = BuildStorageBoundary(outputDirectory, captureDirectory, resolvedCaptureId, source);
        var inventory = BuildInputInventory(captureDirectory, diagnosticsBundlePath, resolvedCaptureId);
        var report = BuildReport(outputDirectory, boundary, inventory, source);

        WriteJson(Path.Combine(outputDirectory, "storage-boundary.json"), boundary);
        WriteJson(Path.Combine(outputDirectory, "input-inventory.json"), inventory);
        WriteJson(Path.Combine(outputDirectory, "overlay-forensics.json"), report);
        File.WriteAllText(
            Path.Combine(outputDirectory, "overlay-forensics.md"),
            RenderMarkdown(report),
            System.Text.Encoding.UTF8);

        _events.Record("overlay_forensics_package_created", new Dictionary<string, string?>
        {
            ["captureId"] = resolvedCaptureId,
            ["captureDirectory"] = captureDirectory,
            ["outputDirectory"] = outputDirectory,
            ["diagnosticsBundlePath"] = diagnosticsBundlePath,
            ["source"] = source
        });
        _logger.LogInformation(
            "Created initial overlay forensics package {ForensicsDirectory} for {CaptureId}.",
            outputDirectory,
            resolvedCaptureId);

        return outputDirectory;
    }

    private string ForensicsRoot()
    {
        return !string.IsNullOrWhiteSpace(_storageOptions.ForensicsRoot)
            ? _storageOptions.ForensicsRoot
            : Path.Combine(_storageOptions.AppDataRoot, "forensics");
    }

    private static string ResolveCaptureId(string captureDirectory, string? captureId)
    {
        if (!string.IsNullOrWhiteSpace(captureId))
        {
            return captureId;
        }

        var manifestPath = Path.Combine(captureDirectory, "capture-manifest.json");
        if (File.Exists(manifestPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                if (document.RootElement.TryGetProperty("captureId", out var value)
                    && value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return value.GetString()!;
                }
            }
            catch (JsonException)
            {
                // Fall back to the directory name below.
            }
            catch (IOException)
            {
                // Fall back to the directory name below.
            }
        }

        return Path.GetFileName(captureDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            ?? "capture";
    }

    private static bool HasEnrichedForensicsReport(string outputDirectory)
    {
        var reportPath = Path.Combine(outputDirectory, "overlay-forensics.json");
        if (!File.Exists(reportPath))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = document.RootElement;
            var tool = root.TryGetProperty("tool", out var toolProperty) && toolProperty.ValueKind == JsonValueKind.String
                ? toolProperty.GetString()
                : null;
            var status = root.TryGetProperty("status", out var statusProperty) && statusProperty.ValueKind == JsonValueKind.String
                ? statusProperty.GetString()
                : null;

            return !string.Equals(tool, "TmrOverlay.App initial forensics package", StringComparison.Ordinal)
                || !string.Equals(status, "initial-package-created", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return true;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static object BuildStorageBoundary(
        string outputDirectory,
        string captureDirectory,
        string captureId,
        string source)
    {
        return new
        {
            SchemaVersion = 1,
            CaptureId = captureId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedBy = "TmrOverlay.App",
            Source = source,
            OutputPath = outputDirectory,
            DefaultWindowsOutputPath = "%LOCALAPPDATA%\\TmrOverlay\\forensics\\<capture-id>",
            InputMutationPolicy = "read-only",
            CollectionBoundary = new
            {
                WindowsHighFidelityCollection = "requires Enhanced iRacing Telemetry Capture / TelemetryCapture:RawCaptureEnabled opt-in",
                DiagnosticsBundles = "may reference or summarize forensics evidence but are not the primary storage location",
                OfflineCli = "may enrich this package from the explicit capture directory without starting collection"
            },
            Inputs = new
            {
                CaptureDirectory = captureDirectory
            },
            StorageOwners = new
            {
                RawSessionTruth = "%LOCALAPPDATA%\\TmrOverlay\\captures\\capture-*",
                ForensicsOutput = "%LOCALAPPDATA%\\TmrOverlay\\forensics\\<capture-id>",
                DiagnosticsBundles = "%LOCALAPPDATA%\\TmrOverlay\\diagnostics"
            }
        };
    }

    private static object BuildInputInventory(string captureDirectory, string? diagnosticsBundlePath, string captureId)
    {
        return new
        {
            SchemaVersion = 1,
            Capture = new
            {
                CaptureId = captureId,
                Directory = captureDirectory,
                Manifest = FileInfoOrNull(Path.Combine(captureDirectory, "capture-manifest.json")),
                TelemetrySchema = FileInfoOrNull(Path.Combine(captureDirectory, "telemetry-schema.json")),
                TelemetryBin = FileInfoOrNull(Path.Combine(captureDirectory, "telemetry.bin")),
                LatestSessionYaml = FileInfoOrNull(Path.Combine(captureDirectory, "latest-session.yaml")),
                CaptureSynthesis = FileInfoOrNull(Path.Combine(captureDirectory, "capture-synthesis.json")),
                LiveOverlayDiagnostics = FileInfoOrNull(Path.Combine(captureDirectory, "live-overlay-diagnostics.json")),
                LiveModelParity = FileInfoOrNull(Path.Combine(captureDirectory, "live-model-parity.json")),
                IbtAnalysisStatus = FileInfoOrNull(Path.Combine(captureDirectory, "ibt-analysis", "status.json"))
            },
            DiagnosticsBundle = FileInfoOrNull(diagnosticsBundlePath)
        };
    }

    private static object BuildReport(string outputDirectory, object boundary, object inventory, string source)
    {
        return new
        {
            SchemaVersion = 1,
            Tool = "TmrOverlay.App initial forensics package",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Source = source,
            Status = "initial-package-created",
            OutputDirectory = outputDirectory,
            StorageBoundary = boundary,
            InputInventory = inventory,
            Limitations = new[]
            {
                "This app-created package indexes enhanced-capture evidence automatically.",
                "It does not run offline replay, browser screenshot replay, or native pixel validation.",
                "Use tools/analysis/overlay_forensics.py to enrich this package from the explicit capture when needed."
            },
            ArtifactFiles = new[]
            {
                "storage-boundary.json",
                "input-inventory.json",
                "overlay-forensics.json",
                "overlay-forensics.md"
            }
        };
    }

    private static object? FileInfoOrNull(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var info = new FileInfo(path);
        return new
        {
            Path = path,
            Bytes = info.Length,
            LastWriteTimeUtc = info.LastWriteTimeUtc
        };
    }

    private static void WriteJson(string path, object value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine);
    }

    private static string RenderMarkdown(object report)
    {
        var json = JsonSerializer.Serialize(report, JsonOptions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var output = root.GetProperty("outputDirectory").GetString();
        var source = root.GetProperty("source").GetString();
        return string.Join(
            Environment.NewLine,
            "# Overlay Forensics",
            "",
            $"Status: `{root.GetProperty("status").GetString()}`",
            $"Source: `{source}`",
            $"Output: `{output}`",
            "",
            "This package was created automatically because enhanced telemetry capture produced an explicit raw capture.",
            "Run the offline overlay forensics replay tool against the same capture to add sampled model rows, renderer screenshots, and semantic checks.",
            "");
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "capture" : sanitized;
    }
}
