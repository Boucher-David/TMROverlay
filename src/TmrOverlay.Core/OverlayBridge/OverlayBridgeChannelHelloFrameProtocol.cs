using System.Buffers.Binary;

namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// Bounded length-prefix framing for the first protected-channel control record. This is separate
/// from sector-publication framing so a host must complete Hello admission before it can read a
/// fact frame. The stream is supplied by the caller and is expected to be an already established
/// mutually authenticated <c>SslStream</c> virtual circuit.
/// </summary>
internal static class OverlayBridgeChannelHelloFrameProtocol
{
    private const int LengthPrefixBytes = sizeof(int);

    public static async Task WriteAsync(
        Stream stream,
        OverlayBridgeChannelHello hello,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(hello);

        var payload = OverlayBridgeChannelHelloCborCodec.Encode(hello);
        var prefix = new byte[LengthPrefixBytes];
        BinaryPrimitives.WriteInt32BigEndian(prefix, payload.Length);
        await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<OverlayBridgeChannelHelloFrameReadResult> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var prefix = new byte[LengthPrefixBytes];
        var prefixStatus = await ReadExactlyAsync(
                stream,
                prefix,
                allowEndOfStreamBeforeAnyBytes: true,
                cancellationToken)
            .ConfigureAwait(false);
        if (prefixStatus == OverlayBridgeChannelHelloFrameReadStatus.EndOfStream)
        {
            return OverlayBridgeChannelHelloFrameReadResult.EndOfStream();
        }

        if (prefixStatus != OverlayBridgeChannelHelloFrameReadStatus.Hello)
        {
            return OverlayBridgeChannelHelloFrameReadResult.Truncated();
        }

        var declaredLength = BinaryPrimitives.ReadInt32BigEndian(prefix);
        if (declaredLength <= 0)
        {
            return OverlayBridgeChannelHelloFrameReadResult.InvalidLength(declaredLength);
        }

        if (declaredLength > OverlayBridgeChannelHelloContracts.MaximumEncodedBytes)
        {
            return OverlayBridgeChannelHelloFrameReadResult.Oversized(declaredLength);
        }

        var payload = new byte[declaredLength];
        var payloadStatus = await ReadExactlyAsync(
                stream,
                payload,
                allowEndOfStreamBeforeAnyBytes: false,
                cancellationToken)
            .ConfigureAwait(false);
        if (payloadStatus != OverlayBridgeChannelHelloFrameReadStatus.Hello)
        {
            return OverlayBridgeChannelHelloFrameReadResult.Truncated();
        }

        return OverlayBridgeChannelHelloCborCodec.TryDecode(payload, out var hello, out var decodeError)
            ? OverlayBridgeChannelHelloFrameReadResult.Decoded(hello!, declaredLength)
            : OverlayBridgeChannelHelloFrameReadResult.DecodeRejected(decodeError);
    }

