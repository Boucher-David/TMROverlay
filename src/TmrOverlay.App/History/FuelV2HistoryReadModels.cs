using TmrOverlay.Core.Fuel.V2;
using TmrOverlay.Core.PitService;

namespace TmrOverlay.App.History;

// The normal-history reader is intentionally explicit about its caller. The
// factual V2 presentation may inspect classified history as a labeled display
// bucket while production strategy remains behind FuelV2History:UseForStrategy.
// V1 never receives this result.
internal enum FuelV2HistoryLookupPurpose
{
    Workbench = 0,
    Strategy = 1
}

internal enum FuelV2HistoryAggregateReadStatus
{
    Available = 0,
    Missing = 1,
    Unreadable = 2,
    LegacyVersion = 3,
    FutureVersion = 4,
    ScopeMismatch = 5,
    Unclassified = 6,
    NotLearningEligible = 7,
    MetricUnavailable = 8
}

internal sealed record FuelV2HistoryAggregateReadResult(
    FuelV2HistoryAggregateReadStatus Status,
    FuelV2HistoryAggregate? Aggregate)
{
    public bool IsAvailable => Status == FuelV2HistoryAggregateReadStatus.Available && Aggregate is not null;
}

// Raw stationary-service observations have a different safety contract from
// the fuel-burn aggregate. Keep their read status separate so a tire-only
// sample is not rejected merely because it has no normal lap-burn metric.
internal enum FuelV2HistorySummaryReadStatus
{
    Available = 0,
    Missing = 1,
    Unreadable = 2,
    LegacyVersion = 3,
    FutureVersion = 4,
    ScopeMismatch = 5,
    Unclassified = 6
}

internal sealed record FuelV2HistorySummaryReadResult(
    FuelV2HistorySummaryReadStatus Status,
    IReadOnlyList<FuelV2HistorySummary> Summaries,
    int IgnoredUnreadableSummaryCount,
    int IgnoredUnclassifiedSummaryCount)
{
    public bool IsAvailable => Status == FuelV2HistorySummaryReadStatus.Available
        && Summaries.Count > 0;
}

internal enum FuelV2TireServiceHistorySelectionStatus
{
    Selected = 0,
    NoTiresSelected = 1,
    Disabled = 2,
    UnsupportedSessionFamily = 3,
    InexactIdentity = 4,
    Missing = 5,
    Unreadable = 6,
    LegacyVersion = 7,
    FutureVersion = 8,
    ScopeMismatch = 9,
    Unclassified = 10,
    RequestedOnly = 11,
    RuleScopeMismatch = 12
}

internal sealed record FuelV2TireServiceHistorySelection(
    FuelV2TireServiceHistorySelectionStatus Status,
    PitServiceTireShape? RequestedShape,
    PitServiceRuleScope RuleScope,
    PitServiceTireHistoryProfile? Profile,
    string? SelectedSessionFamily,
    string Detail,
    int IgnoredDuplicateObservationCount)
{
    public bool HasConfirmedExactOutcome => Status == FuelV2TireServiceHistorySelectionStatus.Selected
        && Profile?.HasConfirmedOutcome == true;

    public bool IsColumnEligible => RequestedShape?.RequestedTireCount > 0;
}

internal enum FuelV2HistoryNormalBurnSelectionStatus
{
    Selected = 0,
    Disabled = 1,
    StrategyPromotionDisabled = 2,
    UnsupportedSessionFamily = 3,
    InexactIdentity = 4,
    Missing = 5,
    Unreadable = 6,
    LegacyVersion = 7,
    FutureVersion = 8,
    ScopeMismatch = 9,
    Unclassified = 10,
    NotLearningEligible = 11,
    MetricUnavailable = 12
}

internal sealed record FuelV2HistoryNormalBurnSelection(
    FuelV2HistoryNormalBurnSelectionStatus Status,
    FuelV2Scalar? Burn,
    string? SelectedSessionFamily,
    string Detail,
    int? ClassifiedSessionCount,
    int? LearningEligibleSessionCount,
    int? AcceptedLapBurnSampleCount,
    DateTimeOffset? AggregateUpdatedAtUtc,
    bool CanSeedPlan,
    bool CanDriveAdvice)
{
    public bool IsAvailable => Status == FuelV2HistoryNormalBurnSelectionStatus.Selected
        && Burn?.HasTypedBurnEvidence == true;

    public static FuelV2HistoryNormalBurnSelection Unavailable(
        FuelV2HistoryNormalBurnSelectionStatus status,
        string detail)
    {
        return new FuelV2HistoryNormalBurnSelection(
            Status: status,
            Burn: FuelV2Scalar.Unavailable(detail),
            SelectedSessionFamily: null,
            Detail: detail,
            ClassifiedSessionCount: null,
            LearningEligibleSessionCount: null,
            AcceptedLapBurnSampleCount: null,
            AggregateUpdatedAtUtc: null,
            CanSeedPlan: false,
            CanDriveAdvice: false);
    }
}
