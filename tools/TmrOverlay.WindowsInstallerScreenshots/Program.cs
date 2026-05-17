using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace TmrOverlay.WindowsInstallerScreenshots;

internal static class Program
{
    private const string ProductTitle = "Tech Mates Racing Overlay";
    private const string Surface = "windows-installer-menu";
    private const string Renderer = "msiexec-ui";
    private const string SourceContract = "windows-installer-msi/v1";
    private const int MaxPreInstallPages = 6;
    private const int ContactSheetColumns = 2;
    private const int ContactCellWidth = 680;
    private const int ContactCellHeight = 470;
    private const int ContactPadding = 24;
    private const int ContactHeaderHeight = 48;
    private const int BmClick = 0x00F5;
    private const int SwRestore = 9;
    private const int WmClose = 0x0010;
    private const uint PrintWindowRenderFullContent = 0x00000002;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly string[] InstallerSourcePaths =
    [
        "Directory.Build.props",
        "src/TmrOverlay.App/TmrOverlay.App.csproj",
        "src/TmrOverlay.App/Assets/TmrOverlay.ico",
        "assets/brand/TMRInstallerSplash.png",
        "assets/brand/TMRMsiBanner.bmp",
        "assets/brand/TMRMsiLogo.bmp",
        "assets/brand/TMRLogo.png"
    ];

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
            {
                Console.Error.WriteLine("Usage: TmrOverlay.WindowsInstallerScreenshots <installer.msi> [output-root]");
                return 2;
            }

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var msiPath = Path.GetFullPath(args[0]);
            if (!File.Exists(msiPath))
            {
                throw new FileNotFoundException("Installer MSI was not found.", msiPath);
            }

