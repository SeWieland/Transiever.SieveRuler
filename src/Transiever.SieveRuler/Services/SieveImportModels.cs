using Transiever.SieveRuler.Models;

namespace Transiever.SieveRuler.Services;

public sealed record SieveSourceSpan(int Start, int Length)
{
    public int End => Start + Length;
}

public sealed record ImportedSieveRule(
    RuleDefinition Rule,
    SieveSourceSpan SourceSpan);

public sealed record SieveImportResult
{
    public required byte[] OriginalContent { get; init; }

    public IReadOnlyList<ImportedSieveRule> ExternalRules { get; init; } = [];

    public IReadOnlyList<RuleDefinition> ManagedSourceRules { get; init; } = [];

    public IReadOnlyList<SieveSourceSpan> ManagedSpans { get; init; } = [];

    public IReadOnlyList<SieveSourceSpan> LeadingRequireSpans { get; init; } = [];

    public IReadOnlySet<string> DeclaredCapabilities { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ReconciliationDiagnostic> Diagnostics { get; init; } = [];

    public int RequirementsInsertionOffset { get; init; }

    public bool ManagedRegionConflict { get; init; }
}

public interface ISieveImporter
{
    SieveImportResult Import(ReadOnlyMemory<byte> content);
}
