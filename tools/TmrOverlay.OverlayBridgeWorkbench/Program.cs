using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TmrOverlay.Core.OverlayBridge;

namespace TmrOverlay.OverlayBridgeWorkbench;

/// <summary>
/// Developer-only local proof host for a synthetic Overlay Bridge producer and receiver.
/// It is deliberately not referenced by the Windows application, LocalhostOverlays, OBS assets,
/// release solution, or any production transport path.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (!OverlayBridgeWorkbenchOptions.TryParse(args, out var options, out var error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine(OverlayBridgeWorkbenchOptions.Usage);
            return 2;
        }

        if (options.SelfTest)
        {
            return await OverlayBridgeWorkbenchSelfTest.RunAsync().ConfigureAwait(false) ? 0 : 1;
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(),
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = Environments.Production
        });
        // This developer executable owns a fixed, synthetic configuration. Do not let ambient
        // appsettings, user-secrets, environment URL values, command-line values, or a hosting
        // startup assembly supply a listener, source, or service into its proof boundary.
        builder.Configuration.Sources.Clear();
        builder.WebHost.UseSetting(WebHostDefaults.PreventHostingStartupKey, bool.TrueString);
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            // Do not substitute ListenAnyIP/UseUrls here. This tool must never be reachable from
            // a LAN or act as a relay endpoint, even while a developer is experimenting locally.
            kestrel.Listen(IPAddress.Loopback, options.Port, listen =>
            {
                listen.Protocols = HttpProtocols.Http1;
            });
        });
        builder.Services.AddSingleton<OverlayBridgeWorkbenchRuntime>();

        await using var app = builder.Build();
        OverlayBridgeWorkbenchHost.Map(app);

        Console.WriteLine($"Overlay Bridge developer workbench listening only on http://127.0.0.1:{options.Port}");
        Console.WriteLine($"Producer: http://127.0.0.1:{options.Port}{OverlayBridgeWorkbenchHost.ProducerPath}");
        Console.WriteLine($"Receiver: http://127.0.0.1:{options.Port}{OverlayBridgeWorkbenchHost.ReceiverPath}");
        Console.WriteLine("Synthetic fixtures only; no iRacing telemetry, persistence, Oracle, pairing, invites, or secrets.");

        await app.RunAsync().ConfigureAwait(false);
        return 0;
    }
}

/// <summary>
/// Explicit command-line allowlist. There is intentionally no host/address argument.
/// </summary>
internal sealed record OverlayBridgeWorkbenchOptions(int Port, bool SelfTest)
{
    public const int DefaultPort = 51931;

    public const string Usage =
        "Usage: dotnet run --project tools/TmrOverlay.OverlayBridgeWorkbench -- [--port 51931] [--self-test]";

    public static bool TryParse(
        IReadOnlyList<string> args,
        out OverlayBridgeWorkbenchOptions options,
        out string? error)
    {
        var port = DefaultPort;
        var selfTest = false;

        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--self-test":
                    selfTest = true;
                    break;
                case "--port" when index + 1 < args.Count
                    && int.TryParse(args[++index], out var parsedPort)
                    && parsedPort is >= 1024 and <= 65535:
                    port = parsedPort;
                    break;
                case "--port":
                    options = default!;
                    error = "--port must be an integer from 1024 through 65535.";
                    return false;
                default:
                    options = default!;
                    error = $"Unsupported argument: {args[index]}";
                    return false;
            }
        }

        options = new OverlayBridgeWorkbenchOptions(port, selfTest);
        error = null;
        return true;
    }
}

/// <summary>
/// Routes intentionally use a private developer namespace instead of the application's
/// <c>/overlays</c> localhost/OBS namespace.
/// </summary>
internal static class OverlayBridgeWorkbenchHost
{
    public const string ProducerPath = "/developer/bridge/producer";
    public const string ReceiverPath = "/developer/bridge/receiver";
    public const string HealthPath = "/developer/bridge/health";
    private const string StatePath = "/developer/bridge/api/state";
    private const string PublishPath = "/developer/bridge/api/publish";
    private const string DirectLocalPrecedencePath = "/developer/bridge/api/direct-local-precedence";

