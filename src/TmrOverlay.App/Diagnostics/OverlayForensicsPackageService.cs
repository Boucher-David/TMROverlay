using System.Text.Json;
using System.IO.Compression;
using Microsoft.Extensions.Logging;
using TmrOverlay.App.Events;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Storage;

namespace TmrOverlay.App.Diagnostics;

internal sealed class OverlayForensicsPackageService
{
    private static readonly string[] BrowserOverlayIds = BrowserOverlayCatalog.Pages
        .Select(page => page.Id)
        .ToArray();

    private static readonly string[] TelemetryOverlayIds = BrowserOverlayCatalog.Pages
        .Where(page => page.RequiresTelemetry)
        .Select(page => page.Id)
        .ToArray();

    private static readonly IReadOnlyDictionary<string, string[]> BrowserOverlayRoutesById = BrowserOverlayCatalog.Pages
        .ToDictionary(
            page => page.Id,
            page => page.Routes.ToArray(),
            StringComparer.OrdinalIgnoreCase);

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
        var packageStatus = BuildPackageStatus(outputDirectory, resolvedCaptureId, source);
        var obsReadiness = BuildObsReadiness(diagnosticsBundlePath);
        var evidenceGaps = BuildEvidenceGaps(obsReadiness);
        var report = BuildReport(outputDirectory, boundary, inventory, source, packageStatus, obsReadiness, evidenceGaps);

        WriteJson(Path.Combine(outputDirectory, "storage-boundary.json"), boundary);
        WriteJson(Path.Combine(outputDirectory, "input-inventory.json"), inventory);
        WriteJson(Path.Combine(outputDirectory, "package-status.json"), packageStatus);
        WriteJson(Path.Combine(outputDirectory, "obs-readiness.json"), obsReadiness);
        WriteJson(Path.Combine(outputDirectory, "evidence-gaps.json"), evidenceGaps);
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

    private static object BuildPackageStatus(string outputDirectory, string captureId, string source)
    {
        return new
        {
            SchemaVersion = 1,
            CaptureId = captureId,
            Status = "initial-package-created",
            EnrichmentStatus = "initial",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedBy = "TmrOverlay.App",
            Source = source,
            OutputDirectory = outputDirectory,
            ModelReplay = new
            {
                Status = "not-run",
                Reason = "App finalization created a starter package; production model replay has not enriched this package yet."
            },
            RendererReplay = new
            {
                Status = "not-run",
                Reason = "Browser/localhost/native replay screenshots have not enriched this package yet."
            },
            NextActions = new[]
            {
                "Run tools/analysis/overlay_forensics.py against the explicit capture to add active model samples, renderer screenshots, and semantic checks.",
                "Preserve this forensics directory; app startup recovery should not replace an enriched package."
            }
        };
    }

    private static OverlayReadinessDocument BuildObsReadiness(string? diagnosticsBundlePath)
    {
        using var localhostDocument = TryReadJsonEntry(diagnosticsBundlePath, "metadata/localhost-overlays.json");
        using var localhostModelsDocument = TryReadJsonEntry(diagnosticsBundlePath, "metadata/localhost-overlay-models.json");
        using var windowDocument = TryReadJsonEntry(diagnosticsBundlePath, "metadata/window-z-order.json");

        if (localhostDocument is null && localhostModelsDocument is null && windowDocument is null)
        {
            return new OverlayReadinessDocument(
                SchemaVersion: 1,
                Status: "unavailable",
                ObsProcessPresent: null,
                Reason: "No diagnostics localhost/window metadata was available in the starter package.",
                Overlays: BrowserOverlayIds.ToDictionary(
                    overlayId => overlayId,
                    overlayId => new OverlayReadinessState(
                        OverlayId: overlayId,
                        State: "unknown",
                        Severity: "warn",
                        Detail: "Route readiness cannot be classified until diagnostics metadata or offline enrichment is available.",
                        HtmlRouteRequestCount: 0,
                        ModelApiRequestCount: 0,
                        PageLoadedEventCount: 0,
                        ModelRenderEventCount: 0,
                        ModelHiddenEventCount: 0,
                        ModelNullEventCount: 0,
                        ModelErrorEventCount: 0),
                    StringComparer.OrdinalIgnoreCase));
        }

        var pathCounts = ReadStringIntMap(localhostDocument, "pathCounts");
        var pageEventCounts = ReadStringIntMap(localhostDocument, "pageEventOverlayCounts");
        var clientCounts = ReadStringIntMap(localhostDocument, "clientCounts");
        var modelPageCounts = ReadModelPageCounts(localhostModelsDocument);
        var obsProcessPresent = HasObsWindow(windowDocument) || clientCounts.GetValueOrDefault("obs") > 0
            ? true
            : (bool?)null;
        var overlays = BrowserOverlayIds.ToDictionary(
            overlayId => overlayId,
            overlayId => BuildOverlayReadinessState(overlayId, pathCounts, pageEventCounts, modelPageCounts),
            StringComparer.OrdinalIgnoreCase);

        return new OverlayReadinessDocument(
            SchemaVersion: 1,
            Status: "classified",
            ObsProcessPresent: obsProcessPresent,
            Reason: null,
            Overlays: overlays);
    }

