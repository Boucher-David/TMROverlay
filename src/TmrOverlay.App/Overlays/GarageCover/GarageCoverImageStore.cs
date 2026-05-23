using System.Drawing;

namespace TmrOverlay.App.Overlays.GarageCover;

internal static class GarageCoverImageStore
{
    private const string DefaultCoverOutputFileName = "GarageCoverDefault.png";
    private const string DefaultCoverSourceRelativePath = "assets/brand/Team_Logo_4k_TMRBRANDING.png";

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif"
    };

    public static string ImportImage(string sourcePath, string settingsRoot)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The selected garage cover image does not exist.", sourcePath);
        }

        var extension = Path.GetExtension(sourcePath);
        if (!AllowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Garage cover images must be PNG, JPG, BMP, or GIF files.");
        }

        var directory = ImageDirectory(settingsRoot);
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"cover{extension.ToLowerInvariant()}");
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
        {
            return destination;
        }

        var temporaryDestination = Path.Combine(directory, $"cover-import{extension.ToLowerInvariant()}");
        File.Copy(sourcePath, temporaryDestination, overwrite: true);
        foreach (var existing in Directory.EnumerateFiles(directory, "cover.*"))
        {
            File.Delete(existing);
        }

        File.Move(temporaryDestination, destination, overwrite: true);
        return destination;
    }

    public static bool IsSupportedImagePath(string? path)
    {
        return GetSupportedImageInfo(path) is not null;
    }

    public static void ClearImportedImages(string settingsRoot)
    {
        var directory = ImageDirectory(settingsRoot);
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var existing in Directory.EnumerateFiles(directory, "cover.*"))
        {
            File.Delete(existing);
        }
    }

    public static GarageCoverImageInfo? GetSupportedImageInfo(string? path)
    {
        var status = InspectImage(path);
        return status.IsUsable
            ? new GarageCoverImageInfo(status.LastWriteTimeUtc!.Value, status.Length!.Value)
            : null;
    }

    public static GarageCoverImageStatus InspectImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new GarageCoverImageStatus(
                HasConfiguredPath: false,
                IsUsable: false,
                Status: "not_configured",
                FileName: null,
                Extension: null,
                Length: null,
                LastWriteTimeUtc: null);
        }

        var extension = Path.GetExtension(path);
        if (!AllowedExtensions.Contains(extension))
        {
            return new GarageCoverImageStatus(
                HasConfiguredPath: true,
                IsUsable: false,
                Status: "unsupported_extension",
                FileName: Path.GetFileName(path),
                Extension: extension,
                Length: null,
                LastWriteTimeUtc: null);
        }

        if (!File.Exists(path))
        {
            return new GarageCoverImageStatus(
                HasConfiguredPath: true,
                IsUsable: false,
                Status: "file_missing",
                FileName: Path.GetFileName(path),
                Extension: extension,
                Length: null,
                LastWriteTimeUtc: null);
        }

        var info = new FileInfo(path);
        return new GarageCoverImageStatus(
            HasConfiguredPath: true,
            IsUsable: true,
            Status: "ready",
            FileName: info.Name,
            Extension: info.Extension,
            Length: info.Length,
            LastWriteTimeUtc: info.LastWriteTimeUtc);
    }

    public static Image? TryLoadPreviewImage(string? path)
    {
        if (GetSupportedImageInfo(path) is null)
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path!);
            using var image = Image.FromStream(stream);
            return new Bitmap(image);
        }
        catch
        {
            return null;
        }
    }

    public static string? ResolveDefaultImagePath()
    {
        var outputPath = Path.Combine(AppContext.BaseDirectory, "Assets", DefaultCoverOutputFileName);
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        foreach (var ancestor in Ancestors(AppContext.BaseDirectory).Concat(Ancestors(Directory.GetCurrentDirectory())).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var repositoryPath = Path.Combine(ancestor, DefaultCoverSourceRelativePath);
            if (File.Exists(repositoryPath))
            {
                return repositoryPath;
            }
        }

        return null;
    }

    public static Image? TryLoadDefaultPreviewImage()
    {
        var path = ResolveDefaultImagePath();
        if (path is null)
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var image = Image.FromStream(stream);
            return new Bitmap(image);
        }
        catch
        {
            return null;
        }
    }

    private static string ImageDirectory(string settingsRoot)
    {
        return Path.Combine(settingsRoot, "garage-cover");
    }

    private static IEnumerable<string> Ancestors(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            yield return directory.FullName;
            directory = directory.Parent;
        }
    }
}

internal sealed record GarageCoverImageInfo(DateTimeOffset LastWriteTimeUtc, long Length);

internal sealed record GarageCoverImageStatus(
    bool HasConfiguredPath,
    bool IsUsable,
    string Status,
    string? FileName,
    string? Extension,
    long? Length,
    DateTimeOffset? LastWriteTimeUtc);
