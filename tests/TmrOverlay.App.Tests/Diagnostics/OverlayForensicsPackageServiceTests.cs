using System.Text.Json;
using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using TmrOverlay.App.Diagnostics;
using TmrOverlay.App.Events;
using TmrOverlay.App.Storage;
using Xunit;

namespace TmrOverlay.App.Tests.Diagnostics;

public sealed class OverlayForensicsPackageServiceTests
{
    [Fact]
    public void CreateInitialPackage_WritesToAppOwnedForensicsRootAndLeavesCaptureUntouched()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-forensics-service-test", Guid.NewGuid().ToString("N"));
        var storage = CreateStorage(root);
        var service = CreateService(storage);
        var captureId = "capture-20260522-204847-774";
        var captureDirectory = Path.Combine(storage.CaptureRoot, captureId);
        Directory.CreateDirectory(captureDirectory);
        Directory.CreateDirectory(storage.DiagnosticsRoot);
        File.WriteAllText(
            Path.Combine(captureDirectory, "capture-manifest.json"),
            $$"""
            {
              "formatVersion": 1,
              "captureId": "{{captureId}}"
            }
            """);
        File.WriteAllBytes(Path.Combine(captureDirectory, "telemetry.bin"), [1, 2, 3]);
        var diagnosticsBundle = Path.Combine(storage.DiagnosticsRoot, "session-finalization.zip");
        File.WriteAllText(diagnosticsBundle, "diagnostics");
        var originalCaptureFiles = TopLevelFileNames(captureDirectory);

        var output = service.CreateInitialPackage(captureDirectory, null, diagnosticsBundle, "unit-test");

        Assert.Equal(Path.Combine(storage.ForensicsRoot, captureId), output);
        Assert.True(File.Exists(Path.Combine(output, "storage-boundary.json")));
        Assert.True(File.Exists(Path.Combine(output, "input-inventory.json")));
        Assert.True(File.Exists(Path.Combine(output, "package-status.json")));
        Assert.True(File.Exists(Path.Combine(output, "obs-readiness.json")));
        Assert.True(File.Exists(Path.Combine(output, "evidence-gaps.json")));
        Assert.True(File.Exists(Path.Combine(output, "overlay-forensics.json")));
        Assert.True(File.Exists(Path.Combine(output, "overlay-forensics.md")));
        Assert.Equal(originalCaptureFiles, TopLevelFileNames(captureDirectory));

