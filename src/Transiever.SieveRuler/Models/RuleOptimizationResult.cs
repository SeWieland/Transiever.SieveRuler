namespace Transiever.SieveRuler.Models;

public sealed record RuleOptimizationResult
{
    public IReadOnlyCollection<RuleDefinition> Rules { get; init; } = [];

    public IReadOnlyCollection<RuleOptimizationDiagnostic> Diagnostics { get; init; } = [];

    public int OriginalRuleCount { get; init; }

    public int OptimizedRuleCount => Rules.Count;
}