    private static async Task<OverlayBridgeChannelHelloFrameReadStatus> ReadExactlyAsync(
        Stream stream,
        Memory<byte> destination,
        bool allowEndOfStreamBeforeAnyBytes,
        CancellationToken cancellationToken)
    {
        var written = 0;
        while (written < destination.Length)
        {
            var read = await stream.ReadAsync(destination[written..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return written == 0 && allowEndOfStreamBeforeAnyBytes
                    ? OverlayBridgeChannelHelloFrameReadStatus.EndOfStream
                    : OverlayBridgeChannelHelloFrameReadStatus.Truncated;
            }

            written += read;
        }

        return OverlayBridgeChannelHelloFrameReadStatus.Hello;
    }
}

/// <summary>
/// One bounded post-TLS Hello read. Only <see cref="Hello"/> with <see cref="IsDecoded"/> true
/// may be supplied to the expected-binding validator.
/// </summary>
internal sealed record OverlayBridgeChannelHelloFrameReadResult(
    OverlayBridgeChannelHelloFrameReadStatus Status,
    OverlayBridgeChannelHello? Hello,
    OverlayBridgeChannelHelloDecodeError DecodeError,
    int? DeclaredLength)
{
    public bool IsDecoded => Status == OverlayBridgeChannelHelloFrameReadStatus.Hello
        && Hello is not null
        && DecodeError == OverlayBridgeChannelHelloDecodeError.None;

    public static OverlayBridgeChannelHelloFrameReadResult Decoded(
        OverlayBridgeChannelHello hello,
        int declaredLength) => new(
        OverlayBridgeChannelHelloFrameReadStatus.Hello,
        hello,
        OverlayBridgeChannelHelloDecodeError.None,
        declaredLength);

    public static OverlayBridgeChannelHelloFrameReadResult EndOfStream() => new(
        OverlayBridgeChannelHelloFrameReadStatus.EndOfStream,
        Hello: null,
        OverlayBridgeChannelHelloDecodeError.None,
        DeclaredLength: null);

    public static OverlayBridgeChannelHelloFrameReadResult Truncated() => new(
        OverlayBridgeChannelHelloFrameReadStatus.Truncated,
        Hello: null,
        OverlayBridgeChannelHelloDecodeError.None,
        DeclaredLength: null);

    public static OverlayBridgeChannelHelloFrameReadResult InvalidLength(int declaredLength) => new(
        OverlayBridgeChannelHelloFrameReadStatus.InvalidLength,
        Hello: null,
        OverlayBridgeChannelHelloDecodeError.None,
        declaredLength);

    public static OverlayBridgeChannelHelloFrameReadResult Oversized(int declaredLength) => new(
        OverlayBridgeChannelHelloFrameReadStatus.Oversized,
        Hello: null,
        OverlayBridgeChannelHelloDecodeError.OversizedPayload,
        declaredLength);

    public static OverlayBridgeChannelHelloFrameReadResult DecodeRejected(
        OverlayBridgeChannelHelloDecodeError error) => new(
        OverlayBridgeChannelHelloFrameReadStatus.DecodeRejected,
        Hello: null,
        error,
        DeclaredLength: null);
}

internal enum OverlayBridgeChannelHelloFrameReadStatus
{
    Hello = 0,
    EndOfStream = 1,
    Truncated = 2,
    InvalidLength = 3,
    Oversized = 4,
    DecodeRejected = 5
}

/// <summary>
/// Sends the local Hello and accepts exactly one matching peer Hello. Both endpoints invoke this
/// method concurrently over their independent full-duplex TLS streams. A failure gives callers a
/// safe category and never opens a fact-frame admission path.
/// </summary>
internal static class OverlayBridgeChannelHelloHandshake
{
    public static async Task<OverlayBridgeChannelHelloHandshakeResult> ExchangeAsync(
        Stream stream,
        OverlayBridgeChannelHelloExpectedBinding expected,
        OverlayBridgeChannelNonceReplayCache? nonceReplayCache = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(expected);

        if (!expected.TryValidate(out _))
        {
            return OverlayBridgeChannelHelloHandshakeResult.Rejected(
                OverlayBridgeChannelHelloHandshakeError.InvalidExpectedBinding);
        }

        try
        {
            await OverlayBridgeChannelHelloFrameProtocol.WriteAsync(
                    stream,
                    expected.CreateLocalHello(),
                    cancellationToken)
                .ConfigureAwait(false);
            var frame = await OverlayBridgeChannelHelloFrameProtocol.ReadAsync(stream, cancellationToken)
                .ConfigureAwait(false);
            if (!frame.IsDecoded)
            {
                return OverlayBridgeChannelHelloHandshakeResult.Rejected(
                    OverlayBridgeChannelHelloHandshakeError.FrameRejected);
            }

            var validation = new OverlayBridgeChannelHelloValidator(expected, nonceReplayCache)
                .ValidatePeer(frame.Hello);
            return validation.IsAccepted
                ? OverlayBridgeChannelHelloHandshakeResult.Accepted()
                : OverlayBridgeChannelHelloHandshakeResult.Rejected(
                    OverlayBridgeChannelHelloHandshakeError.BindingRejected);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException)
        {
            return OverlayBridgeChannelHelloHandshakeResult.Rejected(
                OverlayBridgeChannelHelloHandshakeError.TransportFailure);
        }
        catch (InvalidOperationException)
        {
            return OverlayBridgeChannelHelloHandshakeResult.Rejected(
                OverlayBridgeChannelHelloHandshakeError.TransportFailure);
        }
    }
}

internal sealed record OverlayBridgeChannelHelloHandshakeResult(
    bool IsAccepted,
    OverlayBridgeChannelHelloHandshakeError Error)
{
    public static OverlayBridgeChannelHelloHandshakeResult Accepted() => new(
        IsAccepted: true,
        OverlayBridgeChannelHelloHandshakeError.None);

    public static OverlayBridgeChannelHelloHandshakeResult Rejected(
        OverlayBridgeChannelHelloHandshakeError error) => new(
        IsAccepted: false,
        error);
}

internal enum OverlayBridgeChannelHelloHandshakeError
{
    None = 0,
    InvalidExpectedBinding = 1,
    FrameRejected = 2,
    BindingRejected = 3,
    TransportFailure = 4
}
