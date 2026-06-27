using Transiever.SieveRuler.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Transiever.SieveRuler.Services;

/// <summary>
/// Final composed Sieve script and its supporting metadata.
/// </summary>
public sealed record SieveCompositionResult
{
    public required byte[] Content { get; init; }

    public IReadOnlyCollection<string> RequiredCapabilities { get; init; } = [];

    public IReadOnlyList<ReconciliationDiagnostic> Diagnostics { get; init; } = [];

    public bool IsBlocked { get; init; }
}

/// <summary>
/// Combines imported server content and reconciled rules into a final script.
/// </summary>
public interface ISieveScriptComposer
{
    SieveCompositionResult Compose(
        SieveImportResult imported,
        RuleReconciliationResult reconciliation);
}

public sealed class SieveScriptComposer(
    ISieveImporter importer,
    ISieveGenerator generator) : ISieveScriptComposer
{
    private static readonly JsonSerializerOptions MetadataOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new StringEnumJsonConverter<RuleConditionMode>(),
            new StringEnumJsonConverter<RuleConditionType>(),
            new StringEnumJsonConverter<RuleOwnership>()
        }
    };

    public SieveCompositionResult Compose(
        SieveImportResult imported,
        RuleReconciliationResult reconciliation)
    {
        var diagnostics = new List<ReconciliationDiagnostic>(
            reconciliation.Diagnostics);
        if (imported.ManagedRegionConflict)
        {
            return new SieveCompositionResult
            {
                Content = imported.OriginalContent,
                Diagnostics = diagnostics,
                IsBlocked = true
            };
        }

        List<SieveSourceSpan> removals =
        [
            .. imported.ManagedSpans,
            .. reconciliation.AdoptedExternalSpans
        ];
        byte[] preservedWithRequires = RemoveSpans(
            imported.OriginalContent,
            removals);
        SieveImportResult preservedImport = importer.Import(preservedWithRequires);
        byte[] preserved = RemoveSpans(
            preservedWithRequires,
            preservedImport.LeadingRequireSpans);

        string preservedText = Encoding.UTF8.GetString(preserved);
        if (SieveControlFlowAnalyzer.HasUnconditionalTerminatingCommand(preservedText))
        {
            diagnostics.Add(
                new ReconciliationDiagnostic
                {
                    Severity = "Error",
                    Code = "ManagedRegionUnreachable",
                    Message = "An unconditional terminating command appears before the appended managed region."
                });
            return new SieveCompositionResult
            {
                Content = preserved,
                Diagnostics = diagnostics,
                IsBlocked = true
            };
        }

        IReadOnlyCollection<string> generatedCapabilities =
            generator.GetRequiredCapabilities(reconciliation.RenderedRules);
        var capabilities = new SortedSet<string>(
            preservedImport.DeclaredCapabilities,
            StringComparer.Ordinal);
        foreach (string capability in generatedCapabilities)
            capabilities.Add(capability);

        string body = generator.GenerateRuleBody(reconciliation.RenderedRules)
            .TrimEnd('\r', '\n');
        string metadata = Convert.ToBase64String(
            JsonSerializer.SerializeToUtf8Bytes(
                reconciliation.OwnedSourceRules,
                MetadataOptions));
        string metadataHash = Convert.ToHexString(
            SHA256.HashData(Convert.FromBase64String(metadata)));
        string bodyHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        string requirementStatement =
            $"require {RenderStringList(capabilities)};";
        var rulesRegionBuilder = new StringBuilder();
        AppendCrLf(rulesRegionBuilder, SieveImporter.RulesBegin);
        foreach (string chunk in Chunk(metadata, 3000))
            AppendCrLf(rulesRegionBuilder, $"{SieveImporter.MetadataPrefix}{chunk}");
        AppendCrLf(
            rulesRegionBuilder,
            $"{SieveImporter.MetadataHashPrefix}{metadataHash}");
        AppendCrLf(rulesRegionBuilder, $"{SieveImporter.BodyHashPrefix}{bodyHash}");
        AppendCrLf(rulesRegionBuilder, "");
        AppendCrLf(rulesRegionBuilder, body);
        AppendCrLf(rulesRegionBuilder, SieveImporter.RulesEnd);
        string rulesRegion = rulesRegionBuilder.ToString();

        int insertion = Math.Clamp(
            GetMergedRequireInsertionOffset(preservedImport),
            0,
            preservedText.Length);
        string withRequirements = preservedText.Insert(
            insertion,
            requirementStatement + "\r\n\r\n");
        if (!withRequirements.EndsWith('\n'))
            withRequirements += "\r\n";
        if (!withRequirements.EndsWith("\r\n\r\n", StringComparison.Ordinal))
            withRequirements += "\r\n";

        string candidate = withRequirements + rulesRegion;
        return new SieveCompositionResult
        {
            Content = Encoding.UTF8.GetBytes(candidate),
            RequiredCapabilities = capabilities.ToArray(),
            Diagnostics = diagnostics
        };
    }

    private static byte[] RemoveSpans(
        byte[] content,
        IEnumerable<SieveSourceSpan> spans)
    {
        string text = Encoding.UTF8.GetString(content);
        foreach (SieveSourceSpan span in spans
            .OrderByDescending(span => span.Start)
            .Distinct())
        {
            if (span.Start >= 0 && span.End <= text.Length)
                text = text.Remove(span.Start, span.Length);
        }

        return Encoding.UTF8.GetBytes(text);
    }

    private static int GetMergedRequireInsertionOffset(
        SieveImportResult preservedImport)
    {
        if (preservedImport.LeadingRequireSpans.Count > 0)
            return preservedImport.LeadingRequireSpans.Min(span => span.Start);

        return preservedImport.RequirementsInsertionOffset;
    }

    private static string RenderStringList(IEnumerable<string> values) =>
        $"[{string.Join(", ", values.Select(value => $"\"{SieveStringEscaper.Escape(value)}\""))}]";

    private static IEnumerable<string> Chunk(string value, int size)
    {
        for (int offset = 0; offset < value.Length; offset += size)
            yield return value.Substring(offset, Math.Min(size, value.Length - offset));
    }

    private static void AppendCrLf(StringBuilder builder, string value)
    {
        builder.Append(value);
        builder.Append("\r\n");
    }
}

internal static class SieveControlFlowAnalyzer
{
    public static bool HasUnconditionalTerminatingCommand(string text)
    {
        List<SieveToken> tokens = SieveLexer.Tokenize(text);
        int depth = 0;
        for (int index = 0; index < tokens.Count; index++)
        {
            SieveToken token = tokens[index];
            if (token.Kind == SieveTokenKind.LeftBrace)
                depth++;
            else if (token.Kind == SieveTokenKind.RightBrace)
                depth = Math.Max(0, depth - 1);
            else if (depth == 0 &&
                token.Kind == SieveTokenKind.Identifier &&
                token.Value.Equals("stop", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
