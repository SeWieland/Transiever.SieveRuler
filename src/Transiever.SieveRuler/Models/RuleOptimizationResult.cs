namespace Transiever.SieveRuler.Models;

/// <summary>
/// Result of optimizing a rule set.
/// </summary>
public sealed record RuleOptimizationResult
{
    public IReadOnlyCollection<RuleDefinition> Rules { get; init; } = [];

    public IReadOnlyCollection<RuleOptimizationDiagnostic> Diagnostics { get; init; } = [];

    public int OriginalRuleCount { get; init; }

    public int OptimizedRuleCount => Rules.Count;
}
