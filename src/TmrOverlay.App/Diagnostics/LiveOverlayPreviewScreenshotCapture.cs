using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Extensions.Logging.Abstractions;
using TmrOverlay.App.History;
using TmrOverlay.App.Overlays;
using TmrOverlay.App.Overlays.CarRadar;
using TmrOverlay.App.Overlays.DesignV2;
using TmrOverlay.App.Overlays.Flags;
using TmrOverlay.App.Overlays.FuelCalculator;
using TmrOverlay.App.Overlays.GapToLeader;
using TmrOverlay.App.Overlays.InputState;
using TmrOverlay.App.Overlays.PitService;
using TmrOverlay.App.Overlays.Relative;
using TmrOverlay.App.Overlays.SessionWeather;
using TmrOverlay.App.Overlays.Standings;
using TmrOverlay.App.Overlays.StreamChat;
using TmrOverlay.App.Overlays.TrackMap;
using TmrOverlay.App.Performance;
using TmrOverlay.App.Settings;
using TmrOverlay.App.Storage;
using TmrOverlay.App.Telemetry;
using TmrOverlay.App.TrackMaps;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Diagnostics;

internal static class LiveOverlayPreviewScreenshotCapture
{
    private const string EntryDirectory = "live-overlays/previews";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static LiveOverlayPreviewScreenshotCaptureResult Capture(
        AppStorageOptions storageOptions,
        LiveOverlayWindowCaptureOptions options,
        AppSettingsStore settingsStore,
        TrackMapStore trackMapStore,
        AppPerformanceState performanceState)
    {
        var generatedAtUtc = DateTimeOffset.UtcNow;
        if (!options.CapturePreviewScreenshots)
        {
            return Disabled(generatedAtUtc, options);
        }

        try
        {
            var settingsSnapshot = CloneSettings(settingsStore.Load());
            return RunOnStaThread(() => CaptureCore(
                generatedAtUtc,
                storageOptions,
                options,
                settingsSnapshot,
                trackMapStore,
                performanceState));
        }
        catch (Exception exception)
        {
            return Failed(generatedAtUtc, options, exception);
        }
    }