            var outputRoot = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1])
                ? Path.GetFullPath(args[1])
                : Path.GetFullPath(Path.Combine("artifacts", "windows-installer-screenshots"));
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }

            Directory.CreateDirectory(Path.Combine(outputRoot, "installer-menus"));

            var evidenceContext = CreateEvidenceContext(msiPath);
            var screenshots = CaptureInstallerMenus(msiPath, outputRoot, evidenceContext);
            if (screenshots.Count == 0)
            {
                throw new InvalidOperationException("The installer window did not produce any screenshots.");
            }

            RenderContactSheet(outputRoot, screenshots);
            WriteManifest(outputRoot, screenshots, evidenceContext);
            Console.WriteLine($"Wrote {screenshots.Count} Windows installer screenshots to {outputRoot}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static IReadOnlyList<RenderedInstallerScreenshot> CaptureInstallerMenus(
        string msiPath,
        string outputRoot,
        EvidenceContext evidenceContext)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "msiexec.exe",
            Arguments = $"/i \"{msiPath}\"",
            UseShellExecute = false,
            CreateNoWindow = false
        }) ?? throw new InvalidOperationException("Failed to start msiexec.exe.");

        var screenshots = new List<RenderedInstallerScreenshot>();
        var seenPageHashes = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            for (var pageIndex = 1; pageIndex <= MaxPreInstallPages; pageIndex++)
            {
                var window = WaitForInstallerWindow(TimeSpan.FromSeconds(45), IsMainInstallerWizardWindow);
                Thread.Sleep(400);
                window = WaitForInstallerWindow(TimeSpan.FromSeconds(8), IsMainInstallerWizardWindow);

                var pageHash = PageFingerprint(window);
                if (!seenPageHashes.Add(pageHash) && screenshots.Count > 0)
                {
                    break;
                }

                var menuId = pageIndex == 1 ? "welcome" : MenuIdFor(window, pageIndex);
                var status = StatusFor(window);
                screenshots.Add(SaveInstallerScreenshot(
                    outputRoot,
                    menuId,
                    pageIndex,
                    status,
                    window,
                    evidenceContext));

                if (FindActionButton(window, "install") is not null || FindActionButton(window, "finish") is not null)
                {
                    break;
                }

                var nextButton = FindActionButton(window, "next");
                if (nextButton is null)
                {
                    break;
                }

                ClickButton(nextButton.Handle);
                Thread.Sleep(900);
            }

            CaptureCancelConfirmation(outputRoot, screenshots, evidenceContext);
        }
        finally
        {
            CloseInstallerWindows();
            if (!process.HasExited)
            {
                if (!process.WaitForExit(2500))
                {
                    process.Kill(entireProcessTree: true);
                }
            }
        }

        return screenshots;
    }

    private static void CaptureCancelConfirmation(
        string outputRoot,
        ICollection<RenderedInstallerScreenshot> screenshots,
        EvidenceContext evidenceContext)
    {
        WindowSnapshot mainWindow;
        try
        {
            mainWindow = WaitForInstallerWindow(TimeSpan.FromSeconds(8), IsMainInstallerWizardWindow);
        }
        catch (TimeoutException)
        {
            return;
        }

        var cancelButton = FindActionButton(mainWindow, "cancel");
        if (cancelButton is null)
        {
            PostMessage(mainWindow.Handle, WmClose, IntPtr.Zero, IntPtr.Zero);
            return;
        }

        ClickButton(cancelButton.Handle);
        Thread.Sleep(500);

        try
        {
            var confirmation = WaitForInstallerWindow(
                TimeSpan.FromSeconds(8),
                window => IsCancelConfirmation(window) || window.Handle != mainWindow.Handle && HasYesNoButtons(window));
            Thread.Sleep(250);
            confirmation = WaitForInstallerWindow(
                TimeSpan.FromSeconds(4),
                window => IsCancelConfirmation(window) || window.Handle != mainWindow.Handle && HasYesNoButtons(window));
            screenshots.Add(SaveInstallerScreenshot(
                outputRoot,
                "cancel-confirm",
                screenshots.Count + 1,
                "cancel_confirmation",
                confirmation,
                evidenceContext));

            var yesButton = FindActionButton(confirmation, "yes") ?? FindActionButton(confirmation, "ok");
            if (yesButton is not null)
            {
                ClickButton(yesButton.Handle);
            }
            else
            {
                PostMessage(confirmation.Handle, WmClose, IntPtr.Zero, IntPtr.Zero);
            }
        }
        catch (TimeoutException)
        {
            PostMessage(mainWindow.Handle, WmClose, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private static RenderedInstallerScreenshot SaveInstallerScreenshot(
        string outputRoot,
        string menuId,
        int pageIndex,
        string status,
        WindowSnapshot window,
        EvidenceContext evidenceContext)
    {
        using var bitmap = CaptureWindowBitmap(window);
        var path = UniqueScreenshotPath(outputRoot, menuId, pageIndex);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bitmap.Save(path, ImageFormat.Png);

        var relativePath = Path.GetRelativePath(outputRoot, path).Replace('\\', '/');
        var sampledElements = ElementEvidence(window, bitmap);
        var palette = SamplePalette(bitmap);
        var contentBounds = ContentBoundsEvidence(window);
        var layoutEvidence = LayoutEvidence(window, sampledElements);
        var uiEvidence = UiEvidence(window, menuId, pageIndex, sampledElements, palette, evidenceContext);
        var textSample = NormalizeTextSample([window.Title, .. window.Elements.Select(element => element.Text)]);
        var scenarioEvidence = ScenarioEvidence(
            menuId,
            pageIndex,
            status,
            window,
            textSample,
            layoutEvidence,
            evidenceContext);

        return new RenderedInstallerScreenshot(
            LabelFor(menuId, pageIndex),
            path,
            relativePath,
            bitmap.Width,
            bitmap.Height,
            menuId,
            pageIndex,
            status,
            window.Title,
            window.ClassName,
            textSample,
            contentBounds,
            layoutEvidence,
            uiEvidence,
            scenarioEvidence,
            new FileInfo(path).Length);
    }

    private static WindowSnapshot WaitForInstallerWindow(
        TimeSpan timeout,
        Func<WindowSnapshot, bool>? predicate = null)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var candidates = EnumerateInstallerWindows()
                .Where(window => predicate?.Invoke(window) ?? true)
                .OrderByDescending(ScoreInstallerWindow)
                .ToList();
            if (candidates.Count > 0)
            {
                return candidates[0];
            }

            Application.DoEvents();
            Thread.Sleep(250);
        }

        throw new TimeoutException("Timed out waiting for a visible installer window.");
    }

    private static IReadOnlyList<WindowSnapshot> EnumerateInstallerWindows()
    {
        var snapshots = new List<WindowSnapshot>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || !GetWindowRect(handle, out var rect))
            {
                return true;
            }

            var bounds = rect.ToRectangle();
            if (bounds.Width < 280 || bounds.Height < 140)
            {
                return true;
            }

            var snapshot = TryCaptureWindowSnapshot(handle);
            if (snapshot is null)
            {
                return true;
            }

            if (IsInstallerWindowCandidate(snapshot) && ScoreInstallerWindow(snapshot) >= 40)
            {
                snapshots.Add(snapshot);
            }

            return true;
        }, IntPtr.Zero);

        return snapshots;
    }

    private static bool IsInstallerWindowCandidate(WindowSnapshot window)
    {
        var combined = $"{window.Title}\n{window.TextSample}".ToLowerInvariant();
        var hasInstallerClass = window.ClassName.Contains("Msi", StringComparison.OrdinalIgnoreCase);
        var hasSetupTitle = window.Title.Contains("setup", StringComparison.OrdinalIgnoreCase);
        var hasWizardAction =
            FindActionButton(window, "next") is not null ||
            FindActionButton(window, "install") is not null ||
            FindActionButton(window, "finish") is not null ||
            FindActionButton(window, "cancel") is not null;
        var hasInstallerLanguage =
            combined.Contains("installer", StringComparison.Ordinal) ||
            combined.Contains("installation", StringComparison.Ordinal) ||
            combined.Contains("setup", StringComparison.Ordinal) ||
            combined.Contains("install location", StringComparison.Ordinal) ||
            combined.Contains("destination", StringComparison.Ordinal);

        return IsCancelConfirmation(window) ||
            hasInstallerClass ||
            hasSetupTitle ||
            hasWizardAction && hasInstallerLanguage ||
            hasInstallerLanguage && window.Elements.Count > 0;
    }

    private static bool IsMainInstallerWizardWindow(WindowSnapshot window)
    {
        if (IsCancelConfirmation(window) || window.Bounds.Width < 460 || window.Bounds.Height < 330)
        {
            return false;
        }

        return FindActionButton(window, "next") is not null ||
            FindActionButton(window, "install") is not null ||
            FindActionButton(window, "finish") is not null ||
            (FindActionButton(window, "cancel") is not null &&
                window.Title.Contains("setup", StringComparison.OrdinalIgnoreCase));
    }

    private static int ScoreInstallerWindow(WindowSnapshot window)
    {
        var combined = $"{window.Title}\n{window.TextSample}".ToLowerInvariant();
        var score = 0;
        if (combined.Contains(ProductTitle.ToLowerInvariant(), StringComparison.Ordinal))
        {
            score += 30;
        }

        if (combined.Contains("tmroverlay", StringComparison.Ordinal))
        {
            score += 30;
        }

        if (window.Title.Contains("setup", StringComparison.OrdinalIgnoreCase))
        {
            score += 80;
        }

        if (combined.Contains("installer", StringComparison.Ordinal))
        {
            score += 30;
        }

        if (combined.Contains("installation", StringComparison.Ordinal))
        {
            score += 30;
        }

        if (combined.Contains("cancel", StringComparison.Ordinal))
        {
            score += 30;
        }

        if (window.ClassName.Contains("Msi", StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        if (window.Elements.Any(element => element.Role == "button" && !string.IsNullOrWhiteSpace(element.Text)))
        {
            score += 20;
        }

        if (FindActionButton(window, "next") is not null ||
            FindActionButton(window, "install") is not null ||
            FindActionButton(window, "cancel") is not null)
        {
            score += 20;
        }

        if (HasYesNoButtons(window))
        {
            score += 40;
        }

        return score;
    }

    private static WindowSnapshot CaptureWindowSnapshot(IntPtr handle)
    {
        return TryCaptureWindowSnapshot(handle)
            ?? throw new InvalidOperationException("Could not read installer window bounds.");
    }

    private static WindowSnapshot? TryCaptureWindowSnapshot(IntPtr handle)
    {
        if (!GetWindowRect(handle, out var rect))
        {
            return null;
        }

        var bounds = rect.ToRectangle();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        var title = WindowText(handle);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = ProductTitle;
        }
        var className = WindowClass(handle);
        var clientBounds = ClientBoundsRelativeToWindow(handle, bounds);
        var elements = EnumerateElements(handle, bounds);
        var textSample = NormalizeTextSample([title, .. elements.Select(element => element.Text)]);
        return new WindowSnapshot(handle, title, className, bounds, clientBounds, elements, textSample);
    }

    private static IReadOnlyList<WindowElement> EnumerateElements(IntPtr root, Rectangle rootBounds)
    {
        var elements = new List<WindowElement>();
        EnumChildWindows(root, (handle, _) =>
        {
            if (!IsWindowVisible(handle) || !GetWindowRect(handle, out var rect))
            {
                return true;
            }

            var absoluteBounds = rect.ToRectangle();
            if (absoluteBounds.Width <= 0 || absoluteBounds.Height <= 0 || !rootBounds.IntersectsWith(absoluteBounds))
            {
                return true;
            }

            var bounds = new Rectangle(
                absoluteBounds.Left - rootBounds.Left,
                absoluteBounds.Top - rootBounds.Top,
                absoluteBounds.Width,
                absoluteBounds.Height);
            var className = WindowClass(handle);
            var text = WindowText(handle);
            elements.Add(new WindowElement(
                handle,
                GetDlgCtrlID(handle),
                className,
                text,
                RoleFor(className, text),
                bounds,
                absoluteBounds,
                IsWindowEnabled(handle),
                true));
            return true;
        }, IntPtr.Zero);

        return elements
            .OrderBy(element => element.Bounds.Top)
            .ThenBy(element => element.Bounds.Left)
            .ThenBy(element => element.ControlId)
            .ToList();
    }

    private static Bitmap CaptureWindowBitmap(WindowSnapshot window)
    {
        ShowWindow(window.Handle, SwRestore);
        SetForegroundWindow(window.Handle);
        Thread.Sleep(150);

        var bitmap = new Bitmap(Math.Max(1, window.Bounds.Width), Math.Max(1, window.Bounds.Height), PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            var hdc = graphics.GetHdc();
            var printed = false;
            try
            {
                printed = PrintWindow(window.Handle, hdc, PrintWindowRenderFullContent) ||
                    PrintWindow(window.Handle, hdc, 0);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }

            if (!printed || IsLowInformation(bitmap))
            {
                graphics.CopyFromScreen(window.Bounds.Left, window.Bounds.Top, 0, 0, bitmap.Size);
            }
        }

        return bitmap;
    }

    private static object ContentBoundsEvidence(WindowSnapshot window)
    {
        return RectEvidence(window.ClientBounds.Width > 0 && window.ClientBounds.Height > 0
            ? window.ClientBounds
            : new Rectangle(Point.Empty, window.Bounds.Size));
    }

    private static object LayoutEvidence(
        WindowSnapshot window,
        IReadOnlyList<object> elements)
    {
        return new
        {
            contract = "windows-installer-layout-evidence/v1",
            root = RectEvidence(new Rectangle(Point.Empty, window.Bounds.Size)),
            screenBounds = RectEvidence(window.Bounds),
            client = RectEvidence(window.ClientBounds),
            title = window.Title,
            className = window.ClassName,
            elementCount = elements.Count,
            elements
        };
    }

    private static object UiEvidence(
        WindowSnapshot window,
        string menuId,
        int pageIndex,
        IReadOnlyList<object> elements,
        IReadOnlyList<object> palette,
        EvidenceContext evidenceContext)
    {
        var controls = elements.ToArray();
        var buttons = window.Elements
            .Where(element => element.Role == "button" && !string.IsNullOrWhiteSpace(element.Text))
            .Select((element, index) => ControlEvidence(element, index, null))
            .ToArray();
        var textBlocks = window.Elements
            .Where(element => !string.IsNullOrWhiteSpace(element.Text) && element.Role != "button")
            .Select((element, index) => ControlEvidence(element, index, null))
            .ToArray();

        return new
        {
            contract = "windows-installer-ui-evidence/v1",
            root = RectEvidence(new Rectangle(Point.Empty, window.Bounds.Size)),
            contentBounds = ContentBoundsEvidence(window),
            menuId,
            pageIndex,
            windowTitle = window.Title,
            windowClass = window.ClassName,
            textSample = window.TextSample,
            controlCount = controls.Length,
            buttonCount = buttons.Length,
            textBlockCount = textBlocks.Length,
            controls,
            buttons,
            textBlocks,
            primaryAction = PrimaryAction(window),
            palette,
            sourceAssets = evidenceContext.SourceFiles
        };
    }

    private static IReadOnlyList<object> ElementEvidence(WindowSnapshot window, Bitmap bitmap)
    {
        return window.Elements
            .Select((element, index) => ControlEvidence(element, index, SampleColor(bitmap, element.Bounds)))
            .ToArray();
    }

    private static object ControlEvidence(WindowElement element, int index, string? sampledColor)
    {
        return new
        {
            index,
            controlId = element.ControlId,
            role = element.Role,
            className = element.ClassName,
            text = string.IsNullOrWhiteSpace(element.Text) ? null : element.Text,
            bounds = RectEvidence(element.Bounds),
            absoluteBounds = RectEvidence(element.AbsoluteBounds),
            enabled = element.Enabled,
            visible = element.Visible,
            sampledColor
        };
    }

    private static object ScenarioEvidence(
        string menuId,
        int pageIndex,
        string status,
        WindowSnapshot window,
        string textSample,
        object layoutEvidence,
        EvidenceContext evidenceContext)
    {
        var layoutHash = Sha256(JsonSerializer.Serialize(layoutEvidence));
        var payload = new
        {
            contract = "windows-installer-scenario-evidence/v1",
            surface = Surface,
            renderer = Renderer,
            sourceContract = SourceContract,
            menuId,
            pageIndex,
            status,
            title = window.Title,
            className = window.ClassName,
            textHash = Sha256(textSample),
            layoutHash,
            package = evidenceContext.Package,
            sourceFiles = evidenceContext.SourceFiles
        };

        return new
        {
            payload.contract,
            payload.surface,
            payload.renderer,
            payload.sourceContract,
            payload.menuId,
            payload.pageIndex,
            payload.status,
            payload.title,
            payload.className,
            payload.textHash,
            payload.layoutHash,
            package = evidenceContext.Package,
            sourceFiles = evidenceContext.SourceFiles,
            sourceHash = Sha256(JsonSerializer.Serialize(evidenceContext.SourceFiles)),
            packageHash = evidenceContext.Package.Sha256,
            scenarioHash = Sha256(JsonSerializer.Serialize(payload))
        };
    }

    private static EvidenceContext CreateEvidenceContext(string msiPath)
    {
        var sourceFiles = InstallerSourcePaths.Select(SourceFileEvidence).ToArray();
        return new EvidenceContext(PackageEvidence(msiPath), sourceFiles);
    }

    private static SourceFileEvidenceRecord SourceFileEvidence(string relativePath)
    {
        var absolutePath = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absolutePath))
        {
            return new SourceFileEvidenceRecord(relativePath, false, null, null, null, null);
        }

        int? imageWidth = null;
        int? imageHeight = null;
        try
        {
            using var image = Image.FromFile(absolutePath);
            imageWidth = image.Width;
            imageHeight = image.Height;
        }
        catch (OutOfMemoryException)
        {
            // Non-image source files still contribute bytes and hashes.
        }
        catch (ArgumentException)
        {
            // Non-image source files still contribute bytes and hashes.
        }

        var data = File.ReadAllBytes(absolutePath);
        return new SourceFileEvidenceRecord(
            relativePath,
            true,
            data.Length,
            Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
            imageWidth,
            imageHeight);
    }

    private static PackageEvidenceRecord PackageEvidence(string msiPath)
    {
        var data = File.ReadAllBytes(msiPath);
        return new PackageEvidenceRecord(
            Path.GetFileName(msiPath),
            msiPath,
            data.Length,
            Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant());
    }

    private static void WriteManifest(
        string outputRoot,
        IReadOnlyList<RenderedInstallerScreenshot> screenshots,
        EvidenceContext evidenceContext)
    {
        var manifest = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            version = CurrentInformationalVersion(),
            packageEvidence = evidenceContext.Package,
            sourceFiles = evidenceContext.SourceFiles,
            screenshots = screenshots.Select(screenshot => new
            {
                label = screenshot.Label,
                path = screenshot.RelativePath,
                width = screenshot.Width,
                height = screenshot.Height,
                bytes = screenshot.Bytes,
                surface = Surface,
                renderer = Renderer,
                sourceContract = SourceContract,
                menuId = screenshot.MenuId,
                pageIndex = screenshot.PageIndex,
                status = screenshot.Status,
                title = screenshot.Title,
                windowClass = screenshot.WindowClass,
                textSample = screenshot.TextSample,
                contentBounds = screenshot.ContentBounds,
                layout = screenshot.LayoutEvidence,
                uiEvidence = screenshot.UiEvidence,
                scenarioEvidence = screenshot.ScenarioEvidence,
                packageEvidence = evidenceContext.Package,
                metadata = new
                {
                    surface = Surface,
                    renderer = Renderer,
                    sourceContract = SourceContract,
                    menuId = screenshot.MenuId,
                    pageIndex = screenshot.PageIndex,
                    status = screenshot.Status,
                    title = screenshot.Title,
                    windowClass = screenshot.WindowClass,
                    layout = screenshot.LayoutEvidence,
                    uiEvidence = screenshot.UiEvidence,
                    scenarioEvidence = screenshot.ScenarioEvidence
                }
            })
        };

        File.WriteAllText(
            Path.Combine(outputRoot, "manifest.json"),
            $"{JsonSerializer.Serialize(manifest, JsonOptions)}{Environment.NewLine}");
    }

    private static void RenderContactSheet(string outputRoot, IReadOnlyList<RenderedInstallerScreenshot> screenshots)
    {
        var rows = Math.Max(1, (int)Math.Ceiling(screenshots.Count / (double)ContactSheetColumns));
        var width = ContactPadding * 2 + ContactSheetColumns * ContactCellWidth;
        var height = ContactHeaderHeight + ContactPadding * 2 + rows * ContactCellHeight;
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(246, 248, 250));

        var fontFamily = SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif;
        using var titleFont = new Font(fontFamily, 16, FontStyle.Bold);
        using var labelFont = new Font(fontFamily, 10, FontStyle.Bold);
        using var titleBrush = new SolidBrush(Color.FromArgb(16, 24, 32));
        using var labelBrush = new SolidBrush(Color.FromArgb(16, 24, 32));
        using var borderPen = new Pen(Color.FromArgb(189, 204, 212));
        graphics.DrawString(
            $"Tech Mates Racing Overlay installer menus - {CurrentInformationalVersion()}",
            titleFont,
            titleBrush,
            ContactPadding,
            15);

        for (var index = 0; index < screenshots.Count; index++)
        {
            var column = index % ContactSheetColumns;
            var row = index / ContactSheetColumns;
            var cell = new Rectangle(
                ContactPadding + column * ContactCellWidth,
                ContactHeaderHeight + ContactPadding + row * ContactCellHeight,
                ContactCellWidth - 14,
                ContactCellHeight - 14);
            graphics.FillRectangle(Brushes.White, cell);
            graphics.DrawRectangle(borderPen, cell);
            graphics.DrawString(screenshots[index].Label, labelFont, labelBrush, cell.Left + 12, cell.Top + 10);

            using var image = Image.FromFile(screenshots[index].Path);
            var imageArea = new Rectangle(cell.Left + 12, cell.Top + 38, cell.Width - 24, cell.Height - 50);
            var scale = Math.Min(imageArea.Width / (double)image.Width, imageArea.Height / (double)image.Height);
            var drawWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
            var drawHeight = Math.Max(1, (int)Math.Round(image.Height * scale));
            var drawBounds = new Rectangle(
                imageArea.Left + (imageArea.Width - drawWidth) / 2,
                imageArea.Top + (imageArea.Height - drawHeight) / 2,
                drawWidth,
                drawHeight);
            graphics.DrawImage(image, drawBounds);
        }

        bitmap.Save(Path.Combine(outputRoot, "contact-sheet.png"), ImageFormat.Png);
    }

    private static string UniqueScreenshotPath(string outputRoot, string menuId, int pageIndex)
    {
        var stem = SanitizeFileStem(menuId);
        var directory = Path.Combine(outputRoot, "installer-menus");
        var path = Path.Combine(directory, $"{stem}.png");
        if (!File.Exists(path))
        {
            return path;
        }

        return Path.Combine(directory, $"{stem}-{pageIndex:00}.png");
    }

    private static string MenuIdFor(WindowSnapshot window, int pageIndex)
    {
        var text = window.TextSample.ToLowerInvariant();
        if (pageIndex == 2)
        {
            return "installer-page-02";
        }

        if (text.Contains("welcome", StringComparison.Ordinal))
        {
            return "welcome";
        }

        if (FindActionButton(window, "install") is not null)
        {
            return "ready-to-install";
        }

        if (text.Contains("location", StringComparison.Ordinal) ||
            text.Contains("folder", StringComparison.Ordinal) ||
            text.Contains("destination", StringComparison.Ordinal) ||
            text.Contains("browse", StringComparison.Ordinal) ||
            text.Contains("install for", StringComparison.Ordinal) ||
            text.Contains("everyone", StringComparison.Ordinal))
        {
            return "install-options";
        }

        return $"installer-page-{pageIndex:00}";
    }

    private static string StatusFor(WindowSnapshot window)
    {
        if (IsCancelConfirmation(window))
        {
            return "cancel_confirmation";
        }

        if (FindActionButton(window, "install") is not null)
        {
            return "ready_to_install_not_clicked";
        }

        if (FindActionButton(window, "next") is not null)
        {
            return "pre_install_navigation";
        }

        return "captured";
    }

    private static string LabelFor(string menuId, int pageIndex)
    {
        return menuId switch
        {
            "welcome" => "Installer welcome",
            "install-options" => "Installer options",
            "ready-to-install" => "Installer ready",
            "cancel-confirm" => "Installer cancel confirmation",
            _ => $"Installer page {pageIndex}"
        };
    }

    private static bool IsCancelConfirmation(WindowSnapshot window)
    {
        var text = window.TextSample.ToLowerInvariant();
        return (text.Contains("cancel", StringComparison.Ordinal) ||
                text.Contains("are you sure", StringComparison.Ordinal)) &&
            HasYesNoButtons(window);
    }

    private static bool HasYesNoButtons(WindowSnapshot window)
    {
        return FindActionButton(window, "yes") is not null && FindActionButton(window, "no") is not null;
    }

    private static WindowElement? FindActionButton(WindowSnapshot window, string action)
    {
        return window.Elements
            .Where(element => element.Role == "button")
            .FirstOrDefault(element =>
            {
                var text = NormalizeButtonText(element.Text);
                return text == action;
            });
    }

    private static string? PrimaryAction(WindowSnapshot window)
    {
        foreach (var action in new[] { "install", "finish", "next", "yes", "ok", "cancel" })
        {
            var button = FindActionButton(window, action);
            if (button is not null)
            {
                return button.Text;
            }
        }

        return null;
    }

    private static void ClickButton(IntPtr handle)
    {
        ShowWindow(handle, SwRestore);
        SetForegroundWindow(handle);
        SendMessage(handle, BmClick, IntPtr.Zero, IntPtr.Zero);
    }

    private static void CloseInstallerWindows()
    {
        foreach (var window in EnumerateInstallerWindows())
        {
            PostMessage(window.Handle, WmClose, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private static Rectangle ClientBoundsRelativeToWindow(IntPtr handle, Rectangle windowBounds)
    {
        if (!GetClientRect(handle, out var client))
        {
            return new Rectangle(Point.Empty, windowBounds.Size);
        }

        var point = new POINT { X = 0, Y = 0 };
        if (!ClientToScreen(handle, ref point))
        {
            return new Rectangle(Point.Empty, client.ToRectangle().Size);
        }

        return new Rectangle(
            point.X - windowBounds.Left,
            point.Y - windowBounds.Top,
            client.Right - client.Left,
            client.Bottom - client.Top);
    }

    private static string WindowText(IntPtr handle)
    {
        var length = Math.Max(256, GetWindowTextLength(handle) + 1);
        var builder = new StringBuilder(length);
        _ = GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString().Trim();
    }

    private static string WindowClass(IntPtr handle)
    {
        var builder = new StringBuilder(256);
        _ = GetClassName(handle, builder, builder.Capacity);
        return builder.ToString().Trim();
    }

    private static string RoleFor(string className, string text)
    {
        if (className.Contains("Button", StringComparison.OrdinalIgnoreCase))
        {
            return "button";
        }

        if (className.Contains("Edit", StringComparison.OrdinalIgnoreCase))
        {
            return "input";
        }

        if (className.Contains("Combo", StringComparison.OrdinalIgnoreCase))
        {
            return "choice";
        }

        if (className.Contains("Static", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(text))
        {
            return "text";
        }

        return "control";
    }

    private static string NormalizeButtonText(string value)
    {
        return value
            .Replace("&", string.Empty, StringComparison.Ordinal)
            .Replace(">", " ", StringComparison.Ordinal)
            .Replace("<", " ", StringComparison.Ordinal)
            .Replace("...", " ", StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
    }

    private static string NormalizeTextSample(IEnumerable<string?> values)
    {
        var parts = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(24)
            .ToArray();
        return string.Join(" | ", parts);
    }

    private static string SanitizeFileStem(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-');
        }

        var stem = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(stem) ? "installer-page" : stem;
    }

    private static string PageFingerprint(WindowSnapshot window)
    {
        return Sha256($"{window.Title}\n{window.TextSample}\n{string.Join("\n", window.Elements.Select(element => $"{element.ClassName}:{element.Text}:{element.Bounds}"))}");
    }

    private static bool IsLowInformation(Bitmap bitmap)
    {
        var colors = new HashSet<int>();
        var min = 255;
        var max = 0;
        var stepX = Math.Max(1, bitmap.Width / 18);
        var stepY = Math.Max(1, bitmap.Height / 18);
        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                var color = bitmap.GetPixel(x, y);
                colors.Add(color.ToArgb());
                min = Math.Min(min, Math.Min(color.R, Math.Min(color.G, color.B)));
                max = Math.Max(max, Math.Max(color.R, Math.Max(color.G, color.B)));
            }
        }

        return colors.Count < 8 || max - min < 24;
    }

    private static IReadOnlyList<object> SamplePalette(Bitmap bitmap)
    {
        var counts = new Dictionary<int, int>();
        var stepX = Math.Max(1, bitmap.Width / 48);
        var stepY = Math.Max(1, bitmap.Height / 48);
        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                var color = bitmap.GetPixel(x, y);
                var key = Color.FromArgb(color.R, color.G, color.B).ToArgb();
                counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
            }
        }

        return counts
            .OrderByDescending(pair => pair.Value)
            .Take(16)
            .Select(pair => new
            {
                color = CssColor(Color.FromArgb(pair.Key)),
                samples = pair.Value
            })
            .ToArray();
    }

    private static string? SampleColor(Bitmap bitmap, Rectangle bounds)
    {
        var clipped = Rectangle.Intersect(new Rectangle(Point.Empty, bitmap.Size), bounds);
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return null;
        }

        var x = Math.Clamp(clipped.Left + clipped.Width / 2, 0, bitmap.Width - 1);
        var y = Math.Clamp(clipped.Top + clipped.Height / 2, 0, bitmap.Height - 1);
        return CssColor(bitmap.GetPixel(x, y));
    }

    private static object RectEvidence(Rectangle rectangle)
    {
        return new
        {
            x = rectangle.X,
            y = rectangle.Y,
            width = rectangle.Width,
            height = rectangle.Height,
            right = rectangle.Right,
            bottom = rectangle.Bottom,
            aspectRatio = rectangle.Height == 0 ? (double?)null : Math.Round(rectangle.Width / (double)rectangle.Height, 4)
        };
    }

    private static string CurrentInformationalVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(Program).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
    }

    private static string CssColor(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}".ToLowerInvariant();
    }

    private static string RepoRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "tmrOverlay.sln")))
            {
                return directory.FullName;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private static string Sha256(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parentHandle, EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out RECT rectangle);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr handle, out RECT rectangle);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr handle, ref POINT point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr handle, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr handle, IntPtr hdc, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly Rectangle ToRectangle()
        {
            return Rectangle.FromLTRB(Left, Top, Right, Bottom);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private sealed record EvidenceContext(
        PackageEvidenceRecord Package,
        IReadOnlyList<SourceFileEvidenceRecord> SourceFiles);

    private sealed record PackageEvidenceRecord(
        string FileName,
        string FullPath,
        long Bytes,
        string Sha256);

    private sealed record SourceFileEvidenceRecord(
        string Path,
        bool Exists,
        long? Bytes,
        string? Sha256,
        int? ImageWidth,
        int? ImageHeight);

    private sealed record RenderedInstallerScreenshot(
        string Label,
        string Path,
        string RelativePath,
        int Width,
        int Height,
        string MenuId,
        int PageIndex,
        string Status,
        string Title,
        string WindowClass,
        string TextSample,
        object ContentBounds,
        object LayoutEvidence,
        object UiEvidence,
        object ScenarioEvidence,
        long Bytes);

    private sealed record WindowSnapshot(
        IntPtr Handle,
        string Title,
        string ClassName,
        Rectangle Bounds,
        Rectangle ClientBounds,
        IReadOnlyList<WindowElement> Elements,
        string TextSample);

    private sealed record WindowElement(
        IntPtr Handle,
        int ControlId,
        string ClassName,
        string Text,
        string Role,
        Rectangle Bounds,
        Rectangle AbsoluteBounds,
        bool Enabled,
        bool Visible);
}
