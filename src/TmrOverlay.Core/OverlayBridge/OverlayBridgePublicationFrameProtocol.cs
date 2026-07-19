using System.Buffers.Binary;

namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// Bounded length-prefix framing for one canonical Overlay Bridge CBOR publication. This is a
/// transport-neutral byte boundary: callers supply an already protected <see cref="Stream"/>,
/// such as a future end-to-end TLS circuit. It deliberately does not open a socket, negotiate
/// pairing, authenticate a peer, or retain receiver state.
/// </summary>
internal static class OverlayBridgePublicationFrameProtocol
{
    private const int LengthPrefixBytes = sizeof(int);

    /// <summary>
    /// Canonically encodes and writes one complete publication. The codec owns the payload's
    /// declared-length convergence; the outer prefix only delimits the protected byte stream.
    /// </summary>
    public static async Task WriteAsync(
        Stream stream,
        OverlayBridgeSectorPublication publication,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(publication);

        var payload = OverlayBridgeCborCodec.Encode(publication);
        var lengthPrefix = new byte[LengthPrefixBytes];
        BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, payload.Length);

        await stream.WriteAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads exactly one bounded frame and decodes it before it can reach admission state. A
    /// malformed, truncated, or oversized frame is a transport/session concern for the caller;
    /// this method never converts one into a retained fact or an admission command.
    /// </summary>
    public static async Task<OverlayBridgePublicationFrameReadResult> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var lengthPrefix = new byte[LengthPrefixBytes];
        var prefixRead = await ReadExactlyAsync(
                stream,
                lengthPrefix,
                allowEndOfStreamBeforeAnyBytes: true,
                cancellationToken)
            .ConfigureAwait(false);
        if (prefixRead == OverlayBridgeFrameReadStatus.EndOfStream)
        {
            return OverlayBridgePublicationFrameReadResult.EndOfStream();
        }

        if (prefixRead != OverlayBridgeFrameReadStatus.Publication)
        {
            return OverlayBridgePublicationFrameReadResult.Truncated();
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(lengthPrefix);
        if (length <= 0)
        {
            return OverlayBridgePublicationFrameReadResult.InvalidLength(length);
        }

        if (length > OverlayBridgeFactContracts.MaxDeclaredPayloadBytes)
        {
            return OverlayBridgePublicationFrameReadResult.Oversized(length);
        }

        var payload = new byte[length];
        var payloadRead = await ReadExactlyAsync(
                stream,
                payload,
                allowEndOfStreamBeforeAnyBytes: false,
                cancellationToken)
            .ConfigureAwait(false);
        if (payloadRead != OverlayBridgeFrameReadStatus.Publication)
        {
            return OverlayBridgePublicationFrameReadResult.Truncated();
        }

        if (!OverlayBridgeCborCodec.TryDecode(payload, out var publication, out var decodeError))
        {
            return OverlayBridgePublicationFrameReadResult.DecodeRejected(decodeError);
        }

        return OverlayBridgePublicationFrameReadResult.Decoded(publication!);
    }

    private static async Task<OverlayBridgeFrameReadStatus> ReadExactlyAsync(
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
                    ? OverlayBridgeFrameReadStatus.EndOfStream
                    : OverlayBridgeFrameReadStatus.Truncated;
            }

            written += read;
        }

        return OverlayBridgeFrameReadStatus.Publication;
    }
}

/// <summary>
/// The terminal result for one outer-frame read. Only <see cref="Publication"/> contains facts;
/// hosts must not call receiver admission for any other status.
/// </summary>
internal sealed record OverlayBridgePublicationFrameReadResult(
    OverlayBridgeFrameReadStatus Status,
    OverlayBridgeSectorPublication? Publication,
    OverlayBridgeCborDecodeError DecodeError,
    int? DeclaredLength)
{
    public bool IsDecoded => Status == OverlayBridgeFrameReadStatus.Publication
        && Publication is not null
        && DecodeError == OverlayBridgeCborDecodeError.None;

    public static OverlayBridgePublicationFrameReadResult Decoded(OverlayBridgeSectorPublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        return new OverlayBridgePublicationFrameReadResult(
            OverlayBridgeFrameReadStatus.Publication,
            publication,
            OverlayBridgeCborDecodeError.None,
            publication.Header.DeclaredPayloadBytes);
    }

    public static OverlayBridgePublicationFrameReadResult EndOfStream() => new(
        OverlayBridgeFrameReadStatus.EndOfStream,
        Publication: null,
        OverlayBridgeCborDecodeError.None,
        DeclaredLength: null);

    public static OverlayBridgePublicationFrameReadResult Truncated() => new(
        OverlayBridgeFrameReadStatus.Truncated,
        Publication: null,
        OverlayBridgeCborDecodeError.None,
        DeclaredLength: null);

    public static OverlayBridgePublicationFrameReadResult InvalidLength(int declaredLength) => new(
        OverlayBridgeFrameReadStatus.InvalidLength,
        Publication: null,
        OverlayBridgeCborDecodeError.None,
        declaredLength);

    public static OverlayBridgePublicationFrameReadResult Oversized(int declaredLength) => new(
        OverlayBridgeFrameReadStatus.Oversized,
        Publication: null,
        OverlayBridgeCborDecodeError.OversizedPayload,
        declaredLength);

    public static OverlayBridgePublicationFrameReadResult DecodeRejected(OverlayBridgeCborDecodeError error) => new(
        OverlayBridgeFrameReadStatus.DecodeRejected,
        Publication: null,
        error,
        DeclaredLength: null);
}

internal enum OverlayBridgeFrameReadStatus
{
    Publication = 0,
    EndOfStream = 1,
    Truncated = 2,
    InvalidLength = 3,
    Oversized = 4,
    DecodeRejected = 5
}
