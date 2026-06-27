using Transiever.SieveRuler.Models;
using Transiever.SieveRuler.Services;

namespace Transiever.SieveRuler.Application;

public sealed record GenerateSieveRequest(
    string RulesFile,
    string OutputFile,
    string SieveFile,
    RuleOptimizationMode? OptimizationMode = null,
    bool DryRun = false);

public sealed record InspectRulesRequest(string RulesFile);

public sealed record OptimizeRulesRequest(
    string RulesFile,
    string OutputFile,
    RuleOptimizationMode OptimizationMode,
    bool DryRun = false);

public sealed record PreviewSynchronizationRequest(
    SieveServerConfiguration Configuration,
    string RulesFile,
    string ReconciledRulesFile,
    string CandidateRulesFile,
    string ServerSnapshotFile,
    string CandidateFile,
    string PlanFile,
    bool? AdoptCompatible = null,
    RuleOptimizationMode? OptimizationMode = null,
    bool DryRun = false,
    RuleDocument? SourceDocument = null,
    string? TargetScriptName = null);

public sealed record DeploySynchronizationRequest(
    SieveServerConfiguration? Configuration,
    string PlanFile,
    bool DryRun = false,
    int HistoryLimit = 5,
    bool PruneHistory = true);

public sealed record RollbackSynchronizationRequest(
    SieveServerConfiguration? Configuration,
    string PlanFile,
    bool Force = false,
    bool DryRun = false);

public sealed record HistoryListRequest(SieveServerConfiguration Configuration);

public sealed record HistoryShowRequest(
    SieveServerConfiguration Configuration,
    string ScriptName);

public sealed record HistoryRestoreRequest(
    SieveServerConfiguration? Configuration,
    string ScriptName,
    bool Force = false,
    bool DryRun = false);

public sealed record HistoryDeleteRequest(
    SieveServerConfiguration Configuration,
    string ScriptName,
    bool DryRun = false);

public sealed record HistoryPruneRequest(
    SieveServerConfiguration Configuration,
    bool DryRun = false);

public sealed record GenerateSieveResult(
    int RuleCount,
    string SieveFile,
    RuleOptimizationResult? Optimization,
    bool FilesWritten);

public sealed record InspectRulesResult(RuleDocument Document, string SourceFile);

public sealed record OptimizeRulesResult(
    RuleDocument Document,
    RuleOptimizationResult Optimization,
    string OutputFile,
    bool FilesWritten);

public enum PreviewSynchronizationStatus
{
    Prepared,
    Blocked,
    MissingCapabilities,
    InsufficientSpace
}

public sealed record PreviewSynchronizationResult
{
    public required PreviewSynchronizationStatus Status { get; init; }

    public IReadOnlyCollection<ReconciliationDiagnostic> Diagnostics { get; init; } = [];

    public IReadOnlyCollection<string> MissingCapabilities { get; init; } = [];

    public int ManagedRuleCount { get; init; }

    public string? TargetScriptName { get; init; }

    public bool ReplacesActiveScript { get; init; }

    public bool FilesWritten { get; init; }
}

public enum DeploySynchronizationStatus
{
    PlanValidated,
    Skipped,
    UploadedInactive,
    Activated,
    ReplacedActive,
    InsufficientSpace
}

public sealed record DeploySynchronizationResult
{
    public required DeploySynchronizationStatus Status { get; init; }

    public required string ScriptName { get; init; }

    public string? PreviousActiveScriptName { get; init; }

    public string? BackupScriptName { get; init; }

    public int HistoryLimit { get; init; } = 5;

    public bool PruneHistory { get; init; } = true;

    public IReadOnlyList<string> DeletedHistoryScriptNames { get; init; } = [];

    public IReadOnlyList<string> CleanupWarnings { get; init; } = [];
}

public enum RollbackSynchronizationStatus
{
    PlanValidated,
    ReactivatedSource,
    RestoredBackup
}

public sealed record RollbackSynchronizationResult
{
    public required RollbackSynchronizationStatus Status { get; init; }

    public required string TargetScriptName { get; init; }

    public string? RestoredScriptName { get; init; }

    public string? BackupScriptName { get; init; }
}

public enum SieveHistoryEntryKind
{
    Backup,
    Candidate,
    NoActiveOriginalMarker
}

public sealed record SieveHistoryEntry
{
    public required string Name { get; init; }

    public required SieveHistoryEntryKind Kind { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public bool IsActive { get; init; }

    public bool IsOriginal { get; init; }

    public string? ContentSha256 { get; init; }

    public long? ContentLength { get; init; }
}

public sealed record HistoryListResult
{
    public required string ActiveScriptName { get; init; }

    public IReadOnlyList<SieveHistoryEntry> Entries { get; init; } = [];
}

public sealed record HistoryShowResult
{
    public required SieveHistoryEntry Entry { get; init; }

    public required byte[] Content { get; init; }
}

public enum HistoryRestoreStatus
{
    PlanValidated,
    AlreadyActive,
    RestoredScript,
    DisabledActive
}

public sealed record HistoryRestoreResult
{
    public required HistoryRestoreStatus Status { get; init; }

    public required string SourceScriptName { get; init; }

    public string? TargetScriptName { get; init; }

    public string? BackupScriptName { get; init; }

    public string? RestoredContentSha256 { get; init; }
}

public enum HistoryDeleteStatus
{
    PlanValidated,
    Deleted
}

public sealed record HistoryDeleteResult
{
    public required HistoryDeleteStatus Status { get; init; }

    public required string ScriptName { get; init; }
}

public enum HistoryPruneStatus
{
    PlanValidated,
    Pruned
}

public sealed record HistoryPruneResult
{
    public required HistoryPruneStatus Status { get; init; }

    public IReadOnlyList<string> DeletedScriptNames { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}
