using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Storage;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;

namespace TmrOverlay.App.Diagnostics;

internal sealed class LiveOverlayWindowCaptureStore
{
    private const int CaptureCadenceSeconds = 10;
    private const int MinimumCaptureIntervalMilliseconds = 500;
    private const int MaximumCaptureDimension = 4096;
    private readonly AppStorageOptions _storageOptions;
    private readonly LiveOverlayWindowCaptureOptions _options;
    private readonly object _sync = new();
    private readonly Dictionary<string, LiveOverlayWindowState> _states = new(StringComparer.OrdinalIgnoreCase);
    private long _nextCaptureAttemptTicks;

    public LiveOverlayWindowCaptureStore(AppStorageOptions storageOptions)
        : this(storageOptions, new LiveOverlayWindowCaptureOptions())
    {
    }

    public LiveOverlayWindowCaptureStore(
        AppStorageOptions storageOptions,
        LiveOverlayWindowCaptureOptions options)
    {
        _storageOptions = storageOptions;
        _options = options;
    }

    public string CaptureRoot => Path.Combine(_storageOptions.LogsRoot, "live-overlay-windows");

    public void RecordOverlayWindow(
        OverlayDefinition definition,
        OverlaySettings settings,
        Form? form,
        bool enabled,
        bool sessionAllowed,
        bool settingsPreview,
        bool desiredVisible,
        bool actualVisible,
        bool topMost,
        bool liveTelemetryAvailable,
        string contextRequirement,
        bool contextAvailable,
        string contextReason,
        bool settingsOverlayActive,
        bool settingsWindowVisible,
        bool settingsWindowIntersects,
        bool settingsWindowInputProtected,
        bool inputTransparent,
        bool noActivate,
        string implementation,
        string? nativeFormType,
        string? nativeRenderer,
        string? nativeBodyKind)
    {
        var capturedAtUtc = DateTimeOffset.UtcNow;
        var bounds = form?.Bounds ?? new Rectangle(settings.X, settings.Y, settings.Width, settings.Height);
        var opacity = Math.Round(form?.Opacity ?? settings.Opacity, 3);
        var effectiveSettingsOverlayActive = settingsOverlayActive && settingsWindowVisible;
        var inputInterceptRisk = actualVisible
            && !inputTransparent
            && (effectiveSettingsOverlayActive
                || settingsWindowInputProtected
                || settingsWindowIntersects
                || (settingsWindowVisible && topMost && noActivate)
                || opacity <= 0.01d);
        var browserRecommendedSize = BrowserOverlayRecommendedSize.For(definition, settings);
        var hasBrowserPage = BrowserOverlayCatalog.TryGetPageByOverlayId(definition.Id, out var browserPage);
        LiveOverlayWindowState? previous;
        lock (_sync)
        {
            _states.TryGetValue(definition.Id, out previous);
        }

        var currentScreenshotSignature = CaptureSignature(
            bounds,
            opacity,
            effectiveSettingsOverlayActive,
            implementation,
            nativeFormType,
            nativeRenderer,
            nativeBodyKind);
        var captureAllowedByTelemetryState = !definition.FadeWhenLiveTelemetryUnavailable || liveTelemetryAvailable || settingsPreview;
        var screenshot = _options.CaptureScreenshots
            ? TryCaptureOverlay(
                definition.Id,
                form,
                bounds,
                opacity,
                actualVisible,
                captureAllowedByTelemetryState,
                effectiveSettingsOverlayActive,
                capturedAtUtc,
                previous,
                currentScreenshotSignature)
            : LiveOverlayCaptureAttempt.None;

        lock (_sync)
        {
            var screenshotPath = _options.CaptureScreenshots ? screenshot.Path ?? previous?.ScreenshotPath : null;
            var screenshotSignature = _options.CaptureScreenshots ? screenshot.Signature ?? previous?.ScreenshotSignature : null;
            var screenshotRepresentsCurrentState = actualVisible
                && captureAllowedByTelemetryState
                && !string.IsNullOrWhiteSpace(screenshotPath)
                && string.Equals(screenshotSignature, currentScreenshotSignature, StringComparison.Ordinal);
            _states[definition.Id] = new LiveOverlayWindowState(
                OverlayId: definition.Id,
                DisplayName: definition.DisplayName,
                CapturedAtUtc: capturedAtUtc,
                Implementation: implementation,
                NativeFormType: nativeFormType,
                NativeRenderer: nativeRenderer,
                NativeBodyKind: nativeBodyKind,
                BrowserSourceSupported: hasBrowserPage,
                BrowserRoute: hasBrowserPage ? browserPage.CanonicalRoute : null,
                BrowserRequiresTelemetry: hasBrowserPage ? browserPage.RequiresTelemetry : (bool?)null,
                BrowserRenderWhenTelemetryUnavailable: hasBrowserPage ? browserPage.RenderWhenTelemetryUnavailable : (bool?)null,
                BrowserFadeWhenTelemetryUnavailable: hasBrowserPage ? browserPage.FadeWhenTelemetryUnavailable : (bool?)null,
                BrowserRefreshIntervalMilliseconds: hasBrowserPage ? browserPage.RefreshIntervalMilliseconds : (int?)null,
                BrowserRecommendedWidth: hasBrowserPage ? browserRecommendedSize.Width : (int?)null,
                BrowserRecommendedHeight: hasBrowserPage ? browserRecommendedSize.Height : (int?)null,
                Enabled: enabled,
                SessionAllowed: sessionAllowed,
                SettingsPreview: settingsPreview,
                DesiredVisible: desiredVisible,
                ActualVisible: actualVisible,
                LiveTelemetryAvailable: liveTelemetryAvailable,
                ContextRequirement: contextRequirement,
                ContextAvailable: contextAvailable,
                ContextReason: contextReason,
                SettingsOverlayActive: effectiveSettingsOverlayActive,
                SettingsWindowVisible: settingsWindowVisible,
                SettingsWindowIntersects: settingsWindowIntersects,
                SettingsWindowInputProtected: settingsWindowInputProtected,
                InputTransparent: inputTransparent,
                NoActivate: noActivate,
                TopMost: topMost,
                InputInterceptRisk: inputInterceptRisk,
                AlwaysOnTopSetting: settings.AlwaysOnTop,
                DefaultWidth: definition.DefaultWidth,
                DefaultHeight: definition.DefaultHeight,
                X: bounds.X,
                Y: bounds.Y,
                Width: Math.Max(0, bounds.Width),
                Height: Math.Max(0, bounds.Height),
                Scale: Math.Round(Math.Clamp(settings.Scale, 0.6d, 2d), 3),
                Opacity: opacity,
                ScreenshotPath: screenshotPath,
                ScreenshotSource: _options.CaptureScreenshots ? screenshot.Source ?? previous?.ScreenshotSource : null,
                ScreenshotCapturedAtUtc: _options.CaptureScreenshots ? screenshot.CapturedAtUtc ?? previous?.ScreenshotCapturedAtUtc : null,
                ScreenshotAgeSeconds: null,
                ScreenshotError: _options.CaptureScreenshots ? screenshot.Error ?? previous?.ScreenshotError : null,
                ScreenshotRepresentsCurrentState: screenshotRepresentsCurrentState,
                ScreenshotSignature: screenshotSignature);
        }
    }