    private static LiveOverlayPreviewScreenshotCaptureResult CaptureCore(
        DateTimeOffset generatedAtUtc,
        AppStorageOptions storageOptions,
        LiveOverlayWindowCaptureOptions options,
        ApplicationSettings settingsSnapshot,
        TrackMapStore trackMapStore,
        AppPerformanceState performanceState)
    {
        var requests = PreviewCaptureRequests().ToArray();
        var maxScreenshots = Math.Clamp(options.MaxPreviewScreenshots, 1, 128);
        var selectedRequests = requests.Take(maxScreenshots).ToArray();
        var omittedByCap = Math.Max(0, requests.Length - selectedRequests.Length);
        var warnings = new List<string>();
        if (omittedByCap > 0)
        {
            warnings.Add("preview_screenshot_cap_reached");
        }

        var screenshots = new List<LiveOverlayPreviewScreenshotState>();
        var images = new List<LiveOverlayPreviewScreenshotImage>();
        using var streamChatSource = new StreamChatOverlaySource(
            NullLogger<StreamChatOverlaySource>.Instance,
            performanceState);
        var history = new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = storageOptions.UserHistoryRoot,
            ResolvedBaselineHistoryRoot = storageOptions.BaselineHistoryRoot
        });

        foreach (var request in selectedRequests)
        {
            try
            {
                var capture = CaptureRequest(
                    request,
                    settingsSnapshot,
                    trackMapStore,
                    history,
                    streamChatSource,
                    performanceState);
                screenshots.Add(capture.State);
                images.Add(capture.Image);
                if (capture.State.StreamChatExternalSourceSuppressed == true)
                {
                    warnings.Add("stream_chat_external_source_suppressed_for_preview_capture");
                }
            }
            catch (Exception exception)
            {
                screenshots.Add(FailedState(request, exception));
                warnings.Add("preview_screenshot_capture_errors_present");
            }
        }

        var manifest = Manifest(
            generatedAtUtc,
            options,
            requestCount: requests.Length,
            capturedCount: images.Count,
            failedCount: screenshots.Count(state => !state.Captured),
            omittedByCap,
            warnings,
            screenshots);
        return new LiveOverlayPreviewScreenshotCaptureResult(manifest, images);
    }

    private static LiveOverlayPreviewCapture CaptureRequest(
        LiveOverlayPreviewCaptureRequest request,
        ApplicationSettings settingsSnapshot,
        TrackMapStore trackMapStore,
        SessionHistoryQueryService history,
        StreamChatOverlaySource streamChatSource,
        AppPerformanceState performanceState)
    {
        var requestSettingsSnapshot = CloneSettings(settingsSnapshot);
        var overlaySettings = requestSettingsSnapshot.GetOrAddOverlay(
            request.Spec.Definition.Id,
            request.Spec.Definition.DefaultWidth,
            request.Spec.Definition.DefaultHeight,
            defaultEnabled: false);
        var enabledSetting = overlaySettings.Enabled;
        var streamChatExternalSourceSuppressed = request.Spec.Kind == DesignV2LiveOverlayKind.StreamChat
            && !string.Equals(
                overlaySettings.GetStringOption(OverlayOptionKeys.StreamChatProvider, StreamChatOverlaySettings.ProviderTwitch),
                StreamChatOverlaySettings.ProviderNone,
                StringComparison.OrdinalIgnoreCase);
        if (request.Spec.Kind == DesignV2LiveOverlayKind.StreamChat)
        {
            overlaySettings.SetStringOption(OverlayOptionKeys.StreamChatProvider, StreamChatOverlaySettings.ProviderNone);
        }

        using var form = CreatePreviewForm(
            request,
            overlaySettings,
            trackMapStore,
            history,
            streamChatSource,
            performanceState);
        PrepareForm(form, RefreshPassesFor(request.Spec.Kind));
        using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height, PixelFormat.Format32bppArgb);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.ClientSize));
        LiveOverlayWindowCaptureStore.ApplyTransparencyKeyForCapture(bitmap, form.TransparencyKey);
        var pngBytes = BitmapToPng(bitmap);
        var fileName = $"{SafeFileName(request.Spec.Definition.Id)}-{ModeFileStem(request.Mode)}.png";
        var entryName = $"{EntryDirectory}/{fileName}";
        var state = new LiveOverlayPreviewScreenshotState(
            OverlayId: request.Spec.Definition.Id,
            DisplayName: request.Spec.Definition.DisplayName,
            PreviewMode: request.Mode.ToString(),
            Captured: true,
            EntryName: entryName,
            Error: null,
            Width: form.ClientSize.Width,
            Height: form.ClientSize.Height,
            Renderer: "native-v2",
            NativeFormType: nameof(DesignV2LiveOverlayForm),
            NativeRenderer: $"design-v2/{form.DiagnosticKind}",
            NativeBodyKind: form.DiagnosticBodyKind,
            Status: form.DiagnosticStatus,
            Evidence: form.DiagnosticEvidence,
            ShouldRender: form.DiagnosticShouldRender,
            EnabledSetting: enabledSetting,
            ShowInPracticeSetting: overlaySettings.ShowInPractice,
            ShowInQualifyingSetting: overlaySettings.ShowInQualifying,
            ShowInRaceSetting: overlaySettings.ShowInRace,
            Scale: Math.Round(Math.Clamp(overlaySettings.Scale, 0.6d, 2d), 3),
            Opacity: Math.Round(overlaySettings.Opacity, 3),
            TransparentBackground: request.Spec.UsesTransparentBackdrop,
            ForcedVisibleForDiagnostics: true,
            SettingsPreviewForced: request.Spec.Kind == DesignV2LiveOverlayKind.CarRadar,
            StreamChatExternalSourceSuppressed: streamChatExternalSourceSuppressed,
            Fixture: "SessionPreviewTelemetryFixtures",
            SourceContract: "src/TmrOverlay.App/Telemetry/SessionPreviewState.cs",
            Layout: form.DiagnosticLayout);
        return new LiveOverlayPreviewCapture(state, new LiveOverlayPreviewScreenshotImage(entryName, pngBytes));
    }

    private static DesignV2LiveOverlayForm CreatePreviewForm(
        LiveOverlayPreviewCaptureRequest request,
        OverlaySettings settings,
        TrackMapStore trackMapStore,
        SessionHistoryQueryService history,
        StreamChatOverlaySource streamChatSource,
        AppPerformanceState performanceState)
    {
        var telemetry = new PreviewTelemetrySource(request.Mode);
        var form = new DesignV2LiveOverlayForm(
            request.Spec.Kind,
            request.Spec.Definition,
            telemetry,
            trackMapStore,
            history,
            streamChatSource,
            performanceState,
            NullLogger<DesignV2LiveOverlayForm>.Instance,
            settings,
            SharedOverlayContract.Current.DefaultFontFamily,
            SharedOverlayContract.Current.DefaultUnitSystem,
            () => { });
        form.ShowInTaskbar = false;
        form.ClientSize = OverlayManager.TargetOverlayClientSizeForApply(
            request.Spec.Definition,
            settings,
            form.ClientSize,
            sessionPreviewActive: true,
            sessionKind: request.Mode);
        if (request.Spec.Kind == DesignV2LiveOverlayKind.CarRadar)
        {
            form.SetSettingsPreviewVisible(true);
        }

        return form;
    }

    private static void PrepareForm(DesignV2LiveOverlayForm form, int refreshPasses)
    {
        var targetClientSize = form.ClientSize;
        form.Location = new Point(-20000, -20000);
        form.CreateControl();
        CreateControlHandles(form);
        if (form.ClientSize != targetClientSize)
        {
            form.ClientSize = targetClientSize;
        }

        form.PerformLayout();
        Application.DoEvents();
        for (var pass = 0; pass < refreshPasses; pass++)
        {
            form.RefreshForDiagnostics();
            Application.DoEvents();
        }

        if (form.ClientSize != targetClientSize)
        {
            form.ClientSize = targetClientSize;
            Application.DoEvents();
        }

        form.PerformLayout();
    }

    private static void CreateControlHandles(Control control)
    {
        _ = control.Handle;
        foreach (Control child in control.Controls)
        {
            CreateControlHandles(child);
        }
    }

    private static byte[] BitmapToPng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static LiveOverlayPreviewScreenshotState FailedState(
        LiveOverlayPreviewCaptureRequest request,
        Exception exception)
    {
        return new LiveOverlayPreviewScreenshotState(
            OverlayId: request.Spec.Definition.Id,
            DisplayName: request.Spec.Definition.DisplayName,
            PreviewMode: request.Mode.ToString(),
            Captured: false,
            EntryName: null,
            Error: exception.Message,
            Width: null,
            Height: null,
            Renderer: "native-v2",
            NativeFormType: nameof(DesignV2LiveOverlayForm),
            NativeRenderer: $"design-v2/{KindName(request.Spec.Kind)}",
            NativeBodyKind: null,
            Status: null,
            Evidence: null,
            ShouldRender: null,
            EnabledSetting: null,
            ShowInPracticeSetting: null,
            ShowInQualifyingSetting: null,
            ShowInRaceSetting: null,
            Scale: null,
            Opacity: null,
            TransparentBackground: request.Spec.UsesTransparentBackdrop,
            ForcedVisibleForDiagnostics: true,
            SettingsPreviewForced: request.Spec.Kind == DesignV2LiveOverlayKind.CarRadar,
            StreamChatExternalSourceSuppressed: request.Spec.Kind == DesignV2LiveOverlayKind.StreamChat,
            Fixture: "SessionPreviewTelemetryFixtures",
            SourceContract: "src/TmrOverlay.App/Telemetry/SessionPreviewState.cs",
            Layout: null);
    }

    private static LiveOverlayPreviewScreenshotCaptureResult Disabled(
        DateTimeOffset generatedAtUtc,
        LiveOverlayWindowCaptureOptions options)
    {
        var manifest = Manifest(
            generatedAtUtc,
            options,
            requestCount: PreviewCaptureRequests().Count,
            capturedCount: 0,
            failedCount: 0,
            omittedByCap: 0,
            ["preview_screenshot_capture_disabled"],
            []);
        return new LiveOverlayPreviewScreenshotCaptureResult(manifest, []);
    }

    private static LiveOverlayPreviewScreenshotCaptureResult Failed(
        DateTimeOffset generatedAtUtc,
        LiveOverlayWindowCaptureOptions options,
        Exception exception)
    {
        var manifest = Manifest(
            generatedAtUtc,
            options,
            requestCount: PreviewCaptureRequests().Count,
            capturedCount: 0,
            failedCount: 1,
            omittedByCap: 0,
            ["preview_screenshot_capture_failed"],
            [new LiveOverlayPreviewScreenshotState(
                OverlayId: "preview-capture",
                DisplayName: "Preview Capture",
                PreviewMode: null,
                Captured: false,
                EntryName: null,
                Error: exception.Message,
                Width: null,
                Height: null,
                Renderer: "native-v2",
                NativeFormType: null,
                NativeRenderer: null,
                NativeBodyKind: null,
                Status: null,
                Evidence: null,
                ShouldRender: null,
                EnabledSetting: null,
                ShowInPracticeSetting: null,
                ShowInQualifyingSetting: null,
                ShowInRaceSetting: null,
                Scale: null,
                Opacity: null,
                TransparentBackground: null,
                ForcedVisibleForDiagnostics: true,
                SettingsPreviewForced: null,
                StreamChatExternalSourceSuppressed: null,
                Fixture: "SessionPreviewTelemetryFixtures",
                SourceContract: "src/TmrOverlay.App/Telemetry/SessionPreviewState.cs",
                Layout: null)]);
        return new LiveOverlayPreviewScreenshotCaptureResult(manifest, []);
    }

    private static LiveOverlayPreviewScreenshotManifest Manifest(
        DateTimeOffset generatedAtUtc,
        LiveOverlayWindowCaptureOptions options,
        int requestCount,
        int capturedCount,
        int failedCount,
        int omittedByCap,
        IReadOnlyList<string> warnings,
        IReadOnlyList<LiveOverlayPreviewScreenshotState> screenshots)
    {
        return new LiveOverlayPreviewScreenshotManifest(
            GeneratedAtUtc: generatedAtUtc,
            CaptureKind: "deterministic-session-preview-native-renders",
            CapturePreviewScreenshotsEnabled: options.CapturePreviewScreenshots,
            MaxPreviewScreenshots: Math.Clamp(options.MaxPreviewScreenshots, 1, 128),
            Description: "Deterministic native Design V2 overlay screenshots rendered from the same SessionPreviewTelemetryFixtures used by Show Preview. These images are generated off-screen for diagnostics and do not capture the user's desktop or mutate visible overlay windows.",
            Limitation: "Preview screenshots force each native overlay to render once per supported preview mode so support can inspect current settings-driven layout. They are not proof of real desktop z-order, live telemetry timing, OBS browser-source rendering, or actual current on-track pixels.",
            Coverage: new LiveOverlayPreviewScreenshotCoverage(
                RequestedScreenshotCount: requestCount,
                CapturedScreenshotCount: capturedCount,
                FailedScreenshotCount: failedCount,
                OmittedByCapCount: omittedByCap),
            EvidenceWarnings: warnings.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            Screenshots: screenshots);
    }

    private static ApplicationSettings CloneSettings(ApplicationSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        return JsonSerializer.Deserialize<ApplicationSettings>(json, JsonOptions) ?? new ApplicationSettings();
    }

    private static T RunOnStaThread<T>(Func<T> action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return action();
        }

        T? result = default;
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception capturedException)
            {
                exception = capturedException;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        if (exception is not null)
        {
            throw exception;
        }

        return result!;
    }

    private static IReadOnlyList<LiveOverlayPreviewCaptureRequest> PreviewCaptureRequests()
    {
        return NativeOverlaySpecs()
            .SelectMany(spec => PreviewModesForOverlay(spec.Definition.Id)
                .Select(mode => new LiveOverlayPreviewCaptureRequest(spec, mode)))
            .ToArray();
    }

    private static IReadOnlyList<LiveOverlayPreviewSpec> NativeOverlaySpecs()
    {
        return
        [
            new LiveOverlayPreviewSpec(DesignV2LiveOverlayKind.Standings, StandingsOverlayDefinition.Definition),
            new LiveOverlayPreviewSpec(DesignV2LiveOverlayKind.FuelCalculator, FuelCalculatorOverlayDefinition.Definition),
            new LiveOverlayPreviewSpec(DesignV2LiveOverlayKind.Relative, RelativeOverlayDefinition.Definition),
            new LiveOverlayPreviewSpec(DesignV2LiveOverlayKind.TrackMap, TrackMapOverlayDefinition.Definition, UsesTransparentBackdrop: true),
            new LiveOverlayPreviewSpec(DesignV2LiveOverlayKind.StreamChat, StreamChatOverlayDefinition.Definition),
            new LiveOverlayPreviewSpec(DesignV2LiveOverlayKind.Flags, FlagsOverlayDefinition.Definition, UsesTransparentBackdrop: true),
            new LiveOverlayPreviewSpec(DesignV2LiveOverlayKind.SessionWeather, SessionWeatherOverlayDefinition.Definition),
            new LiveOverlayPreviewSpec(DesignV2LiveOverlayKind.PitService, PitServiceOverlayDefinition.Definition),
            new LiveOverlayPreviewSpec(DesignV2LiveOverlayKind.InputState, InputStateOverlayDefinition.Definition),
            new LiveOverlayPreviewSpec(DesignV2LiveOverlayKind.CarRadar, CarRadarOverlayDefinition.Definition, UsesTransparentBackdrop: true),
            new LiveOverlayPreviewSpec(DesignV2LiveOverlayKind.GapToLeader, GapToLeaderOverlayDefinition.Definition)
        ];
    }

    private static IReadOnlyList<OverlaySessionKind> PreviewModesForOverlay(string overlayId)
    {
        return string.Equals(overlayId, GapToLeaderOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            ? [OverlaySessionKind.Race]
            : [OverlaySessionKind.Practice, OverlaySessionKind.Qualifying, OverlaySessionKind.Race];
    }

    private static int RefreshPassesFor(DesignV2LiveOverlayKind kind)
    {
        return kind switch
        {
            DesignV2LiveOverlayKind.InputState => 28,
            DesignV2LiveOverlayKind.GapToLeader => 42,
            DesignV2LiveOverlayKind.TrackMap => 3,
            DesignV2LiveOverlayKind.CarRadar => 4,
            _ => 4
        };
    }

    private static string ModeFileStem(OverlaySessionKind mode)
    {
        return mode switch
        {
            OverlaySessionKind.Practice => "practice",
            OverlaySessionKind.Qualifying => "qualifying",
            OverlaySessionKind.Race => "race",
            _ => "test"
        };
    }

    private static string KindName(DesignV2LiveOverlayKind kind)
    {
        return kind switch
        {
            DesignV2LiveOverlayKind.Standings => "standings",
            DesignV2LiveOverlayKind.FuelCalculator => "fuel-calculator",
            DesignV2LiveOverlayKind.Relative => "relative",
            DesignV2LiveOverlayKind.TrackMap => "track-map",
            DesignV2LiveOverlayKind.StreamChat => "stream-chat",
            DesignV2LiveOverlayKind.Flags => "flags",
            DesignV2LiveOverlayKind.SessionWeather => "session-weather",
            DesignV2LiveOverlayKind.PitService => "pit-service",
            DesignV2LiveOverlayKind.InputState => "input-state",
            DesignV2LiveOverlayKind.CarRadar => "car-radar",
            DesignV2LiveOverlayKind.GapToLeader => "gap-to-leader",
            _ => kind.ToString()
        };
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
    }

    private sealed class PreviewTelemetrySource(OverlaySessionKind mode) : ILiveTelemetrySource
    {
        private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
        private long _index;

        public LiveTelemetrySnapshot Snapshot()
        {
            var index = Interlocked.Increment(ref _index);
            return SessionPreviewTelemetryFixtures.Build(
                mode,
                _startedAtUtc.AddMilliseconds(index * 250d),
                generation: index);
        }

        public LiveTelemetrySnapshot? LastActiveSnapshot()
        {
            return Snapshot();
        }
    }

    private sealed record LiveOverlayPreviewSpec(
        DesignV2LiveOverlayKind Kind,
        OverlayDefinition Definition,
        bool UsesTransparentBackdrop = false);

    private sealed record LiveOverlayPreviewCaptureRequest(
        LiveOverlayPreviewSpec Spec,
        OverlaySessionKind Mode);

    private sealed record LiveOverlayPreviewCapture(
        LiveOverlayPreviewScreenshotState State,
        LiveOverlayPreviewScreenshotImage Image);
}

