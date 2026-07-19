namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// A pair of connected, in-memory byte streams for one Overlay Bridge virtual circuit.
/// The circuit is transport-neutral: a relay adapter can pump bytes between either endpoint
/// and any physical transport without this type knowing about sockets, rooms, or telemetry.
/// </summary>
public sealed class OverlayBridgeVirtualCircuitPair : IDisposable, IAsyncDisposable
{
    internal OverlayBridgeVirtualCircuitPair(int maximumQueuedBytes)
    {
        First = new OverlayBridgeVirtualCircuitStream(maximumQueuedBytes);
        Second = new OverlayBridgeVirtualCircuitStream(maximumQueuedBytes);
        First.ConnectTo(Second);
        Second.ConnectTo(First);
    }

    /// <summary>
    /// Gets the first endpoint. Endpoint labels intentionally carry no publisher/viewer
    /// semantics; callers assign those roles for the lifetime of the circuit.
    /// </summary>
    public OverlayBridgeVirtualCircuitStream First { get; }

    /// <summary>Gets the second endpoint of this two-party circuit.</summary>
    public OverlayBridgeVirtualCircuitStream Second { get; }

    public void Dispose()
    {
        First.Dispose();
        Second.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await First.DisposeAsync().ConfigureAwait(false);
        await Second.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// A bounded, ordered, full-duplex <see cref="Stream"/> endpoint. Each write is copied into
/// the peer's bounded unread-byte queue before it returns, so callers may safely reuse their
/// source buffer. The queue capacity is a hard safety boundary: a write that would exceed it
/// fails with <see cref="IOException"/> rather than retaining unbounded telemetry or TLS data.
/// </summary>
public sealed class OverlayBridgeVirtualCircuitStream : Stream
{
    /// <summary>
    /// Default unread-byte allowance for each direction. It reserves enough headroom for one
    /// maximum declared Bridge frame plus protected-record overhead, while still keeping a
    /// stalled peer from consuming unbounded process memory. A future relay host must retain
    /// this one-frame invariant when it chooses a different backpressure policy.
    /// </summary>
    public const int DefaultMaximumQueuedBytes = 160 * 1024;

    private readonly InboundBuffer _inbound;
    private OverlayBridgeVirtualCircuitStream? _peer;
    private int _disposed;

    private OverlayBridgeVirtualCircuitStream(int maximumQueuedBytes)
    {
        _inbound = new InboundBuffer(maximumQueuedBytes);
    }

    /// <summary>
    /// Creates exactly two connected endpoints. There is no listener, fan-out, or implicit
    /// third party; a relay is responsible for creating one pair for each authorized viewer.
    /// </summary>
    /// <param name="maximumQueuedBytes">
    /// Maximum unread bytes allowed in either direction. A single write must fit in this bound.
    /// </param>
    public static OverlayBridgeVirtualCircuitPair CreatePair(
        int maximumQueuedBytes = DefaultMaximumQueuedBytes)
    {
        if (maximumQueuedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumQueuedBytes),
                maximumQueuedBytes,
                "The queued byte bound must be greater than zero.");
        }

        return new OverlayBridgeVirtualCircuitPair(maximumQueuedBytes);
    }

    /// <summary>Gets the maximum unread bytes permitted for this endpoint's inbound direction.</summary>
    public int MaximumQueuedBytes => _inbound.MaximumQueuedBytes;

    /// <summary>Gets the current unread bytes retained for this endpoint.</summary>
    public int QueuedBytes => _inbound.QueuedBytes;

    public override bool CanRead => Volatile.Read(ref _disposed) == 0;

    public override bool CanSeek => false;

    public override bool CanWrite => Volatile.Read(ref _disposed) == 0;

    public override bool CanTimeout => false;

    public override long Length => throw new NotSupportedException("A virtual circuit is not seekable.");

    public override long Position
    {
        get => throw new NotSupportedException("A virtual circuit is not seekable.");
        set => throw new NotSupportedException("A virtual circuit is not seekable.");
    }

    public override void Flush()
    {
        ThrowIfDisposed();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (buffer.IsEmpty)
        {
            return 0;
        }

        while (true)
        {
            var read = _inbound.TryRead(buffer.Span, out var bytesRead, out var readableTask);
            if (read == InboundReadResult.Data)
            {
                return bytesRead;
            }

            if (read == InboundReadResult.EndOfStream)
            {
                return 0;
            }

            await readableTask!.WaitAsync(cancellationToken).ConfigureAwait(false);
            ThrowIfDisposed();
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        WriteCore(buffer.AsMemory(offset, count), CancellationToken.None);
    }

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);