    public LiveOverlayWindowCaptureManifest Snapshot()
    {
        lock (_sync)
        {
            var generatedAtUtc = DateTimeOffset.UtcNow;
            var overlays = _states
                .Values
                .OrderBy(state => state.OverlayId, StringComparer.OrdinalIgnoreCase)
                .Select(state => state with
                {
                    ScreenshotAgeSeconds = state.ScreenshotCapturedAtUtc is { } capturedAtUtc
                        ? Math.Round(Math.Max(0d, (generatedAtUtc - capturedAtUtc).TotalSeconds), 3)
                        : null
                })
                .ToArray();
            return new LiveOverlayWindowCaptureManifest(
                GeneratedAtUtc: generatedAtUtc,
                CaptureKind: "live-window-screen-crops",
                CaptureScreenshotsEnabled: _options.CaptureScreenshots,
                CaptureCadenceSeconds: CaptureCadenceSeconds,
                Description: "Expected live overlay state plus the latest visible Windows overlay crop captured from the real desktop when possible. A form-render-fallback image is the overlay's own rendered pixels, not a desktop capture. Browser-source fields show the matching localhost route and recommended source size for native/browser parity checks.",
                ScreenshotFreshnessNote: "screenshotRepresentsCurrentState means the latest PNG still matches the current native window bounds, opacity, native renderer/body identity, visibility, and settings-overlay capture mode. Live telemetry content can still be older than the manifest; use screenshotAgeSeconds for that.",
                ScreenshotCoverage: BuildScreenshotCoverage(overlays),
                EvidenceWarnings: BuildEvidenceWarnings(overlays),
                Overlays: overlays);
        }
    }

    public IReadOnlyList<LiveOverlayCaptureFile> CaptureFiles()
    {
        lock (_sync)
        {
            return _states
                .Values
                .Where(state => !string.IsNullOrWhiteSpace(state.ScreenshotPath))
                .Select(state => new LiveOverlayCaptureFile(
                    SourcePath: Path.Combine(CaptureRoot, state.ScreenshotPath!),
                    EntryName: $"live-overlays/{state.ScreenshotPath!.Replace('\\', '/')}"))
                .ToArray();
        }
    }