    public static void Map(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; connect-src 'self'; img-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";
            await next().ConfigureAwait(false);
        });

        app.MapGet("/", () => Results.Redirect(ReceiverPath, permanent: false));
        app.MapGet(ProducerPath, () => Results.Content(ProducerPage(), "text/html", Encoding.UTF8));
        app.MapGet(ReceiverPath, () => Results.Content(ReceiverPage(), "text/html", Encoding.UTF8));
        app.MapGet(HealthPath, (OverlayBridgeWorkbenchRuntime runtime) => Results.Json(runtime.Health()));
        app.MapGet(StatePath, async (
            OverlayBridgeWorkbenchRuntime runtime,
            CancellationToken cancellationToken) =>
        {
            var state = await runtime.SnapshotAsync(cancellationToken).ConfigureAwait(false);
            return Results.Json(state);
        });
        app.MapPost(PublishPath, async (
            OverlayBridgeWorkbenchRuntime runtime,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!IsLoopbackRequest(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var result = await runtime.PublishSyntheticSectorAsync(cancellationToken).ConfigureAwait(false);
            return Results.Json(result);
        });
        app.MapPost(DirectLocalPrecedencePath, async (
            OverlayBridgeWorkbenchRuntime runtime,
            HttpContext context,
            OverlayBridgeWorkbenchDirectLocalRequest? request,
            CancellationToken cancellationToken) =>
        {
            if (!IsLoopbackRequest(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var result = await runtime.ApplyDirectLocalPrecedenceAsync(
                    request?.Active == true,
                    cancellationToken)
                .ConfigureAwait(false);
            return Results.Json(result);
        });
    }

    private static bool IsLoopbackRequest(HttpContext context)
    {
        // Kestrel itself binds only IPAddress.Loopback. The request check makes that boundary
        // fail closed if the hosting code is ever refactored incorrectly.
        return context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address);
    }

    private static string ProducerPage() => """
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><title>Overlay Bridge — synthetic producer</title>
        <style>body{font-family:system-ui,sans-serif;background:#10141b;color:#e9eef7;margin:2rem;max-width:850px}button{background:#00a878;color:#07150f;border:0;border-radius:.3rem;padding:.7rem 1rem;font-weight:700;cursor:pointer}button+button{margin-left:.6rem;background:#285ea8}code,pre{background:#1b2431;padding:.7rem;border-radius:.3rem;display:block;overflow:auto}.warn{color:#ffcf70}.ok{color:#83e5bc}</style></head>
        <body><h1>Synthetic producer</h1><p class="warn">Developer-only. This page never reads iRacing, captures, history, settings, secrets, invites, or Oracle.</p>
        <p>Clicking publish asks the loopback host to build a synthetic sector publication, send it through an ephemeral virtual-circuit mTLS channel, frame/decode it with Core, and admit it into the separate synthetic receiver.</p>
        <button id="publish">Publish synthetic sector</button><a href="/developer/bridge/receiver"><button type="button">Open receiver</button></a>
        <h2>Latest result</h2><pre id="state">Loading…</pre>
        <script>const out=document.querySelector('#state');async function show(){const r=await fetch('/developer/bridge/api/state',{cache:'no-store'});out.textContent=JSON.stringify(await r.json(),null,2)}document.querySelector('#publish').addEventListener('click',async()=>{const r=await fetch('/developer/bridge/api/publish',{method:'POST'});out.textContent=JSON.stringify(await r.json(),null,2)});show();setInterval(show,1000);</script>
        </body></html>
        """;

    private static string ReceiverPage() => """
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><title>Overlay Bridge — synthetic receiver</title>
        <style>body{font-family:system-ui,sans-serif;background:#10141b;color:#e9eef7;margin:2rem;max-width:850px}button{background:#285ea8;color:#eef5ff;border:0;border-radius:.3rem;padding:.7rem 1rem;font-weight:700;cursor:pointer}button+button{margin-left:.6rem;background:#8b303b}code,pre{background:#1b2431;padding:.7rem;border-radius:.3rem;display:block;overflow:auto}.warn{color:#ffcf70}</style></head>
        <body><h1>Synthetic receiver</h1><p class="warn">This is not an overlay and not an OBS/localhost app route. It visualizes only the developer host’s synthetic Core result.</p>
        <p>Current facts are exposed only while the Core composition result is current. Held, expired, or direct-local-precedence results retain no facts for calculation.</p>
        <a href="/developer/bridge/producer"><button type="button">Open producer</button></a><button id="direct">Simulate direct-local precedence</button><button id="clear">Clear direct-local precedence</button>
        <h2>Receiver Core state</h2><pre id="state">Loading…</pre>
        <script>const out=document.querySelector('#state');async function show(){const r=await fetch('/developer/bridge/api/state',{cache:'no-store'});out.textContent=JSON.stringify(await r.json(),null,2)}async function precedence(active){const r=await fetch('/developer/bridge/api/direct-local-precedence',{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify({active})});out.textContent=JSON.stringify(await r.json(),null,2)}document.querySelector('#direct').addEventListener('click',()=>precedence(true));document.querySelector('#clear').addEventListener('click',()=>precedence(false));show();setInterval(show,1000);</script>
        </body></html>
        """;
}

