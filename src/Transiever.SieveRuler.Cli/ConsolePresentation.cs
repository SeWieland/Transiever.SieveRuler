using Transiever.SieveRuler.Models;

namespace Transiever.SieveRuler.Cli;

public static class ConsolePresentation
{
    public static void PrintHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  srtx inspect  [--rules rules.json]");
        Console.WriteLine("  srtx optimize [mode] [--rules rules.json] [--output rules.optimized.json]");
        Console.WriteLine("  srtx generate [--rules rules.json] [--sieve rules.sieve] [optimization]");
        Console.WriteLine("  srtx preview  [--rules rules.json] [--script-name name] [--reconciled-rules reconciled-rules.json] [--candidate-rules candidate-rules.json]");
        Console.WriteLine("  srtx deploy   [--plan deployment-plan.json] [--history-limit count] [--no-prune-history]");
        Console.WriteLine("  srtx rollback [--plan deployment-plan.json] [--force]");
        Console.WriteLine("  srtx history list");
        Console.WriteLine("  srtx history show <script-name> [--sieve restored.sieve]");
        Console.WriteLine("  srtx history restore <script-name|latest|original> [--dry-run] [--force]");
        Console.WriteLine("  srtx history delete <script-name|original> [--dry-run]");
        Console.WriteLine("  srtx history prune [--dry-run]");
        Console.WriteLine();
        Console.WriteLine("Optimization: conservative, balanced, aggressive; aliases -o, -oo, -ooo.");
        Console.WriteLine("Synchronization options: --script-name, --adopt-compatible, --preserve-compatible, --dry-run.");
        Console.WriteLine("ManageSieve options: --sieve-host, --sieve-port, --sieve-username, --sieve-password, --sieve-security-mode override TRANSIEVER_SIEVE_*.");
        Console.WriteLine("Generated managed rules include provider UI metadata comments for Open-Xchange-compatible rule editors.");
        Console.WriteLine("Preview preserves the current active script name by default; deploy activates the previewed candidate after creating any required server-side backup.");
        Console.WriteLine("Deploy prunes inactive SieveRuler history automatically, keeping the oldest backup plus the newest retained history scripts.");
        Console.WriteLine("History restore creates a fresh backup before restoring a retained SieveRuler version, latest backup, or original state.");
        Console.WriteLine("History delete and prune remove inactive SieveRuler-owned history; prune keeps the active script and non-SieveRuler scripts.");
        Console.WriteLine("Review artifacts: --reconciled-rules, --candidate-rules, --candidate, --server-snapshot, --plan.");
    }

    public static void PrintOptimization(RuleOptimizationResult result)
    {
        Console.WriteLine(
            $"Optimized {result.OriginalRuleCount} rules into {result.OptimizedRuleCount} rules.");
        foreach (RuleOptimizationDiagnostic diagnostic in result.Diagnostics)
        {
            Console.WriteLine(
                $"[{diagnostic.Severity}] {diagnostic.Action}: {diagnostic.Message}");
        }
    }

    public static void PrintDiagnostics(
        IEnumerable<ReconciliationDiagnostic> diagnostics)
    {
        foreach (ReconciliationDiagnostic diagnostic in diagnostics)
        {
            Console.WriteLine(
                $"[{diagnostic.Severity}] {diagnostic.Code}: {diagnostic.Message}");
        }
    }
}
