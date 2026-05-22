using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;
using TmrOverlay.App.Performance;
using TmrOverlay.Core.Settings;

namespace TmrOverlay.App.Overlays.StreamChat;

internal sealed class StreamChatOverlaySource : IDisposable
{
    private static readonly Uri TwitchChatUri = new("wss://irc-ws.chat.twitch.tv:443");
    private static readonly TimeSpan ChatHistoryRetention = TimeSpan.FromMinutes(5);

    private readonly object _sync = new();
    private readonly ILogger<StreamChatOverlaySource> _logger;
    private readonly AppPerformanceState _performanceState;
    private readonly List<StoredStreamChatMessage> _messages = [];
    private CancellationTokenSource? _connectionCancellation;
    private Task? _connectionTask;
    private string _status = "chat source not configured";
    private string _source = string.Empty;
    private string? _activeSettingsKey;
    private string? _lastLoggedError;
    private string? _lastErrorPhase;
    private string? _lastErrorType;
    private string? _lastErrorMessage;
    private DateTimeOffset? _lastErrorAtUtc;
    private int _settingsGeneration;
    private bool _connectedAnnounced;
    private bool _disposed;

    public StreamChatOverlaySource(
        ILogger<StreamChatOverlaySource> logger,
        AppPerformanceState performanceState)
    {
        _logger = logger;
        _performanceState = performanceState;
    }

    public StreamChatOverlayViewModel Snapshot(ApplicationSettings settings)
    {
        return Snapshot(StreamChatOverlaySettings.From(settings));
    }

    public StreamChatOverlayViewModel Snapshot(OverlaySettings settings)
    {
        return Snapshot(StreamChatOverlaySettings.FromOverlay(settings));
    }

    public StreamChatOverlayViewModel Snapshot(StreamChatBrowserSettings settings)
    {
        return Snapshot(settings, DateTimeOffset.UtcNow);
    }

    internal StreamChatOverlayViewModel Snapshot(StreamChatBrowserSettings settings, DateTimeOffset now)
    {
        ReconcileSettings(settings, now);
        lock (_sync)
        {
            PruneExpiredChatMessages(now);
            return StreamChatOverlayViewModel.From(
                _status,
                _messages.Select(message => message.Message).ToArray(),
                StreamChatOverlayViewModel.VisibleMessageBudget,
                _source,
                settings.ContentOptions);
        }
    }

    internal void RecordMessage(StreamChatMessage message, DateTimeOffset receivedAtUtc)
    {
        AddMessage(_settingsGeneration, message, receivedAtUtc);
    }

    public StreamChatDiagnosticsSnapshot DiagnosticsSnapshot(StreamChatBrowserSettings settings)
    {
        return DiagnosticsSnapshot(settings, DateTimeOffset.UtcNow);
    }

    internal StreamChatDiagnosticsSnapshot DiagnosticsSnapshot(StreamChatBrowserSettings settings, DateTimeOffset now)
    {
        lock (_sync)
        {
            var retainedMessages = _messages.ToArray();
            var visibleMessages = retainedMessages
                .Where(message => settings.ContentOptions.ShouldShow(message.Message))
                .ToArray();
            var recentRetainedMessages = retainedMessages
                .Where(message =>
                    message.Message.Kind is not (StreamChatMessageKind.Message or StreamChatMessageKind.Notice)
                    || now - message.ReceivedAtUtc <= ChatHistoryRetention)
                .ToArray();
            var status = _status;
            return new StreamChatDiagnosticsSnapshot(
                Provider: settings.Provider,
                IsConfigured: settings.IsConfigured,
                TwitchChannel: settings.TwitchChannel,
                HasValidTwitchChannel: string.Equals(settings.Provider, StreamChatOverlaySettings.ProviderTwitch, StringComparison.Ordinal)
                    && settings.TwitchChannel is not null,
                TwitchChannelStatus: TwitchChannelStatus(settings),
                HasValidStreamlabsUrl: string.Equals(settings.Provider, StreamChatOverlaySettings.ProviderStreamlabs, StringComparison.Ordinal)
                    && settings.StreamlabsWidgetUrl is not null,
                StreamlabsUrlStatus: StreamlabsUrlStatus(settings),
                Generation: _settingsGeneration,
                ActiveSettingsMatch: string.Equals(_activeSettingsKey, SettingsKey(settings), StringComparison.Ordinal),
                Status: status,
                Source: _source,
                Connected: _connectedAnnounced && status.Contains("connected", StringComparison.OrdinalIgnoreCase),
                Connecting: status.Contains("connecting", StringComparison.OrdinalIgnoreCase)
                    || status.Contains("joining", StringComparison.OrdinalIgnoreCase),
                Reconnecting: status.Contains("reconnecting", StringComparison.OrdinalIgnoreCase),
                RetainedMessageCount: retainedMessages.Length,
                RecentRetainedMessageCount: recentRetainedMessages.Length,
                VisibleMessageCount: visibleMessages.Length,
                MessageCountsByKind: CountMessagesByKind(retainedMessages),
                VisibleMessageCountsByKind: CountMessagesByKind(visibleMessages),
                LastReceivedAtUtc: retainedMessages.Length == 0
                    ? null
                    : retainedMessages.Max(message => message.ReceivedAtUtc),
                LastErrorPhase: _lastErrorPhase,
                LastErrorType: _lastErrorType,
                LastErrorMessage: _lastErrorMessage,
                LastErrorAtUtc: _lastErrorAtUtc);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
        }

        StopConnection();
    }

