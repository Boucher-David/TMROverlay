using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using TmrOverlay.Core.AppInfo;

namespace TmrOverlay.App.Telemetry;

internal sealed class TelemetryCaptureSession : IAsyncDisposable
{
    private const string TelemetryFileName = "telemetry.bin";
    private const string SchemaFileName = "telemetry-schema.json";
    private const string ManifestFileName = "capture-manifest.json";
    private const string LatestSessionInfoFileName = "latest-session.yaml";
    private const string SessionInfoDirectoryName = "session-info";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly Channel<CaptureMessage> _messages;
    private readonly Task _writerTask;
    private readonly bool _storeSessionInfoSnapshots;
    private readonly string _telemetryFilePath;
    private readonly string _manifestFilePath;
    private readonly string _latestSessionInfoPath;
    private readonly string _sessionInfoDirectoryPath;
    private readonly CaptureManifest _manifest;
    private readonly Action<TelemetryCaptureWriteStatus>? _writeStatusChanged;
    private Exception? _writerFault;
    private int _droppedFrameCount;
    private int _pendingMessageCount;
    private int _disposed;

    private TelemetryCaptureSession(
        string directoryPath,
        bool storeSessionInfoSnapshots,
        CaptureManifest manifest,
        int queueCapacity,
        Action<TelemetryCaptureWriteStatus>? writeStatusChanged)
    {
        DirectoryPath = directoryPath;
        StartedAtUtc = manifest.StartedAtUtc;
        _storeSessionInfoSnapshots = storeSessionInfoSnapshots;
        _manifest = manifest;
        _writeStatusChanged = writeStatusChanged;
        _telemetryFilePath = Path.Combine(directoryPath, TelemetryFileName);
        _manifestFilePath = Path.Combine(directoryPath, ManifestFileName);
        _latestSessionInfoPath = Path.Combine(directoryPath, LatestSessionInfoFileName);
        _sessionInfoDirectoryPath = Path.Combine(directoryPath, SessionInfoDirectoryName);
        _messages = Channel.CreateBounded<CaptureMessage>(new BoundedChannelOptions(queueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        _writerTask = Task.Run(RunWriterLoopAsync);
    }

    public string DirectoryPath { get; }

    public string CaptureId => _manifest.CaptureId;

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset? FinishedAtUtc => _manifest.FinishedAtUtc;

    public int FrameCount => _manifest.FrameCount;

    public int DroppedFrameCount => _manifest.DroppedFrameCount;

    public int SessionInfoSnapshotCount => _manifest.SessionInfoSnapshotCount;

    public int PendingMessageCount => Volatile.Read(ref _pendingMessageCount);

    public Exception? WriterFault => Volatile.Read(ref _writerFault);

    public static TelemetryCaptureSession Create(
        string rootDirectory,
        int queueCapacity,
        bool storeSessionInfoSnapshots,
        int sdkVersion,
        int tickRate,
        int bufferLength,
        IReadOnlyCollection<TelemetryVariableSchema> schema,
        Action<TelemetryCaptureWriteStatus>? writeStatusChanged = null)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var captureId = CreateUniqueCaptureId(
            rootDirectory,
            $"capture-{startedAtUtc:yyyyMMdd-HHmmss-fff}");
        var directoryPath = Path.Combine(rootDirectory, captureId);

        Directory.CreateDirectory(directoryPath);

        var manifest = new CaptureManifest
        {
            CaptureId = captureId,
            CollectionId = captureId,
            StartedAtUtc = startedAtUtc,
            TelemetryFile = TelemetryFileName,
            SchemaFile = SchemaFileName,
            LatestSessionInfoFile = LatestSessionInfoFileName,
            SessionInfoDirectory = SessionInfoDirectoryName,
            SdkVersion = sdkVersion,
            TickRate = tickRate,
            BufferLength = bufferLength,
            VariableCount = schema.Count,
            AppVersion = AppVersionInfo.Current
        };

        File.WriteAllText(
            Path.Combine(directoryPath, SchemaFileName),
            JsonSerializer.Serialize(schema, JsonOptions));

        File.WriteAllText(
            Path.Combine(directoryPath, ManifestFileName),
            JsonSerializer.Serialize(manifest, JsonOptions));

        return new TelemetryCaptureSession(
            directoryPath,
            storeSessionInfoSnapshots,
            manifest,
            queueCapacity,
            writeStatusChanged);
    }

    // Raw capture directories are durable evidence. A fast reconnect can
    // occur within the old millisecond timestamp resolution, so preserve the
    // timestamp stem for support readability while reserving a unique suffix
    // instead of reopening and overwriting the prior capture directory.
    internal static string CreateUniqueCaptureId(string rootDirectory, string timestampStem)
    {
        Directory.CreateDirectory(rootDirectory);
        var candidate = timestampStem;
        var ordinal = 0;
        while (Directory.Exists(Path.Combine(rootDirectory, candidate)))
        {
            ordinal++;
            candidate = $"{timestampStem}-r{ordinal:D3}";
        }

        return candidate;
    }

    public bool TryQueueFrame(TelemetryFrameEnvelope frame)
    {
        if (WriterFault is not null)
        {
            return false;
        }

        Interlocked.Increment(ref _pendingMessageCount);
        if (!_messages.Writer.TryWrite(new CaptureFrameMessage(frame)))
        {
            Interlocked.Decrement(ref _pendingMessageCount);
            return false;
        }

        return true;
    }

    public bool TryQueueSessionInfo(SessionInfoSnapshot sessionInfo)
    {
        if (WriterFault is not null)
        {
            return false;
        }

        Interlocked.Increment(ref _pendingMessageCount);
        if (!_messages.Writer.TryWrite(new CaptureSessionInfoMessage(sessionInfo)))
        {
            Interlocked.Decrement(ref _pendingMessageCount);
            return false;
        }

        return true;
    }

    public void RecordDroppedFrame()
    {
        Interlocked.Increment(ref _droppedFrameCount);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _messages.Writer.TryComplete();
        await _writerTask.ConfigureAwait(false);
    }

    private async Task RunWriterLoopAsync()
    {
        try
        {
            Directory.CreateDirectory(_sessionInfoDirectoryPath);

            await using var telemetryStream = new FileStream(
                _telemetryFilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 64 * 1024,
                useAsync: true);

            await WriteFileHeaderAsync(telemetryStream).ConfigureAwait(false);
            ReportWriteStatus(telemetryStream.Length);

            await foreach (var message in _messages.Reader.ReadAllAsync())
            {
                Interlocked.Decrement(ref _pendingMessageCount);
                switch (message)
                {
                    case CaptureFrameMessage frameMessage:
                    {
                        var writeStarted = Stopwatch.GetTimestamp();
                        await WriteFrameAsync(telemetryStream, frameMessage.Frame).ConfigureAwait(false);
                        _manifest.FrameCount++;
                        ReportWriteStatus(telemetryStream.Length, Stopwatch.GetElapsedTime(writeStarted));
                        break;
                    }

                    case CaptureSessionInfoMessage sessionInfoMessage:
                    {
                        var writeStarted = Stopwatch.GetTimestamp();
                        await WriteSessionInfoAsync(sessionInfoMessage.SessionInfo).ConfigureAwait(false);
                        _manifest.SessionInfoSnapshotCount++;
                        ReportWriteStatus(telemetryStream.Length, Stopwatch.GetElapsedTime(writeStarted));
                        break;
                    }
                }
            }

            await telemetryStream.FlushAsync().ConfigureAwait(false);

            _manifest.FinishedAtUtc = DateTimeOffset.UtcNow;
            _manifest.DroppedFrameCount = Volatile.Read(ref _droppedFrameCount);

            await File.WriteAllTextAsync(
                    _manifestFilePath,
                    JsonSerializer.Serialize(_manifest, JsonOptions))
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _writerFault, exception);
            _messages.Writer.TryComplete(exception);
            ReportWriteStatus(null, exception: exception);
            throw;
        }
    }