        using var boundary = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "storage-boundary.json")));
        Assert.Equal("%LOCALAPPDATA%\\TmrOverlay\\forensics\\<capture-id>", boundary.RootElement.GetProperty("defaultWindowsOutputPath").GetString());
        Assert.Equal("read-only", boundary.RootElement.GetProperty("inputMutationPolicy").GetString());
        Assert.Contains(
            "Enhanced iRacing Telemetry Capture",
            boundary.RootElement.GetProperty("collectionBoundary").GetProperty("windowsHighFidelityCollection").GetString() ?? string.Empty);

        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "overlay-forensics.json")));
        var rootElement = report.RootElement;
        Assert.Equal("initial-package-created", rootElement.GetProperty("status").GetString());
        Assert.Equal(output, rootElement.GetProperty("outputDirectory").GetString());
        Assert.Equal(diagnosticsBundle, rootElement.GetProperty("inputInventory").GetProperty("diagnosticsBundle").GetProperty("path").GetString());
        Assert.Equal(captureDirectory, rootElement.GetProperty("inputInventory").GetProperty("capture").GetProperty("directory").GetString());
        Assert.Equal("initial", rootElement.GetProperty("packageStatus").GetProperty("enrichmentStatus").GetString());
    }

    [Fact]
    public void CreateInitialPackage_ClassifiesObsReadinessFromDiagnosticsBundle()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-forensics-service-test", Guid.NewGuid().ToString("N"));
        var storage = CreateStorage(root);
        var service = CreateService(storage);
        var captureId = "capture-20260522-204847-774";
        var captureDirectory = Path.Combine(storage.CaptureRoot, captureId);
        Directory.CreateDirectory(captureDirectory);
        Directory.CreateDirectory(storage.DiagnosticsRoot);
        File.WriteAllText(
            Path.Combine(captureDirectory, "capture-manifest.json"),
            $$"""{ "formatVersion": 1, "captureId": "{{captureId}}" }""");
        var diagnosticsBundle = Path.Combine(storage.DiagnosticsRoot, "session-finalization.zip");
        using (var archive = ZipFile.Open(diagnosticsBundle, ZipArchiveMode.Create))
        {
            AddZipText(
                archive,
                "metadata/localhost-overlays.json",
                """
                {
                  "pathCounts": {
                    "/overlays/stream-chat": 1,
                    "/api/overlay-model/stream-chat": 38
                  },
                  "pageEventOverlayCounts": {
                    "stream-chat|page-loaded": 1,
                    "stream-chat|model-render": 2
                  },
                  "clientCounts": {
                    "obs": 41
                  }
                }
                """);
            AddZipText(
                archive,
                "metadata/window-z-order.json",
                """
                {
                  "windows": [
                    { "processName": "obs64", "title": "OBS 32.1.2" }
                  ]
                }
                """);
        }

        var output = service.CreateInitialPackage(captureDirectory, captureId, diagnosticsBundle, "unit-test");

        using var readiness = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "obs-readiness.json")));
        var readinessRoot = readiness.RootElement;
        Assert.Equal("classified", readinessRoot.GetProperty("status").GetString());
        Assert.True(readinessRoot.GetProperty("obsProcessPresent").GetBoolean());
        Assert.Equal(
            "model-rendered",
            readinessRoot.GetProperty("overlays").GetProperty("stream-chat").GetProperty("state").GetString());
        Assert.Equal(
            "not-requested",
            readinessRoot.GetProperty("overlays").GetProperty("standings").GetProperty("state").GetString());

        using var gaps = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "evidence-gaps.json")));
        Assert.Contains(
            gaps.RootElement.GetProperty("gaps").EnumerateArray(),
            gap => string.Equals(
                gap.GetProperty("kind").GetString(),
                "obs-process-present-no-telemetry-overlay-routes",
                StringComparison.Ordinal));
    }

    [Fact]
    public void CreateInitialPackage_ClassifiesCanonicalObsReadinessStatesForExpectedOverlays()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-forensics-service-test", Guid.NewGuid().ToString("N"));
        var storage = CreateStorage(root);
        var service = CreateService(storage);
        var captureId = "capture-20260522-obs-readiness";
        var captureDirectory = Path.Combine(storage.CaptureRoot, captureId);
        Directory.CreateDirectory(captureDirectory);
        Directory.CreateDirectory(storage.DiagnosticsRoot);
        File.WriteAllText(
            Path.Combine(captureDirectory, "capture-manifest.json"),
            $$"""{ "formatVersion": 1, "captureId": "{{captureId}}" }""");
        var diagnosticsBundle = Path.Combine(storage.DiagnosticsRoot, "session-finalization.zip");
        using (var archive = ZipFile.Open(diagnosticsBundle, ZipArchiveMode.Create))
        {
            AddZipText(
                archive,
                "metadata/localhost-overlays.json",
                """
                {
                  "pathCounts": {
                    "/overlays/relative": 1,
                    "/overlays/garage-cover": 1,
                    "/overlays/calculator": 1,
                    "/api/overlay-model/fuel-calculator": 5,
                    "/api/overlay-model/session-weather": 4,
                    "/overlays/pit-service": 1,
                    "/api/overlay-model/pit-service": 6,
                    "/overlays/flags": 1,
                    "/api/overlay-model/flags": 7,
                    "/overlays/track-map": 1,
                    "/api/overlay-model/track-map": 3,
                    "/overlays/stream-chat": 1,
                    "/api/overlay-model/stream-chat": 2
                  },
                  "pageEventOverlayCounts": {
                    "relative|page-loaded": 1,
                    "garage-cover|page-loaded": 1,
                    "fuel-calculator|page-loaded": 1,
                    "pit-service|page-loaded": 1,
                    "pit-service|model-hidden": 3,
                    "flags|page-loaded": 1,
                    "flags|model-render": 2,
                    "track-map|page-loaded": 1,
                    "track-map|model-error": 1,
                    "stream-chat|page-loaded": 1,
                    "stream-chat|model-render": 1
                  },
                  "clientCounts": {
                    "obs": 42
                  }
                }
                """);
            AddZipText(
                archive,
                "metadata/localhost-overlay-models.json",
                """
                {
                  "schemaVersion": 1,
                  "pages": [
                    {
                      "id": "input-state",
                      "htmlRouteRequestCount": 1,
                      "modelApiRequestCount": 4,
                      "pageLoadedEventCount": 1,
                      "modelRenderEventCount": 0,
                      "modelHiddenEventCount": 2,
                      "modelErrorEventCount": 0
                    }
                  ]
                }
                """);
        }

        var output = service.CreateInitialPackage(captureDirectory, captureId, diagnosticsBundle, "unit-test");

        using var readiness = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "obs-readiness.json")));
        var overlays = readiness.RootElement.GetProperty("overlays");
        var expectedStates = new HashSet<string>(StringComparer.Ordinal)
        {
            "not-requested",
            "page-loaded-no-model",
            "model-polled-hidden",
            "model-rendered",
            "browser-source-error"
        };

        Assert.Equal(12, overlays.EnumerateObject().Count());
        Assert.All(overlays.EnumerateObject(), overlay =>
            Assert.Contains(overlay.Value.GetProperty("state").GetString(), expectedStates));
        AssertOverlayReadiness(overlays, "standings", "not-requested");
        AssertOverlayReadiness(overlays, "relative", "page-loaded-no-model");
        AssertOverlayReadiness(overlays, "gap-to-leader", "not-requested");
        AssertOverlayReadiness(overlays, "car-radar", "not-requested");
        AssertOverlayReadiness(overlays, "garage-cover", "page-loaded-no-model");
        AssertOverlayReadiness(overlays, "fuel-calculator", "model-polled-hidden");
        Assert.Equal(
            1,
            overlays.GetProperty("fuel-calculator").GetProperty("htmlRouteRequestCount").GetInt32());
        AssertOverlayReadiness(overlays, "session-weather", "model-polled-hidden");
        AssertOverlayReadiness(overlays, "input-state", "model-polled-hidden");
        AssertOverlayReadiness(overlays, "pit-service", "model-polled-hidden");
        AssertOverlayReadiness(overlays, "flags", "model-rendered");
        AssertOverlayReadiness(overlays, "track-map", "browser-source-error");
        AssertOverlayReadiness(overlays, "stream-chat", "model-rendered");
    }

    [Fact]
    public void CreateInitialPackage_PreservesExistingEnrichedPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-forensics-service-test", Guid.NewGuid().ToString("N"));
        var storage = CreateStorage(root);
        var service = CreateService(storage);
        var captureId = "capture-20260522-204847-774";
        var captureDirectory = Path.Combine(storage.CaptureRoot, captureId);
        Directory.CreateDirectory(captureDirectory);
        File.WriteAllText(
            Path.Combine(captureDirectory, "capture-manifest.json"),
            $$"""{ "formatVersion": 1, "captureId": "{{captureId}}" }""");
        var outputDirectory = Path.Combine(storage.ForensicsRoot, captureId);
        Directory.CreateDirectory(outputDirectory);
        var enrichedReportPath = Path.Combine(outputDirectory, "overlay-forensics.json");
        const string enrichedReport = """{"schemaVersion":1,"tool":"tools/analysis/overlay_forensics.py","status":"ok"}""";
        File.WriteAllText(enrichedReportPath, enrichedReport);

        var output = service.CreateInitialPackage(captureDirectory, captureId, null, "startup-recovery");

        Assert.Equal(outputDirectory, output);
        Assert.Equal(enrichedReport, File.ReadAllText(enrichedReportPath));
        Assert.False(File.Exists(Path.Combine(outputDirectory, "storage-boundary.json")));
    }

    private static OverlayForensicsPackageService CreateService(AppStorageOptions storage)
    {
        return new OverlayForensicsPackageService(
            storage,
            new AppEventRecorder(storage),
            NullLogger<OverlayForensicsPackageService>.Instance);
    }

    private static AppStorageOptions CreateStorage(string root)
    {
        return new AppStorageOptions
        {
            AppDataRoot = root,
            CaptureRoot = Path.Combine(root, "captures"),
            UserHistoryRoot = Path.Combine(root, "history", "user"),
            BaselineHistoryRoot = Path.Combine(root, "history", "baseline"),
            LogsRoot = Path.Combine(root, "logs"),
            SettingsRoot = Path.Combine(root, "settings"),
            DiagnosticsRoot = Path.Combine(root, "diagnostics"),
            ForensicsRoot = Path.Combine(root, "forensics"),
            TrackMapRoot = Path.Combine(root, "track-maps", "user"),
            EventsRoot = Path.Combine(root, "logs", "events"),
            RuntimeStatePath = Path.Combine(root, "runtime-state.json"),
            UseRepositoryLocalStorage = false
        };
    }

    private static string[] TopLevelFileNames(string directory)
    {
        return Directory.GetFiles(directory)
            .Select(path => Path.GetFileName(path) ?? string.Empty)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddZipText(ZipArchive archive, string entryName, string text)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(text);
    }

    private static void AssertOverlayReadiness(JsonElement overlays, string overlayId, string expectedState)
    {
        Assert.Equal(
            expectedState,
            overlays.GetProperty(overlayId).GetProperty("state").GetString());
    }
}
