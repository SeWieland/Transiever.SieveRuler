
using System.Text.Json.Serialization;

namespace Transiever.SieveRuler.Models;

/// <summary>
/// A single abstract mailbox rule in the Transiever rule model.
/// </summary>
public sealed class RuleDefinition
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("targetFolder")]
    public string TargetFolder { get; set; } = "";

    [JsonPropertyName("actions")]
    public List<RuleAction> Actions { get; init; } = [];

    [JsonPropertyName("conditionMode")]
    public RuleConditionMode ConditionMode { get; init; } = RuleConditionMode.All;

    [JsonPropertyName("conditions")]
    public List<RuleCondition> Conditions { get; init; } = [];

    [JsonPropertyName("exceptions")]
    public List<RuleCondition> Exceptions { get; init; } = [];

    [JsonPropertyName("sourceId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceId { get; init; }

    [JsonPropertyName("ownership")]
    public RuleOwnership Ownership { get; init; } = RuleOwnership.Managed;

    [JsonPropertyName("originalOrder")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? OriginalOrder { get; init; }

    [JsonPropertyName("requiredCapabilities")]
    public List<string> RequiredCapabilities { get; init; } = [];
}

/// <summary>
/// Ownership classification for a rule in a composed document.
/// </summary>
public enum RuleOwnership
{
    Managed,
    External
}
