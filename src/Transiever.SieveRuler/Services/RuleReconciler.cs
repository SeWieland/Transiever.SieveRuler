using Transiever.SieveRuler.Models;

namespace Transiever.SieveRuler.Services;

/// <summary>
/// Result of reconciling source rules with imported server state.
/// </summary>
public sealed record RuleReconciliationResult
{
    public IReadOnlyList<RuleDefinition> OwnedSourceRules { get; init; } = [];

    public IReadOnlyList<RuleDefinition> RenderedRules { get; init; } = [];

    public IReadOnlyList<SieveSourceSpan> AdoptedExternalSpans { get; init; } = [];

    public IReadOnlyList<ReconciliationDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>
/// Reconciles source rules with imported server state.
/// </summary>
public interface IRuleReconciler
{
    RuleReconciliationResult Reconcile(
        string authoritativeSourceId,
        IEnumerable<RuleDefinition> sourceRules,
        SieveImportResult imported,
        bool adoptCompatible,
        RuleOptimizationMode? optimizationMode);
}

public sealed class RuleReconciler(IRuleOptimizer optimizer) : IRuleReconciler
{
    public RuleReconciliationResult Reconcile(
        string authoritativeSourceId,
        IEnumerable<RuleDefinition> sourceRules,
        SieveImportResult imported,
        bool adoptCompatible,
        RuleOptimizationMode? optimizationMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authoritativeSourceId);

        var diagnostics = new List<ReconciliationDiagnostic>(imported.Diagnostics);
        HashSet<string> previousSourceFingerprints = imported.ManagedSourceRules
            .Where(rule => SourceEquals(rule, authoritativeSourceId))
            .Select(RuleFingerprint.Create)
            .ToHashSet(StringComparer.Ordinal);
        var owned = imported.ManagedSourceRules
            .Where(rule => !SourceEquals(rule, authoritativeSourceId))
            .Select(Clone)
            .ToList();
        var adoptedSpans = new List<SieveSourceSpan>();

        HashSet<string> externalFingerprints = imported.ExternalRules
            .Select(item => RuleFingerprint.Create(item.Rule))
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> ownedFingerprints = owned
            .Select(RuleFingerprint.Create)
            .ToHashSet(StringComparer.Ordinal);

        List<RuleDefinition> currentSourceRules = sourceRules
            .Select(rule => AsManagedSourceRule(
                rule,
                string.IsNullOrWhiteSpace(rule.SourceId)
                    ? authoritativeSourceId
                    : rule.SourceId))
            .ToList();
        HashSet<string> currentSourceFingerprints = currentSourceRules
            .Where(rule => SourceEquals(rule, authoritativeSourceId))
            .Select(RuleFingerprint.Create)
            .ToHashSet(StringComparer.Ordinal);

        foreach (RuleDefinition removed in imported.ManagedSourceRules
            .Where(rule =>
                SourceEquals(rule, authoritativeSourceId) &&
                !currentSourceFingerprints.Contains(RuleFingerprint.Create(rule))))
        {
            diagnostics.Add(
                Diagnostic(
                    "Info",
                    "ObsoleteManagedSourceRuleRemoved",
                    $"Removed obsolete managed rule '{removed.Name}' from source '{authoritativeSourceId}'."));
        }

        foreach (RuleDefinition sourceRule in currentSourceRules)
        {
            string fingerprint = RuleFingerprint.Create(sourceRule);
            if (externalFingerprints.Contains(fingerprint))
            {
                diagnostics.Add(
                    Diagnostic(
                        "Info",
                        "DuplicateSuppressedByExternalRule",
                        $"Source rule '{sourceRule.Name}' is already represented by an external server rule."));
                continue;
            }

            if (ownedFingerprints.Add(fingerprint))
            {
                owned.Add(sourceRule);
                if (!previousSourceFingerprints.Contains(fingerprint))
                {
                    diagnostics.Add(
                        Diagnostic(
                            "Info",
                            "SourceRuleAdded",
                            $"Added rule '{sourceRule.Name}' from source '{authoritativeSourceId}'."));
                }
            }
        }

        if (adoptCompatible)
        {
            foreach (ImportedSieveRule importedRule in imported.ExternalRules)
            {
                RuleDefinition adopted = Clone(
                    importedRule.Rule,
                    "server",
                    RuleOwnership.Managed);
                if (ownedFingerprints.Add(RuleFingerprint.Create(adopted)))
                {
                    owned.Add(adopted);
                }

                adoptedSpans.Add(importedRule.SourceSpan);
                diagnostics.Add(
                    Diagnostic(
                        "Warning",
                        "ExternalRuleAdopted",
                        $"Adopted server rule '{adopted.Name}' from source span {importedRule.SourceSpan.Start}..{importedRule.SourceSpan.End}."));
            }
        }

        IReadOnlyList<RuleDefinition> rendered = optimizationMode is null
            ? owned.Select(Clone).ToList()
            : optimizer.Optimize(owned, optimizationMode.Value).Rules.ToList();

        return new RuleReconciliationResult
        {
            OwnedSourceRules = owned,
            RenderedRules = rendered,
            AdoptedExternalSpans = adoptedSpans,
            Diagnostics = diagnostics
        };
    }

    private static bool SourceEquals(RuleDefinition rule, string sourceId) =>
        string.Equals(rule.SourceId, sourceId, StringComparison.OrdinalIgnoreCase);

    private static RuleDefinition AsManagedSourceRule(
        RuleDefinition rule,
        string sourceId) =>
        Clone(rule, sourceId, RuleOwnership.Managed);

    private static RuleDefinition Clone(RuleDefinition rule) =>
        Clone(rule, rule.SourceId, rule.Ownership);

    private static RuleDefinition Clone(
        RuleDefinition rule,
        string? sourceId,
        RuleOwnership ownership) =>
        new()
        {
            Id = rule.Id ?? RuleFingerprint.Create(rule),
            Name = rule.Name,
            TargetFolder = rule.TargetFolder,
            Actions = rule.Actions.Select(CloneAction).ToList(),
            ConditionMode = rule.ConditionMode,
            Conditions = rule.Conditions
                .Select(CloneCondition)
                .ToList(),
            Exceptions = rule.Exceptions
                .Select(CloneCondition)
                .ToList(),
            SourceId = sourceId,
            Ownership = ownership,
            OriginalOrder = rule.OriginalOrder,
            RequiredCapabilities = [.. rule.RequiredCapabilities]
        };

    private static RuleAction CloneAction(RuleAction action) =>
        new()
        {
            Type = action.Type,
            Values = [.. action.GetValues()]
        };

    private static RuleCondition CloneCondition(RuleCondition condition) =>
        new()
        {
            Type = condition.Type,
            Values = [.. condition.GetValues()]
        };

    private static ReconciliationDiagnostic Diagnostic(
        string severity,
        string code,
        string message) =>
        new()
        {
            Severity = severity,
            Code = code,
            Message = message
        };
}
