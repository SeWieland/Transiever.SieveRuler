using System.Text.Json.Serialization;

namespace Transiever.SieveRuler.Models;

/// <summary>
/// A single rule action with optional string values.
/// </summary>
public sealed record RuleAction
{
    [JsonPropertyName("type")]
    public RuleActionType Type { get; init; }

    [JsonPropertyName("values")]
    public List<string> Values { get; init; } = [];

    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value { get; init; }

    public IReadOnlyList<string> GetValues()
    {
        if (Values.Count > 0)
            return Values;

        return string.IsNullOrWhiteSpace(Value)
            ? []
            : [Value];
    }
}