internal sealed record OverlayBridgeWorkbenchDirectLocalRequest(bool Active);

/// <summary>
/// A real Core execution path wrapped in an explicitly synthetic developer fixture. The browser
/// never carries a publication, chooses source priority, or simulates receiver state itself.
/// </summary>
internal sealed class OverlayBridgeWorkbenchRuntime : IAsyncDisposable
{
    private static readonly OverlayBridgeSessionBinding SyntheticSession = new(
        SessionId: "synthetic-session-daytona",
        SessionEpoch: 1,
        TrackKey: "synthetic-track-daytona-road",
        TeamCarKey: "synthetic-team-car-47");
    private static readonly OverlayBridgeActiveTeamCarFreshnessPolicy FreshnessPolicy = new(
        CurrentMaximumAge: TimeSpan.FromSeconds(5),
        HeldMaximumAge: TimeSpan.FromSeconds(20));

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly OverlayBridgeActiveTeamCarReceiverPipeline receiver = new(
        new OverlayBridgeReceiverAdmissionContext(
            RoomId: "synthetic-room-local",
            StreamId: "synthetic-stream-team-car-47",
            ExpectedSession: SyntheticSession,
            HasFreshDirectInCarTelemetry: false),
        FreshnessPolicy);
    private OverlayBridgeWorkbenchTlsCircuit? circuit;
    private OverlayBridgeActiveTeamCarReceiverPipelineResult? lastResult;
    private long nextSequence = 1;
    private DateTimeOffset? startedAtUtc;
    private DateTimeOffset? lastPublishedAtUtc;
    private int lastFrameBytes;
    private string? lastFailure;

    public object Health() => new
    {
        kind = "developer-only-overlay-bridge-workbench",
        bind = "127.0.0.1-only",
        source = "synthetic-contract-fixture",
        pipeline = "virtual-circuit + ephemeral-mutual-tls + Core-frame + Core-admission + Core-composition",
        permittedTlsProtocols = "Tls12|Tls13",
        persistence = "none",
        telemetry = "none",
        secrets = "none",
        oracle = "none",
        obsOrLocalhostOverlayRoute = false,
        ready = lastFailure is null,
        lastFailure
    };

    public async Task<object> SnapshotAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = receiver.Observe(DateTimeOffset.UtcNow);
            lastResult = result;
            return ToSnapshot(result);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<object> PublishSyntheticSectorAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            startedAtUtc ??= DateTimeOffset.UtcNow;
            circuit ??= await OverlayBridgeWorkbenchTlsCircuit.CreateAsync(cancellationToken).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var sequence = nextSequence++;
            var publication = OverlayBridgeWorkbenchSyntheticFixture.CreatePublication(
                sequence,
                now);

            await OverlayBridgePublicationFrameProtocol.WriteAsync(
                    circuit.Producer,
                    publication,
                    cancellationToken)
                .ConfigureAwait(false);
            var frame = await OverlayBridgePublicationFrameProtocol.ReadAsync(
                    circuit.Receiver,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!frame.IsDecoded)
            {
                lastFailure = $"Core frame read failed: {frame.Status} ({frame.DecodeError}).";
                return ToSnapshot(receiver.Observe(DateTimeOffset.UtcNow));
            }

            var observedAtUtc = DateTimeOffset.UtcNow;
            lastResult = receiver.Admit(frame.Publication!, observedAtUtc, observedAtUtc);
            lastPublishedAtUtc = observedAtUtc;
            lastFrameBytes = frame.DeclaredLength ?? 0;
            lastFailure = null;
            return ToSnapshot(lastResult);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            lastFailure = $"Synthetic loopback failure: {exception.GetType().Name}.";
            return ToSnapshot(receiver.Observe(DateTimeOffset.UtcNow));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<object> ApplyDirectLocalPrecedenceAsync(
        bool active,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lastResult = receiver.ApplyLocalDirectPrecedence(active, DateTimeOffset.UtcNow);
            return ToSnapshot(lastResult);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (circuit is not null)
            {
                await circuit.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
            gate.Dispose();
        }
    }