    private void ReconcileSettings(StreamChatBrowserSettings settings, DateTimeOffset now)
    {
        var settingsKey = SettingsKey(settings);
        string? twitchChannel = null;
        var generation = 0;
        lock (_sync)
        {
            if (_disposed || string.Equals(_activeSettingsKey, settingsKey, StringComparison.Ordinal))
            {
                return;
            }

            _activeSettingsKey = settingsKey;
            _settingsGeneration++;
            generation = _settingsGeneration;
            _connectedAnnounced = false;
            _status = StreamChatOverlayViewModel.InitialStatus(settings);
            _source = SourceText(settings);
            _messages.Clear();
            _messages.Add(Store(StreamChatOverlayViewModel.InitialMessage(settings), now));

            if (string.Equals(settings.Provider, StreamChatOverlaySettings.ProviderTwitch, StringComparison.Ordinal)
                && settings.TwitchChannel is { Length: > 0 } channel)
            {
                twitchChannel = channel;
            }
        }

        StopConnection();
        if (twitchChannel is not null)
        {
            StartTwitchConnection(twitchChannel, generation);
        }
    }

    private static string SettingsKey(StreamChatBrowserSettings settings)
    {
        return $"{settings.Provider}|{settings.StreamlabsWidgetUrl}|{settings.TwitchChannel}|{settings.Status}";
    }

    private static string SourceText(StreamChatBrowserSettings settings)
    {
        return string.Empty;
    }

    private static string TwitchChannelStatus(StreamChatBrowserSettings settings)
    {
        if (!string.Equals(settings.Provider, StreamChatOverlaySettings.ProviderTwitch, StringComparison.Ordinal))
        {
            return "not_selected";
        }

        return settings.TwitchChannel is null ? "missing_or_invalid" : "valid";
    }

    private static string StreamlabsUrlStatus(StreamChatBrowserSettings settings)
    {
        if (!string.Equals(settings.Provider, StreamChatOverlaySettings.ProviderStreamlabs, StringComparison.Ordinal))
        {
            return "not_selected";
        }

        return settings.StreamlabsWidgetUrl is null ? "missing_or_invalid" : "valid";
    }

    private void StartTwitchConnection(string channel, int generation)
    {
        var cancellation = new CancellationTokenSource();
        _connectionCancellation = cancellation;
        _connectionTask = Task.Run(
            () => RunTwitchConnectionLoopAsync(channel, generation, cancellation.Token),
            CancellationToken.None);
    }

