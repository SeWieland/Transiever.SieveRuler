using Transiever.SieveRuler.Models;

namespace Transiever.SieveRuler.Services;

/// <summary>
/// Default rule optimizer.
/// </summary>
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
            if (!TryCreateCandidate(rule, mode, out var candidate))
            {
                optimizedRules.Add(CloneRule(rule));
                continue;
            }

            if (candidate.ConditionType != RuleConditionType.HasAttachment &&
                candidate.Values.Count == 0)
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

            if (!groups.TryGetValue(candidate.Key, out var group))
            {
                group = new OptimizationGroup(
                    candidate.Key,
                    optimizedRules.Count,
                    DisplayRuleName(rule),
                    candidate.Actions,
                    candidate.Exceptions);

                groups.Add(candidate.Key, group);
                optimizedRules.Add(BuildRule(group));
            }

            group.AddRule(candidate.ConditionType, candidate.Values);
            optimizedRules[group.OutputIndex] = BuildRule(group);
        }

        AddMergeDiagnostics(groups.Values, diagnostics);

        if (mode != RuleOptimizationMode.Conservative)
        {
            foreach (var group in groups.Values)
            {
                if (group.TryGetValues(
                    RuleConditionType.SenderContains,
                    out HashSet<string> senderValues))
                {
                    SenderDomainInferenceResult inference = SenderDomainInference.Apply(
                        senderValues,
                        mode,
                        group.Key.TargetFolder);

                    group.ReplaceAllValues(
                        RuleConditionType.SenderContains,
                        inference.Values);
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

    private static bool TryCreateCandidate(
        RuleDefinition rule,
        RuleOptimizationMode mode,
        out OptimizationCandidate candidate)
    {
        candidate = default!;

        if (rule.Conditions.Count != 1)
            return false;

        IReadOnlyList<RuleAction> actions = GetEffectiveActions(rule);
        if (actions.Count == 0)
            return false;

        string targetFolder = GetFirstDeliveryFolder(actions);
        if (string.IsNullOrWhiteSpace(targetFolder))
            return false;

        var condition = rule.Conditions[0];
        IReadOnlyList<RuleCondition> exceptions = rule.Exceptions
            .Select(CloneCondition)
            .ToList();

        var key = new OptimizationKey(
            targetFolder.Trim(),
            CreateActionSignature(actions),
            CreateConditionSignature(exceptions),
            mode == RuleOptimizationMode.Conservative
                ? condition.Type
                : null);
        candidate = new OptimizationCandidate(
            key,
            condition.Type,
            CleanValues(condition.Values).ToArray(),
            actions,
            exceptions);

        return true;
    }

    private static IReadOnlyList<RuleAction> GetEffectiveActions(RuleDefinition rule)
    {
        if (rule.Actions.Count > 0)
            return rule.Actions.Select(CloneAction).ToList();

        return string.IsNullOrWhiteSpace(rule.TargetFolder)
            ? []
            :
            [
                new RuleAction
                {
                    Type = RuleActionType.FileInto,
                    Values = [rule.TargetFolder.Trim()]
                }
            ];
    }

    private static string GetFirstDeliveryFolder(IEnumerable<RuleAction> actions)
    {
        foreach (RuleAction action in actions)
        {
            if (action.Type is not (RuleActionType.FileInto or RuleActionType.CopyInto))
                continue;

            string? folder = action.Values
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (!string.IsNullOrWhiteSpace(folder))
                return folder.Trim();
        }

        return "";
    }

    private static string CreateActionSignature(IEnumerable<RuleAction> actions) =>
        string.Join(
            "\n",
            actions.Select(action => $"{action.Type}:{CanonicalValues(action.Values)}"));

    private static string CreateConditionSignature(IEnumerable<RuleCondition> conditions) =>
        string.Join(
            "\n",
            conditions
                .Select(condition => $"{condition.Type}:{CanonicalValues(condition.Values)}")
                .Order(StringComparer.Ordinal));

    private static string CanonicalValues(IEnumerable<string> values) =>
        string.Join(
            "|",
            CleanValues(values).Select(value => value.ToUpperInvariant()));

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
            Actions = rule.Actions.Select(CloneAction).ToList(),
            ConditionMode = rule.ConditionMode,
            Conditions = rule.Conditions.Select(CloneCondition).ToList(),
            Exceptions = rule.Exceptions.Select(CloneCondition).ToList(),
            SourceId = rule.SourceId,
            Ownership = rule.Ownership,
            OriginalOrder = rule.OriginalOrder,
            RequiredCapabilities = [.. rule.RequiredCapabilities]
        };
    }

    private static RuleAction CloneAction(RuleAction action)
    {
        return new RuleAction
        {
            Type = action.Type,
            Values = CleanValues(action.Values).ToList()
        };
    }

    private static RuleCondition CloneCondition(RuleCondition condition)
    {
        return new RuleCondition
        {
            Type = condition.Type,
            Values = CleanValues(condition.Values).ToList()
        };
    }

    private static RuleDefinition BuildRule(OptimizationGroup group)
    {
        return new RuleDefinition
        {
            Name = group.RuleCount == 1
                ? group.FirstRuleName
                : $"Optimized: {group.Key.TargetFolder} / {group.ConditionSummary}",
            TargetFolder = group.Key.TargetFolder,
            Actions = group.Actions.Select(CloneAction).ToList(),
            ConditionMode = group.ConditionBucketCount > 1
                ? RuleConditionMode.Any
                : RuleConditionMode.All,
            Conditions = group.BuildConditions().ToList(),
            Exceptions = group.Exceptions.Select(CloneCondition).ToList(),
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
                Message = $"Merged {group.RuleCount} {group.ConditionSummary} rules for '{group.Key.TargetFolder}'.",
                Detail = $"{group.ConditionBucketCount} condition bucket(s)."
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
        string ActionSignature,
        string ExceptionSignature,
        RuleConditionType? ConditionType);

    private sealed record OptimizationCandidate(
        OptimizationKey Key,
        RuleConditionType ConditionType,
        IReadOnlyList<string> Values,
        IReadOnlyList<RuleAction> Actions,
        IReadOnlyList<RuleCondition> Exceptions);

    private sealed class OptimizationGroup
    {
        private readonly Dictionary<RuleConditionType, HashSet<string>> conditionValues = [];

        public OptimizationGroup(
            OptimizationKey key,
            int outputIndex,
            string firstRuleName,
            IReadOnlyList<RuleAction> actions,
            IReadOnlyList<RuleCondition> exceptions)
        {
            Key = key;
            OutputIndex = outputIndex;
            FirstRuleName = firstRuleName;
            Actions = actions;
            Exceptions = exceptions;
        }

        public OptimizationKey Key { get; }

        public int OutputIndex { get; }

        public string FirstRuleName { get; }

        public IReadOnlyList<RuleAction> Actions { get; }

        public IReadOnlyList<RuleCondition> Exceptions { get; }

        public int RuleCount { get; private set; }

        public int ConditionBucketCount => conditionValues.Count;

        public string ConditionSummary =>
            conditionValues.Count == 1
                ? conditionValues.Keys.Single().ToString()
                : "Any";

        public void AddRule(
            RuleConditionType conditionType,
            IEnumerable<string> ruleValues)
        {
            RuleCount++;

            if (!conditionValues.TryGetValue(conditionType, out HashSet<string>? values))
            {
                values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                conditionValues.Add(conditionType, values);
            }

            foreach (var value in ruleValues)
                values.Add(value);
        }

        public bool TryGetValues(
            RuleConditionType conditionType,
            out HashSet<string> values) =>
            conditionValues.TryGetValue(conditionType, out values!);

        public void ReplaceAllValues(
            RuleConditionType conditionType,
            IEnumerable<string> replacements)
        {
            if (!conditionValues.TryGetValue(conditionType, out HashSet<string>? values))
                return;

            values.Clear();

            foreach (var replacement in replacements)
                values.Add(replacement);
        }

        public IEnumerable<RuleCondition> BuildConditions()
        {
            foreach ((RuleConditionType conditionType, HashSet<string> values) in conditionValues
                .OrderBy(item => item.Key.ToString(), StringComparer.Ordinal))
            {
                yield return new RuleCondition
                {
                    Type = conditionType,
                    Values = values
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                };
            }
        }
    }
}
