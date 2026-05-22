using TmrOverlay.Core.Settings;

namespace TmrOverlay.App.Overlays.StreamChat;

internal sealed record StreamChatOverlayViewModel(
    string Title,
    string Status,
    string Source,
    IReadOnlyList<StreamChatMessage> Rows,
    bool HasLiveRows,
    bool HasErrorRows)
{
    public const int MaximumMessages = 64;
    public const int VisibleMessageBudget = 36;

    public static StreamChatOverlayViewModel From(
        string status,
        IReadOnlyList<StreamChatMessage> messages,
        int visibleMessageBudget = VisibleMessageBudget,
        string source = "",
        StreamChatContentOptions? contentOptions = null)
    {
        var options = contentOptions ?? StreamChatContentOptions.Default;
        var visibleMessages = messages
            .Where(options.ShouldShow)
            .ToArray();
        var rows = visibleMessages
            .Skip(Math.Max(0, visibleMessages.Length - Math.Max(1, visibleMessageBudget)))
            .ToArray();
        if (rows.Length == 0)
        {
            rows = messages.Count > 0
                ? [new StreamChatMessage("TMR", "No visible Twitch chat rows with current content settings.", StreamChatMessageKind.System)]
                : [new StreamChatMessage("TMR", "Choose Twitch or Streamlabs in settings.", StreamChatMessageKind.System)];
        }

        return new StreamChatOverlayViewModel(
            Title: "Stream Chat",
            Status: status,
            Source: source,
            Rows: rows,
            HasLiveRows: rows.Any(row => row.Kind is StreamChatMessageKind.Message or StreamChatMessageKind.Notice),
            HasErrorRows: rows.Any(row => row.Kind == StreamChatMessageKind.Error));
    }

    public static StreamChatBrowserSettings BrowserSettingsFrom(ApplicationSettings settings)
    {
        return StreamChatOverlaySettings.From(settings);
    }

    public static StreamChatMessage InitialMessage(StreamChatBrowserSettings settings)
    {
        if (!settings.IsConfigured)
        {
            return new StreamChatMessage("TMR", StatusText(settings.Status), StreamChatMessageKind.System);
        }

        if (string.Equals(settings.Provider, StreamChatOverlaySettings.ProviderTwitch, StringComparison.Ordinal)
            && settings.TwitchChannel is { Length: > 0 } channel)
        {
            return new StreamChatMessage("TMR", $"Connecting to #{channel}...", StreamChatMessageKind.System);
        }

        if (string.Equals(settings.Provider, StreamChatOverlaySettings.ProviderStreamlabs, StringComparison.Ordinal))
        {
            return new StreamChatMessage("TMR", "Streamlabs is browser-source only in this build.", StreamChatMessageKind.Error);
        }

        return new StreamChatMessage("TMR", "Stream chat provider unavailable.", StreamChatMessageKind.Error);
    }

    public static string InitialStatus(StreamChatBrowserSettings settings)
    {
        if (!settings.IsConfigured)
        {
            return "chat source not configured";
        }

        if (string.Equals(settings.Provider, StreamChatOverlaySettings.ProviderTwitch, StringComparison.Ordinal)
            && settings.TwitchChannel is { Length: > 0 })
        {
            return "connecting | twitch";
        }

        if (string.Equals(settings.Provider, StreamChatOverlaySettings.ProviderStreamlabs, StringComparison.Ordinal))
        {
            return "streamlabs browser-source only";
        }

        return "chat provider not configured";
    }

    public static string StatusText(string status)
    {
        return status switch
        {
            "missing_or_invalid_streamlabs_url" => "Choose Streamlabs and paste a valid Streamlabs Chat Box widget URL.",
            "missing_or_invalid_twitch_channel" => "Choose Twitch and enter a valid public channel name.",
            _ => "Choose Streamlabs or Twitch in the Stream Chat settings tab."
        };
    }
}
