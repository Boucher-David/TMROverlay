namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// Versioned, transport-neutral facts for the Overlay Bridge. These records are deliberately
/// detached from iRacing SDK samples, live snapshots, renderer models, capture/history records,
/// pairing, and cryptographic transport. A publisher may project into this boundary and a
/// receiver may compose from it, but neither direction is implied by these contracts.
/// </summary>
internal static class OverlayBridgeFactContracts
{
    /// <summary>
    /// The facts-family major. Additive wire changes use the enclosing protocol minor and
    /// optional CBOR keys, so this value changes only for an explicit facts-family break.
    /// </summary>
    public const int CurrentFactSchemaVersion = 1;
    public const int MaxOpaqueIdentifierLength = 128;
    public const int MaxMapIdentityLength = 256;
    public const int MaxMapCompatibilityHashLength = 160;
    public const int MaxFieldCars = 128;
    public const int MaxSpatialCars = 128;
    public const int MaxCleanBurnSamples = 10;
    public const int MaxDeclaredPayloadBytes = 128 * 1024;
    /// <summary>
    /// Small allowance for simulator/serialization rounding when comparing a fuel level with a
    /// declared litre capacity. It is not a strategy reserve or a permission to overfill.
    /// </summary>
    public const double FuelCapacityRoundingToleranceLiters = 0.1d;

    /// <summary>
    /// The complete vocabulary this pre-release Core facts codec can validate. Product policy
    /// still decides which of these capabilities an Owner may grant or a projector may emit.
    /// </summary>
    public static OverlayBridgeCapability KnownCapabilities =>
        OverlayBridgeCapability.RaceContext
        | OverlayBridgeCapability.ActiveTeamCar
        | OverlayBridgeCapability.Environment
        | OverlayBridgeCapability.SpatialTraffic
        | OverlayBridgeCapability.MapAdvertisement;

    /// <summary>
    /// The only live-facts capability in the first remote release. Session/environment facts
    /// require capture confirmation; field and spatial facts remain research-gated; map assets
    /// use a separate explicit local import path.
    /// </summary>
    public static OverlayBridgeCapability FirstRemoteReleaseCapabilities =>
        OverlayBridgeCapability.ActiveTeamCar;

    public static bool IsKnownCapabilitySet(OverlayBridgeCapability capabilities)
    {
        return capabilities != OverlayBridgeCapability.None
            && (capabilities & ~KnownCapabilities) == OverlayBridgeCapability.None;
    }
}

[Flags]
internal enum OverlayBridgeCapability
{
    None = 0,
    RaceContext = 1 << 0,
    ActiveTeamCar = 1 << 1,
    Environment = 1 << 2,
    SpatialTraffic = 1 << 3,
    MapAdvertisement = 1 << 4
}

/// <summary>
/// The data source is visible to composition and presentation, but transport implementation is
/// intentionally not represented here.
/// </summary>
internal enum OverlayBridgeSourceMode
{
    Live = 0,
    RawCaptureReplay = 1
}

internal enum OverlayBridgeFactGroupAvailability
{
    Available = 0,
    Unavailable = 1,
    Unsupported = 2
}

/// <summary>
/// Safe, finite reasons for removing a complete remote group. Do not put raw host, transport, or
/// simulator text in a Bridge facts contract.
/// </summary>
internal enum OverlayBridgeFactGroupUnavailableReason
{
    None = 0,
    SourceUnavailable = 1,
    SessionMismatch = 2,
    Stale = 3,
    DriverHandoff = 4,
    GarageOrSpectator = 5,
    SourceError = 6,
    Tombstoned = 7,
    Unsupported = 8
}

internal sealed record OverlayBridgeProtocolVersion(int Major, int Minor)
{
    public static OverlayBridgeProtocolVersion Current { get; } = new(Major: 1, Minor: 0);

    /// <summary>
    /// A protocol minor is additive within a major. A receiver accepts a peer that advertises a
    /// later minor because any fields it does not understand must occupy the optional CBOR-key
    /// range and are skipped as bounded canonical values. A major change is never implicit.
    /// </summary>
    public bool IsCompatibleWith(OverlayBridgeProtocolVersion? localImplementation)
    {
        return localImplementation is not null
            && Major == localImplementation.Major
            && Minor >= 0;
    }

    /// <summary>
    /// Produces the minor both peers can use for an outbound publication. The caller must omit
    /// optional fields introduced after the returned minor, and must separately intersect the
    /// negotiated capability set. Major mismatches deliberately have no fallback.
    /// </summary>
    public static bool TryNegotiate(
        OverlayBridgeProtocolVersion? localImplementation,
        OverlayBridgeProtocolVersion? peerImplementation,
        out OverlayBridgeProtocolVersion? negotiated)
    {
        negotiated = null;
        if (localImplementation is null
            || peerImplementation is null
            || localImplementation.Major != peerImplementation.Major
            || localImplementation.Minor < 0
            || peerImplementation.Minor < 0)
        {
            return false;
        }

        negotiated = new OverlayBridgeProtocolVersion(
            localImplementation.Major,
            Math.Min(localImplementation.Minor, peerImplementation.Minor));
        return true;
    }
}

/// <summary>
/// Opaque, session-scoped identifiers only. Track and team-car keys allow a receiver to reject a
/// validly formed message that belongs to a different race or team-car stream.
/// </summary>
internal sealed record OverlayBridgeSessionBinding(
    string SessionId,
    long SessionEpoch,
    string TrackKey,
    string TeamCarKey);

