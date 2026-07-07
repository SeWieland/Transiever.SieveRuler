using System.Text.Json.Serialization;

namespace Transiever.SieveRuler.Models;

/// <summary>
/// A single rule condition with one or more string values.
/// </summary>
public sealed record RuleCondition
{
    [JsonPropertyName("type")]
    public RuleConditionType Type { get; init; }

    [JsonPropertyName("values")]
    public List<string> Values { get; init; } = [];
}
