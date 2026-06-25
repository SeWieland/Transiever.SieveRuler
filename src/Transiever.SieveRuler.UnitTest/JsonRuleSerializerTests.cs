using Transiever.SieveRuler.Models;
using Transiever.SieveRuler.Services;
using System.Text.Json;

namespace Transiever.SieveRuler.UnitTest;

public sealed class JsonRuleSerializerTests
{
    [Fact]
    public async Task Serializer_ReadsLegacyArrayAndWritesVersionedDocument()
    {
        string file = Path.Combine(
            Path.GetTempPath(),
            $"SieveRuler-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(
                file,
                """
                [
                  {
                    "name": "Legacy",
                    "targetFolder": "INBOX/Legacy",
                    "conditionMode": "All",
                    "conditions": [
                      {
                        "type": "SubjectContains",
                        "values": [ "legacy" ]
                      }
                    ]
                  }
                ]
                """,
                TestContext.Current.CancellationToken);
            var serializer = new JsonRuleSerializer();

            RuleDocument document = await serializer.LoadDocumentAsync(
                file,
                TestContext.Current.CancellationToken);
            await serializer.SaveDocumentAsync(
                document,
                file,
                TestContext.Current.CancellationToken);
            string saved = await File.ReadAllTextAsync(
                file,
                TestContext.Current.CancellationToken);

            Assert.Equal("Legacy", Assert.Single(document.Rules).Name);
            Assert.Equal("outlook", document.SourceId);
            Assert.Contains("\"schemaVersion\": 2", saved);
            Assert.Contains("\"rules\":", saved);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Serializer_MigratesVersionOneSourceAndOwnership()
    {
        await using var stream = new MemoryStream(
            """
            {
              "schemaVersion": 1,
              "rules": [
                {
                  "name": "Legacy",
                  "targetFolder": "INBOX/Legacy",
                  "conditionMode": "All",
                  "conditions": [],
                  "source": "Server",
                  "ownership": "OutlookResiever",
                  "requiredCapabilities": []
                }
              ]
            }
            """u8.ToArray());

        RuleDocument document = await new JsonRuleSerializer().LoadDocumentAsync(
            stream,
            TestContext.Current.CancellationToken);

        RuleDefinition rule = Assert.Single(document.Rules);
        Assert.Equal("outlook", document.SourceId);
        Assert.Equal("server", rule.SourceId);
        Assert.Equal(RuleOwnership.Managed, rule.Ownership);
    }

    [Fact]
    public async Task Serializer_RejectsInvalidVersionTwoDocument()
    {
        await using var stream = new MemoryStream(
            """{"schemaVersion":2,"rules":[]}"""u8.ToArray());

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new JsonRuleSerializer().LoadDocumentAsync(
                stream,
                TestContext.Current.CancellationToken));

        Assert.Contains("sourceId", exception.Message);
    }

    [Fact]
    public void PublishedSchema_HasStableIdentifierAndVersion()
    {
        string schemaFile = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "schemas",
                "sieveruler.rules.schema.json"));
        using JsonDocument schema = JsonDocument.Parse(File.ReadAllBytes(schemaFile));

        Assert.Equal(
            RuleDocument.SchemaId,
            schema.RootElement.GetProperty("$id").GetString());
        Assert.Contains(
            "schemaVersion",
            schema.RootElement.GetProperty("required")
                .EnumerateArray()
                .Select(value => value.GetString()));
    }
}
