
using Transiever.SieveRuler.Models;

namespace Transiever.SieveRuler.Services;

/// <summary>
/// Renders rule definitions into Sieve text.
/// </summary>
public interface ISieveGenerator
{
    string Generate(IEnumerable<RuleDefinition> rules, string sourceFile = "rules.json");

    string GenerateRuleBody(IEnumerable<RuleDefinition> rules);

    IReadOnlyCollection<string> GetRequiredCapabilities(IEnumerable<RuleDefinition> rules);
}
