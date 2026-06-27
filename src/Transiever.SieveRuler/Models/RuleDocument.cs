using System.Text.Json.Serialization;

namespace Transiever.SieveRuler.Models;

public sealed record RuleDocument
{
    public const int CurrentSchemaVersion = 1;

    public const string SchemaId = "urn:sieveruler:rules:v1";

    [JsonPropertyName("$schema")]
    public string Schema { get; init; } = SchemaId;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    [JsonPropertyName("sourceId")]
    public string SourceId { get; init; } = "generic";

    [JsonPropertyName("rules")]
    public List<RuleDefinition> Rules { get; init; } = [];

    [JsonPropertyName("diagnostics")]
    public List<ReconciliationDiagnostic> Diagnostics { get; init; } = [];
}

public sealed record ReconciliationDiagnostic
{
    [JsonPropertyName("severity")]
    public string Severity { get; init; } = "Info";

    [JsonPropertyName("code")]
    public string Code { get; init; } = "";

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
}

public sealed record ServerScriptSnapshot
{
    [JsonPropertyName("activeScriptName")]
    public required string ActiveScriptName { get; init; }

    [JsonPropertyName("contentSha256")]
    public required string ContentSha256 { get; init; }

    [JsonPropertyName("storedScriptNames")]
    public List<string> StoredScriptNames { get; init; } = [];

    [JsonPropertyName("sieveCapabilities")]
    public List<string> SieveCapabilities { get; init; } = [];
}

public sealed record DeploymentPlan
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string SourceActiveScriptName { get; init; } = "";

    public string SourceContentSha256 { get; init; } = "";

    public string CandidateContentBase64 { get; init; } = "";

    public string CandidateContentSha256 { get; init; } = "";

    public string? TargetScriptName { get; init; }

    public string? BackupScriptName { get; init; }

    public string? BackupContentSha256 { get; init; }

    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
