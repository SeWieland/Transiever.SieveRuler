
using System.Text.Json.Serialization;

namespace Transiever.SieveRuler.Models;

public sealed record RuleCondition
{
    [JsonPropertyName("type")]
    public RuleConditionType Type { get; init; }

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
