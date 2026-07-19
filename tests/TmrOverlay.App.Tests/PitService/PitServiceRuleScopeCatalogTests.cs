using TmrOverlay.Core.PitService;
using Xunit;

namespace TmrOverlay.App.Tests.PitService;

public sealed class PitServiceRuleScopeCatalogTests
{
    [Theory]
    [InlineData("None")]
    [InlineData("GLOBAL")]
    [InlineData("IMSA")]
    [InlineData("NEC")]
    [InlineData("DTM")]
    [InlineData("DriveFairShare_AllMustDrive")]
    [InlineData("future-ruleset")]
    public void FromDCRuleSet_PreservesRawIdentityButNeverInfersExecutionMode(string ruleSet)
    {
        var scope = PitServiceRuleScopeCatalog.FromDCRuleSet(ruleSet);

        Assert.Equal(ruleSet, scope.RuleSetIdentity);
        Assert.Equal(PitServiceExecutionMode.Unknown, scope.ExecutionMode);
        Assert.False(scope.HasVerifiedExecutionMode);
        Assert.Equal(PitServiceRuleScopeVerification.RawMetadataOnly, scope.Verification);
    }

    [Fact]
    public void FromDCRuleSet_FailsClosedWhenUnavailable()
    {
        var unavailable = PitServiceRuleScopeCatalog.FromDCRuleSet(null);

        Assert.Equal(PitServiceExecutionMode.Unknown, unavailable.ExecutionMode);
        Assert.False(unavailable.HasVerifiedExecutionMode);
        Assert.Equal(PitServiceRuleScopeVerification.Unavailable, unavailable.Verification);
    }
}