internal sealed record LiveOverlayPreviewScreenshotCaptureResult(
    LiveOverlayPreviewScreenshotManifest Manifest,
    IReadOnlyList<LiveOverlayPreviewScreenshotImage> Images);

internal sealed record LiveOverlayPreviewScreenshotManifest(
    DateTimeOffset GeneratedAtUtc,
    string CaptureKind,
    bool CapturePreviewScreenshotsEnabled,
    int MaxPreviewScreenshots,
    string Description,
    string Limitation,
    LiveOverlayPreviewScreenshotCoverage Coverage,
    IReadOnlyList<string> EvidenceWarnings,
    IReadOnlyList<LiveOverlayPreviewScreenshotState> Screenshots);

internal sealed record LiveOverlayPreviewScreenshotCoverage(
    int RequestedScreenshotCount,
    int CapturedScreenshotCount,
    int FailedScreenshotCount,
    int OmittedByCapCount);

internal sealed record LiveOverlayPreviewScreenshotState(
    string OverlayId,
    string DisplayName,
    string? PreviewMode,
    bool Captured,
    string? EntryName,
    string? Error,
    int? Width,
    int? Height,
    string Renderer,
    string? NativeFormType,
    string? NativeRenderer,
    string? NativeBodyKind,
    string? Status,
    string? Evidence,
    bool? ShouldRender,
    bool? EnabledSetting,
    bool? ShowInPracticeSetting,
    bool? ShowInQualifyingSetting,
    bool? ShowInRaceSetting,
    double? Scale,
    double? Opacity,
    bool? TransparentBackground,
    bool ForcedVisibleForDiagnostics,
    bool? SettingsPreviewForced,
    bool? StreamChatExternalSourceSuppressed,
    string Fixture,
    string SourceContract,
    DesignV2LayoutDiagnostics? Layout);

internal sealed record LiveOverlayPreviewScreenshotImage(string EntryName, byte[] PngBytes);