    private object ToSnapshot(OverlayBridgeActiveTeamCarReceiverPipelineResult result)
    {
        var active = result.ActiveTeamCar;
        var publication = active.RemoteProvenance?.Publication;
        return new
        {
            kind = "synthetic-core-loopback-result",
            source = "synthetic-contract-fixture",
            startedAtUtc,
            lastPublishedAtUtc,
            lastFrameBytes,
            negotiatedTlsProtocol = circuit?.Producer.SslProtocol.ToString(),
            nextSyntheticSequence = nextSequence,
            lastFailure,
            receiver = new
            {
                admission = result.Admission?.Outcome.ToString() ?? "Observed",
                availability = active.Availability.ToString(),
                source = active.Source.ToString(),
                unavailableReason = active.UnavailableReason.ToString(),
                terminalReason = active.ReceiverTerminalReason.ToString(),
                receiverObservedAgeMilliseconds = active.Freshness.ReceiverObservedAge?.TotalMilliseconds,
                acceptedPublicationCount = active.Freshness.Cadence.AcceptedPublicationCount,
                publication = publication is null
                    ? null
                    : new
                    {
                        sequence = publication.Sequence,
                        lap = active.RemoteProvenance!.FactGroup.LapNumber,
                        sector = active.RemoteProvenance.FactGroup.SectorNumber
                    },
                // Do not reveal stale/held facts. This mirrors the Core boundary exactly.
                currentFacts = active.IsUsableForCalculation && active.Facts is { } facts
                    ? new
                    {
                        fuelLiters = facts.CurrentFuelLiters,
                        progressLaps = facts.TeamCarProgressLaps,
                        fuelCapacityLiters = facts.FuelCapacity?.PhysicalTankCapacityLiters,
                        cleanBurnSampleCount = facts.CleanBurnEvidence?.AcceptedSampleCount
                    }
                    : null,
                fuelInputAvailable = result.FuelInput.IsAvailable,
                fuelInputUnavailableReason = result.FuelInput.UnavailableReason.ToString()
            },
            limitations = new[]
            {
                "synthetic-only-no-live-telemetry",
                "ephemeral-in-memory-circuit-only",
                "no-listener-pairing-room-invite-or-relay",
                "no-persistence-secrets-or-oracle",
                "not-a-production-transport-or-obs-route"
            }
        };
    }
}

/// <summary>
/// Creates throwaway self-signed identities in memory and pins each endpoint to the peer's exact
/// certificate. Nothing is written to the certificate store, a file, or any user-data path.
/// </summary>
internal sealed class OverlayBridgeWorkbenchTlsCircuit : IAsyncDisposable
{
    private readonly OverlayBridgeVirtualCircuitPair pair;
    private readonly X509Certificate2 producerCertificate;
    private readonly X509Certificate2 receiverCertificate;

    private OverlayBridgeWorkbenchTlsCircuit(
        OverlayBridgeVirtualCircuitPair pair,
        X509Certificate2 producerCertificate,
        X509Certificate2 receiverCertificate,
        SslStream producer,
        SslStream receiver)
    {
        this.pair = pair;
        this.producerCertificate = producerCertificate;
        this.receiverCertificate = receiverCertificate;
        Producer = producer;
        Receiver = receiver;
    }

    public SslStream Producer { get; }

    public SslStream Receiver { get; }

