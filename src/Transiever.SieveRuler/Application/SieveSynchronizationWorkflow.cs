using Transiever.SieveRuler.Models;
using Transiever.SieveRuler.Services;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Transiever.SieveRuler.Application;

/// <summary>
/// High-level workflow for previewing, deploying, rolling back, and managing retained history.
/// </summary>
public interface ISieveSynchronizationWorkflow
{
    Task<PreviewSynchronizationResult> PreviewAsync(
        PreviewSynchronizationRequest request,
        CancellationToken cancellationToken);

    Task<DeploySynchronizationResult> DeployAsync(
        DeploySynchronizationRequest request,
        CancellationToken cancellationToken);

    Task<RollbackSynchronizationResult> RollbackAsync(
        RollbackSynchronizationRequest request,
        CancellationToken cancellationToken);

    Task<HistoryListResult> ListHistoryAsync(
        HistoryListRequest request,
        CancellationToken cancellationToken);

    Task<HistoryShowResult> ShowHistoryAsync(
        HistoryShowRequest request,
        CancellationToken cancellationToken);

    Task<HistoryRestoreResult> RestoreHistoryAsync(
        HistoryRestoreRequest request,
        CancellationToken cancellationToken);

    Task<HistoryDeleteResult> DeleteHistoryAsync(
        HistoryDeleteRequest request,
        CancellationToken cancellationToken);

