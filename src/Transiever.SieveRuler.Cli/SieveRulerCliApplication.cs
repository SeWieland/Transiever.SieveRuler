using Transiever.SieveRuler.Application;
using Transiever.SieveRuler.Models;
using Transiever.SieveRuler.Services;
using System.Text;

namespace Transiever.SieveRuler.Cli;

public sealed class SieveRulerCliApplication(
    SieveRulerApplication application,
    ISieveSynchronizationWorkflow synchronizationWorkflow,
    ISieveServerConfigurationProvider configurationProvider)
{
    public Task<int> RunAsync(
        CommandLineOptions options,
        CancellationToken cancellationToken = default) =>
        options.Command switch
        {
            SieveRulerCommand.Inspect => InspectAsync(options, cancellationToken),
            SieveRulerCommand.Optimize => OptimizeAsync(options, cancellationToken),
            SieveRulerCommand.Generate => GenerateAsync(options, cancellationToken),
            SieveRulerCommand.Preview => PreviewAsync(options, cancellationToken),
            SieveRulerCommand.Deploy => DeployAsync(options, cancellationToken),
            SieveRulerCommand.Rollback => RollbackAsync(options, cancellationToken),
            SieveRulerCommand.History => HistoryAsync(options, cancellationToken),
            _ => throw new InvalidOperationException(
                $"Unsupported command: {options.Command}")
        };

    private async Task<int> InspectAsync(
        CommandLineOptions options,
        CancellationToken cancellationToken)
    {
        InspectRulesResult result = await application.InspectAsync(
            new InspectRulesRequest(options.RulesFile),
            cancellationToken);
        Console.WriteLine($"Source: {result.Document.SourceId}");
        RuleInspector.Print(result.Document.Rules, result.SourceFile);
        return 0;
    }

    private async Task<int> OptimizeAsync(
        CommandLineOptions options,
        CancellationToken cancellationToken)
    {
        OptimizeRulesResult result = await application.OptimizeAsync(
            new OptimizeRulesRequest(
                options.RulesFile,
                options.OutputFile,
                options.OptimizationMode ?? RuleOptimizationMode.Conservative,
                options.DryRun),
            cancellationToken);
        if (result.FilesWritten)
        {
            Console.WriteLine($"Wrote {result.OutputFile}.");
        }

        ConsolePresentation.PrintOptimization(result.Optimization);
        return 0;
    }

    private async Task<int> GenerateAsync(
        CommandLineOptions options,
        CancellationToken cancellationToken)
    {
        GenerateSieveResult result = await application.GenerateAsync(
            new GenerateSieveRequest(
                options.RulesFile,
                options.OutputFile,
                options.SieveFile,
                options.OptimizationMode,
                options.DryRun),
            cancellationToken);
        if (result.Optimization is not null)
        {
            ConsolePresentation.PrintOptimization(result.Optimization);
        }

        Console.WriteLine(
            result.FilesWritten
                ? $"Generated {result.SieveFile} from {result.RuleCount} rules."
                : $"Generated Sieve from {result.RuleCount} rules. No files written.");
        return 0;
    }

    private async Task<int> PreviewAsync(
        CommandLineOptions options,
        CancellationToken cancellationToken)
    {
        PreviewSynchronizationResult result =
            await synchronizationWorkflow.PreviewAsync(
                new PreviewSynchronizationRequest(
                    configurationProvider.GetConfiguration(),
                    options.RulesFile,
                    options.ReconciledRulesFile,
                    options.CandidateRulesFile,
                    options.ServerSnapshotFile,
                    options.CandidateFile,
                    options.PlanFile,
                    options.AdoptCompatible,
                    options.OptimizationMode,
                    options.DryRun,
                    TargetScriptName: options.ScriptName),
                cancellationToken);
        ConsolePresentation.PrintDiagnostics(result.Diagnostics);

        return result.Status switch
        {
            PreviewSynchronizationStatus.Prepared =>
                PrintPreparedPreview(result, options),
            PreviewSynchronizationStatus.Blocked =>
                PrintError("Candidate generation is blocked by reconciliation errors."),
            PreviewSynchronizationStatus.MissingCapabilities =>
                PrintError(
                    $"Server does not advertise required Sieve capabilities: {string.Join(", ", result.MissingCapabilities)}."),
            PreviewSynchronizationStatus.InsufficientSpace =>
                PrintError("The server reported insufficient space for the candidate script."),
            _ => throw new InvalidOperationException(
                $"Unsupported preview status: {result.Status}")
        };
    }

    private async Task<int> DeployAsync(
        CommandLineOptions options,
        CancellationToken cancellationToken)
    {
        DeploySynchronizationResult result =
            await synchronizationWorkflow.DeployAsync(
                new DeploySynchronizationRequest(
                    options.DryRun
                        ? null
                        : configurationProvider.GetConfiguration(),
                    options.PlanFile,
                    options.Activate,
                    options.DryRun,
                    options.HistoryLimit,
                    options.PruneHistory),
                cancellationToken);

        switch (result.Status)
        {
            case DeploySynchronizationStatus.PlanValidated:
                Console.WriteLine(
                    $"Deployment plan is valid for target script '{result.ScriptName}'. No server changes were made.");
                PrintDeploymentCleanup(result);
                return 0;
            case DeploySynchronizationStatus.Skipped:
                Console.WriteLine(
                    "Deployment skipped. No server changes were made.");
                PrintDeploymentCleanup(result);
                return 0;
            case DeploySynchronizationStatus.UploadedInactive:
                Console.WriteLine(
                    $"Uploaded inactive script '{result.ScriptName}'.");
                PrintDeploymentCleanup(result);
                return 0;
            case DeploySynchronizationStatus.Activated:
                Console.WriteLine(
                    $"Activated '{result.ScriptName}'. Previous script '{result.PreviousActiveScriptName}' was retained.");
                PrintDeploymentCleanup(result);
                return 0;
            case DeploySynchronizationStatus.ReplacedActive:
                Console.WriteLine(
                    $"Replaced active script '{result.ScriptName}'. Backup '{result.BackupScriptName}' was retained.");
                PrintDeploymentCleanup(result);
                return 0;
            case DeploySynchronizationStatus.InsufficientSpace:
                PrintDeploymentCleanup(result);
                return PrintError(
                    "The server reported insufficient space for the target or backup script.");
            default:
                throw new InvalidOperationException(
                    $"Unsupported deployment status: {result.Status}");
        }
    }

    private async Task<int> RollbackAsync(
        CommandLineOptions options,
        CancellationToken cancellationToken)
    {
        RollbackSynchronizationResult result =
            await synchronizationWorkflow.RollbackAsync(
                new RollbackSynchronizationRequest(
                    options.DryRun
                        ? null
                        : configurationProvider.GetConfiguration(),
                    options.PlanFile,
                    options.Force,
                    options.DryRun),
                cancellationToken);

        switch (result.Status)
        {
            case RollbackSynchronizationStatus.PlanValidated:
                Console.WriteLine(
                    $"Rollback plan is valid for target script '{result.TargetScriptName}'. No server changes were made.");
                return 0;
            case RollbackSynchronizationStatus.ReactivatedSource:
                Console.WriteLine(
                    result.RestoredScriptName is null
                        ? "Rollback disabled active Sieve processing."
                        : $"Rollback reactivated '{result.RestoredScriptName}'.");
                return 0;
            case RollbackSynchronizationStatus.RestoredBackup:
                Console.WriteLine(
                    $"Rollback restored '{result.TargetScriptName}' from backup '{result.BackupScriptName}'.");
                return 0;
            default:
                throw new InvalidOperationException(
                    $"Unsupported rollback status: {result.Status}");
        }
    }

    private Task<int> HistoryAsync(
        CommandLineOptions options,
        CancellationToken cancellationToken) =>
        options.HistoryAction switch
        {
            SieveRulerHistoryAction.List =>
                HistoryListAsync(options, cancellationToken),
            SieveRulerHistoryAction.Show =>
                HistoryShowAsync(options, cancellationToken),
            SieveRulerHistoryAction.Restore =>
                HistoryRestoreAsync(options, cancellationToken),
            SieveRulerHistoryAction.Delete =>
                HistoryDeleteAsync(options, cancellationToken),
            SieveRulerHistoryAction.Prune =>
                HistoryPruneAsync(options, cancellationToken),
            _ => throw new InvalidOperationException(
                "Unsupported or missing history subcommand.")
        };

    private async Task<int> HistoryListAsync(
        CommandLineOptions options,
        CancellationToken cancellationToken)
    {
        HistoryListResult result = await synchronizationWorkflow.ListHistoryAsync(
            new HistoryListRequest(configurationProvider.GetConfiguration()),
            cancellationToken);
        Console.WriteLine(
            result.ActiveScriptName.Length == 0
                ? "Active script: <none>"
                : $"Active script: {result.ActiveScriptName}");
        Console.WriteLine("Kind      Created UTC           Flags      Name");
        foreach (SieveHistoryEntry entry in result.Entries)
        {
            string flags = string.Concat(
                entry.IsActive ? "active " : "",
                entry.IsOriginal ? "original" : "").Trim();
            Console.WriteLine(
                $"{FormatKind(entry.Kind),-9} {entry.CreatedUtc:yyyy-MM-dd HH:mm:ss} {flags,-10} {entry.Name}");
        }

        return 0;
    }

    private async Task<int> HistoryShowAsync(
        CommandLineOptions options,
        CancellationToken cancellationToken)
    {
        string scriptName = options.HistoryScriptName ??
            throw new InvalidOperationException("History script name is required.");
        HistoryShowResult result = await synchronizationWorkflow.ShowHistoryAsync(
            new HistoryShowRequest(
                configurationProvider.GetConfiguration(),
                scriptName),
            cancellationToken);
        if (options.SieveFileSpecified)
        {
            await File.WriteAllBytesAsync(
                options.SieveFile,
                result.Content,
                cancellationToken);
            Console.WriteLine(
                $"Wrote history script '{result.Entry.Name}' to {options.SieveFile}.");
            return 0;
        }

        PrintHistoryEntry(result.Entry);
        Console.WriteLine();
        Console.Write(Encoding.UTF8.GetString(result.Content));
        return 0;
    }

    private async Task<int> HistoryRestoreAsync(
        CommandLineOptions options,
        CancellationToken cancellationToken)
    {
        string scriptName = options.HistoryScriptName ??
            throw new InvalidOperationException("History script name is required.");
        HistoryRestoreResult result =
            await synchronizationWorkflow.RestoreHistoryAsync(
                new HistoryRestoreRequest(
                    options.DryRun
                        ? configurationProvider.GetConfiguration()
                        : configurationProvider.GetConfiguration(),
                    scriptName,
                    options.Force,
                    options.DryRun),
                cancellationToken);

        switch (result.Status)
        {
            case HistoryRestoreStatus.PlanValidated:
                Console.WriteLine(
                    $"History restore is valid for '{result.SourceScriptName}'. No server changes were made.");
                return 0;
            case HistoryRestoreStatus.AlreadyActive:
                Console.WriteLine(
                    $"History script '{result.SourceScriptName}' already matches the active state.");
                return 0;
            case HistoryRestoreStatus.RestoredScript:
                Console.WriteLine(
                    $"Restored '{result.SourceScriptName}' into active script '{result.TargetScriptName}'. Backup '{result.BackupScriptName}' was retained.");
                return 0;
            case HistoryRestoreStatus.DisabledActive:
                Console.WriteLine(
                    $"Restored original no-active state from '{result.SourceScriptName}'. Backup '{result.BackupScriptName}' was retained.");
                return 0;
            default:
                throw new InvalidOperationException(
                    $"Unsupported history restore status: {result.Status}");
        }
    }

    private async Task<int> HistoryDeleteAsync(
        CommandLineOptions options,
        CancellationToken cancellationToken)
    {
        string scriptName = options.HistoryScriptName ??
            throw new InvalidOperationException("History script name is required.");
        HistoryDeleteResult result =
            await synchronizationWorkflow.DeleteHistoryAsync(
                new HistoryDeleteRequest(
                    configurationProvider.GetConfiguration(),
                    scriptName,
                    options.DryRun),
                cancellationToken);

        switch (result.Status)
        {
            case HistoryDeleteStatus.PlanValidated:
                Console.WriteLine(
                    $"History script '{result.ScriptName}' can be deleted. No server changes were made.");
                return 0;
            case HistoryDeleteStatus.Deleted:
                Console.WriteLine(
                    $"Deleted history script '{result.ScriptName}'.");
                return 0;
            default:
                throw new InvalidOperationException(
                    $"Unsupported history delete status: {result.Status}");
        }
    }

    private async Task<int> HistoryPruneAsync(
        CommandLineOptions options,
        CancellationToken cancellationToken)
    {
        HistoryPruneResult result =
            await synchronizationWorkflow.PruneHistoryAsync(
                new HistoryPruneRequest(
                    configurationProvider.GetConfiguration(),
                    options.DryRun),
                cancellationToken);

        switch (result.Status)
        {
            case HistoryPruneStatus.PlanValidated:
                PrintHistoryPruneScripts(
                    result.DeletedScriptNames,
                    "Would delete history script");
                Console.WriteLine("History prune is valid. No server changes were made.");
                return 0;
            case HistoryPruneStatus.Pruned:
                if (result.DeletedScriptNames.Count > 0)
                {
                    PrintHistoryPruneScripts(
                        result.DeletedScriptNames,
                        "Deleted history script");
                }
                else if (result.Warnings.Count == 0)
                {
                    Console.WriteLine("No inactive SieveRuler history scripts matched.");
                }

                foreach (string warning in result.Warnings)
                    Console.Error.WriteLine($"Warning: {warning}");
                return 0;
            default:
                throw new InvalidOperationException(
                    $"Unsupported history prune status: {result.Status}");
        }
    }

    private static int PrintPreparedPreview(
        PreviewSynchronizationResult result,
        CommandLineOptions options)
    {
        Console.WriteLine(
            $"Prepared candidate with {result.ManagedRuleCount} managed rules. No server changes were made.");
        if (result.TargetScriptName is not null)
        {
            Console.WriteLine(
                result.ReplacesActiveScript
                    ? $"Target script '{result.TargetScriptName}' is the current active script and will be replaced in place during deployment."
                    : $"Target script: {result.TargetScriptName}");
        }

        if (result.FilesWritten)
        {
            Console.WriteLine(
                $"Review {options.ReconciledRulesFile}, {options.CandidateRulesFile}, {options.ServerSnapshotFile}, {options.CandidateFile}, and {options.PlanFile}.");
        }

        return 0;
    }

    private static int PrintError(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }

    private static void PrintDeploymentCleanup(DeploySynchronizationResult result)
    {
        foreach (string scriptName in result.DeletedHistoryScriptNames)
        {
            Console.WriteLine(
                $"Deleted obsolete SieveRuler history script '{scriptName}'.");
        }

        foreach (string warning in result.CleanupWarnings)
        {
            Console.Error.WriteLine($"Warning: {warning}");
        }
    }

    private static void PrintHistoryPruneScripts(
        IReadOnlyList<string> scriptNames,
        string prefix)
    {
        if (scriptNames.Count == 0)
        {
            Console.WriteLine("No inactive SieveRuler history scripts matched.");
            return;
        }

        foreach (string scriptName in scriptNames)
            Console.WriteLine($"{prefix} '{scriptName}'.");
    }

    private static string FormatKind(SieveHistoryEntryKind kind) =>
        kind switch
        {
            SieveHistoryEntryKind.Backup => "backup",
            SieveHistoryEntryKind.Candidate => "candidate",
            SieveHistoryEntryKind.NoActiveOriginalMarker => "no-active",
            _ => kind.ToString()
        };

    private static void PrintHistoryEntry(SieveHistoryEntry entry)
    {
        Console.WriteLine($"Name: {entry.Name}");
        Console.WriteLine($"Kind: {FormatKind(entry.Kind)}");
        Console.WriteLine($"Created UTC: {entry.CreatedUtc:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"Active: {entry.IsActive}");
        Console.WriteLine($"Original: {entry.IsOriginal}");
        if (entry.ContentLength is not null)
            Console.WriteLine($"Bytes: {entry.ContentLength}");
        if (entry.ContentSha256 is not null)
            Console.WriteLine($"SHA256: {entry.ContentSha256}");
    }
}