    private static OverlayReadinessState BuildOverlayReadinessState(
        string overlayId,
        IReadOnlyDictionary<string, int> pathCounts,
        IReadOnlyDictionary<string, int> pageEventCounts,
        IReadOnlyDictionary<string, OverlayRouteCounts> modelPageCounts)
    {
        var html = BrowserOverlayRoutesById.TryGetValue(overlayId, out var htmlRoutes)
            ? htmlRoutes.Sum(route => pathCounts.GetValueOrDefault(route))
            : pathCounts.GetValueOrDefault($"/overlays/{overlayId}");
        var model = pathCounts.GetValueOrDefault($"/api/overlay-model/{overlayId}");
        var pageLoaded = pageEventCounts.GetValueOrDefault($"{overlayId}|page-loaded");
        var render = pageEventCounts.GetValueOrDefault($"{overlayId}|model-render");
        var hidden = pageEventCounts.GetValueOrDefault($"{overlayId}|model-hidden");
        var modelNull = pageEventCounts.GetValueOrDefault($"{overlayId}|model-null");
        var error = pageEventCounts.GetValueOrDefault($"{overlayId}|model-error");

        if (modelPageCounts.TryGetValue(overlayId, out var pageCounts))
        {
            html = Math.Max(html, pageCounts.HtmlRouteRequestCount);
            model = Math.Max(model, pageCounts.ModelApiRequestCount);
            pageLoaded = Math.Max(pageLoaded, pageCounts.PageLoadedEventCount);
            render = Math.Max(render, pageCounts.ModelRenderEventCount);
            hidden = Math.Max(hidden, pageCounts.ModelHiddenEventCount);
            modelNull = Math.Max(modelNull, pageCounts.ModelNullEventCount);
            error = Math.Max(error, pageCounts.ModelErrorEventCount);
        }

        string state;
        string detail;
        if (error > 0)
        {
            state = "browser-source-error";
            detail = "Browser source posted model-error events.";
        }
        else if (render > 0)
        {
            state = "model-rendered";
            detail = "Browser source requested models and reported rendered frames.";
        }
        else if (model > 0 || hidden > 0 || modelNull > 0)
        {
            state = "model-polled-hidden";
            detail = hidden > 0
                ? "Browser source requested models, but observed page events were hidden."
                : modelNull > 0
                    ? "Browser source requested models, but observed page events reported no model."
                    : "Browser source requested models, but no rendered frame was observed.";
        }
        else if (pageLoaded > 0 || html > 0)
        {
            state = "page-loaded-no-model";
            detail = pageLoaded > 0
                ? "Browser source page loaded but did not request the overlay model API."
                : "Overlay HTML route was requested, but no page-loaded event or model API request was observed.";
        }
        else
        {
            state = "not-requested";
            detail = "No overlay HTML, model API, or page events were observed.";
        }

        var severity = state is "model-rendered" ? "info" : "warn";
        return new OverlayReadinessState(
            OverlayId: overlayId,
            State: state,
            Severity: severity,
            Detail: detail,
            HtmlRouteRequestCount: html,
            ModelApiRequestCount: model,
            PageLoadedEventCount: pageLoaded,
            ModelRenderEventCount: render,
            ModelHiddenEventCount: hidden,
            ModelNullEventCount: modelNull,
            ModelErrorEventCount: error);
    }

    private static object BuildEvidenceGaps(OverlayReadinessDocument obsReadiness)
    {
        var gaps = new List<object>
        {
            new
            {
                Status = "warn",
                Kind = "production-model-replay-missing",
                Detail = "Starter package has not been enriched with active production model samples."
            },
            new
            {
                Status = "warn",
                Kind = "renderer-replay-missing",
                Detail = "Starter package has not been enriched with browser/localhost/native replay screenshots."
            }
        };

