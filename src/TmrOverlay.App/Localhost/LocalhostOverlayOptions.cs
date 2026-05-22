using Microsoft.Extensions.Configuration;

namespace TmrOverlay.App.Localhost;

internal sealed class LocalhostOverlayOptions
{
    private const int DefaultPort = 8765;

    public bool Enabled { get; init; }

    public string Host { get; init; } = "localhost";

    public int Port { get; init; } = DefaultPort;

    public string Prefix => $"http://{Host}:{Port}/";

    public static LocalhostOverlayOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("LocalhostOverlays");
        return new LocalhostOverlayOptions
        {
            Enabled = !bool.TryParse(section["Enabled"], out var enabled) || enabled,
            Port = ParsePort(section["Port"])
        };
    }

    private static int ParsePort(string? configuredValue)
    {
        return int.TryParse(configuredValue, out var parsed) && parsed is >= 1024 and <= 65535
            ? parsed
            : DefaultPort;
    }
}