    Task<HistoryPruneResult> PruneHistoryAsync(
        HistoryPruneRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Operator interaction required by preview when compatible rules may be adopted.
/// </summary>
public interface ISynchronizationInteraction
{
    bool ResolveAdoption(bool? explicitChoice, int compatibleRuleCount);
}

public sealed class SieveSynchronizationWorkflow(
    IRuleSerializer serializer,
    ISieveImporter importer,
    IRuleReconciler reconciler,
    ISieveScriptComposer composer,
    ISieveServerConnectionFactory connectionFactory,
    ISynchronizationInteraction interaction)
    : ISieveSynchronizationWorkflow
{
    private static readonly byte[] NoActiveOriginalMarkerContent =
        "# SieveRuler original state marker: no active script\r\nkeep;\r\n"u8.ToArray();

    private static readonly JsonSerializerOptions PlanOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<PreviewSynchronizationResult> PreviewAsync(
        PreviewSynchronizationRequest request,
        CancellationToken cancellationToken)
    {
        RuleDocument source = request.SourceDocument ??
            await serializer.LoadDocumentAsync(
                request.RulesFile,
                cancellationToken);
        await using ISieveServerConnection connection =
            await connectionFactory.ConnectAsync(
                request.Configuration,
                cancellationToken);
        RemoteSieveState remote = await connection.ReadStateAsync(cancellationToken);
        SieveImportResult imported = importer.Import(remote.ActiveContent);

        bool adopt = interaction.ResolveAdoption(
            request.AdoptCompatible,
            imported.ExternalRules.Count);
        RuleReconciliationResult reconciliation = reconciler.Reconcile(
            source.SourceId,
            source.Rules,
            imported,
            adopt,
            request.OptimizationMode);
        SieveCompositionResult composition = composer.Compose(imported, reconciliation);
        if (composition.IsBlocked)
        {
            return Result(
                PreviewSynchronizationStatus.Blocked,
                composition.Diagnostics);
        }

        HashSet<string> availableCapabilities = remote.Capabilities.SieveExtensions
            .Concat(imported.DeclaredCapabilities)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] missingCapabilities = composition.RequiredCapabilities
            .Except(availableCapabilities, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingCapabilities.Length > 0)
        {
            return new PreviewSynchronizationResult
            {
                Status = PreviewSynchronizationStatus.MissingCapabilities,
                Diagnostics = composition.Diagnostics,
                MissingCapabilities = missingCapabilities
            };
        }

        string candidateHash = Hash(composition.Content);
        string targetName = ResolvePreviewTargetName(
            request.TargetScriptName,
            remote,
            candidateHash);
        bool replacesActive = IsActiveScript(remote.ActiveScriptName, targetName);
        if (!replacesActive && ScriptExists(remote, targetName))
        {
            throw new InvalidOperationException(
                $"Stored script '{targetName}' already exists; refusing to overwrite it.");
        }

        string? backupName = replacesActive
            ? CreateAvailableScriptName(
                "srtx-backup",
                remote.ActiveContentSha256,
                remote)
            : null;

        await connection.CheckScriptAsync(composition.Content, cancellationToken);
        if (!await connection.HaveSpaceAsync(
            targetName,
            composition.Content.Length,
            cancellationToken) ||
            (backupName is not null &&
                !await connection.HaveSpaceAsync(
                    backupName,
                    remote.ActiveContent.Length,
                    cancellationToken)))
        {
            return new PreviewSynchronizationResult
            {
                Status = PreviewSynchronizationStatus.InsufficientSpace,
                Diagnostics = composition.Diagnostics,
                TargetScriptName = targetName,
                ReplacesActiveScript = replacesActive
            };
        }

        var reconciledDocument = new RuleDocument
        {
            SourceId = source.SourceId,
            Rules =
            [
                .. reconciliation.OwnedSourceRules,
                .. adopt
                    ? []
                    : imported.ExternalRules.Select(item => item.Rule)
            ],
            Diagnostics = [.. composition.Diagnostics]
        };
        var candidateRulesDocument = new RuleDocument
        {
            SourceId = source.SourceId,
            Rules = reconciliation.RenderedRules.ToList(),
            Diagnostics = [.. composition.Diagnostics]
        };
        var plan = new DeploymentPlan
        {
            SchemaVersion = DeploymentPlan.CurrentSchemaVersion,
            SourceActiveScriptName = remote.ActiveScriptName,
            SourceContentSha256 = remote.ActiveContentSha256,
            CandidateContentBase64 = Convert.ToBase64String(composition.Content),
            CandidateContentSha256 = candidateHash,
            TargetScriptName = targetName,
            BackupScriptName = backupName,
            BackupContentSha256 = backupName is null
                ? null
                : remote.ActiveContentSha256
        };

        if (!request.DryRun)
        {
            await serializer.SaveDocumentAsync(
                reconciledDocument,
                request.ReconciledRulesFile,
                cancellationToken);
            await serializer.SaveDocumentAsync(
                candidateRulesDocument,
                request.CandidateRulesFile,
                cancellationToken);
            await File.WriteAllBytesAsync(
                request.ServerSnapshotFile,
                remote.ActiveContent,
                cancellationToken);
            await File.WriteAllBytesAsync(
                request.CandidateFile,
                composition.Content,
                cancellationToken);
            await File.WriteAllTextAsync(
                request.PlanFile,
                JsonSerializer.Serialize(plan, PlanOptions),
                cancellationToken);
        }

        return new PreviewSynchronizationResult
        {
            Status = PreviewSynchronizationStatus.Prepared,
            Diagnostics = composition.Diagnostics,
            ManagedRuleCount = reconciliation.RenderedRules.Count,
            TargetScriptName = targetName,
            ReplacesActiveScript = replacesActive,
            FilesWritten = !request.DryRun
        };
    }

    public async Task<DeploySynchronizationResult> DeployAsync(
        DeploySynchronizationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(request.HistoryLimit);

        DeploymentPlan plan = await LoadPlanAsync(request.PlanFile, cancellationToken);
        byte[] candidate = ValidateCandidate(plan);
        string targetName = GetTargetScriptName(plan);

        if (request.DryRun)
        {
            return DeploymentResult(
                DeploySynchronizationStatus.PlanValidated,
                plan,
                targetName,
                request);
        }

        if (request.Configuration is null)
        {
            throw new ArgumentException(
                "Server configuration is required for deployment.",
                nameof(request));
        }

        await using ISieveServerConnection connection =
            await connectionFactory.ConnectAsync(
                request.Configuration,
                cancellationToken);
        await connection.CheckScriptAsync(candidate, cancellationToken);
        RemoteSieveState before = await connection.ReadStateAsync(cancellationToken);
        EnsureFresh(plan, before);

        if (HasBackupPlan(plan))
        {
            return await DeployActiveReplacementAsync(
                request,
                plan,
                targetName,
                candidate,
                before,
                connection,
                cancellationToken);
        }

        return await DeployInactiveCandidateAsync(
            request,
            plan,
            targetName,
            candidate,
            before,
            connection,
            cancellationToken);
    }

    public async Task<RollbackSynchronizationResult> RollbackAsync(
        RollbackSynchronizationRequest request,
        CancellationToken cancellationToken)
    {
        DeploymentPlan plan = await LoadPlanAsync(request.PlanFile, cancellationToken);
        ValidateCandidate(plan);
        string targetName = GetTargetScriptName(plan);

        if (request.DryRun)
        {
            return new RollbackSynchronizationResult
            {
                Status = RollbackSynchronizationStatus.PlanValidated,
                TargetScriptName = targetName
            };
        }

        if (request.Configuration is null)
        {
            throw new ArgumentException(
                "Server configuration is required for rollback.",
                nameof(request));
        }

        await using ISieveServerConnection connection =
            await connectionFactory.ConnectAsync(
                request.Configuration,
                cancellationToken);
        RemoteSieveState current = await connection.ReadStateAsync(cancellationToken);
        if (!request.Force)
            EnsureCurrentActiveMatchesCandidate(plan, targetName, current);

        if (HasBackupPlan(plan))
        {
            return await RollbackBackupAsync(
                request,
                plan,
                targetName,
                connection,
                cancellationToken);
        }

        return await RollbackSourceActivationAsync(
            plan,
            targetName,
            connection,
            cancellationToken);
    }

    public async Task<HistoryListResult> ListHistoryAsync(
        HistoryListRequest request,
        CancellationToken cancellationToken)
    {
        await using ISieveServerConnection connection =
            await connectionFactory.ConnectAsync(
                request.Configuration,
                cancellationToken);
        RemoteSieveState state = await connection.ReadStateAsync(cancellationToken);
        return new HistoryListResult
        {
            ActiveScriptName = state.ActiveScriptName,
            Entries = BuildHistoryEntries(state)
        };
    }

    public async Task<HistoryShowResult> ShowHistoryAsync(
        HistoryShowRequest request,
        CancellationToken cancellationToken)
    {
        await using ISieveServerConnection connection =
            await connectionFactory.ConnectAsync(
                request.Configuration,
                cancellationToken);
        RemoteSieveState state = await connection.ReadStateAsync(cancellationToken);
        HistoryScript history = ResolveHistoryScript(state, request.ScriptName);
        byte[] content = await connection.GetScriptAsync(history.Name, cancellationToken);
        return new HistoryShowResult
        {
            Entry = ToHistoryEntry(
                history,
                FindOriginalHistory(state)?.Name,
                Hash(content),
                content.Length),
            Content = content
        };
    }

    public async Task<HistoryRestoreResult> RestoreHistoryAsync(
        HistoryRestoreRequest request,
        CancellationToken cancellationToken)
    {
        if (request.DryRun)
        {
            if (request.Configuration is null)
            {
                throw new ArgumentException(
                    "Server configuration is required for history restore dry-run.",
                    nameof(request));
            }

            await using ISieveServerConnection dryRunConnection =
                await connectionFactory.ConnectAsync(
                    request.Configuration,
                    cancellationToken);
            RemoteSieveState dryRunState =
                await dryRunConnection.ReadStateAsync(cancellationToken);
            HistoryScript dryRunHistory =
                ResolveHistoryScript(dryRunState, request.ScriptName);
            return new HistoryRestoreResult
            {
                Status = HistoryRestoreStatus.PlanValidated,
                SourceScriptName = dryRunHistory.Name,
                TargetScriptName = NullIfEmpty(dryRunState.ActiveScriptName) ??
                    dryRunHistory.Name
            };
        }

        if (request.Configuration is null)
        {
            throw new ArgumentException(
                "Server configuration is required for history restore.",
                nameof(request));
        }

        await using ISieveServerConnection connection =
            await connectionFactory.ConnectAsync(
                request.Configuration,
                cancellationToken);
        RemoteSieveState state = await connection.ReadStateAsync(cancellationToken);
        HistoryScript history = ResolveHistoryScript(state, request.ScriptName);
        if (history.Kind == SieveHistoryEntryKind.NoActiveOriginalMarker)
        {
            return await RestoreNoActiveOriginalAsync(
                history,
                state,
                connection,
                cancellationToken);
        }

        byte[] selected = await connection.GetScriptAsync(history.Name, cancellationToken);
        string selectedHash = Hash(selected);
        await connection.CheckScriptAsync(selected, cancellationToken);
        if (state.ActiveScriptName.Length > 0 &&
            state.ActiveContentSha256.Equals(
                selectedHash,
                StringComparison.OrdinalIgnoreCase))
        {
            return new HistoryRestoreResult
            {
                Status = HistoryRestoreStatus.AlreadyActive,
                SourceScriptName = history.Name,
                TargetScriptName = state.ActiveScriptName,
                RestoredContentSha256 = selectedHash
            };
        }

        if (state.ActiveScriptName.Length == 0)
        {
            string markerName = CreateAvailableNoActiveMarkerName(state);
            if (!await connection.HaveSpaceAsync(
                markerName,
                NoActiveOriginalMarkerContent.Length,
                cancellationToken))
            {
                throw new InvalidOperationException(
                    "The server reported insufficient space for the no-active restore marker.");
            }

            await connection.PutScriptAsync(
                markerName,
                NoActiveOriginalMarkerContent,
                cancellationToken);
            await connection.ActivateAsync(history.Name, cancellationToken);
            RemoteSieveState activated = await connection.ReadStateAsync(cancellationToken);
            EnsureActiveContent(
                history.Name,
                selectedHash,
                activated,
                "History restore did not activate the selected history script.");
            return new HistoryRestoreResult
            {
                Status = HistoryRestoreStatus.RestoredScript,
                SourceScriptName = history.Name,
                TargetScriptName = history.Name,
                BackupScriptName = markerName,
                RestoredContentSha256 = selectedHash
            };
        }

        string backupName = CreateAvailableScriptName(
            "srtx-backup",
            state.ActiveContentSha256,
            state);
        if (!await HasSpaceForActiveReplacementAsync(
            connection,
            state.ActiveScriptName,
            selected.Length,
            backupName,
            state.ActiveContent.Length,
            cancellationToken))
        {
            throw new InvalidOperationException(
                "The server reported insufficient space for the restore target or backup script.");
        }

        await connection.PutScriptAsync(
            backupName,
            state.ActiveContent,
            cancellationToken);
        byte[] storedBackup = await connection.GetScriptAsync(
            backupName,
            cancellationToken);
        EnsureHash(
            storedBackup,
            state.ActiveContentSha256,
            "The server-side restore backup does not match the previous active script.");

        await connection.PutScriptAsync(
            state.ActiveScriptName,
            selected,
            cancellationToken);
        RemoteSieveState restored = await connection.ReadStateAsync(cancellationToken);
        EnsureActiveContent(
            state.ActiveScriptName,
            selectedHash,
            restored,
            "History restore did not restore the selected content as the active script.");
        return new HistoryRestoreResult
        {
            Status = HistoryRestoreStatus.RestoredScript,
            SourceScriptName = history.Name,
            TargetScriptName = state.ActiveScriptName,
            BackupScriptName = backupName,
            RestoredContentSha256 = selectedHash
        };
    }

    public async Task<HistoryDeleteResult> DeleteHistoryAsync(
        HistoryDeleteRequest request,
        CancellationToken cancellationToken)
    {
        await using ISieveServerConnection connection =
            await connectionFactory.ConnectAsync(
                request.Configuration,
                cancellationToken);
        RemoteSieveState state = await connection.ReadStateAsync(cancellationToken);
        HistoryScript history = ResolveHistoryScript(state, request.ScriptName);
        if (history.IsActive)
        {
            throw new InvalidOperationException(
                $"Refusing to delete active history script '{history.Name}'.");
        }

        if (request.DryRun)
        {
            return new HistoryDeleteResult
            {
                Status = HistoryDeleteStatus.PlanValidated,
                ScriptName = history.Name
            };
        }

        await connection.DeleteScriptAsync(history.Name, cancellationToken);
        return new HistoryDeleteResult
        {
            Status = HistoryDeleteStatus.Deleted,
            ScriptName = history.Name
        };
    }

    public async Task<HistoryPruneResult> PruneHistoryAsync(
        HistoryPruneRequest request,
        CancellationToken cancellationToken)
    {
        await using ISieveServerConnection connection =
            await connectionFactory.ConnectAsync(
                request.Configuration,
                cancellationToken);
        RemoteSieveState state = await connection.ReadStateAsync(cancellationToken);
        IReadOnlyList<string> scriptsToDelete =
            SelectInactiveHistoryScriptsToDelete(state);

        if (request.DryRun)
        {
            return new HistoryPruneResult
            {
                Status = HistoryPruneStatus.PlanValidated,
                DeletedScriptNames = scriptsToDelete
            };
        }

        var deleted = new List<string>();
        var warnings = new List<string>();
        foreach (string scriptName in scriptsToDelete)
        {
            try
            {
                await connection.DeleteScriptAsync(scriptName, cancellationToken);
                deleted.Add(scriptName);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                warnings.Add(
                    $"Could not delete SieveRuler history script '{scriptName}': {exception.Message}");
            }
        }

        return new HistoryPruneResult
        {
            Status = HistoryPruneStatus.Pruned,
            DeletedScriptNames = deleted,
            Warnings = warnings
        };
    }

    private async Task<DeploySynchronizationResult> DeployActiveReplacementAsync(
        DeploySynchronizationRequest request,
        DeploymentPlan plan,
        string targetName,
        byte[] candidate,
        RemoteSieveState before,
        ISieveServerConnection connection,
        CancellationToken cancellationToken)
    {
        ValidateBackupPlan(plan, targetName);
        string backupName = plan.BackupScriptName!;
        string backupHash = plan.BackupContentSha256!;

        if (ScriptExists(before, backupName))
        {
            throw new InvalidOperationException(
                $"Stored script '{backupName}' already exists; refusing to overwrite it.");
        }

        HistoryCleanupResult cleanup = HistoryCleanupResult.Empty;
        if (!await HasSpaceForActiveReplacementAsync(
            connection,
            targetName,
            candidate.Length,
            backupName,
            before.ActiveContent.Length,
            cancellationToken))
        {
            (before, cleanup) = await PruneHistoryForSpaceRetryAsync(
                request,
                plan,
                targetName,
                before,
                connection,
                cancellationToken);
            if (ScriptExists(before, backupName))
            {
                throw new InvalidOperationException(
                    $"Stored script '{backupName}' already exists; refusing to overwrite it.");
            }

            if (!await HasSpaceForActiveReplacementAsync(
                connection,
                targetName,
                candidate.Length,
                backupName,
                before.ActiveContent.Length,
                cancellationToken))
            {
                return DeploymentResult(
                    DeploySynchronizationStatus.InsufficientSpace,
                    plan,
                    targetName,
                    request,
                    cleanup);
            }
        }

        await connection.PutScriptAsync(
            backupName,
            before.ActiveContent,
            cancellationToken);
        byte[] storedBackup = await connection.GetScriptAsync(
            backupName,
            cancellationToken);
        EnsureHash(
            storedBackup,
            backupHash,
            "The server-side backup does not match the previewed active script.");

        await connection.PutScriptAsync(targetName, candidate, cancellationToken);
        RemoteSieveState after = await connection.ReadStateAsync(cancellationToken);
        EnsureActiveContent(
            targetName,
            plan.CandidateContentSha256,
            after,
            "Deployment did not leave the target script active with the candidate content.");

        cleanup = MergeCleanup(
            cleanup,
            await PruneHistoryAfterSuccessfulDeployAsync(
                request,
                plan,
                targetName,
                connection,
                after,
                cancellationToken));

        return DeploymentResult(
            DeploySynchronizationStatus.ReplacedActive,
            plan,
            targetName,
            request,
            cleanup);
    }

    private async Task<DeploySynchronizationResult> DeployInactiveCandidateAsync(
        DeploySynchronizationRequest request,
        DeploymentPlan plan,
        string targetName,
        byte[] candidate,
        RemoteSieveState before,
        ISieveServerConnection connection,
        CancellationToken cancellationToken)
    {
        if (ScriptExists(before, targetName))
        {
            throw new InvalidOperationException(
                $"Stored script '{targetName}' already exists; refusing to overwrite it.");
        }

        string? originalNoActiveMarkerName = before.ActiveScriptName.Length == 0
            ? CreateAvailableNoActiveMarkerName(before)
            : null;

        HistoryCleanupResult cleanup = HistoryCleanupResult.Empty;
        if (!await HasSpaceForInactiveDeploymentAsync(
            connection,
            targetName,
            candidate.Length,
            originalNoActiveMarkerName,
            cancellationToken))
        {
            (before, cleanup) = await PruneHistoryForSpaceRetryAsync(
                request,
                plan,
                targetName,
                before,
                connection,
                cancellationToken);
            if (ScriptExists(before, targetName))
            {
                throw new InvalidOperationException(
                    $"Stored script '{targetName}' already exists; refusing to overwrite it.");
            }

            if (originalNoActiveMarkerName is not null &&
                ScriptExists(before, originalNoActiveMarkerName))
            {
                throw new InvalidOperationException(
                    $"Stored script '{originalNoActiveMarkerName}' already exists; refusing to overwrite it.");
            }

            if (!await HasSpaceForInactiveDeploymentAsync(
                connection,
                targetName,
                candidate.Length,
                originalNoActiveMarkerName,
                cancellationToken))
            {
                return DeploymentResult(
                    DeploySynchronizationStatus.InsufficientSpace,
                    plan,
                    targetName,
                    request,
                    cleanup);
            }
        }

        if (originalNoActiveMarkerName is not null)
        {
            await connection.PutScriptAsync(
                originalNoActiveMarkerName,
                NoActiveOriginalMarkerContent,
                cancellationToken);
        }

        await connection.PutScriptAsync(targetName, candidate, cancellationToken);
        RemoteSieveState afterUpload =
            await connection.ReadStateAsync(cancellationToken);
        EnsureFresh(plan, afterUpload);

        await connection.ActivateAsync(targetName, cancellationToken);
        RemoteSieveState afterActivation =
            await connection.ReadStateAsync(cancellationToken);
        EnsureActiveContent(
            targetName,
            plan.CandidateContentSha256,
            afterActivation,
            "Deployment did not activate the candidate content.");
        cleanup = MergeCleanup(
            cleanup,
            await PruneHistoryAfterSuccessfulDeployAsync(
                request,
                plan,
                targetName,
                connection,
                afterActivation,
                cancellationToken));
        return DeploymentResult(
            DeploySynchronizationStatus.Activated,
            plan,
            targetName,
            request,
            cleanup,
            originalNoActiveMarkerName);
    }

    private static async Task<RollbackSynchronizationResult> RollbackBackupAsync(
        RollbackSynchronizationRequest request,
        DeploymentPlan plan,
        string targetName,
        ISieveServerConnection connection,
        CancellationToken cancellationToken)
    {
        ValidateBackupPlan(plan, targetName);
        string backupName = plan.BackupScriptName!;
        string backupHash = plan.BackupContentSha256!;
        byte[] backup = await connection.GetScriptAsync(backupName, cancellationToken);
        EnsureHash(
            backup,
            backupHash,
            "The server-side backup does not match the deployment plan.");

        await connection.PutScriptAsync(targetName, backup, cancellationToken);
        RemoteSieveState restored =
            await connection.ReadStateAsync(cancellationToken);
        if (request.Force &&
            !restored.ActiveScriptName.Equals(targetName, StringComparison.Ordinal))
        {
            await connection.ActivateAsync(targetName, cancellationToken);
            restored = await connection.ReadStateAsync(cancellationToken);
        }

        EnsureActiveContent(
            targetName,
            backupHash,
            restored,
            "Rollback did not restore the backup content as the active script.");

        return new RollbackSynchronizationResult
        {
            Status = RollbackSynchronizationStatus.RestoredBackup,
            TargetScriptName = targetName,
            RestoredScriptName = targetName,
            BackupScriptName = backupName
        };
    }

    private static async Task<RollbackSynchronizationResult> RollbackSourceActivationAsync(
        DeploymentPlan plan,
        string targetName,
        ISieveServerConnection connection,
        CancellationToken cancellationToken)
    {
        if (plan.SourceActiveScriptName.Length > 0)
        {
            byte[] source = await connection.GetScriptAsync(
                plan.SourceActiveScriptName,
                cancellationToken);
            EnsureHash(
                source,
                plan.SourceContentSha256,
                "The original source script no longer matches the deployment plan.");
        }
        else
        {
            EnsureHash(
                [],
                plan.SourceContentSha256,
                "The deployment plan source snapshot is not the disabled active state.");
        }

        await connection.ActivateAsync(
            NullIfEmpty(plan.SourceActiveScriptName),
            cancellationToken);
        RemoteSieveState after = await connection.ReadStateAsync(cancellationToken);
        EnsureActiveContent(
            plan.SourceActiveScriptName,
            plan.SourceContentSha256,
            after,
            "Rollback did not restore the source active script.");

        return new RollbackSynchronizationResult
        {
            Status = RollbackSynchronizationStatus.ReactivatedSource,
            TargetScriptName = targetName,
            RestoredScriptName = NullIfEmpty(plan.SourceActiveScriptName)
        };
    }

    private static PreviewSynchronizationResult Result(
        PreviewSynchronizationStatus status,
        IReadOnlyCollection<ReconciliationDiagnostic> diagnostics) =>
        new()
        {
            Status = status,
            Diagnostics = diagnostics
        };

    private static DeploySynchronizationResult DeploymentResult(
        DeploySynchronizationStatus status,
        DeploymentPlan plan,
        string targetName,
        DeploySynchronizationRequest request,
        HistoryCleanupResult? cleanup = null,
        string? backupScriptNameOverride = null) =>
        new()
        {
            Status = status,
            ScriptName = targetName,
            PreviousActiveScriptName = plan.SourceActiveScriptName,
            BackupScriptName = backupScriptNameOverride ?? plan.BackupScriptName,
            HistoryLimit = request.HistoryLimit,
            PruneHistory = request.PruneHistory,
            DeletedHistoryScriptNames = cleanup?.DeletedScriptNames ?? [],
            CleanupWarnings = cleanup?.Warnings ?? []
        };

    private static async Task<bool> HasSpaceForActiveReplacementAsync(
        ISieveServerConnection connection,
        string targetName,
        long targetContentLength,
        string backupName,
        long backupContentLength,
        CancellationToken cancellationToken) =>
        await connection.HaveSpaceAsync(
            targetName,
            targetContentLength,
            cancellationToken) &&
        await connection.HaveSpaceAsync(
            backupName,
            backupContentLength,
            cancellationToken);

    private static async Task<bool> HasSpaceForInactiveDeploymentAsync(
        ISieveServerConnection connection,
        string targetName,
        long targetContentLength,
        string? originalNoActiveMarkerName,
        CancellationToken cancellationToken) =>
        await connection.HaveSpaceAsync(
            targetName,
            targetContentLength,
            cancellationToken) &&
        (originalNoActiveMarkerName is null ||
            await connection.HaveSpaceAsync(
                originalNoActiveMarkerName,
                NoActiveOriginalMarkerContent.Length,
                cancellationToken));

    private static async Task<(RemoteSieveState State, HistoryCleanupResult Cleanup)>
        PruneHistoryForSpaceRetryAsync(
            DeploySynchronizationRequest request,
            DeploymentPlan plan,
            string targetName,
            RemoteSieveState state,
            ISieveServerConnection connection,
            CancellationToken cancellationToken)
    {
        HistoryCleanupResult cleanup = request.PruneHistory
            ? await TryPruneHistoryAsync(
                connection,
                state,
                plan,
                targetName,
                request.HistoryLimit,
                cancellationToken)
            : HistoryCleanupResult.Empty;
        RemoteSieveState refreshed = await connection.ReadStateAsync(cancellationToken);
        EnsureFresh(plan, refreshed);
        return (refreshed, cleanup);
    }

    private static async Task<HistoryCleanupResult> PruneHistoryAfterSuccessfulDeployAsync(
        DeploySynchronizationRequest request,
        DeploymentPlan plan,
        string targetName,
        ISieveServerConnection connection,
        RemoteSieveState state,
        CancellationToken cancellationToken)
    {
        if (!request.PruneHistory)
            return HistoryCleanupResult.Empty;

        return await TryPruneHistoryAsync(
            connection,
            state,
            plan,
            targetName,
            request.HistoryLimit,
            cancellationToken);
    }

    private static async Task<HistoryCleanupResult> TryPruneHistoryAsync(
        ISieveServerConnection connection,
        RemoteSieveState state,
        DeploymentPlan plan,
        string targetName,
        int historyLimit,
        CancellationToken cancellationToken)
    {
        var deleted = new List<string>();
        var warnings = new List<string>();
        foreach (string scriptName in SelectHistoryScriptsToDelete(
            state,
            plan,
            targetName,
            historyLimit))
        {
            try
            {
                await connection.DeleteScriptAsync(scriptName, cancellationToken);
                deleted.Add(scriptName);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                warnings.Add(
                    $"Could not delete obsolete SieveRuler history script '{scriptName}': {exception.Message}");
            }
        }

        return new HistoryCleanupResult(deleted, warnings);
    }

    private static IReadOnlyList<string> SelectHistoryScriptsToDelete(
        RemoteSieveState state,
        DeploymentPlan plan,
        string targetName,
        int historyLimit)
    {
        var protectedNames = new HashSet<string>(StringComparer.Ordinal);
        AddProtectedName(protectedNames, state.ActiveScriptName);
        AddProtectedName(protectedNames, targetName);
        AddProtectedName(protectedNames, plan.SourceActiveScriptName);
        AddProtectedName(protectedNames, plan.BackupScriptName);
        foreach (var script in state.Scripts.Where(script => script.IsActive))
            AddProtectedName(protectedNames, script.Name);

        List<HistoryScript> histories = state.Scripts
            .Select(script => TryParseHistoryScript(script.Name, script.IsActive))
            .Where(script => script is not null)
            .Cast<HistoryScript>()
            .ToList();
        HistoryScript? original = FindOriginalHistory(histories);

        var keep = new HashSet<string>(protectedNames, StringComparer.Ordinal);
        if (original is not null)
            keep.Add(original.Name);

        foreach (HistoryScript retained in histories
            .Where(script => !script.IsActive && !keep.Contains(script.Name))
            .OrderByDescending(script => script.CreatedUtc)
            .ThenByDescending(script => script.Name, StringComparer.Ordinal)
            .Take(historyLimit))
        {
            keep.Add(retained.Name);
        }

        return histories
            .Where(script => !script.IsActive && !keep.Contains(script.Name))
            .OrderBy(script => script.CreatedUtc)
            .ThenBy(script => script.Name, StringComparer.Ordinal)
            .Select(script => script.Name)
            .ToArray();
    }

    private static IReadOnlyList<string> SelectInactiveHistoryScriptsToDelete(
        RemoteSieveState state) =>
        GetHistoryScripts(state)
            .Where(script => !script.IsActive)
            .OrderBy(script => script.CreatedUtc)
            .ThenBy(script => script.Name, StringComparer.Ordinal)
            .Select(script => script.Name)
            .ToArray();

    private static HistoryScript? TryParseHistoryScript(string name, bool isActive)
    {
        if (TryParseHistoryName(
            name,
            "srtx-backup-",
            out DateTimeOffset backupUtc,
            out string backupSuffix))
        {
            SieveHistoryEntryKind kind = backupSuffix.StartsWith(
                "no-active",
                StringComparison.Ordinal)
                ? SieveHistoryEntryKind.NoActiveOriginalMarker
                : SieveHistoryEntryKind.Backup;
            return new HistoryScript(name, backupUtc, kind, isActive);
        }

        if (TryParseHistoryName(
            name,
            "srtx-",
            out DateTimeOffset candidateUtc,
            out _))
        {
            return new HistoryScript(
                name,
                candidateUtc,
                SieveHistoryEntryKind.Candidate,
                isActive);
        }

        return null;
    }

    private static IReadOnlyList<SieveHistoryEntry> BuildHistoryEntries(
        RemoteSieveState state)
    {
        List<HistoryScript> histories = GetHistoryScripts(state);
        string? originalName = FindOriginalHistory(histories)?.Name;
        return histories
            .OrderByDescending(script => script.CreatedUtc)
            .ThenByDescending(script => script.Name, StringComparer.Ordinal)
            .Select(script => ToHistoryEntry(script, originalName))
            .ToArray();
    }

    private static List<HistoryScript> GetHistoryScripts(RemoteSieveState state) =>
        state.Scripts
            .Select(script => TryParseHistoryScript(script.Name, script.IsActive))
            .Where(script => script is not null)
            .Cast<HistoryScript>()
            .ToList();

    private static HistoryScript ResolveHistoryScript(
        RemoteSieveState state,
        string requestedName)
    {
        List<HistoryScript> histories = GetHistoryScripts(state);
        if (requestedName.Equals("original", StringComparison.OrdinalIgnoreCase))
        {
            return FindOriginalHistory(histories) ??
                throw new InvalidOperationException(
                    "No SieveRuler original backup or no-active marker exists on the server.");
        }

        return histories.SingleOrDefault(
                script => script.Name.Equals(requestedName, StringComparison.Ordinal)) ??
            throw new InvalidOperationException(
                $"SieveRuler history script '{requestedName}' was not found.");
    }

    private static HistoryScript? FindOriginalHistory(RemoteSieveState state) =>
        FindOriginalHistory(GetHistoryScripts(state));

    private static HistoryScript? FindOriginalHistory(
        IEnumerable<HistoryScript> histories) =>
        histories
            .Where(script => script.Kind is
                SieveHistoryEntryKind.Backup or
                SieveHistoryEntryKind.NoActiveOriginalMarker)
            .OrderBy(script => script.CreatedUtc)
            .ThenBy(script => script.Name, StringComparer.Ordinal)
            .FirstOrDefault();

    private static SieveHistoryEntry ToHistoryEntry(
        HistoryScript script,
        string? originalName,
        string? contentSha256 = null,
        long? contentLength = null) =>
        new()
        {
            Name = script.Name,
            Kind = script.Kind,
            CreatedUtc = script.CreatedUtc,
            IsActive = script.IsActive,
            IsOriginal = originalName is not null &&
                script.Name.Equals(originalName, StringComparison.Ordinal),
            ContentSha256 = contentSha256,
            ContentLength = contentLength
        };

    private static async Task<HistoryRestoreResult> RestoreNoActiveOriginalAsync(
        HistoryScript history,
        RemoteSieveState state,
        ISieveServerConnection connection,
        CancellationToken cancellationToken)
    {
        if (state.ActiveScriptName.Length == 0)
        {
            return new HistoryRestoreResult
            {
                Status = HistoryRestoreStatus.AlreadyActive,
                SourceScriptName = history.Name
            };
        }

        string backupName = CreateAvailableScriptName(
            "srtx-backup",
            state.ActiveContentSha256,
            state);
        if (!await connection.HaveSpaceAsync(
            backupName,
            state.ActiveContent.Length,
            cancellationToken))
        {
            throw new InvalidOperationException(
                "The server reported insufficient space for the restore backup script.");
        }

        await connection.PutScriptAsync(
            backupName,
            state.ActiveContent,
            cancellationToken);
        byte[] storedBackup = await connection.GetScriptAsync(
            backupName,
            cancellationToken);
        EnsureHash(
            storedBackup,
            state.ActiveContentSha256,
            "The server-side restore backup does not match the previous active script.");

        await connection.ActivateAsync(null, cancellationToken);
        RemoteSieveState restored = await connection.ReadStateAsync(cancellationToken);
        EnsureActiveContent(
            "",
            Hash([]),
            restored,
            "History restore did not disable active Sieve processing.");
        return new HistoryRestoreResult
        {
            Status = HistoryRestoreStatus.DisabledActive,
            SourceScriptName = history.Name,
            BackupScriptName = backupName
        };
    }

    private static bool TryParseHistoryName(
        string name,
        string prefix,
        out DateTimeOffset createdUtc,
        out string suffix)
    {
        createdUtc = default;
        suffix = "";
        const int timestampLength = 14;
        int separatorIndex = prefix.Length + timestampLength;
        if (!name.StartsWith(prefix, StringComparison.Ordinal) ||
            name.Length <= separatorIndex ||
            name[separatorIndex] != '-')
        {
            return false;
        }

        string stamp = name.Substring(prefix.Length, timestampLength);
        if (stamp.Any(character => !char.IsAsciiDigit(character)))
            return false;

        if (!DateTimeOffset.TryParseExact(
            stamp,
            "yyyyMMddHHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out createdUtc))
        {
            return false;
        }

        suffix = name[(separatorIndex + 1)..];
        return true;
    }

    private static HistoryCleanupResult MergeCleanup(
        HistoryCleanupResult first,
        HistoryCleanupResult second) =>
        new(
            [.. first.DeletedScriptNames, .. second.DeletedScriptNames],
            [.. first.Warnings, .. second.Warnings]);

    private static void AddProtectedName(
        HashSet<string> names,
        string? scriptName)
    {
        if (!string.IsNullOrWhiteSpace(scriptName))
            names.Add(scriptName);
    }

    private static async Task<DeploymentPlan> LoadPlanAsync(
        string planFile,
        CancellationToken cancellationToken)
    {
        DeploymentPlan plan = JsonSerializer.Deserialize<DeploymentPlan>(
            await File.ReadAllTextAsync(planFile, cancellationToken),
            PlanOptions) ?? throw new InvalidDataException("Deployment plan was empty.");
        if (plan.SchemaVersion != DeploymentPlan.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported deployment plan version {plan.SchemaVersion}.");
        }

        return plan;
    }

    private static byte[] ValidateCandidate(DeploymentPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.CandidateContentBase64) ||
            string.IsNullOrWhiteSpace(plan.CandidateContentSha256))
        {
            throw new InvalidDataException(
                "Deployment plan does not contain candidate content and hash.");
        }

        byte[] candidate = Convert.FromBase64String(plan.CandidateContentBase64);
        EnsureHash(
            candidate,
            plan.CandidateContentSha256,
            "Deployment plan candidate content does not match its hash.");
        return candidate;
    }

    private static string ResolvePreviewTargetName(
        string? requestedTargetName,
        RemoteSieveState remote,
        string candidateHash)
    {
        string? normalized = NullIfEmpty(requestedTargetName?.Trim());
        if (normalized is not null)
            return normalized;

        return remote.ActiveScriptName.Length > 0
            ? remote.ActiveScriptName
            : CreateAvailableScriptName("srtx", candidateHash, remote);
    }

    private static string GetTargetScriptName(DeploymentPlan plan)
    {
        string? target = NullIfEmpty(plan.TargetScriptName);
        if (target is null)
        {
            throw new InvalidDataException(
                "Deployment plan does not contain a target script name.");
        }

        return target;
    }

    private static bool HasBackupPlan(DeploymentPlan plan) =>
        NullIfEmpty(plan.BackupScriptName) is not null ||
        NullIfEmpty(plan.BackupContentSha256) is not null;

    private static void ValidateBackupPlan(DeploymentPlan plan, string targetName)
    {
        if (NullIfEmpty(plan.BackupScriptName) is null ||
            NullIfEmpty(plan.BackupContentSha256) is null)
        {
            throw new InvalidDataException(
                "Deployment plan backup name and hash are both required for in-place rollback.");
        }

        if (!targetName.Equals(plan.SourceActiveScriptName, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Deployment plan backup can only be used when the target is the previewed active script.");
        }

        if (!plan.BackupContentSha256!.Equals(
            plan.SourceContentSha256,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Deployment plan backup hash does not match the previewed active script hash.");
        }
    }

    private static void EnsureFresh(
        DeploymentPlan plan,
        RemoteSieveState state)
    {
        if (!state.ActiveScriptName.Equals(
            plan.SourceActiveScriptName,
            StringComparison.Ordinal) ||
            !state.ActiveContentSha256.Equals(
                plan.SourceContentSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The active server script changed after preview. Generate a new deployment plan.");
        }
    }

    private static void EnsureCurrentActiveMatchesCandidate(
        DeploymentPlan plan,
        string targetName,
        RemoteSieveState state)
    {
        if (!state.ActiveScriptName.Equals(targetName, StringComparison.Ordinal) ||
            !state.ActiveContentSha256.Equals(
                plan.CandidateContentSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The current active server script does not match the deployment plan candidate. Use --force only if rollback should proceed anyway.");
        }
    }

    private static void EnsureActiveContent(
        string expectedScriptName,
        string expectedHash,
        RemoteSieveState state,
        string message)
    {
        if (!state.ActiveScriptName.Equals(expectedScriptName, StringComparison.Ordinal) ||
            !state.ActiveContentSha256.Equals(
                expectedHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void EnsureHash(
        byte[] content,
        string expectedHash,
        string message)
    {
        if (!Hash(content).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(message);
    }

    private static string Hash(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content));

    private static bool ScriptExists(RemoteSieveState state, string scriptName) =>
        state.Scripts.Any(
            script => script.Name.Equals(scriptName, StringComparison.Ordinal));

    private static bool IsActiveScript(string activeScriptName, string scriptName) =>
        activeScriptName.Length > 0 &&
        activeScriptName.Equals(scriptName, StringComparison.Ordinal);

    private static string CreateAvailableScriptName(
        string prefix,
        string hash,
        RemoteSieveState remote)
    {
        string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        string hashPart = hash.Length >= 8
            ? hash[..8].ToLowerInvariant()
            : "snapshot";
        for (int attempt = 0; attempt < 100; attempt++)
        {
            string suffix = attempt == 0
                ? hashPart
                : $"{hashPart}-{attempt}";
            string name = $"{prefix}-{stamp}-{suffix}";
            if (!ScriptExists(remote, name))
                return name;
        }

        return $"{prefix}-{stamp}-{Guid.NewGuid():N}"[..(prefix.Length + 1 + 14 + 1 + 8)];
    }

    private static string CreateAvailableNoActiveMarkerName(RemoteSieveState remote)
    {
        string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        for (int attempt = 0; attempt < 100; attempt++)
        {
            string suffix = attempt == 0
                ? "no-active"
                : $"no-active-{attempt}";
            string name = $"srtx-backup-{stamp}-{suffix}";
            if (!ScriptExists(remote, name))
                return name;
        }

        return $"srtx-backup-{stamp}-no-active-{Guid.NewGuid():N}"[..44];
    }

    private sealed record HistoryCleanupResult(
        IReadOnlyList<string> DeletedScriptNames,
        IReadOnlyList<string> Warnings)
    {
        public static HistoryCleanupResult Empty { get; } = new([], []);
    }

    private sealed record HistoryScript(
        string Name,
        DateTimeOffset CreatedUtc,
        SieveHistoryEntryKind Kind,
        bool IsActive);

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
