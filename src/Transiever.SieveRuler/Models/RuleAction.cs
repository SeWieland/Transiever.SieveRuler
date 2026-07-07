using System.Text.Json.Serialization;

namespace Transiever.SieveRuler.Models;

/// <summary>
/// A single rule action with string values.
/// </summary>
public sealed record RuleAction
{
    [JsonPropertyName("type")]
    public RuleActionType Type { get; init; }

    [JsonPropertyName("values")]
    public List<string> Values { get; init; } = [];
}