    public static async Task<OverlayBridgeWorkbenchTlsCircuit> CreateAsync(CancellationToken cancellationToken)
    {
        var pair = OverlayBridgeVirtualCircuitStream.CreatePair();
        X509Certificate2? producerCertificate = null;
        X509Certificate2? receiverCertificate = null;
        SslStream? producer = null;
        SslStream? receiver = null;
        try
        {
            // The agreed Bridge protocol makes the active publisher the inner TLS server
            // and each viewer the client. Keep the local proof in that same direction.
            producerCertificate = CreateCertificate("synthetic-bridge-producer", "1.3.6.1.5.5.7.3.1");
            receiverCertificate = CreateCertificate("synthetic-bridge-receiver", "1.3.6.1.5.5.7.3.2");
            producer = new SslStream(
                pair.First,
                leaveInnerStreamOpen: true,
                (_, certificate, _, _) => HasSameThumbprint(certificate, receiverCertificate));
            receiver = new SslStream(
                pair.Second,
                leaveInnerStreamOpen: true,
                (_, certificate, _, _) => HasSameThumbprint(certificate, producerCertificate));

            await Task.WhenAll(
                    producer.AuthenticateAsServerAsync(
                        new SslServerAuthenticationOptions
                        {
                            ServerCertificate = producerCertificate,
                            ClientCertificateRequired = true,
                            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                            AllowRenegotiation = false
                        },
                        cancellationToken),
                    receiver.AuthenticateAsClientAsync(
                        new SslClientAuthenticationOptions
                        {
                            TargetHost = "overlay-bridge.synthetic.local",
                            ClientCertificates = new X509CertificateCollection(receiverCertificate),
                            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                            AllowRenegotiation = false
                        },
                        cancellationToken))
                .ConfigureAwait(false);

            return new OverlayBridgeWorkbenchTlsCircuit(
                pair,
                producerCertificate,
                receiverCertificate,
                producer,
                receiver);
        }
        catch
        {
            producer?.Dispose();
            receiver?.Dispose();
            producerCertificate?.Dispose();
            receiverCertificate?.Dispose();
            pair.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Producer.Dispose();
        Receiver.Dispose();
        producerCertificate.Dispose();
        receiverCertificate.Dispose();
        await pair.DisposeAsync().ConfigureAwait(false);
    }

    private static X509Certificate2 CreateCertificate(string subjectName, string enhancedKeyUsageOid)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={subjectName}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid(enhancedKeyUsageOid) },
            critical: false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
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

/// <summary>
/// Synthetic values only. It creates a valid contract-shaped publication but does not project a
/// snapshot, access app storage, or imitate a future production publisher host.
/// </summary>
internal static class OverlayBridgeWorkbenchSyntheticFixture
{
    public static OverlayBridgeSectorPublication CreatePublication(
        long sequence,
        DateTimeOffset now)
    {
        var lap = checked(41 + (int)((sequence - 1) / 3));
        var sector = (int)(((sequence - 1) % 3) + 1);
        var header = new OverlayBridgeSectorPublicationHeader(
            ProtocolVersion: OverlayBridgeProtocolVersion.Current,
            NegotiatedCapabilities: OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities,
            RoomId: "synthetic-room-local",
            StreamId: "synthetic-stream-team-car-47",
            Session: new OverlayBridgeSessionBinding(
                SessionId: "synthetic-session-daytona",
                SessionEpoch: 1,
                TrackKey: "synthetic-track-daytona-road",
                TeamCarKey: "synthetic-team-car-47"),
            PublisherDeviceId: "synthetic-producer-local",
            PublisherLeaseId: "synthetic-lease-local",
            PublisherLeaseEpoch: 1,
            PublicationEpoch: 1,
            SnapshotId: DeterministicSnapshotId(sequence),
            Sequence: sequence,
            SourceMode: OverlayBridgeSourceMode.Live,
            LapNumber: lap,
            SectorNumber: sector,
            PublishedAtUtc: now,
            DeclaredPayloadBytes: 1,
            PublisherAppVersion: "synthetic-workbench",
            PublisherSchemaHash: "synthetic-contract-fixture");

        OverlayBridgeFactGroupProvenance Provenance(OverlayBridgeCapability capability) => new(
            Capability: capability,
            FactSchemaVersion: OverlayBridgeFactContracts.CurrentFactSchemaVersion,
            SourceMode: header.SourceMode,
            SourceDeviceId: header.PublisherDeviceId,
            PublisherLeaseEpoch: header.PublisherLeaseEpoch,
            PublicationEpoch: header.PublicationEpoch,
            SnapshotId: header.SnapshotId,
            Sequence: header.Sequence,
            LapNumber: header.LapNumber,
            SectorNumber: header.SectorNumber,
            PublishedAtUtc: header.PublishedAtUtc);

        var fuelLiters = Math.Max(0d, 78.4d - ((sequence - 1) * 0.8d));
        var progressLaps = 41.25d + ((sequence - 1) * 0.32d);
        return new OverlayBridgeSectorPublication(
            Header: header,
            RaceContext: OverlayBridgeFactGroup.Unsupported<OverlayBridgeRaceContextFacts>(
                Provenance(OverlayBridgeCapability.RaceContext)),
            ActiveTeamCar: OverlayBridgeFactGroup.Available(
                Provenance(OverlayBridgeCapability.ActiveTeamCar),
                new OverlayBridgeActiveTeamCarFacts(
                    ContractVersion: OverlayBridgeFactContracts.CurrentFactSchemaVersion,
                    TeamCarId: header.Session.TeamCarKey,
                    SourceState: OverlayBridgeTeamCarSourceState.ConfirmedInCar,
                    IsDriverChangeInProgress: false,
                    IsOnPitRoad: false,
                    IsInPitStall: false,
                    IsInGarage: false,
                    IsPitstopActive: false,
                    CurrentFuelLiters: fuelLiters,
                    FuelCapacity: new OverlayBridgeFuelCapacityFacts(
                        PhysicalTankCapacityLiters: 100d,
                        EffectiveSessionCapacityLiters: 100d,
                        MaximumFuelPercent: 100d,
                        FuelKgPerLiter: 0.75d),
                    CleanBurnEvidence: new OverlayBridgeCleanBurnEvidence(
                        AcceptedSampleCount: 2,
                        Confidence: OverlayBridgeEvidenceConfidence.Measured,
                        Samples:
                        [
                            new OverlayBridgeCleanBurnSample(lap - 2, 2.35d, 100.2d),
                            new OverlayBridgeCleanBurnSample(lap - 1, 2.31d, 99.8d)
                        ]),
                    RepairService: new OverlayBridgeRepairServiceFacts(
                        ServiceState: OverlayBridgePitServiceState.None,
                        RequestedFuelLiters: null,
                        RequiredRepairSeconds: null,
                        OptionalRepairSeconds: null,
                        FastRepairAvailable: false,
                        FastRepairUsed: false),
                    TeamCarProgressLaps: progressLaps)),
            Environment: OverlayBridgeFactGroup.Unsupported<OverlayBridgeEnvironmentFacts>(
                Provenance(OverlayBridgeCapability.Environment)),
            SpatialTraffic: OverlayBridgeFactGroup.Unsupported<OverlayBridgeSpatialTrafficFacts>(
                Provenance(OverlayBridgeCapability.SpatialTraffic)),
            MapAdvertisement: OverlayBridgeFactGroup.Unsupported<OverlayBridgeMapAdvertisementFacts>(
                Provenance(OverlayBridgeCapability.MapAdvertisement)));
    }

