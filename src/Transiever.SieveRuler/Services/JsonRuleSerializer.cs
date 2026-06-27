using Transiever.SieveRuler.Models;
using System.Text.Json;

namespace Transiever.SieveRuler.Services;

public sealed class JsonRuleSerializer : IRuleSerializer
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new StringEnumJsonConverter<RuleConditionMode>(),
            new StringEnumJsonConverter<RuleConditionType>(),
            new StringEnumJsonConverter<RuleOwnership>()
        }
    };

    public Task SaveAsync(
        IEnumerable<RuleDefinition> rules,
        string file,
        string sourceId = "generic",
        CancellationToken cancellationToken = default) =>
        SaveDocumentAsync(
            new RuleDocument
            {
                SourceId = ValidateSourceId(sourceId),
                Rules = rules.ToList()
            },
            file,
            cancellationToken);

    public async Task SaveDocumentAsync(
        RuleDocument document,
        string file,
        CancellationToken cancellationToken = default)
    {
        await using FileStream stream = File.Create(file);
        await SaveDocumentAsync(document, stream, cancellationToken);
    }

    public async Task SaveDocumentAsync(
        RuleDocument document,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);
        ValidateSourceId(document.SourceId);

        await JsonSerializer.SerializeAsync(
            destination,
            document,
            Options,
            cancellationToken);
    }

    public async Task<List<RuleDefinition>> LoadAsync(
        string file,
        CancellationToken cancellationToken = default) =>
        (await LoadDocumentAsync(file, cancellationToken)).Rules;

    public async Task<RuleDocument> LoadDocumentAsync(
        string file,
        CancellationToken cancellationToken = default)
    {
        await using FileStream stream = File.OpenRead(file);
        return await LoadDocumentAsync(stream, cancellationToken);
    }

    public async Task<RuleDocument> LoadDocumentAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        using JsonDocument json = await JsonDocument.ParseAsync(
            source,
            cancellationToken: cancellationToken);

        if (json.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "Rules JSON must be an object.");
        }

        int version = json.RootElement.TryGetProperty(
            "schemaVersion",
            out JsonElement versionElement)
            ? versionElement.GetInt32()
            : throw new InvalidDataException("Rules JSON requires a schemaVersion.");

        if (version != RuleDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported rules schema version {version}.");
        }

        if (!json.RootElement.TryGetProperty(
            "sourceId",
            out JsonElement sourceIdElement) ||
            sourceIdElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(sourceIdElement.GetString()))
        {
            throw new InvalidDataException(
                "Transiever.SieveRuler schema v1 requires a non-empty sourceId.");
        }

        if (!json.RootElement.TryGetProperty(
            "rules",
            out JsonElement rulesElement) ||
            rulesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "Transiever.SieveRuler schema v1 requires a rules array.");
        }

        if (json.RootElement.TryGetProperty(
            "$schema",
            out JsonElement schemaElement) &&
            !string.Equals(
                schemaElement.GetString(),
                RuleDocument.SchemaId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported rules schema identifier '{schemaElement.GetString()}'.");
        }

        RuleDocument document = json.RootElement.Deserialize<RuleDocument>(Options)
            ?? throw new InvalidDataException("Rules document was empty.");
        ValidateSourceId(document.SourceId);
        return document;
    }

    private static string ValidateSourceId(string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        return sourceId;
    }
}