    private void StopConnection()
    {
        var cancellation = _connectionCancellation;
        var task = _connectionTask;
        _connectionCancellation = null;
        _connectionTask = null;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        _ = (task ?? Task.CompletedTask).ContinueWith(
            _ => cancellation.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task RunTwitchConnectionLoopAsync(
        string channel,
        int generation,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var shouldReconnect = true;
            try
            {
                shouldReconnect = await ConnectAndReadTwitchAsync(channel, generation, cancellationToken).ConfigureAwait(false);
                attempt = shouldReconnect ? attempt + 1 : 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                attempt++;
                LogWarningOnce(exception, "connection");
                ConfirmConnectionMessage(
                    generation,
                    new StreamChatMessage("TMR", "Twitch chat connection error.", StreamChatMessageKind.Error),
                    DateTimeOffset.UtcNow);
                SetStatus(generation, "chat reconnecting | twitch");
            }

            if (!shouldReconnect || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var delay = TimeSpan.FromSeconds(Math.Min(15, 3 + attempt * 2));
            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task<bool> ConnectAndReadTwitchAsync(
        string channel,
        int generation,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var succeeded = false;
        using var socket = new ClientWebSocket();
        try
        {
            SetStatus(generation, "connecting | twitch");
            await socket.ConnectAsync(TwitchChatUri, cancellationToken).ConfigureAwait(false);
            await SendRawAsync(socket, "CAP REQ :twitch.tv/tags twitch.tv/commands", cancellationToken).ConfigureAwait(false);
            await SendRawAsync(socket, "PASS SCHMOOPIIE", cancellationToken).ConfigureAwait(false);
            await SendRawAsync(socket, $"NICK justinfan{Random.Shared.Next(10000, 99999)}", cancellationToken).ConfigureAwait(false);
            await SendRawAsync(socket, $"JOIN #{channel}", cancellationToken).ConfigureAwait(false);
            SetStatus(generation, "joining | twitch");
            succeeded = true;
        }
        finally
        {
            _performanceState.RecordOperation(AppPerformanceMetricIds.OverlayStreamChatConnect, started, succeeded);
        }

        return await ReceiveTwitchMessagesAsync(socket, channel, generation, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ReceiveTwitchMessagesAsync(
        ClientWebSocket socket,
        string channel,
        int generation,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var builder = new StringBuilder();
        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                HandleSocketClosed(generation);
                return true;
            }

            builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage)
            {
                continue;
            }

            var shouldContinue = await ProcessTwitchPayloadAsync(
                socket,
                channel,
                generation,
                builder.ToString(),
                cancellationToken).ConfigureAwait(false);
            builder.Clear();
            if (!shouldContinue)
            {
                return false;
            }
        }

        HandleSocketClosed(generation);
        return true;
    }

    private async Task<bool> ProcessTwitchPayloadAsync(
        ClientWebSocket socket,
        string channel,
        int generation,
        string payload,
        CancellationToken cancellationToken)
    {
        foreach (var rawLine in payload.Split("\r\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (StreamChatIrcParser.TryGetPingResponse(rawLine, out var pong))
            {
                await SendRawAsync(socket, pong, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (StreamChatIrcParser.IsAuthFailure(rawLine))
            {
                ConfirmConnectionMessage(
                    generation,
                    new StreamChatMessage("TMR", "Twitch rejected the chat connection.", StreamChatMessageKind.Error),
                    DateTimeOffset.UtcNow);
                SetStatus(generation, "twitch auth rejected");
                return false;
            }

            if (StreamChatIrcParser.IsReconnect(rawLine))
            {
                SetStatus(generation, "chat reconnecting | twitch");
                return true;
            }

            if (StreamChatIrcParser.IsJoined(rawLine, channel))
            {
                AnnounceConnected(channel, generation);
                continue;
            }

            var notice = StreamChatIrcParser.TryParseUserNotice(rawLine);
            if (notice is not null)
            {
                AddMessage(generation, notice, DateTimeOffset.UtcNow);
                continue;
            }

            var message = StreamChatIrcParser.TryParsePrivMsg(rawLine);
            if (message is not null)
            {
                AddMessage(generation, message, DateTimeOffset.UtcNow);
            }
        }

        return true;
    }

    private static async Task SendRawAsync(ClientWebSocket socket, string line, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
        await socket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken).ConfigureAwait(false);
    }

    private void AnnounceConnected(string channel, int generation)
    {
        lock (_sync)
        {
            if (!IsCurrentGeneration(generation) || _connectedAnnounced)
            {
                return;
            }

            _connectedAnnounced = true;
        }

        ConfirmConnectionMessage(
            generation,
            new StreamChatMessage("TMR", $"Chat connected to #{channel}.", StreamChatMessageKind.System),
            DateTimeOffset.UtcNow);
        SetStatus(generation, "chat connected | twitch");
    }

    private void HandleSocketClosed(int generation)
    {
        var wasConnected = false;
        lock (_sync)
        {
            if (!IsCurrentGeneration(generation))
            {
                return;
            }

            wasConnected = _connectedAnnounced;
        }

        if (!wasConnected)
        {
            ConfirmConnectionMessage(
                generation,
                new StreamChatMessage("TMR", "Twitch chat disconnected before joining.", StreamChatMessageKind.Error),
                DateTimeOffset.UtcNow);
        }

        SetStatus(generation, "chat reconnecting | twitch");
    }

    private void AddMessage(int generation, StreamChatMessage message, DateTimeOffset receivedAtUtc)
    {
        lock (_sync)
        {
            if (!IsCurrentGeneration(generation))
            {
                return;
            }

            PruneExpiredChatMessages(receivedAtUtc);
            _messages.Add(Store(message, receivedAtUtc));
            TrimMessageHistory();
        }
    }

    private void ConfirmConnectionMessage(int generation, StreamChatMessage message, DateTimeOffset receivedAtUtc)
    {
        lock (_sync)
        {
            if (!IsCurrentGeneration(generation))
            {
                return;
            }

            PruneExpiredChatMessages(receivedAtUtc);
            if (_messages.Count == 1 && _messages[0].Message.Kind == StreamChatMessageKind.System)
            {
                _messages[0] = Store(message, receivedAtUtc);
                return;
            }

            _messages.Add(Store(message, receivedAtUtc));
            TrimMessageHistory();
        }
    }

    private void PruneExpiredChatMessages(DateTimeOffset now)
    {
        _messages.RemoveAll(message =>
            message.Message.Kind is StreamChatMessageKind.Message or StreamChatMessageKind.Notice
            && now - message.ReceivedAtUtc > ChatHistoryRetention);
    }

    private void TrimMessageHistory()
    {
        while (_messages.Count > StreamChatOverlayViewModel.MaximumMessages)
        {
            var messageIndex = _messages.FindIndex(message => message.Message.Kind is StreamChatMessageKind.Message or StreamChatMessageKind.Notice);
            if (messageIndex < 0)
            {
                messageIndex = 0;
            }

            _messages.RemoveAt(messageIndex);
        }
    }

    private static StoredStreamChatMessage Store(StreamChatMessage message, DateTimeOffset receivedAtUtc)
    {
        return new StoredStreamChatMessage(message, receivedAtUtc);
    }

    private static IReadOnlyDictionary<string, int> CountMessagesByKind(IReadOnlyList<StoredStreamChatMessage> messages)
    {
        var counts = Enum.GetValues<StreamChatMessageKind>()
            .ToDictionary(kind => kind.ToString().ToLowerInvariant(), _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var message in messages)
        {
            counts[message.Message.Kind.ToString().ToLowerInvariant()]++;
        }

        return counts;
    }

    private void SetStatus(int generation, string status)
    {
        lock (_sync)
        {
            if (!IsCurrentGeneration(generation))
            {
                return;
            }

            _status = status;
        }
    }

    private bool IsCurrentGeneration(int generation)
    {
        return !_disposed && generation == _settingsGeneration;
    }

    private void LogWarningOnce(Exception exception, string phase)
    {
        var key = $"{phase}:{exception.GetType().FullName}:{exception.Message}";
        if (string.Equals(_lastLoggedError, key, StringComparison.Ordinal))
        {
            return;
        }

        _lastLoggedError = key;
        _lastErrorPhase = phase;
        _lastErrorType = exception.GetType().FullName;
        _lastErrorMessage = exception.Message;
        _lastErrorAtUtc = DateTimeOffset.UtcNow;
        _logger.LogWarning(exception, "Stream chat source {Phase} failed.", phase);
    }

    private sealed record StoredStreamChatMessage(
        StreamChatMessage Message,
        DateTimeOffset ReceivedAtUtc);
}

internal sealed record StreamChatDiagnosticsSnapshot(
    string Provider,
    bool IsConfigured,
    string? TwitchChannel,
    bool HasValidTwitchChannel,
    string TwitchChannelStatus,
    bool HasValidStreamlabsUrl,
    string StreamlabsUrlStatus,
    int Generation,
    bool ActiveSettingsMatch,
    string Status,
    string Source,
    bool Connected,
    bool Connecting,
    bool Reconnecting,
    int RetainedMessageCount,
    int RecentRetainedMessageCount,
    int VisibleMessageCount,
    IReadOnlyDictionary<string, int> MessageCountsByKind,
    IReadOnlyDictionary<string, int> VisibleMessageCountsByKind,
    DateTimeOffset? LastReceivedAtUtc,
    string? LastErrorPhase,
    string? LastErrorType,
    string? LastErrorMessage,
    DateTimeOffset? LastErrorAtUtc);
