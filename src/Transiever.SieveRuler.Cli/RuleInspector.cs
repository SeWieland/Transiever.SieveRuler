using Transiever.SieveRuler.Models;

namespace Transiever.SieveRuler.Cli;

public static class RuleInspector
{
    public static void Print(IReadOnlyCollection<RuleDefinition> rules, string sourceFile)
    {
        Console.WriteLine($"Rules file: {sourceFile}");
        Console.WriteLine($"Rules: {rules.Count}");
        Console.WriteLine();

        if (rules.Count == 0)
        {
            Console.WriteLine("No rules found.");
            return;
        }

        PrintSummary(rules);
        Console.WriteLine();

        var index = 1;

        foreach (RuleDefinition rule in rules)
        {
            PrintRule(index, rule);
            index++;
        }

        PrintWarnings(rules);
    }

    private static void PrintSummary(IReadOnlyCollection<RuleDefinition> rules)
    {
        int folders = rules
            .SelectMany(GetDeliveryFolders)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        int conditionCount = rules.Sum(rule => rule.Conditions.Count);
        int exceptionCount = rules.Sum(rule => rule.Exceptions.Count);
        int actionCount = rules.Sum(rule => EffectiveActions(rule).Count());

        Console.WriteLine("Summary:");
        Console.WriteLine($"  Target folders: {folders}");
        Console.WriteLine($"  Conditions: {conditionCount}");
        Console.WriteLine($"  Exceptions: {exceptionCount}");
        Console.WriteLine($"  Actions: {actionCount}");

        foreach (IGrouping<RuleConditionType, RuleCondition> group in rules
            .SelectMany(rule => rule.Conditions)
            .GroupBy(condition => condition.Type)
            .OrderBy(group => group.Key.ToString()))
        {
            Console.WriteLine($"    {group.Key}: {group.Count()}");
        }

        foreach (IGrouping<RuleActionType, RuleAction> group in rules
            .SelectMany(EffectiveActions)
            .GroupBy(action => action.Type)
            .OrderBy(group => group.Key.ToString()))
        {
            Console.WriteLine($"    {group.Key}: {group.Count()}");
        }
    }

    private static void PrintRule(int index, RuleDefinition rule)
    {
        string name = string.IsNullOrWhiteSpace(rule.Name)
            ? "<unnamed rule>"
            : rule.Name;

        Console.WriteLine($"[{index:000}] {name}");
        Console.WriteLine($"  Folder: {DisplayValue(rule.TargetFolder)}");
        Console.WriteLine($"  Mode: {rule.ConditionMode}");
        PrintActions(rule);
        PrintConditions("Conditions", rule.Conditions);
        PrintConditions("Exceptions", rule.Exceptions);
        Console.WriteLine();
    }

    private static void PrintActions(RuleDefinition rule)
    {
        RuleAction[] actions = EffectiveActions(rule).ToArray();

        if (actions.Length == 0)
        {
            Console.WriteLine("  Actions: <none>");
            return;
        }

        Console.WriteLine("  Actions:");

        foreach (RuleAction action in actions)
        {
            string[] values = action.Values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            if (values.Length == 0)
            {
                Console.WriteLine($"    - {action.Type}");
                continue;
            }

            Console.WriteLine($"    - {action.Type}: {string.Join(" | ", values)}");
        }
    }

    private static void PrintConditions(
        string label,
        IReadOnlyCollection<RuleCondition> conditions)
    {
        if (conditions.Count == 0)
        {
            Console.WriteLine($"  {label}: <none>");
            return;
        }

        Console.WriteLine($"  {label}:");

        foreach (RuleCondition condition in conditions)
        {
            string[] values = condition.Values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            if (values.Length == 0)
            {
                Console.WriteLine($"    - {condition.Type}: <empty>");
                continue;
            }

            Console.WriteLine($"    - {condition.Type}: {string.Join(" | ", values)}");
        }
    }

    private static void PrintWarnings(IReadOnlyCollection<RuleDefinition> rules)
    {
        var warnings = new List<string>();

        var index = 1;

        foreach (RuleDefinition rule in rules)
        {
            string prefix = $"Rule {index:000}";

            if (string.IsNullOrWhiteSpace(rule.Name))
            {
                warnings.Add($"{prefix}: missing rule name.");
            }

            if (!EffectiveActions(rule).Any())
            {
                warnings.Add($"{prefix}: no actions.");
            }

            if (rule.Conditions.Count == 0)
            {
                warnings.Add($"{prefix}: no conditions.");
            }
            else if (rule.Conditions.All(condition => condition.Values.Count == 0))
            {
                warnings.Add($"{prefix}: all conditions are empty.");
            }

            index++;
        }

        if (warnings.Count == 0)
        {
            return;
        }

        Console.WriteLine("Warnings:");

        foreach (string warning in warnings)
        {
            Console.WriteLine($"  - {warning}");
        }

        if (warnings.Count == rules.Count)
        {
            Console.WriteLine();
            Console.WriteLine("Every rule looks empty. Check whether the JSON file was generated by the current Transiever.SieveRuler version.");
        }
    }

    private static IEnumerable<string> GetDeliveryFolders(RuleDefinition rule)
    {
        foreach (RuleAction action in EffectiveActions(rule))
        {
            if (action.Type is RuleActionType.FileInto or RuleActionType.CopyInto)
            {
                foreach (string value in action.Values)
                    yield return value;
            }
        }
    }

    private static IEnumerable<RuleAction> EffectiveActions(RuleDefinition rule)
    {
        if (rule.Actions.Count > 0)
        {
            foreach (RuleAction action in rule.Actions)
                yield return action;

            yield break;
        }

        if (!string.IsNullOrWhiteSpace(rule.TargetFolder))
        {
            yield return new RuleAction
            {
                Type = RuleActionType.FileInto,
                Values = [rule.TargetFolder]
            };
        }
    }

    private static string DisplayValue(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "<missing>"
            : value;
    }
}