    private static Guid DeterministicSnapshotId(long sequence)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes[8..], sequence);
        bytes[0] = 0x13;
        bytes[1] = 0x37;
        return new Guid(bytes);
    }
}

/// <summary>
/// Manual/CI-friendly readiness assertion for the isolated tool. It uses no HTTP listener, no
/// app services, and no external network. A successful run proves the same Core route that the
/// producer button uses, including actual virtual-circuit TLS, Core framing, decode, admission,
/// source priority, and Fuel input availability.
/// </summary>
internal static class OverlayBridgeWorkbenchSelfTest
{
    public static async Task<bool> RunAsync()
    {
        try
        {
            await using var runtime = new OverlayBridgeWorkbenchRuntime();
            var result = await runtime.PublishSyntheticSectorAsync(CancellationToken.None).ConfigureAwait(false);
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var receiver = document.RootElement.GetProperty("receiver");
            if (receiver.GetProperty("admission").GetString() != "Accepted"
                || receiver.GetProperty("availability").GetString() != "Current"
                || receiver.GetProperty("source").GetString() != "RemoteBridge"
                || !receiver.GetProperty("fuelInputAvailable").GetBoolean())
            {
                Console.Error.WriteLine("Overlay Bridge workbench self-test did not produce an accepted current synthetic result.");
                return false;
            }

            var precedence = await runtime.ApplyDirectLocalPrecedenceAsync(true, CancellationToken.None)
                .ConfigureAwait(false);
            using var precedenceDocument = JsonDocument.Parse(JsonSerializer.Serialize(precedence));
            var directReceiver = precedenceDocument.RootElement.GetProperty("receiver");
            var succeeded = directReceiver.GetProperty("source").GetString() == "DirectLocalTelemetry"
                && directReceiver.GetProperty("currentFacts").ValueKind == JsonValueKind.Null
                && !directReceiver.GetProperty("fuelInputAvailable").GetBoolean();
            Console.WriteLine(succeeded
                ? "Overlay Bridge developer workbench self-test passed."
                : "Overlay Bridge developer workbench self-test failed direct-local precedence.");
            return succeeded;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Overlay Bridge developer workbench self-test failed: {exception}");
            return false;
        }
    }
}
