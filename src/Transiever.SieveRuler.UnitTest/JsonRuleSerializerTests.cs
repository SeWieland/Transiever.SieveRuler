using System.Text.Json;
using Transiever.SieveRuler.Models;
using Transiever.SieveRuler.Services;

namespace Transiever.SieveRuler.UnitTest;

public sealed class JsonRuleSerializerTests
{
    [Fact]
    public async Task Serializer_ReadsAndWritesVersionOneDocument()
    {
        string file = Path.Combine(
            Path.GetTempPath(),
            $"SieveRuler-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(
                file,
                """
                {
                  "$schema": "urn:sieveruler:rules:v1",
                  "schemaVersion": 1,
                  "sourceId": "outlook",
                  "rules": [
                    {
                      "name": "Inbox",
                      "targetFolder": "INBOX",
                      "conditionMode": "All",
                      "conditions": [
                        {
                          "type": "SubjectContains",
                          "values": [ "invoice" ]
                        }
                      ],
                      "ownership": "Managed",
                      "requiredCapabilities": []
                    }
                  ]
                }
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

            Assert.Equal("Inbox", Assert.Single(document.Rules).Name);
            Assert.Equal("outlook", document.SourceId);
            Assert.Contains("\"schemaVersion\": 1", saved);
            Assert.Contains("\"rules\":", saved);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Serializer_RejectsBareArray()
    {
        await using var stream = new MemoryStream(
            """[]"""u8.ToArray());

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new JsonRuleSerializer().LoadDocumentAsync(
                stream,
                TestContext.Current.CancellationToken));

        Assert.Contains("object", exception.Message);
    }

    [Fact]
    public async Task Serializer_RejectsUnsupportedVersion()
    {
        await using var stream = new MemoryStream(
            """{"schemaVersion":2,"rules":[]}"""u8.ToArray());

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new JsonRuleSerializer().LoadDocumentAsync(
                stream,
                TestContext.Current.CancellationToken));

        Assert.Contains("Unsupported rules schema version 2", exception.Message);
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
