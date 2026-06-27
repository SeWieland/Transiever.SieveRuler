using Transiever.SieveRuler.Models;
using Transiever.SieveRuler.Services;

namespace Transiever.SieveRuler.Application;

/// <summary>
/// Requests and results for the public SieveRuler workflows.
/// </summary>
public sealed record GenerateSieveRequest(
    string RulesFile,
    string OutputFile,
    string SieveFile,
    RuleOptimizationMode? OptimizationMode = null,
    bool DryRun = false);

/// <summary>
/// Requests inspection of a serialized rules document.
/// </summary>
public sealed record InspectRulesRequest(string RulesFile);

/// <summary>
/// Requests optimization of a serialized rules document.
/// </summary>
public sealed record OptimizeRulesRequest(
    string RulesFile,
    string OutputFile,
    RuleOptimizationMode OptimizationMode,
    bool DryRun = false);

/// <summary>
/// Requests a preview of how source rules reconcile with the current server state.
/// </summary>
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

/// <summary>
/// Requests deployment of a previously prepared plan.
/// </summary>
public sealed record DeploySynchronizationRequest(
    SieveServerConfiguration? Configuration,
    string PlanFile,
    bool DryRun = false,
    int HistoryLimit = 5,
    bool PruneHistory = true);

/// <summary>
/// Requests rollback of a previously prepared plan.
/// </summary>
public sealed record RollbackSynchronizationRequest(
    SieveServerConfiguration? Configuration,
    string PlanFile,
    bool Force = false,
    bool DryRun = false);

/// <summary>
/// Requests a listing of retained history entries.
/// </summary>
public sealed record HistoryListRequest(SieveServerConfiguration Configuration);

/// <summary>
/// Requests the contents of a retained history entry.
/// </summary>
public sealed record HistoryShowRequest(
    SieveServerConfiguration Configuration,
    string ScriptName);

/// <summary>
/// Requests restoration of a retained history entry.
/// </summary>
public sealed record HistoryRestoreRequest(
    SieveServerConfiguration? Configuration,
    string ScriptName,
    bool Force = false,
    bool DryRun = false);

/// <summary>
/// Requests deletion of one retained history entry.
/// </summary>
public sealed record HistoryDeleteRequest(
    SieveServerConfiguration Configuration,
    string ScriptName,
    bool DryRun = false);

/// <summary>
/// Requests pruning of retained history entries.
/// </summary>
public sealed record HistoryPruneRequest(
    SieveServerConfiguration Configuration,
    bool DryRun = false);

/// <summary>
/// Result of generating a Sieve script.
/// </summary>
public sealed record GenerateSieveResult(
    int RuleCount,
    string SieveFile,
    RuleOptimizationResult? Optimization,
    bool FilesWritten);

/// <summary>
/// Result of loading a rules document for inspection.
/// </summary>
public sealed record InspectRulesResult(RuleDocument Document, string SourceFile);

/// <summary>
/// Result of optimizing a rules document on disk.
/// </summary>
public sealed record OptimizeRulesResult(
    RuleDocument Document,
    RuleOptimizationResult Optimization,
    string OutputFile,
    bool FilesWritten);

/// <summary>
/// Possible outcomes of a preview operation.
/// </summary>
public enum PreviewSynchronizationStatus
{
    Prepared,
    Blocked,
    MissingCapabilities,
    InsufficientSpace
}

/// <summary>
/// Result of a preview operation.
/// </summary>
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

/// <summary>
/// Possible outcomes of a deploy operation.
/// </summary>
public enum DeploySynchronizationStatus
{
    PlanValidated,
    Skipped,
    UploadedInactive,
    Activated,
    ReplacedActive,
    InsufficientSpace
}

/// <summary>
/// Result of a deploy operation.
/// </summary>
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

/// <summary>
/// Possible outcomes of a rollback operation.
/// </summary>
public enum RollbackSynchronizationStatus
{
    PlanValidated,
    ReactivatedSource,
    RestoredBackup
}

/// <summary>
/// Result of a rollback operation.
/// </summary>
public sealed record RollbackSynchronizationResult
{
    public required RollbackSynchronizationStatus Status { get; init; }

    public required string TargetScriptName { get; init; }

    public string? RestoredScriptName { get; init; }

    public string? BackupScriptName { get; init; }
}

/// <summary>
/// Classifies retained history entries.
/// </summary>
public enum SieveHistoryEntryKind
{
    Backup,
    Candidate,
    NoActiveOriginalMarker
}

/// <summary>
/// Describes one retained history entry.
/// </summary>
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

/// <summary>
/// Result of listing retained history entries.
/// </summary>
public sealed record HistoryListResult
{
    public required string ActiveScriptName { get; init; }

    public IReadOnlyList<SieveHistoryEntry> Entries { get; init; } = [];
}

/// <summary>
/// Result of reading a retained history entry.
/// </summary>
public sealed record HistoryShowResult
{
    public required SieveHistoryEntry Entry { get; init; }

    public required byte[] Content { get; init; }
}

/// <summary>
/// Possible outcomes of a history restore operation.
/// </summary>
public enum HistoryRestoreStatus
{
    PlanValidated,
    AlreadyActive,
    RestoredScript,
    DisabledActive
}

/// <summary>
/// Result of restoring a retained history entry.
/// </summary>
public sealed record HistoryRestoreResult
{
    public required HistoryRestoreStatus Status { get; init; }

    public required string SourceScriptName { get; init; }

    public string? TargetScriptName { get; init; }

    public string? BackupScriptName { get; init; }

    public string? RestoredContentSha256 { get; init; }
}

/// <summary>
/// Possible outcomes of a history delete operation.
/// </summary>
public enum HistoryDeleteStatus
{
    PlanValidated,
    Deleted
}

/// <summary>
/// Result of deleting a retained history entry.
/// </summary>
public sealed record HistoryDeleteResult
{
    public required HistoryDeleteStatus Status { get; init; }

    public required string ScriptName { get; init; }
}

/// <summary>
/// Possible outcomes of a history prune operation.
/// </summary>
public enum HistoryPruneStatus
{
    PlanValidated,
    Pruned
}

/// <summary>
/// Result of pruning retained history entries.
/// </summary>
public sealed record HistoryPruneResult
{
    public required HistoryPruneStatus Status { get; init; }

    public IReadOnlyList<string> DeletedScriptNames { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}
