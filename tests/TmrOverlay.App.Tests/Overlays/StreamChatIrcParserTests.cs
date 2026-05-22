using Microsoft.Extensions.Logging.Abstractions;
using TmrOverlay.App.Performance;
using TmrOverlay.App.Overlays.StreamChat;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class StreamChatIrcParserTests
{
    [Fact]
    public void TryParsePrivMsg_UsesDisplayNameAndDecodesTwitchEscapes()
    {
        const string line = "@display-name=Race\\sEngineer;color=#62C7FF :raceengineer!raceengineer@raceengineer.tmi.twitch.tv PRIVMSG #tmracing :Box\\sthis\\sup\\:\\sgo";

        var message = StreamChatIrcParser.TryParsePrivMsg(line);

        Assert.NotNull(message);
        Assert.Equal("Race Engineer", message.Name);
        Assert.Equal("Box this up; go", message.Text);
        Assert.Equal(StreamChatMessageKind.Message, message.Kind);
        Assert.Equal("twitch", message.Source);
        Assert.Equal("#62C7FF", message.ColorHex);
    }

    [Fact]
    public void TryParsePrivMsg_PreservesTwitchMetadataTags()
    {
        const string line = "@badge-info=subscriber/12;badges=moderator/1,subscriber/12;bits=100;color=#62C7FF;display-name=Race\\sEngineer;emotes=25:6-10;first-msg=1;id=abc12345-0000-4000-8000-000000000000;reply-parent-display-name=Strategist;tmi-sent-ts=1778779200000 :raceengineer!raceengineer@raceengineer.tmi.twitch.tv PRIVMSG #tmracing :hello Kappa";

        var message = StreamChatIrcParser.TryParsePrivMsg(line);

        Assert.NotNull(message);
        Assert.Equal("Race Engineer", message.Name);
        Assert.Equal("#62C7FF", message.ColorHex);
        Assert.Equal(2, message.Badges.Count);
        Assert.Equal(100, message.Bits);
        Assert.True(message.IsFirstMessage);
        Assert.Equal("Strategist", message.ReplyTo);
        Assert.Equal("abc12345-0000-4000-8000-000000000000", message.TwitchMessageId);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1778779200000), message.TimestampUtc);
        var emote = Assert.Single(message.Emotes);
        Assert.Equal("25", emote.Id);
        Assert.Equal("Kappa", emote.Token);
        var badges = StreamChatMessageDisplay.BadgeParts(message, StreamChatContentOptions.Default);
        Assert.Equal(new[] { "mod", "sub 12" }, badges.Select(badge => badge.Label).ToArray());
        var segments = StreamChatMessageDisplay.MessageSegments(message, StreamChatContentOptions.Default);
        Assert.Collection(
            segments,
            segment =>
            {
                Assert.Equal("text", segment.Kind);
                Assert.Equal("hello ", segment.Text);
            },
            segment =>
            {
                Assert.Equal("emote", segment.Kind);
                Assert.Equal("Kappa", segment.Text);
                Assert.Contains("/emoticons/v2/25/default/dark/1.0", segment.ImageUrl);
            });
    }

    [Fact]
    public void TryParseUserNotice_ReturnsNoticeRowWithAlertMetadata()
    {
        const string line = "@badges=subscriber/24;color=#FFD15B;display-name=Viewer;id=notice-123;login=viewer;msg-id=resub;system-msg=Viewer\\ssubscribed\\sfor\\s24\\smonths;tmi-sent-ts=1778779200000 :tmi.twitch.tv USERNOTICE #tmracing :Great race";

        var message = StreamChatIrcParser.TryParseUserNotice(line);

        Assert.NotNull(message);
        Assert.Equal(StreamChatMessageKind.Notice, message.Kind);
        Assert.Equal("Viewer", message.Name);
        Assert.Equal("Viewer subscribed for 24 months Great race", message.Text);
        Assert.Equal("resub", message.NoticeKind);
        Assert.Equal("#FFD15B", message.ColorHex);
        Assert.Equal("twitch", message.Source);
    }

    [Fact]
    public void TryParsePrivMsg_FallsBackToPrefixName()
    {
        const string line = ":justinfan12345!justinfan12345@justinfan12345.tmi.twitch.tv PRIVMSG #tmracing :hello";

        var message = StreamChatIrcParser.TryParsePrivMsg(line);

        Assert.NotNull(message);
        Assert.Equal("justinfan12345", message.Name);
        Assert.Equal("hello", message.Text);
    }

    [Fact]
    public void TryGetPingResponse_ReturnsPongPayload()
    {
        var handled = StreamChatIrcParser.TryGetPingResponse("PING :tmi.twitch.tv", out var response);

        Assert.True(handled);
        Assert.Equal("PONG :tmi.twitch.tv", response);
    }

    [Fact]
    public void StreamChatOverlaySource_SnapshotReturnsProductionModelForUnconfiguredSettings()
    {
        using var source = new StreamChatOverlaySource(
            NullLogger<StreamChatOverlaySource>.Instance,
            new AppPerformanceState());
        var settings = new StreamChatBrowserSettings(
            Provider: StreamChatOverlaySettings.ProviderNone,
            IsConfigured: false,
            StreamlabsWidgetUrl: null,
            TwitchChannel: null,
            Status: "not_configured");

        var viewModel = source.Snapshot(settings);

        Assert.Equal("Stream Chat", viewModel.Title);
        Assert.Equal("chat source not configured", viewModel.Status);
        Assert.Equal(string.Empty, viewModel.Source);
        Assert.False(viewModel.HasLiveRows);
        Assert.Equal(StreamChatMessageKind.System, viewModel.Rows.Single().Kind);
        Assert.Contains("Choose Streamlabs or Twitch", viewModel.Rows.Single().Text, StringComparison.Ordinal);
    }

    [Fact]
    public void StreamChatOverlaySource_RetainsRecentChatAndPrunesExpiredChatRows()
    {
        using var source = new StreamChatOverlaySource(
            NullLogger<StreamChatOverlaySource>.Instance,
            new AppPerformanceState());
        var settings = new StreamChatBrowserSettings(
            Provider: StreamChatOverlaySettings.ProviderNone,
            IsConfigured: false,
            StreamlabsWidgetUrl: null,
            TwitchChannel: null,
            Status: "not_configured");
        _ = source.Snapshot(settings);
        var now = DateTimeOffset.Parse("2026-05-14T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        source.RecordMessage(new StreamChatMessage("old", "expired", StreamChatMessageKind.Message), now.AddMinutes(-6));
        source.RecordMessage(new StreamChatMessage("recent", "kept", StreamChatMessageKind.Message), now.AddMinutes(-2));
        var viewModel = source.Snapshot(settings, now);

        Assert.DoesNotContain(viewModel.Rows, row => row.Text == "expired");
        Assert.Contains(viewModel.Rows, row => row.Text == "kept");
        Assert.Contains(viewModel.Rows, row => row.Kind == StreamChatMessageKind.System);
    }

    [Fact]
    public void StreamChatOverlaySource_DiagnosticsSnapshotSummarizesRuntimeStateWithoutWidgetUrl()
    {
        using var source = new StreamChatOverlaySource(
            NullLogger<StreamChatOverlaySource>.Instance,
            new AppPerformanceState());
        var now = DateTimeOffset.Parse("2026-05-14T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var settings = new StreamChatBrowserSettings(
            Provider: StreamChatOverlaySettings.ProviderStreamlabs,
            IsConfigured: true,
            StreamlabsWidgetUrl: "https://streamlabs.com/widgets/chat-box/private-token",
            TwitchChannel: null,
            Status: "configured_streamlabs")
        {
            ContentOptions = StreamChatContentOptions.Default with { ShowAlerts = false }
        };

        _ = source.Snapshot(settings, now);
        source.RecordMessage(new StreamChatMessage("viewer", "hello", StreamChatMessageKind.Message), now.AddSeconds(1));
        source.RecordMessage(new StreamChatMessage("viewer", "subscribed", StreamChatMessageKind.Notice)
        {
            Source = "twitch",
            NoticeKind = "sub"
        }, now.AddSeconds(2));

        var diagnostics = source.DiagnosticsSnapshot(settings, now.AddSeconds(3));

        Assert.Equal(StreamChatOverlaySettings.ProviderStreamlabs, diagnostics.Provider);
        Assert.True(diagnostics.IsConfigured);
        Assert.False(diagnostics.HasValidTwitchChannel);
        Assert.Equal("not_selected", diagnostics.TwitchChannelStatus);
        Assert.True(diagnostics.HasValidStreamlabsUrl);
        Assert.Equal("valid", diagnostics.StreamlabsUrlStatus);
        Assert.True(diagnostics.ActiveSettingsMatch);
        Assert.Equal(1, diagnostics.Generation);
        Assert.Equal(3, diagnostics.RetainedMessageCount);
        Assert.Equal(3, diagnostics.RecentRetainedMessageCount);
        Assert.Equal(2, diagnostics.VisibleMessageCount);
        Assert.Equal(1, diagnostics.MessageCountsByKind["message"]);
        Assert.Equal(1, diagnostics.MessageCountsByKind["notice"]);
        Assert.Equal(1, diagnostics.MessageCountsByKind["error"]);
        Assert.Equal(0, diagnostics.MessageCountsByKind["system"]);
        Assert.Equal(1, diagnostics.VisibleMessageCountsByKind["message"]);
        Assert.Equal(0, diagnostics.VisibleMessageCountsByKind["notice"]);
        Assert.Equal(now.AddSeconds(2), diagnostics.LastReceivedAtUtc);
        Assert.False(diagnostics.Connected);
        Assert.False(diagnostics.Connecting);
        Assert.False(diagnostics.Reconnecting);
    }

    [Fact]
    public void StreamChatOverlayViewModel_HidesTwitchNoticeRowsWhenAlertsAreDisabled()
    {
        var notice = new StreamChatMessage("viewer", "Viewer subscribed.", StreamChatMessageKind.Notice)
        {
            Source = "twitch",
            NoticeKind = "sub"
        };
        var message = new StreamChatMessage("viewer", "regular chat", StreamChatMessageKind.Message)
        {
            Source = "twitch"
        };
        var options = StreamChatContentOptions.Default with { ShowAlerts = false };

        var viewModel = StreamChatOverlayViewModel.From("chat connected | twitch", [notice, message], contentOptions: options);

        Assert.DoesNotContain(viewModel.Rows, row => row.Kind == StreamChatMessageKind.Notice);
        Assert.Contains(viewModel.Rows, row => row.Text == "regular chat");
    }
}
