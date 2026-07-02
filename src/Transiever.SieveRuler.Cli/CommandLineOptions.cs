using Transiever.SieveRuler.Models;
using Transiever.SieveRuler.Services;
using System.Globalization;

namespace Transiever.SieveRuler.Cli;

public sealed class CommandLineOptions
{
    public SieveRulerCommand Command { get; private init; }

    public string RulesFile { get; private init; } = "rules.json";

    public string OutputFile { get; private init; } = "rules.optimized.json";

    public string SieveFile { get; private init; } = "rules.sieve";

    public bool SieveFileSpecified { get; private init; }

    public string ReconciledRulesFile { get; private init; } =
        "reconciled-rules.json";

    public string CandidateRulesFile { get; private init; } =
        "candidate-rules.json";

    public string CandidateFile { get; private init; } = "candidate.sieve";

    public string ServerSnapshotFile { get; private init; } = "server-active.sieve";

    public string PlanFile { get; private init; } = "deployment-plan.json";

    public string? ScriptName { get; private init; }

    public SieveRulerHistoryAction? HistoryAction { get; private init; }

    public string? HistoryScriptName { get; private init; }

    public RuleOptimizationMode? OptimizationMode { get; private init; }

    public bool? AdoptCompatible { get; private init; }

    public bool Force { get; private init; }

    public bool DryRun { get; private init; }

    public int HistoryLimit { get; private init; } = 5;

    public bool PruneHistory { get; private init; } = true;

    public string? SieveHost { get; private init; }

    public int? SievePort { get; private init; }

    public string? SieveUserName { get; private init; }

    public string? SievePassword { get; private init; }

    public SieveConnectionSecurity? SieveSecurity { get; private init; }

