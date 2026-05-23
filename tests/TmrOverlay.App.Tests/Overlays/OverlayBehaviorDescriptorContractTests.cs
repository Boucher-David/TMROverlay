using System.Text.Json.Nodes;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.Core.Overlays;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class OverlayBehaviorDescriptorContractTests
{
    private const string ScenarioContractRelativePath = "tools/validation/overlay-scenario-contract.json";
    private const string DescriptorCatalogRelativePath = "src/TmrOverlay.Core/Overlays/OverlayBehaviorDescriptorCatalog.cs";

    [Fact]
    public void DescriptorCatalog_TracksProductionBrowserOverlayCatalog()
    {
        var browserOverlayIds = BrowserOverlayCatalog.Pages.Select(page => page.Id).ToArray();
        var descriptorIds = OverlayBehaviorDescriptorCatalog.All.Select(descriptor => descriptor.Id).ToArray();

        Assert.Equal(browserOverlayIds, descriptorIds);
        Assert.All(OverlayBehaviorDescriptorCatalog.All, descriptor =>
        {
            Assert.Equal(OverlaySurfaceSupport.Supported, descriptor.BrowserReview);
            Assert.Equal(OverlaySurfaceSupport.Supported, descriptor.LocalhostObs);
            Assert.NotEmpty(descriptor.EvidenceFields);
            Assert.All(descriptor.EvidenceFields, field => Assert.False(string.IsNullOrWhiteSpace(field)));
        });
    }

    [Fact]
    public void ScenarioContract_ReferencesAppOwnedDescriptorCatalog()
    {
        var root = LoadScenarioContract();
        var productDescriptor = RequiredObject(root["productDescriptor"]);
        var validatedBy = RequiredArray(productDescriptor["validatedBy"])
            .Select(value => Assert.IsAssignableFrom<JsonValue>(value).GetValue<string>())
            .ToArray();

        Assert.Equal(DescriptorCatalogRelativePath, RequiredString(productDescriptor, "source"));
        Assert.Equal("overlays[].id", RequiredString(productDescriptor, "matchKey"));
        Assert.Contains("tests/TmrOverlay.App.Tests/Overlays/OverlayBehaviorDescriptorContractTests.cs", validatedBy);
    }

    [Fact]
    public void ScenarioContract_MatchesAppOwnedDescriptorSurfacesAndBodyKinds()
    {
        var root = LoadScenarioContract();
        var overlays = RequiredArray(root["overlays"])
            .Select(node => RequiredObject(node))
            .ToDictionary(overlay => RequiredString(overlay, "id"), StringComparer.OrdinalIgnoreCase);

        Assert.Equal(
            OverlayBehaviorDescriptorCatalog.All.Select(descriptor => descriptor.Id).OrderBy(id => id, StringComparer.OrdinalIgnoreCase),
            overlays.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase));

        foreach (var descriptor in OverlayBehaviorDescriptorCatalog.All)
        {
            var overlay = overlays[descriptor.Id];
            var surfaces = RequiredObject(overlay["surfaces"]);

            Assert.Equal(descriptor.BodyKind, RequiredString(overlay, "bodyKind"));
            Assert.Equal(ToContractValue(descriptor.BrowserReview), RequiredString(surfaces, "browserReview"));
            Assert.Equal(ToContractValue(descriptor.LocalhostObs), RequiredString(surfaces, "localhostObs"));
            Assert.Equal(ToContractValue(descriptor.WindowsNative), RequiredString(surfaces, "windowsNative"));
        }
    }

    [Fact]
    public void DescriptorCatalog_DeclaresExpectedPolicyExceptions()
    {
        Assert.True(OverlayBehaviorDescriptorCatalog.TryGet("garage-cover", out var garageCover));
        Assert.Equal(OverlaySurfaceSupport.NotApplicable, garageCover.WindowsNative);
        Assert.Equal(OverlaySizingPolicy.FullCanvasCover, garageCover.SizingPolicy);
        Assert.Equal(OverlayObsReadinessPolicy.HiddenOrRenderedStateExpected, garageCover.ObsReadinessPolicy);

        Assert.True(OverlayBehaviorDescriptorCatalog.TryGet("stream-chat", out var streamChat));
        Assert.Equal(OverlayNoDataPolicy.ExternalProviderDriven, streamChat.NoDataPolicy);
        Assert.Equal(OverlayObsReadinessPolicy.ExternalProviderRouteExpected, streamChat.ObsReadinessPolicy);
    }

    private static JsonObject LoadScenarioContract()
    {
        var path = FindRepoRootFile(ScenarioContractRelativePath);
        var root = JsonNode.Parse(File.ReadAllText(path));
        return RequiredObject(root);
    }

    private static string ToContractValue(OverlaySurfaceSupport support)
    {
        return support switch
        {
            OverlaySurfaceSupport.Supported => "supported",
            OverlaySurfaceSupport.NotApplicable => "not_applicable",
            _ => throw new ArgumentOutOfRangeException(nameof(support), support, null)
        };
    }

    private static string FindRepoRootFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find {relativePath} from {AppContext.BaseDirectory}.");
    }

    private static JsonObject RequiredObject(JsonNode? node)
    {
        return Assert.IsType<JsonObject>(node);
    }

    private static JsonArray RequiredArray(JsonNode? node)
    {
        return Assert.IsType<JsonArray>(node);
    }

    private static string RequiredString(JsonObject value, string propertyName)
    {
        var jsonValue = Assert.IsAssignableFrom<JsonValue>(value[propertyName]);
        var text = jsonValue.GetValue<string>();
        Assert.False(string.IsNullOrWhiteSpace(text));
        return text;
    }
}
