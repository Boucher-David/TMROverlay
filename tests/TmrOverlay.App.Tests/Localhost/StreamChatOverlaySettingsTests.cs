using TmrOverlay.App.Overlays.StreamChat;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using Xunit;

namespace TmrOverlay.App.Tests.Localhost;

public sealed class StreamChatOverlaySettingsTests
{
    [Fact]
    public void From_DefaultsToTechMatesRacingTwitchChannel()
    {
        var settings = new ApplicationSettings();

        var result = StreamChatOverlaySettings.From(settings);

        Assert.True(result.IsConfigured);
        Assert.Equal(StreamChatOverlaySettings.ProviderTwitch, result.Provider);
        Assert.Null(result.StreamlabsWidgetUrl);
        Assert.Equal("techmatesracing", result.TwitchChannel);
        Assert.Equal("configured_twitch", result.Status);
        Assert.True(result.ContentOptions.ShowAuthorColor);
        Assert.True(result.ContentOptions.ShowBadges);
        Assert.True(result.ContentOptions.ShowAlerts);
        Assert.False(result.ContentOptions.ShowMessageIds);
    }

    [Fact]
    public void From_RespectsExplicitNotConfiguredProvider()
    {
        var settings = new ApplicationSettings();
        var overlay = settings.GetOrAddOverlay(
            StreamChatOverlayDefinition.Definition.Id,
            StreamChatOverlayDefinition.Definition.DefaultWidth,
            StreamChatOverlayDefinition.Definition.DefaultHeight);
        overlay.SetStringOption(OverlayOptionKeys.StreamChatProvider, StreamChatOverlaySettings.ProviderNone);

        var result = StreamChatOverlaySettings.From(settings);

        Assert.False(result.IsConfigured);
        Assert.Equal(StreamChatOverlaySettings.ProviderNone, result.Provider);
        Assert.Null(result.StreamlabsWidgetUrl);
        Assert.Null(result.TwitchChannel);
        Assert.Equal("not_configured", result.Status);
    }

    [Fact]
    public void From_UsesOnlySelectedStreamlabsProvider()
    {
        var settings = new ApplicationSettings();
        var overlay = settings.GetOrAddOverlay(
            StreamChatOverlayDefinition.Definition.Id,
            StreamChatOverlayDefinition.Definition.DefaultWidth,
            StreamChatOverlayDefinition.Definition.DefaultHeight);
        overlay.SetStringOption(OverlayOptionKeys.StreamChatProvider, StreamChatOverlaySettings.ProviderStreamlabs);
        overlay.SetStringOption(OverlayOptionKeys.StreamChatStreamlabsUrl, "https://streamlabs.com/widgets/chat-box/abc123");
        overlay.SetStringOption(OverlayOptionKeys.StreamChatTwitchChannel, "tmracing");

        var result = StreamChatOverlaySettings.From(settings);

        Assert.True(result.IsConfigured);
        Assert.Equal(StreamChatOverlaySettings.ProviderStreamlabs, result.Provider);
        Assert.Equal("https://streamlabs.com/widgets/chat-box/abc123", result.StreamlabsWidgetUrl);
        Assert.Null(result.TwitchChannel);
    }

    [Fact]
    public void From_UsesOnlySelectedTwitchProvider()
    {
        var settings = new ApplicationSettings();
        var overlay = settings.GetOrAddOverlay(
            StreamChatOverlayDefinition.Definition.Id,
            StreamChatOverlayDefinition.Definition.DefaultWidth,
            StreamChatOverlayDefinition.Definition.DefaultHeight);
        overlay.SetStringOption(OverlayOptionKeys.StreamChatProvider, StreamChatOverlaySettings.ProviderTwitch);
        overlay.SetStringOption(OverlayOptionKeys.StreamChatStreamlabsUrl, "https://streamlabs.com/widgets/chat-box/abc123");
        overlay.SetStringOption(OverlayOptionKeys.StreamChatTwitchChannel, "https://www.twitch.tv/TMRacing");

        var result = StreamChatOverlaySettings.From(settings);

        Assert.True(result.IsConfigured);
        Assert.Equal(StreamChatOverlaySettings.ProviderTwitch, result.Provider);
        Assert.Null(result.StreamlabsWidgetUrl);
        Assert.Equal("tmracing", result.TwitchChannel);
    }

