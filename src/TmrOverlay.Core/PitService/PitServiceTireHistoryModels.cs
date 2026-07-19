namespace TmrOverlay.Core.PitService;

// Exact tire-service history is deliberately kept in Pit Service rather than
// Fuel. Fuel V2 may present this profile beside a stint, but it must never
// reinterpret a requested tire count as proof that those tires were fitted.
internal enum PitServiceExecutionMode
{
    Unknown = 0,
    Sequential = 1,
    Parallel = 2
}

internal enum PitServiceRuleScopeVerification
{
    Unavailable = 0,
    RawMetadataOnly = 1
}

// DCRuleSet is public iRacing WeekendInfo session metadata. Preserve it as a
// raw evidence bucket, but it does not expose an executable fuel/tire-service
// order contract. A future verified SDK/session-rules source may add a distinct
// execution-mode resolver; this type must not infer one from its label.
internal sealed record PitServiceRuleScope(
    string RuleSetIdentity,
    PitServiceExecutionMode ExecutionMode,
    PitServiceRuleScopeVerification Verification,
    string EvidenceSource)
{
    public bool HasVerifiedExecutionMode => false;
}

internal static class PitServiceRuleScopeCatalog
{
    public static PitServiceRuleScope FromDCRuleSet(string? dcRuleSet)
    {
        var identity = string.IsNullOrWhiteSpace(dcRuleSet)
            ? "unavailable"
            : dcRuleSet.Trim();

        return string.Equals(identity, "unavailable", StringComparison.OrdinalIgnoreCase)
            ? new PitServiceRuleScope(
                RuleSetIdentity: identity,
                ExecutionMode: PitServiceExecutionMode.Unknown,
                Verification: PitServiceRuleScopeVerification.Unavailable,
                EvidenceSource: "WeekendInfo.DCRuleSet unavailable")
            : new PitServiceRuleScope(
                RuleSetIdentity: identity,
                ExecutionMode: PitServiceExecutionMode.Unknown,
                Verification: PitServiceRuleScopeVerification.RawMetadataOnly,
                EvidenceSource: "WeekendInfo.DCRuleSet preserved as raw provenance; execution mode unverified");
    }
}

internal enum PitServiceTireHistoryTimingEligibility
{
    BlockedRulesUnverified = 0,
    BlockedInsufficientCleanSamples = 1,
    ReadyForCalibration = 2
}

internal sealed record PitServiceTireHistoryProfile(
    PitServiceTireShape RequestedShape,
    int MatchingObservationCount,
    int ConfirmedOutcomeCount,
    int CleanConfirmedOutcomeCount,
    int RequestedOnlyCount,
    int MismatchCount,
    int AmbiguousCount,
    DateTimeOffset? MostRecentConfirmedAtUtc,
    IReadOnlyList<string> QualificationFlags,
    PitServiceTireHistoryTimingEligibility TimingEligibility)
{
    public bool HasConfirmedOutcome => ConfirmedOutcomeCount > 0;

    public bool HasOnlyRequestedEvidence => !HasConfirmedOutcome && RequestedOnlyCount > 0;

    // No current source proves service execution mode or equivalent component
    // shape. Keep the public bridge fail-closed even if a future caller builds
    // an optimistic enum value by hand; a verified learned-service owner must
    // explicitly replace this guard when it is introduced.
    public bool CanCalibrateServiceTime => false;
}

// This builder is intentionally a read-time projection over immutable
// stationary observations. We do not create a derived timing aggregate until
// a separately verified service-rules contract exists.
internal sealed class PitServiceTireHistoryProfileBuilder
{
    private readonly PitServiceTireShape _requestedShape;
    private readonly HashSet<string> _qualificationFlags = new(StringComparer.OrdinalIgnoreCase);
    private int _matchingObservationCount;
    private int _confirmedOutcomeCount;
    private int _cleanConfirmedOutcomeCount;
    private int _requestedOnlyCount;
    private int _mismatchCount;
    private int _ambiguousCount;
    private DateTimeOffset? _mostRecentConfirmedAtUtc;

    public PitServiceTireHistoryProfileBuilder(PitServiceTireShape requestedShape)
    {
        ArgumentNullException.ThrowIfNull(requestedShape);
        _requestedShape = requestedShape;
    }

    public void Add(
        PitServiceStationaryServiceObservation observation,
        IReadOnlyList<string>? additionalQualificationFlags = null,
        bool disqualifyForTireTimingLearning = false)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var assessment = PitServiceTireChangeClassifier.Classify(observation);
        if (assessment.RequestedShape != _requestedShape)
        {
            return;
        }

        _matchingObservationCount++;
        foreach (var flag in assessment.QualificationFlags)
        {
            _qualificationFlags.Add(flag);
        }

        if (additionalQualificationFlags is not null)
        {
            foreach (var flag in additionalQualificationFlags)
            {
                _qualificationFlags.Add(flag);
            }
        }

        switch (assessment.ExecutionState)
        {
            case PitServiceTireExecutionState.Confirmed:
                _confirmedOutcomeCount++;
                if (assessment.IsCleanForTireTimingLearning && !disqualifyForTireTimingLearning)
                {
                    _cleanConfirmedOutcomeCount++;
                }

                _mostRecentConfirmedAtUtc = _mostRecentConfirmedAtUtc is { } previous
                    ? (observation.EndedAtUtc > previous ? observation.EndedAtUtc : previous)
                    : observation.EndedAtUtc;
                break;
            case PitServiceTireExecutionState.RequestedOnly:
                _requestedOnlyCount++;
                break;
            case PitServiceTireExecutionState.Mismatch:
                _mismatchCount++;
                break;
            case PitServiceTireExecutionState.Ambiguous:
                _ambiguousCount++;
                break;
        }
    }

    public PitServiceTireHistoryProfile Build()
    {
        return new PitServiceTireHistoryProfile(
            RequestedShape: _requestedShape,
            MatchingObservationCount: _matchingObservationCount,
            ConfirmedOutcomeCount: _confirmedOutcomeCount,
            CleanConfirmedOutcomeCount: _cleanConfirmedOutcomeCount,
            RequestedOnlyCount: _requestedOnlyCount,
            MismatchCount: _mismatchCount,
            AmbiguousCount: _ambiguousCount,
            MostRecentConfirmedAtUtc: _mostRecentConfirmedAtUtc,
            QualificationFlags: _qualificationFlags
                .OrderBy(flag => flag, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            TimingEligibility: PitServiceTireHistoryTimingEligibility.BlockedRulesUnverified);
    }
}
