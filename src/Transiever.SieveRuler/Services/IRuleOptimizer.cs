
using Transiever.SieveRuler.Models;

namespace Transiever.SieveRuler.Services;

/// <summary>
/// Optimizes a set of rules for Sieve generation.
/// </summary>
public interface IRuleOptimizer
{
    RuleOptimizationResult Optimize(
        IEnumerable<RuleDefinition> rules,
        RuleOptimizationMode mode);
}