    private LiveOverlayCaptureAttempt TryCaptureOverlay(
        string overlayId,
        Form? form,
        Rectangle bounds,
        double opacity,
        bool actualVisible,
        bool captureAllowedByTelemetryState,
        bool settingsOverlayActive,
        DateTimeOffset capturedAtUtc,
        LiveOverlayWindowState? previous,
        string signature)
    {
        if (form is null
            || form.IsDisposed
            || bounds.Width <= 1
            || bounds.Height <= 1
            || bounds.Width > MaximumCaptureDimension
            || bounds.Height > MaximumCaptureDimension
            || !actualVisible
            || !captureAllowedByTelemetryState
            || opacity <= 0.01d)
        {
            return LiveOverlayCaptureAttempt.None;
        }

        if (previous is not null
            && string.Equals(previous.ScreenshotSignature, signature, StringComparison.Ordinal)
            && previous.ScreenshotCapturedAtUtc is { } previousCapture
            && capturedAtUtc - previousCapture < TimeSpan.FromSeconds(CaptureCadenceSeconds))
        {
            return LiveOverlayCaptureAttempt.None;
        }

        if (ShouldDeferToUncapturedVisibleOverlay(overlayId, previous))
        {
            return LiveOverlayCaptureAttempt.None;
        }

        if (!TryReserveCaptureSlot(capturedAtUtc))
        {
            return LiveOverlayCaptureAttempt.None;
        }

        Directory.CreateDirectory(CaptureRoot);
        var fileName = $"{SafeFileName(overlayId)}.png";
        var path = Path.Combine(CaptureRoot, fileName);
        if (!settingsOverlayActive)
        {
            try
            {
                using var bitmap = new Bitmap(bounds.Width, bounds.Height);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
                }

                bitmap.Save(path, ImageFormat.Png);
                return new LiveOverlayCaptureAttempt(fileName, "screen", capturedAtUtc, null, signature);
            }
            catch (Exception exception)
            {
                return TryCaptureRenderedFallback(form, bounds.Size, path, fileName, capturedAtUtc, signature, exception.Message);
            }
        }

        return TryCaptureRenderedFallback(form, bounds.Size, path, fileName, capturedAtUtc, signature, null);
    }

    private bool ShouldDeferToUncapturedVisibleOverlay(string overlayId, LiveOverlayWindowState? previous)
    {
        if (string.IsNullOrWhiteSpace(previous?.ScreenshotPath))
        {
            return false;
        }

        lock (_sync)
        {
            return _states.Values.Any(state =>
                !string.Equals(state.OverlayId, overlayId, StringComparison.OrdinalIgnoreCase)
                && state.ActualVisible
                && state.Width > 1
                && state.Height > 1
                && string.IsNullOrWhiteSpace(state.ScreenshotPath));
        }
    }

    private bool TryReserveCaptureSlot(DateTimeOffset capturedAtUtc)
    {
        lock (_sync)
        {
            var capturedAtTicks = capturedAtUtc.UtcDateTime.Ticks;
            if (capturedAtTicks < _nextCaptureAttemptTicks)
            {
                return false;
            }

            _nextCaptureAttemptTicks = capturedAtUtc
                .AddMilliseconds(MinimumCaptureIntervalMilliseconds)
                .UtcDateTime
                .Ticks;
            return true;
        }
    }

    private static string CaptureSignature(
        Rectangle bounds,
        double opacity,
        bool settingsOverlayActive,
        string implementation,
        string? nativeFormType,
        string? nativeRenderer,
        string? nativeBodyKind)
    {
        return string.Join(
            "|",
            bounds.X.ToString(System.Globalization.CultureInfo.InvariantCulture),
            bounds.Y.ToString(System.Globalization.CultureInfo.InvariantCulture),
            bounds.Width.ToString(System.Globalization.CultureInfo.InvariantCulture),
            bounds.Height.ToString(System.Globalization.CultureInfo.InvariantCulture),
            opacity.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
            settingsOverlayActive.ToString(),
            implementation,
            nativeFormType ?? string.Empty,
            nativeRenderer ?? string.Empty,
            nativeBodyKind ?? string.Empty);
    }

    private static LiveOverlayCaptureAttempt TryCaptureRenderedFallback(
        Form form,
        Size size,
        string path,
        string fileName,
        DateTimeOffset capturedAtUtc,
        string signature,
        string? screenCaptureError)
    {
        try
        {
            using var bitmap = new Bitmap(size.Width, size.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
            ApplyTransparencyKeyForCapture(bitmap, form.TransparencyKey);
            bitmap.Save(path, ImageFormat.Png);
            return new LiveOverlayCaptureAttempt(fileName, "form-render-fallback", capturedAtUtc, screenCaptureError, signature);
        }
        catch (Exception exception)
        {
            var error = string.IsNullOrWhiteSpace(screenCaptureError)
                ? exception.Message
                : $"{screenCaptureError}; fallback: {exception.Message}";
            return new LiveOverlayCaptureAttempt(null, null, null, error, signature);
        }
    }

    internal static void ApplyTransparencyKeyForCapture(Bitmap bitmap, Color transparencyKey)
    {
        if (transparencyKey.IsEmpty)
        {
            return;
        }

        var keyArgb = transparencyKey.ToArgb();
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).ToArgb() == keyArgb)
                {
                    bitmap.SetPixel(x, y, Color.Transparent);
                }
            }
        }
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
    }

    private LiveOverlayWindowScreenshotCoverage BuildScreenshotCoverage(IReadOnlyList<LiveOverlayWindowState> overlays)
    {
        return new LiveOverlayWindowScreenshotCoverage(
            TotalOverlayCount: overlays.Count,
            VisibleOverlayCount: overlays.Count(overlay => overlay.ActualVisible),
            ScreenshotOverlayCount: overlays.Count(overlay => !string.IsNullOrWhiteSpace(overlay.ScreenshotPath)),
            CurrentScreenshotOverlayCount: overlays.Count(overlay => overlay.ScreenshotRepresentsCurrentState));
    }

    private IReadOnlyList<string> BuildEvidenceWarnings(IReadOnlyList<LiveOverlayWindowState> overlays)
    {
        var warnings = new List<string>();
        if (!_options.CaptureScreenshots)
        {
            warnings.Add("live_overlay_screenshot_capture_disabled");
        }

        if (overlays.Any(overlay => overlay.ActualVisible && string.IsNullOrWhiteSpace(overlay.ScreenshotPath)))
        {
            warnings.Add("visible_overlays_without_pixel_evidence");
        }

        if (overlays.Any(overlay => overlay.ActualVisible && !overlay.ScreenshotRepresentsCurrentState))
        {
            warnings.Add("visible_overlays_without_current_screenshots");
        }

        if (overlays.Any(overlay => overlay.InputInterceptRisk))
        {
            warnings.Add("input_intercept_risk_present");
        }

        return warnings;
    }

    private sealed record LiveOverlayCaptureAttempt(
        string? Path,
        string? Source,
        DateTimeOffset? CapturedAtUtc,
        string? Error,
        string? Signature)
    {
        public static LiveOverlayCaptureAttempt None { get; } = new(null, null, null, null, null);
    }
}