    [Theory]
    [InlineData(
        StreamChatOverlaySettings.ProviderStreamlabs,
        "https://example.com/widgets/chat-box/abc123",
        "tmracing",
        "missing_or_invalid_streamlabs_url",
        "Choose Streamlabs and paste a valid Streamlabs Chat Box widget URL.")]
    [InlineData(
        StreamChatOverlaySettings.ProviderTwitch,
        "https://streamlabs.com/widgets/chat-box/abc123",
        "no spaces allowed",
        "missing_or_invalid_twitch_channel",
        "Choose Twitch and enter a valid public channel name.")]
    public void From_InvalidSelectedProviderConfigReportsDeterministicUnavailableStatus(
        string provider,
        string streamlabsUrl,
        string twitchChannel,
        string expectedStatus,
        string expectedMessage)
    {
        var settings = new ApplicationSettings();
        var overlay = settings.GetOrAddOverlay(
            StreamChatOverlayDefinition.Definition.Id,
            StreamChatOverlayDefinition.Definition.DefaultWidth,
            StreamChatOverlayDefinition.Definition.DefaultHeight);
        overlay.SetStringOption(OverlayOptionKeys.StreamChatProvider, provider);
        overlay.SetStringOption(OverlayOptionKeys.StreamChatStreamlabsUrl, streamlabsUrl);
        overlay.SetStringOption(OverlayOptionKeys.StreamChatTwitchChannel, twitchChannel);

        var result = StreamChatOverlaySettings.From(settings);
        var initialMessage = StreamChatOverlayViewModel.InitialMessage(result);

        Assert.False(result.IsConfigured);
        Assert.Equal(provider, result.Provider);
        Assert.Null(result.StreamlabsWidgetUrl);
        Assert.Null(result.TwitchChannel);
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal("chat source not configured", StreamChatOverlayViewModel.InitialStatus(result));
        Assert.Equal(expectedMessage, initialMessage.Text);
        Assert.Equal(StreamChatMessageKind.System, initialMessage.Kind);
    }

    [Fact]
    public void From_RespectsTwitchContentOptions()
    {
        var settings = new ApplicationSettings();
        var overlay = settings.GetOrAddOverlay(
            StreamChatOverlayDefinition.Definition.Id,
            StreamChatOverlayDefinition.Definition.DefaultWidth,
            StreamChatOverlayDefinition.Definition.DefaultHeight);
        overlay.SetStringOption(OverlayOptionKeys.StreamChatProvider, StreamChatOverlaySettings.ProviderTwitch);
        overlay.SetBooleanOption(OverlayOptionKeys.StreamChatShowBadges, false);
        overlay.SetBooleanOption(OverlayOptionKeys.StreamChatShowAlerts, false);
        overlay.SetBooleanOption(OverlayOptionKeys.StreamChatShowMessageIds, true);

        var result = StreamChatOverlaySettings.From(settings);

        Assert.False(result.ContentOptions.ShowBadges);
        Assert.False(result.ContentOptions.ShowAlerts);
        Assert.True(result.ContentOptions.ShowMessageIds);
    }

    [Theory]
    [InlineData("http://streamlabs.com/widgets/chat-box/abc123")]
    [InlineData("https://example.com/widgets/chat-box/abc123")]
    [InlineData("https://streamlabs.com/widgets/alert-box/abc123")]
    public void NormalizeStreamlabsUrl_RejectsNonChatWidgetUrls(string url)
    {
        Assert.Null(StreamChatOverlaySettings.NormalizeStreamlabsUrl(url));
    }
}
