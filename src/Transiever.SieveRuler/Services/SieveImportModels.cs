using Transiever.SieveRuler.Models;

namespace Transiever.SieveRuler.Services;

/// <summary>
/// Describes a half-open span in a Sieve source document.
/// </summary>
public sealed record SieveSourceSpan(int Start, int Length)
{
    public int End => Start + Length;
}

/// <summary>
/// A rule imported from an existing Sieve script and its source span.
/// </summary>
public sealed record ImportedSieveRule(
    RuleDefinition Rule,
    SieveSourceSpan SourceSpan);

/// <summary>
/// Result of parsing an existing Sieve script.
/// </summary>
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

/// <summary>
/// Parses an existing Sieve script into structured data.
/// </summary>
public interface ISieveImporter
{
    SieveImportResult Import(ReadOnlyMemory<byte> content);
}