internal sealed record LiveOverlayWindowCaptureManifest(
    DateTimeOffset GeneratedAtUtc,
    string CaptureKind,
    bool CaptureScreenshotsEnabled,
    int CaptureCadenceSeconds,
    string Description,
    string ScreenshotFreshnessNote,
    LiveOverlayWindowScreenshotCoverage ScreenshotCoverage,
    IReadOnlyList<string> EvidenceWarnings,
    IReadOnlyList<LiveOverlayWindowState> Overlays);

internal sealed record LiveOverlayWindowScreenshotCoverage(
    int TotalOverlayCount,
    int VisibleOverlayCount,
    int ScreenshotOverlayCount,
    int CurrentScreenshotOverlayCount);

internal sealed record LiveOverlayWindowState(
    string OverlayId,
    string DisplayName,
    DateTimeOffset CapturedAtUtc,
    string Implementation,
    string? NativeFormType,
    string? NativeRenderer,
    string? NativeBodyKind,
    bool BrowserSourceSupported,
    string? BrowserRoute,
    bool? BrowserRequiresTelemetry,
    bool? BrowserRenderWhenTelemetryUnavailable,
    bool? BrowserFadeWhenTelemetryUnavailable,
    int? BrowserRefreshIntervalMilliseconds,
    int? BrowserRecommendedWidth,
    int? BrowserRecommendedHeight,
    bool Enabled,
    bool SessionAllowed,
    bool SettingsPreview,
    bool DesiredVisible,
    bool ActualVisible,
    bool LiveTelemetryAvailable,
    string ContextRequirement,
    bool ContextAvailable,
    string ContextReason,
    bool SettingsOverlayActive,
    bool SettingsWindowVisible,
    bool SettingsWindowIntersects,
    bool SettingsWindowInputProtected,
    bool InputTransparent,
    bool NoActivate,
    bool TopMost,
    bool InputInterceptRisk,
    bool AlwaysOnTopSetting,
    int DefaultWidth,
    int DefaultHeight,
    int X,
    int Y,
    int Width,
    int Height,
    double Scale,
    double Opacity,
    string? ScreenshotPath,
    string? ScreenshotSource,
    DateTimeOffset? ScreenshotCapturedAtUtc,
    double? ScreenshotAgeSeconds,
    string? ScreenshotError,
    bool ScreenshotRepresentsCurrentState,
    [property: JsonIgnore]
    string? ScreenshotSignature);

internal sealed record LiveOverlayCaptureFile(string SourcePath, string EntryName);