    private async Task WriteFileHeaderAsync(FileStream telemetryStream)
    {
        var header = new byte[32];
        Encoding.ASCII.GetBytes("TMRCAP01").CopyTo(header, 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8, 4), _manifest.SdkVersion);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12, 4), _manifest.TickRate);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16, 4), _manifest.BufferLength);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(20, 4), _manifest.VariableCount);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(24, 8), _manifest.StartedAtUtc.ToUnixTimeMilliseconds());
        await telemetryStream.WriteAsync(header).ConfigureAwait(false);
    }

    private static async Task WriteFrameAsync(FileStream telemetryStream, TelemetryFrameEnvelope frame)
    {
        var frameHeader = new byte[32];
        BinaryPrimitives.WriteInt64LittleEndian(frameHeader.AsSpan(0, 8), frame.CapturedAtUtc.ToUnixTimeMilliseconds());
        BinaryPrimitives.WriteInt32LittleEndian(frameHeader.AsSpan(8, 4), frame.FrameIndex);
        BinaryPrimitives.WriteInt32LittleEndian(frameHeader.AsSpan(12, 4), frame.SessionTick);
        BinaryPrimitives.WriteInt32LittleEndian(frameHeader.AsSpan(16, 4), frame.SessionInfoUpdate);
        BinaryPrimitives.WriteInt64LittleEndian(frameHeader.AsSpan(20, 8), BitConverter.DoubleToInt64Bits(frame.SessionTime));
        BinaryPrimitives.WriteInt32LittleEndian(frameHeader.AsSpan(28, 4), frame.Payload.Length);

        await telemetryStream.WriteAsync(frameHeader).ConfigureAwait(false);
        await telemetryStream.WriteAsync(frame.Payload).ConfigureAwait(false);
    }

    private async Task WriteSessionInfoAsync(SessionInfoSnapshot sessionInfo)
    {
        await File.WriteAllTextAsync(_latestSessionInfoPath, sessionInfo.Yaml).ConfigureAwait(false);

        if (!_storeSessionInfoSnapshots)
        {
            return;
        }

        var snapshotPath = Path.Combine(
            _sessionInfoDirectoryPath,
            $"session-{sessionInfo.SessionInfoUpdate:D4}.yaml");

        await File.WriteAllTextAsync(snapshotPath, sessionInfo.Yaml).ConfigureAwait(false);
    }

    private void ReportWriteStatus(long? telemetryFileBytes, TimeSpan? lastWriteDuration = null, Exception? exception = null)
    {
        try
        {
            _writeStatusChanged?.Invoke(new TelemetryCaptureWriteStatus(
                TimestampUtc: DateTimeOffset.UtcNow,
                CaptureId: CaptureId,
                DirectoryPath: DirectoryPath,
                FramesWritten: _manifest.FrameCount,
                SessionInfoSnapshotCount: _manifest.SessionInfoSnapshotCount,
                PendingMessageCount: PendingMessageCount,
                TelemetryFileBytes: telemetryFileBytes,
                Exception: exception,
                LastWriteDuration: lastWriteDuration));
        }
        catch
        {
            // UI health reporting is diagnostic-only and must not affect capture writes.
        }
    }

    private abstract record CaptureMessage;

    private sealed record CaptureFrameMessage(TelemetryFrameEnvelope Frame) : CaptureMessage;

    private sealed record CaptureSessionInfoMessage(SessionInfoSnapshot SessionInfo) : CaptureMessage;
}