        if (string.Equals(obsReadiness.Status, "unavailable", StringComparison.OrdinalIgnoreCase))
        {
            gaps.Add(new
            {
                Status = "warn",
                Kind = "obs-readiness-unavailable",
                Detail = obsReadiness.Reason
            });
        }
        else if (obsReadiness.ObsProcessPresent == true
            && TelemetryOverlayIds.All(overlayId => obsReadiness.Overlays.TryGetValue(overlayId, out var readiness)
                && readiness.ModelApiRequestCount == 0
                && readiness.PageLoadedEventCount == 0))
        {
            gaps.Add(new
            {
                Status = "warn",
                Kind = "obs-process-present-no-telemetry-overlay-routes",
                Detail = "Diagnostics saw OBS or OBS browser clients, but no telemetry overlay routes were requested."
            });
        }

        return new
        {
            SchemaVersion = 1,
            GapCount = gaps.Count,
            Gaps = gaps
        };
    }

    private static object BuildReport(
        string outputDirectory,
        object boundary,
        object inventory,
        string source,
        object packageStatus,
        OverlayReadinessDocument obsReadiness,
        object evidenceGaps)
    {
        return new
        {
            SchemaVersion = 1,
            Tool = "TmrOverlay.App initial forensics package",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Source = source,
            Status = "initial-package-created",
            OutputDirectory = outputDirectory,
            PackageStatus = packageStatus,
            StorageBoundary = boundary,
            InputInventory = inventory,
            ObsReadiness = obsReadiness,
            EvidenceGaps = evidenceGaps,
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
                "package-status.json",
                "obs-readiness.json",
                "evidence-gaps.json",
                "overlay-forensics.json",
                "overlay-forensics.md"
            }
        };
    }

    private static JsonDocument? TryReadJsonEntry(string? zipPath, string entryName)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
        {
            return null;
        }

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var entry = archive.GetEntry(entryName);
            if (entry is null)
            {
                return null;
            }

            using var stream = entry.Open();
            return JsonDocument.Parse(stream);
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<string, int> ReadStringIntMap(JsonDocument? document, string propertyName)
    {
        if (document is null
            || !document.RootElement.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in property.EnumerateObject())
        {
            result[item.Name] = item.Value.ValueKind == JsonValueKind.Number && item.Value.TryGetInt32(out var value)
                ? value
                : 0;
        }

        return result;
    }

    private static IReadOnlyDictionary<string, OverlayRouteCounts> ReadModelPageCounts(JsonDocument? document)
    {
        if (document is null
            || !document.RootElement.TryGetProperty("pages", out var pages)
            || pages.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<string, OverlayRouteCounts>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, OverlayRouteCounts>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages.EnumerateArray())
        {
            if (page.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var overlayId = TryGetString(page, "id");
            if (string.IsNullOrWhiteSpace(overlayId))
            {
                continue;
            }

            result[overlayId] = new OverlayRouteCounts(
                HtmlRouteRequestCount: ReadInt(page, "htmlRouteRequestCount"),
                ModelApiRequestCount: ReadInt(page, "modelApiRequestCount"),
                PageLoadedEventCount: ReadInt(page, "pageLoadedEventCount"),
                ModelRenderEventCount: ReadInt(page, "modelRenderEventCount"),
                ModelHiddenEventCount: ReadInt(page, "modelHiddenEventCount"),
                ModelNullEventCount: ReadInt(page, "modelNullEventCount"),
                ModelErrorEventCount: ReadInt(page, "modelErrorEventCount"));
        }

        return result;
    }

    private static int ReadInt(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out var value))
        {
            return value;
        }

        return 0;
    }

    private static bool HasObsWindow(JsonDocument? document)
    {
        if (document is null
            || !document.RootElement.TryGetProperty("windows", out var windows)
            || windows.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var window in windows.EnumerateArray())
        {
            var process = TryGetString(window, "processName") ?? TryGetString(window, "name") ?? string.Empty;
            var title = TryGetString(window, "title") ?? string.Empty;
            if (process.Contains("obs", StringComparison.OrdinalIgnoreCase)
                || title.Contains("obs", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
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

    private sealed record OverlayReadinessDocument(
        int SchemaVersion,
        string Status,
        bool? ObsProcessPresent,
        string? Reason,
        IReadOnlyDictionary<string, OverlayReadinessState> Overlays);

    private sealed record OverlayReadinessState(
        string OverlayId,
        string State,
        string Severity,
        string Detail,
        int HtmlRouteRequestCount,
        int ModelApiRequestCount,
        int PageLoadedEventCount,
        int ModelRenderEventCount,
        int ModelHiddenEventCount,
        int ModelNullEventCount,
        int ModelErrorEventCount);

    private sealed record OverlayRouteCounts(
        int HtmlRouteRequestCount,
        int ModelApiRequestCount,
        int PageLoadedEventCount,
        int ModelRenderEventCount,
        int ModelHiddenEventCount,
        int ModelNullEventCount,
        int ModelErrorEventCount);
}
