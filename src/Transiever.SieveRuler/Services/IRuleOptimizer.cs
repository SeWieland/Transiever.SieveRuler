
using Transiever.SieveRuler.Models;

namespace Transiever.SieveRuler.Services;

public interface IRuleOptimizer
{
    RuleOptimizationResult Optimize(
        IEnumerable<RuleDefinition> rules,
        RuleOptimizationMode mode);
}
