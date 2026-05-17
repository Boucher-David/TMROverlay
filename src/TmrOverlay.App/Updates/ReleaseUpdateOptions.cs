using Microsoft.Extensions.Configuration;

namespace TmrOverlay.App.Updates;

internal sealed record ReleaseUpdateOptions
{
    public bool Enabled { get; init; } = true;
    public bool CheckOnStartup { get; init; } = true;
    public string RepositoryUrl { get; init; } = "https://github.com/Boucher-David/TMROverlay";
    public bool IncludePrerelease { get; init; }
    public int StartupDelaySeconds { get; init; }

    public static ReleaseUpdateOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("Updates");
        var options = section.Get<ReleaseUpdateOptions>() ?? new ReleaseUpdateOptions();
        return options with
        {
            RepositoryUrl = string.IsNullOrWhiteSpace(options.RepositoryUrl)
                ? "https://github.com/Boucher-David/TMROverlay"
                : options.RepositoryUrl.Trim(),
            CheckOnStartup = true,
            StartupDelaySeconds = Math.Clamp(options.StartupDelaySeconds, 0, 300)
        };
    }
}
