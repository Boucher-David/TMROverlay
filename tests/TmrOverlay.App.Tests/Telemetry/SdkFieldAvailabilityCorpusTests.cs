using System.Text.Json.Nodes;
using Xunit;

namespace TmrOverlay.App.Tests.Telemetry;

public sealed class SdkFieldAvailabilityCorpusTests
{
    private const string CorpusRelativePath = "fixtures/telemetry-analysis/sdk-field-availability-corpus.json";

    [Fact]
    public void Corpus_CoversCurrentSdkSchemas()
    {
        var corpus = ReadCorpus();

        Assert.Equal(1, RequiredInt(corpus, "schemaVersion"));
        Assert.Equal(340, RequiredInt(corpus, "fieldCount"));
        Assert.Equal(340, RequiredArray(corpus["fields"]).Count);

        var sources = RequiredArray(corpus["sources"]).Select(RequiredObject).ToArray();
        Assert.Equal(4, sources.Length);
        Assert.Contains(sources, source => RequiredString(source, "sourceCategory") == "endurance-4h-team-race");
        Assert.Contains(sources, source => RequiredString(source, "sourceCategory") == "endurance-24h-fragment");
        Assert.Contains(sources, source => RequiredString(source, "sourceCategory") == "ai-nascar-limited-tire-race");
        Assert.Contains(sources, source => RequiredString(source, "sourceCategory") == "pcup-open-practice-pit-service");
        Assert.All(
            sources.Where(source => RequiredString(source, "sourceCategory").StartsWith("endurance-", StringComparison.Ordinal)),
            source => Assert.Equal(334, RequiredInt(source, "schemaFieldCount")));
        Assert.Contains(
            sources,
            source => RequiredString(source, "sourceCategory") == "ai-nascar-limited-tire-race"
                && RequiredInt(source, "schemaFieldCount") == 325);
        Assert.Contains(
            sources,
            source => RequiredString(source, "sourceCategory") == "pcup-open-practice-pit-service"
                && RequiredInt(source, "schemaFieldCount") == 334);

        var fields = RequiredArray(corpus["fields"]).Select(RequiredObject).ToDictionary(field => RequiredString(field, "name"));
        foreach (var requiredField in new[]
        {
            "SteeringWheelAngle",
            "Throttle",
            "Brake",
            "FuelLevel",
            "PlayerCarPitSvStatus",
            "DCDriversSoFar",
            "WeatherDeclaredWet",
            "CarIdxF2Time",
            "dpLTireChange",
            "dpRTireChange",
            "dpWeightJackerLeft",
            "dpWeightJackerRight"
        })
        {
            Assert.Contains(requiredField, fields.Keys);
        }
    }

    [Fact]
    public void Corpus_RecordsSdkDeclaredShapeAndObservedRangesSeparately()
    {
        var corpus = ReadCorpus();
        var sourceId = RequiredString(RequiredObject(RequiredArray(corpus["sources"])[0]), "captureId");
        var fields = RequiredArray(corpus["fields"]).Select(RequiredObject).ToDictionary(field => RequiredString(field, "name"));

        var steeringShape = RequiredObject(fields["SteeringWheelAngle"]["sdkDeclaredShape"]);
        Assert.Equal(1, RequiredInt(steeringShape, "elementCount"));
        Assert.Equal(0, RequiredInt(steeringShape, "maxElementIndex"));
        Assert.Equal(4, RequiredInt(steeringShape, "totalByteLength"));
        Assert.True(RequiredDouble(steeringShape, "primitiveValueMaximum") > 1e20);

        var perCarShape = RequiredObject(fields["CarIdxF2Time"]["sdkDeclaredShape"]);
        Assert.Equal(64, RequiredInt(perCarShape, "elementCount"));
        Assert.Equal(63, RequiredInt(perCarShape, "maxElementIndex"));
        Assert.Equal(256, RequiredInt(perCarShape, "totalByteLength"));

        var throttleObserved = RequiredObject(RequiredObject(fields["Throttle"]["observedBySource"])[sourceId]);
        Assert.True(RequiredDouble(throttleObserved, "max") <= 1.00001);
        Assert.True(RequiredDouble(throttleObserved, "min") >= -0.00001);
    }

    [Fact]
    public void Corpus_KeepsIdentityAndRawPayloadsOutOfTrackedArtifacts()
    {
        var corpus = ReadCorpus();
        var text = corpus.ToJsonString().ToLowerInvariant();
        Assert.DoesNotContain("telemetry.bin", text);
        Assert.DoesNotContain(".ibt", text);
        Assert.DoesNotContain("latest-session.yaml", text);
        Assert.DoesNotContain("driverinfo", text);
        Assert.DoesNotContain("weekendinfo", text);

        foreach (var source in RequiredArray(corpus["sources"]).Select(RequiredObject))
        {
            var identityShape = RequiredObject(source["identityShape"]);
            Assert.True(RequiredInt(identityShape, "driverCount") > 0);
            Assert.True(RequiredInt(identityShape, "hasUserNameCount") > 0);
            Assert.True(RequiredInt(identityShape, "hasTeamNameCount") > 0);
            Assert.Null(identityShape["Drivers"]);
            Assert.Null(identityShape["UserName"]);
            Assert.Null(identityShape["TeamName"]);
            Assert.Null(identityShape["UserID"]);
            Assert.Null(identityShape["TeamID"]);
        }

        var fields = RequiredArray(corpus["fields"]).Select(RequiredObject).ToDictionary(field => RequiredString(field, "name"));
        var sessionUniqueIdBySource = RequiredObject(fields["SessionUniqueID"]["observedBySource"]);
        foreach (var stats in sessionUniqueIdBySource.Select(source => RequiredObject(source.Value)))
        {
            Assert.True(RequiredBool(stats, "valueSummaryRedacted"));
            Assert.Null(stats["min"]);
            Assert.Null(stats["max"]);
            Assert.Null(stats["distinctValues"]);
        }
    }

    private static JsonObject ReadCorpus()
    {
        var path = FindRepoRootFile(CorpusRelativePath);
        return RequiredObject(JsonNode.Parse(File.ReadAllText(path)));
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
        return Assert.IsAssignableFrom<JsonValue>(value[propertyName]).GetValue<string>();
    }

    private static int RequiredInt(JsonObject value, string propertyName)
    {
        return Assert.IsAssignableFrom<JsonValue>(value[propertyName]).GetValue<int>();
    }

    private static bool RequiredBool(JsonObject value, string propertyName)
    {
        return Assert.IsAssignableFrom<JsonValue>(value[propertyName]).GetValue<bool>();
    }

    private static double RequiredDouble(JsonObject value, string propertyName)
    {
        return Assert.IsAssignableFrom<JsonValue>(value[propertyName]).GetValue<double>();
    }
}
