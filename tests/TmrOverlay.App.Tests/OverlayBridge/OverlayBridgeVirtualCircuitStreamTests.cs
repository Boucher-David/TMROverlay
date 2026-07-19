using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using TmrOverlay.Core.OverlayBridge;
using Xunit;

namespace TmrOverlay.App.Tests.OverlayBridge;

public sealed class OverlayBridgeVirtualCircuitStreamTests
{
    [Fact]
    public async Task DuplexPair_PreservesIndependentByteOrderInBothDirections()
    {
        using var pair = OverlayBridgeVirtualCircuitStream.CreatePair(maximumQueuedBytes: 64);
        var fromFirst = Encoding.UTF8.GetBytes("first-to-second-in-order");
        var fromSecond = Encoding.UTF8.GetBytes("second-to-first-in-order");

        await Task.WhenAll(
            WriteInTwoSegmentsAsync(pair.First, fromFirst),
            WriteInTwoSegmentsAsync(pair.Second, fromSecond));

        var receivedByFirst = await ReadExactlyAsync(pair.First, fromSecond.Length);
        var receivedBySecond = await ReadExactlyAsync(pair.Second, fromFirst.Length);

        Assert.Equal(fromSecond, receivedByFirst);
        Assert.Equal(fromFirst, receivedBySecond);
        Assert.Equal(0, pair.First.QueuedBytes);
        Assert.Equal(0, pair.Second.QueuedBytes);
    }

    [Fact]
    public async Task Write_WhenPeerUnreadQueueWouldExceedBound_FailsWithoutChangingQueuedBytes()
    {
        using var pair = OverlayBridgeVirtualCircuitStream.CreatePair(maximumQueuedBytes: 4);

        await pair.First.WriteAsync(new byte[] { 1, 2, 3, 4 });
        var overflow = await Assert.ThrowsAsync<IOException>(
            async () => await pair.First.WriteAsync(new byte[] { 5 }));

        Assert.Contains("full", overflow.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4, pair.Second.QueuedBytes);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await ReadExactlyAsync(pair.Second, 4));

        await pair.First.WriteAsync(new byte[] { 5 });
        Assert.Equal(new byte[] { 5 }, await ReadExactlyAsync(pair.Second, 1));
    }

    [Fact]
    public async Task DefaultQueue_AcceptsOneMaximumDeclaredBridgeFrameBeforeBackpressure()
    {
        using var pair = OverlayBridgeVirtualCircuitStream.CreatePair();
        var maximumFrame = new byte[OverlayBridgeFactContracts.MaxDeclaredPayloadBytes];

        await pair.First.WriteAsync(maximumFrame);

        Assert.Equal(maximumFrame.Length, pair.Second.QueuedBytes);
        Assert.Equal(maximumFrame, await ReadExactlyAsync(pair.Second, maximumFrame.Length));
    }

    [Fact]
    public async Task Dispose_DrainsAlreadyQueuedPeerBytesThenSignalsEndOfStreamAndRejectsReverseWrites()
    {
        using var pair = OverlayBridgeVirtualCircuitStream.CreatePair(maximumQueuedBytes: 32);
        await pair.First.WriteAsync(new byte[] { 8, 9, 10 });

        pair.First.Dispose();

        Assert.Equal(new byte[] { 8, 9, 10 }, await ReadExactlyAsync(pair.Second, 3));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var endOfStream = await pair.Second.ReadAsync(new byte[1], timeout.Token);

        Assert.Equal(0, endOfStream);
        await Assert.ThrowsAsync<IOException>(
            async () => await pair.Second.WriteAsync(new byte[] { 1 }));
        Assert.False(pair.First.CanRead);
        Assert.False(pair.First.CanWrite);
    }

    [Fact]
    public async Task ReadAsync_HonorsCancellationWhileNoPeerBytesAreAvailable()
    {
        using var pair = OverlayBridgeVirtualCircuitStream.CreatePair();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await pair.First.ReadAsync(new byte[1], cancellation.Token));
    }

    [Fact]
    public async Task DuplexPair_CarriesLoopbackMutualTlsWithoutANetworkListener()
    {
        using var pair = OverlayBridgeVirtualCircuitStream.CreatePair(maximumQueuedBytes: 128 * 1024);
        using var clientCertificate = CreateCertificate("bridge-client", "1.3.6.1.5.5.7.3.2");
        using var serverCertificate = CreateCertificate("bridge-server", "1.3.6.1.5.5.7.3.1");
        using var client = new SslStream(
            pair.First,
            leaveInnerStreamOpen: true,
            (_, certificate, _, _) => HasSameThumbprint(certificate, serverCertificate));
        using var server = new SslStream(
            pair.Second,
            leaveInnerStreamOpen: true,
            (_, certificate, _, _) => HasSameThumbprint(certificate, clientCertificate));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await Task.WhenAll(
            client.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = "overlay-bridge.test",
                    ClientCertificates = new X509CertificateCollection(clientCertificate),
                    EnabledSslProtocols = SslProtocols.Tls12,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                },
                timeout.Token),
            server.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions
                {
                    ServerCertificate = serverCertificate,
                    ClientCertificateRequired = true,
                    EnabledSslProtocols = SslProtocols.Tls12,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                },
                timeout.Token));

        var expected = Encoding.UTF8.GetBytes("protected bridge bytes");
        await client.WriteAsync(expected, timeout.Token);
        await client.FlushAsync(timeout.Token);

        Assert.Equal(expected, await ReadExactlyAsync(server, expected.Length, timeout.Token));
    }

    private static async Task<byte[]> ReadExactlyAsync(
        Stream stream,
        int length,
        CancellationToken cancellationToken = default)
    {
        var buffer = new byte[length];
        var written = 0;

        while (written < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(written), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("The circuit closed before all expected bytes were received.");
            }

            written += read;
        }

        return buffer;
    }

    private static async Task WriteInTwoSegmentsAsync(Stream stream, byte[] bytes)
    {
        var split = bytes.Length / 2;
        await stream.WriteAsync(bytes.AsMemory(0, split));
        await stream.WriteAsync(bytes.AsMemory(split));
    }

    private static X509Certificate2 CreateCertificate(
        string subjectName,
        string enhancedKeyUsageOid)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            $"CN={subjectName}",
            key,
            HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid(enhancedKeyUsageOid) },
            critical: false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
    }

    private static bool HasSameThumbprint(X509Certificate? presented, X509Certificate2 expected)
    {
        return presented is not null
            && string.Equals(
                presented.GetCertHashString(),
                expected.GetCertHashString(),
                StringComparison.OrdinalIgnoreCase);
    }
}
