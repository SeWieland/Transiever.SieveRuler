
using Transiever.SieveRuler.Models;

namespace Transiever.SieveRuler.Services;

public sealed class RuleOptimizer : IRuleOptimizer
{
    public RuleOptimizationResult Optimize(
        IEnumerable<RuleDefinition> rules,
        RuleOptimizationMode mode)
    {
        var sourceRules = rules.ToList();
        var optimizedRules = new List<RuleDefinition>();
        var diagnostics = new List<RuleOptimizationDiagnostic>();
        var groups = new Dictionary<OptimizationKey, OptimizationGroup>();

        foreach (var rule in sourceRules)
        {
            if (!TryCreateGroupKey(rule, out var key))
            {
                optimizedRules.Add(CloneRule(rule));
                continue;
            }

            var condition = rule.Conditions[0];
            var values = CleanValues(condition.GetValues()).ToArray();

            if (values.Length == 0)
            {
                optimizedRules.Add(CloneRule(rule));
                diagnostics.Add(new RuleOptimizationDiagnostic
                {
                    Severity = "Warning",
                    Action = "SkippedEmptyCondition",
                    Message = $"Rule '{DisplayRuleName(rule)}' has an empty condition and was not optimized."
                });
                continue;
            }

            if (!groups.TryGetValue(key, out var group))
            {
                group = new OptimizationGroup(
                    key,
                    optimizedRules.Count,
                    DisplayRuleName(rule));

                groups.Add(key, group);
                optimizedRules.Add(BuildRule(group));
            }

            group.AddRule(values);
            optimizedRules[group.OutputIndex] = BuildRule(group);
        }

        AddMergeDiagnostics(groups.Values, diagnostics);

        if (mode != RuleOptimizationMode.Conservative)
        {
            foreach (var group in groups.Values)
            {
                if (group.Key.ConditionType == RuleConditionType.SenderContains)
                {
                    SenderDomainInferenceResult inference = SenderDomainInference.Apply(
                        group.Values,
                        mode,
                        group.Key.TargetFolder);

                    group.ReplaceAllValues(inference.Values);
                    diagnostics.AddRange(inference.Diagnostics);
                }

                optimizedRules[group.OutputIndex] = BuildRule(group);
            }
        }

        return new RuleOptimizationResult
        {
            OriginalRuleCount = sourceRules.Count,
            Rules = optimizedRules,
            Diagnostics = diagnostics
        };
    }

    private static bool TryCreateGroupKey(
        RuleDefinition rule,
        out OptimizationKey key)
    {
        key = default;

        if (rule.Conditions.Count != 1)
            return false;

        if (string.IsNullOrWhiteSpace(rule.TargetFolder))
            return false;

        var condition = rule.Conditions[0];

        key = new OptimizationKey(
            rule.TargetFolder.Trim(),
            rule.ConditionMode,
            condition.Type);

        return true;
    }

    private static IEnumerable<string> CleanValues(IEnumerable<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
    }

    private static RuleDefinition CloneRule(RuleDefinition rule)
    {
        return new RuleDefinition
        {
            Name = rule.Name,
            Id = rule.Id,
            TargetFolder = rule.TargetFolder,
            ConditionMode = rule.ConditionMode,
            Conditions = rule.Conditions.Select(CloneCondition).ToList(),
            SourceId = rule.SourceId,
            Ownership = rule.Ownership,
            OriginalOrder = rule.OriginalOrder,
            RequiredCapabilities = [.. rule.RequiredCapabilities]
        };
    }

    private static RuleCondition CloneCondition(RuleCondition condition)
    {
        return new RuleCondition
        {
            Type = condition.Type,
            Values = CleanValues(condition.GetValues()).ToList()
        };
    }

    private static RuleDefinition BuildRule(OptimizationGroup group)
    {
        return new RuleDefinition
        {
            Name = group.RuleCount == 1
                ? group.FirstRuleName
                : $"Optimized: {group.Key.TargetFolder} / {group.Key.ConditionType}",
            TargetFolder = group.Key.TargetFolder,
            ConditionMode = group.Key.ConditionMode,
            Conditions =
            [
                new RuleCondition
                {
                    Type = group.Key.ConditionType,
                    Values = group.Values
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                }
            ],
            SourceId = "generated",
            Ownership = RuleOwnership.Managed
        };
    }

    private static void AddMergeDiagnostics(
        IEnumerable<OptimizationGroup> groups,
        List<RuleOptimizationDiagnostic> diagnostics)
    {
        foreach (var group in groups.Where(group => group.RuleCount > 1))
        {
            diagnostics.Add(new RuleOptimizationDiagnostic
            {
                Severity = "Info",
                Action = "MergedEquivalentRules",
                Message = $"Merged {group.RuleCount} {group.Key.ConditionType} rules for '{group.Key.TargetFolder}'.",
                Detail = $"{group.Values.Count} unique values."
            });
        }
    }

    private static string DisplayRuleName(RuleDefinition rule)
    {
        return string.IsNullOrWhiteSpace(rule.Name)
            ? "<unnamed rule>"
            : rule.Name.Trim();
    }

    private readonly record struct OptimizationKey(
        string TargetFolder,
        RuleConditionMode ConditionMode,
        RuleConditionType ConditionType);

    private sealed class OptimizationGroup
    {
        private readonly HashSet<string> values = new(StringComparer.OrdinalIgnoreCase);

        public OptimizationGroup(
            OptimizationKey key,
            int outputIndex,
            string firstRuleName)
        {
            Key = key;
            OutputIndex = outputIndex;
            FirstRuleName = firstRuleName;
        }

        public OptimizationKey Key { get; }

        public int OutputIndex { get; }

        public string FirstRuleName { get; }

        public int RuleCount { get; private set; }

        public IReadOnlyCollection<string> Values => values;

        public void AddRule(
            IEnumerable<string> ruleValues)
        {
            RuleCount++;

            foreach (var value in ruleValues)
                values.Add(value);
        }

        public void ReplaceAllValues(IEnumerable<string> replacements)
        {
            values.Clear();

            foreach (var replacement in replacements)
                values.Add(replacement);
        }
    }
}