/// <summary>
/// Atomic publication context. The relay/crypto implementation is responsible for authenticating
/// this envelope; this Core type only expresses semantic and ordering checks.
/// </summary>
internal sealed record OverlayBridgeSectorPublicationHeader(
    OverlayBridgeProtocolVersion ProtocolVersion,
    OverlayBridgeCapability NegotiatedCapabilities,
    string RoomId,
    string StreamId,
    OverlayBridgeSessionBinding Session,
    string PublisherDeviceId,
    string PublisherLeaseId,
    long PublisherLeaseEpoch,
    long PublicationEpoch,
    Guid SnapshotId,
    long Sequence,
    OverlayBridgeSourceMode SourceMode,
    int LapNumber,
    int SectorNumber,
    DateTimeOffset PublishedAtUtc,
    int DeclaredPayloadBytes,
    string? PublisherAppVersion = null,
    string? PublisherSchemaHash = null);

/// <summary>
/// Provenance is copied into each fact group so a receiver can preserve source/device/lap/sector
/// context after it has stored a group independently from the enclosing publication. Receiver
/// receipt time is recorded separately by the receiver. <see cref="PublishedAtUtc"/> remains
/// publisher provenance and diagnostics only; it is never the source of a receiver freshness age.
/// </summary>
internal sealed record OverlayBridgeFactGroupProvenance(
    OverlayBridgeCapability Capability,
    int FactSchemaVersion,
    OverlayBridgeSourceMode SourceMode,
    string SourceDeviceId,
    long PublisherLeaseEpoch,
    long PublicationEpoch,
    Guid SnapshotId,
    long Sequence,
    int LapNumber,
    int SectorNumber,
    DateTimeOffset PublishedAtUtc);

/// <summary>
/// A group is never partially updated. An unavailable or unsupported group carries no facts and
/// must be resolved as absent as a whole.
/// </summary>
internal sealed record OverlayBridgeFactGroup<TFacts>(
    OverlayBridgeFactGroupProvenance Provenance,
    OverlayBridgeFactGroupAvailability Availability,
    OverlayBridgeFactGroupUnavailableReason UnavailableReason,
    TFacts? Facts)
    where TFacts : class;

internal static class OverlayBridgeFactGroup
{
    public static OverlayBridgeFactGroup<TFacts> Available<TFacts>(
        OverlayBridgeFactGroupProvenance provenance,
        TFacts facts)
        where TFacts : class
    {
        return new OverlayBridgeFactGroup<TFacts>(
            Provenance: provenance,
            Availability: OverlayBridgeFactGroupAvailability.Available,
            UnavailableReason: OverlayBridgeFactGroupUnavailableReason.None,
            Facts: facts);
    }

    public static OverlayBridgeFactGroup<TFacts> Unavailable<TFacts>(
        OverlayBridgeFactGroupProvenance provenance,
        OverlayBridgeFactGroupUnavailableReason reason)
        where TFacts : class
    {
        return new OverlayBridgeFactGroup<TFacts>(
            Provenance: provenance,
            Availability: OverlayBridgeFactGroupAvailability.Unavailable,
            UnavailableReason: reason,
            Facts: null);
    }

    public static OverlayBridgeFactGroup<TFacts> Unsupported<TFacts>(
        OverlayBridgeFactGroupProvenance provenance)
        where TFacts : class
    {
        return new OverlayBridgeFactGroup<TFacts>(
            Provenance: provenance,
            Availability: OverlayBridgeFactGroupAvailability.Unsupported,
            UnavailableReason: OverlayBridgeFactGroupUnavailableReason.Unsupported,
            Facts: null);
    }
}

/// <summary>
/// One full, coherent sector-boundary publication. It carries every initial group as either a
/// whole available fact set, an explicit unavailable state, or an explicitly unsupported state.
/// It intentionally has no fuel-only or renderer-specific delta form.
/// </summary>
internal sealed record OverlayBridgeSectorPublication(
    OverlayBridgeSectorPublicationHeader Header,
    OverlayBridgeFactGroup<OverlayBridgeRaceContextFacts> RaceContext,
    OverlayBridgeFactGroup<OverlayBridgeActiveTeamCarFacts> ActiveTeamCar,
    OverlayBridgeFactGroup<OverlayBridgeEnvironmentFacts> Environment,
    OverlayBridgeFactGroup<OverlayBridgeSpatialTrafficFacts> SpatialTraffic,
    OverlayBridgeFactGroup<OverlayBridgeMapAdvertisementFacts> MapAdvertisement)
{
    public bool TryValidate(out OverlayBridgePublicationValidationError error)
    {
        return OverlayBridgePublicationValidator.TryValidate(this, serializedPayloadBytes: null, out error);
    }

    /// <summary>
    /// The caller supplies the measured serialized byte count after transport encoding. A declared
    /// header length is not trusted as a substitute for this boundary check.
    /// </summary>
    public bool TryValidateForTransport(int serializedPayloadBytes, out OverlayBridgePublicationValidationError error)
    {
        return OverlayBridgePublicationValidator.TryValidate(this, serializedPayloadBytes, out error);
    }
}

internal enum OverlayBridgePublicationValidationError
{
    None = 0,
    MissingHeader = 1,
    UnsupportedProtocol = 2,
    InvalidHeader = 3,
    UnsupportedCapability = 4,
    IncoherentGroupProvenance = 5,
    InvalidGroupAvailability = 6,
    InvalidRaceContextFacts = 7,
    InvalidActiveTeamCarFacts = 8,
    InvalidEnvironmentFacts = 9,
    InvalidSpatialTrafficFacts = 10,
    InvalidMapAdvertisementFacts = 11,
    OversizedPayload = 12,
    PayloadLengthMismatch = 13
}