        try
        {
            WriteCore(buffer.AsMemory(offset, count), cancellationToken);
            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            return Task.FromException(exception);
        }
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        try
        {
            WriteCore(buffer, cancellationToken);
            return ValueTask.CompletedTask;
        }
        catch (OperationCanceledException exception)
        {
            return ValueTask.FromCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            return ValueTask.FromException(exception);
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException("A virtual circuit is not seekable.");
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException("A virtual circuit has no length.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            // Closing this endpoint stops a local read immediately and presents EOF to the
            // peer after bytes already accepted for its inbound direction have been drained.
            _inbound.CloseReader();
            _peer?.CloseWriter();
        }

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void ConnectTo(OverlayBridgeVirtualCircuitStream peer)
    {
        ArgumentNullException.ThrowIfNull(peer);

        if (Interlocked.CompareExchange(ref _peer, peer, null) is not null)
        {
            throw new InvalidOperationException("A virtual circuit endpoint can have only one peer.");
        }
    }

    private void CloseWriter()
    {
        _inbound.CloseWriter();
    }

    private void WriteCore(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (buffer.IsEmpty)
        {
            return;
        }

        var peer = _peer ?? throw new InvalidOperationException("The virtual circuit endpoint is not connected.");
        peer._inbound.Write(buffer);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private static void ValidateBufferArguments(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (offset < 0 || offset > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (count < 0 || count > buffer.Length - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
    }

    private enum InboundReadResult
    {
        Waiting = 0,
        Data = 1,
        EndOfStream = 2
    }

    private sealed class InboundBuffer
    {
        private readonly object _gate = new();
        private readonly Queue<BufferedSegment> _segments = new();
        private TaskCompletionSource<bool> _readableSignal = CreateReadableSignal();
        private int _queuedBytes;
        private bool _writerClosed;
        private bool _readerClosed;

        public InboundBuffer(int maximumQueuedBytes)
        {
            MaximumQueuedBytes = maximumQueuedBytes;
        }

        public int MaximumQueuedBytes { get; }

        public int QueuedBytes
        {
            get
            {
                lock (_gate)
                {
                    return _queuedBytes;
                }
            }
        }

        public void Write(ReadOnlyMemory<byte> source)
        {
            byte[] copy;
            lock (_gate)
            {
                if (_readerClosed)
                {
                    throw new IOException("The remote virtual circuit endpoint is closed.");
                }

                if (_writerClosed)
                {
                    throw new IOException("This virtual circuit direction is closed.");
                }

                if (source.Length > MaximumQueuedBytes - _queuedBytes)
                {
                    throw new IOException(
                        $"The remote virtual circuit queue is full (maximum {MaximumQueuedBytes} unread bytes).");
                }

                copy = source.ToArray();
                _segments.Enqueue(new BufferedSegment(copy));
                _queuedBytes += copy.Length;
                _readableSignal.TrySetResult(true);
            }
        }

        public InboundReadResult TryRead(
            Span<byte> destination,
            out int bytesRead,
            out Task? readableTask)
        {
            lock (_gate)
            {
                bytesRead = 0;
                readableTask = null;

                if (_segments.Count > 0)
                {
                    var segment = _segments.Peek();
                    bytesRead = Math.Min(destination.Length, segment.Remaining);
                    segment.Buffer.AsSpan(segment.Offset, bytesRead).CopyTo(destination);
                    segment.Offset += bytesRead;
                    _queuedBytes -= bytesRead;

                    if (segment.Remaining == 0)
                    {
                        _segments.Dequeue();
                    }

                    ResetReadableSignalWhenEmpty();
                    return InboundReadResult.Data;
                }

                if (_writerClosed || _readerClosed)
                {
                    return InboundReadResult.EndOfStream;
                }

                readableTask = _readableSignal.Task;
                return InboundReadResult.Waiting;
            }
        }

        public void CloseWriter()
        {
            lock (_gate)
            {
                if (_writerClosed)
                {
                    return;
                }

                _writerClosed = true;
                _readableSignal.TrySetResult(true);
            }
        }

        public void CloseReader()
        {
            lock (_gate)
            {
                if (_readerClosed)
                {
                    return;
                }

                _readerClosed = true;
                _segments.Clear();
                _queuedBytes = 0;
                _readableSignal.TrySetResult(true);
            }
        }

        private void ResetReadableSignalWhenEmpty()
        {
            if (_segments.Count == 0
                && !_writerClosed
                && !_readerClosed
                && _readableSignal.Task.IsCompleted)
            {
                _readableSignal = CreateReadableSignal();
            }
        }

        private static TaskCompletionSource<bool> CreateReadableSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class BufferedSegment
        {
            public BufferedSegment(byte[] buffer)
            {
                Buffer = buffer;
            }

            public byte[] Buffer { get; }

            public int Offset { get; set; }

            public int Remaining => Buffer.Length - Offset;
        }
    }
}