    public bool ShowHelp { get; private init; }

    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || IsHelp(args[0]))
        {
            return new CommandLineOptions { ShowHelp = true };
        }

        SieveRulerCommand command = ParseCommand(args[0])
            ?? throw new ArgumentException($"Unknown command: {args[0]}");
        var index = 1;
        var rulesFile = "rules.json";
        var outputFile = "rules.optimized.json";
        var sieveFile = "rules.sieve";
        var sieveFileSpecified = false;
        var reconciledRulesFile = "reconciled-rules.json";
        var candidateRulesFile = "candidate-rules.json";
        var candidateFile = "candidate.sieve";
        var serverSnapshotFile = "server-active.sieve";
        var planFile = "deployment-plan.json";
        string? scriptName = null;
        SieveRulerHistoryAction? historyAction = null;
        string? historyScriptName = null;
        RuleOptimizationMode? optimizationMode = command == SieveRulerCommand.Optimize
            ? RuleOptimizationMode.Conservative
            : null;
        bool? adoptCompatible = null;
        var force = false;
        var dryRun = false;
        var historyLimit = 5;
        var pruneHistory = true;
        string? sieveHost = null;
        int? sievePort = null;
        string? sieveUserName = null;
        string? sievePassword = null;
        SieveConnectionSecurity? sieveSecurity = null;

        if (command == SieveRulerCommand.Optimize &&
            index < args.Count &&
            !args[index].StartsWith("-", StringComparison.Ordinal))
        {
            optimizationMode = ParseOptimizationMode(args[index++]);
        }

        if (command == SieveRulerCommand.History)
        {
            if (index >= args.Count || args[index].StartsWith("-", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "history requires a subcommand: list, show, restore, delete, or prune.");
            }

            historyAction = ParseHistoryAction(args[index++]);
            if (historyAction is SieveRulerHistoryAction.Show or
                SieveRulerHistoryAction.Restore or
                SieveRulerHistoryAction.Delete)
            {
                if (index >= args.Count || args[index].StartsWith("-", StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"history {historyAction.ToString()!.ToLowerInvariant()} requires a script name or original.");
                }

                historyScriptName = args[index++];
            }
        }

        while (index < args.Count)
        {
            string option = args[index];
            switch (option)
            {
                case "--rules":
                    rulesFile = ReadOptionValue(args, ref index, option);
                    break;
                case "--output":
                    outputFile = ReadOptionValue(args, ref index, option);
                    break;
                case "--sieve":
                    sieveFile = ReadOptionValue(args, ref index, option);
                    sieveFileSpecified = true;
                    break;
                case "--reconciled-rules":
                    reconciledRulesFile = ReadOptionValue(args, ref index, option);
                    break;
                case "--candidate-rules":
                    candidateRulesFile = ReadOptionValue(args, ref index, option);
                    break;
                case "--candidate":
                    candidateFile = ReadOptionValue(args, ref index, option);
                    break;
                case "--server-snapshot":
                    serverSnapshotFile = ReadOptionValue(args, ref index, option);
                    break;
                case "--plan":
                    planFile = ReadOptionValue(args, ref index, option);
                    break;
                case "--script-name":
                    scriptName = ReadOptionValue(args, ref index, option);
                    break;
                case "--adopt-compatible":
                    adoptCompatible = true;
                    break;
                case "--preserve-compatible":
                    adoptCompatible = false;
                    break;
                case "--force":
                    force = true;
                    break;
                case "--optimize":
                    optimizationMode = ReadOptionalOptimizationMode(args, ref index);
                    break;
                case "--optimize-conservative":
                    optimizationMode = RuleOptimizationMode.Conservative;
                    break;
                case "--optimize-balanced":
                    optimizationMode = RuleOptimizationMode.Balanced;
                    break;
                case "--optimize-aggressive":
                    optimizationMode = RuleOptimizationMode.Aggressive;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--history-limit":
                    historyLimit = ReadNonNegativeIntOption(args, ref index, option);
                    break;
                case "--no-prune-history":
                    pruneHistory = false;
                    break;
                case "--sieve-host":
                    sieveHost = ReadOptionValue(args, ref index, option);
                    break;
                case "--sieve-port":
                    sievePort = ReadPortOption(args, ref index, option);
                    break;
                case "--sieve-username":
                    sieveUserName = ReadOptionValue(args, ref index, option);
                    break;
                case "--sieve-password":
                    sievePassword = ReadOptionValue(args, ref index, option);
                    break;
                case "--sieve-security-mode":
                    sieveSecurity = ParseSieveSecurity(
                        ReadOptionValue(args, ref index, option));
                    break;
                case "-h":
                case "--help":
                    return new CommandLineOptions { ShowHelp = true };
                default:
                    if (TryParseOptimizationShorthand(
                        option,
                        out RuleOptimizationMode shorthand))
                    {
                        optimizationMode = shorthand;
                        break;
                    }

                    throw new ArgumentException($"Unknown option: {option}");
            }

            index++;
        }

        return new CommandLineOptions
        {
            Command = command,
            RulesFile = rulesFile,
            OutputFile = outputFile,
            SieveFile = sieveFile,
            SieveFileSpecified = sieveFileSpecified,
            ReconciledRulesFile = reconciledRulesFile,
            CandidateRulesFile = candidateRulesFile,
            CandidateFile = candidateFile,
            ServerSnapshotFile = serverSnapshotFile,
            PlanFile = planFile,
            ScriptName = scriptName,
            HistoryAction = historyAction,
            HistoryScriptName = historyScriptName,
            OptimizationMode = optimizationMode,
            AdoptCompatible = adoptCompatible,
            Force = force,
            DryRun = dryRun,
            HistoryLimit = historyLimit,
            PruneHistory = pruneHistory,
            SieveHost = sieveHost,
            SievePort = sievePort,
            SieveUserName = sieveUserName,
            SievePassword = sievePassword,
            SieveSecurity = sieveSecurity
        };
    }

    private static SieveRulerCommand? ParseCommand(string value) =>
        value.ToLowerInvariant() switch
        {
            "inspect" => SieveRulerCommand.Inspect,
            "optimize" => SieveRulerCommand.Optimize,
            "generate" => SieveRulerCommand.Generate,
            "preview" => SieveRulerCommand.Preview,
            "deploy" => SieveRulerCommand.Deploy,
            "rollback" => SieveRulerCommand.Rollback,
            "history" => SieveRulerCommand.History,
            _ => null
        };

    private static SieveRulerHistoryAction ParseHistoryAction(string value) =>
        value.ToLowerInvariant() switch
        {
            "list" => SieveRulerHistoryAction.List,
            "show" => SieveRulerHistoryAction.Show,
            "restore" => SieveRulerHistoryAction.Restore,
            "delete" => SieveRulerHistoryAction.Delete,
            "prune" => SieveRulerHistoryAction.Prune,
            _ => throw new ArgumentException(
                $"Unknown history subcommand: {value}")
        };

    private static bool IsHelp(string value) =>
        value is "-h" or "--help" or "help";

    private static string ReadOptionValue(
        IReadOnlyList<string> args,
        ref int index,
        string option)
    {
        index++;
        if (index >= args.Count || args[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return args[index];
    }

    private static RuleOptimizationMode ReadOptionalOptimizationMode(
        IReadOnlyList<string> args,
        ref int index)
    {
        int valueIndex = index + 1;
        if (valueIndex >= args.Count ||
            args[valueIndex].StartsWith("-", StringComparison.Ordinal))
        {
            return RuleOptimizationMode.Conservative;
        }

        index = valueIndex;
        return ParseOptimizationMode(args[valueIndex]);
    }

    private static int ReadNonNegativeIntOption(
        IReadOnlyList<string> args,
        ref int index,
        string option)
    {
        string value = ReadOptionValue(args, ref index, option);
        if (!int.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int parsed) ||
            parsed < 0)
        {
            throw new ArgumentException($"{option} must be a non-negative integer.");
        }

        return parsed;
    }

    private static int ReadPortOption(
        IReadOnlyList<string> args,
        ref int index,
        string option)
    {
        string value = ReadOptionValue(args, ref index, option);
        if (!int.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int parsed) ||
            parsed is < 1 or > 65535)
        {
            throw new ArgumentException($"{option} must be a TCP port from 1 to 65535.");
        }

        return parsed;
    }

    private static SieveConnectionSecurity ParseSieveSecurity(string value)
    {
        if (Enum.TryParse(
            value,
            ignoreCase: true,
            out SieveConnectionSecurity mode))
        {
            return mode;
        }

        throw new ArgumentException($"Unknown Sieve security mode: {value}");
    }

    private static RuleOptimizationMode ParseOptimizationMode(string value)
    {
        if (Enum.TryParse(value, ignoreCase: true, out RuleOptimizationMode mode))
        {
            return mode;
        }

        throw new ArgumentException($"Unknown optimization mode: {value}");
    }

    private static bool TryParseOptimizationShorthand(
        string value,
        out RuleOptimizationMode mode)
    {
        mode = default;
        if (value.Length < 2 ||
            value[0] != '-' ||
            value[1..].Any(character => character != 'o'))
        {
            return false;
        }

        mode = value.Length switch
        {
            2 => RuleOptimizationMode.Conservative,
            3 => RuleOptimizationMode.Balanced,
            _ => RuleOptimizationMode.Aggressive
        };
        return true;
    }
}
