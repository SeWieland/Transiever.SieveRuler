using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Transiever.SieveRuler.Models;

namespace Transiever.SieveRuler.Services;

/// <summary>
/// Parses and isolates managed sections in a Sieve script.
/// </summary>
public sealed class SieveImporter : ISieveImporter
{
    public const string RequirementsBegin = "# BEGIN SIEVERULER REQUIREMENTS v1";
    public const string RequirementsEnd = "# END SIEVERULER REQUIREMENTS v1";
    public const string RulesBegin = "# BEGIN SIEVERULER RULES v1";
    public const string RulesEnd = "# END SIEVERULER RULES v1";
    public const string MetadataPrefix = "# Metadata: ";
    public const string MetadataHashPrefix = "# Metadata-SHA256: ";
    public const string BodyHashPrefix = "# Body-SHA256: ";
    public const string RequirementsHashPrefix = "# Content-SHA256: ";

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions MetadataOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter<RuleConditionMode>(allowIntegerValues: false),
            new JsonStringEnumConverter<RuleConditionType>(allowIntegerValues: false),
            new JsonStringEnumConverter<RuleActionType>(allowIntegerValues: false),
            new JsonStringEnumConverter<RuleOwnership>(allowIntegerValues: false)
        }
    };
    private static readonly SieveRulerJsonContext JsonContext = new(MetadataOptions);

    public SieveImportResult Import(ReadOnlyMemory<byte> content)
    {
        byte[] original = content.ToArray();
        string text;
        try
        {
            text = StrictUtf8.GetString(original);
        }
        catch (DecoderFallbackException)
        {
            return new SieveImportResult
            {
                OriginalContent = original,
                ManagedRegionConflict = true,
                Diagnostics =
                [
                    Diagnostic(
                        "Error",
                        "InvalidUtf8",
                        "The active script is not valid UTF-8 and cannot be reconciled safely.")
                ]
            };
        }

        var diagnostics = new List<ReconciliationDiagnostic>();
        var managedSpans = new List<SieveSourceSpan>();
        bool conflict = false;

        Region? requirements = FindRegion(
            text,
            RequirementsBegin,
            RequirementsEnd);
        Region? rules = FindRegion(
            text,
            RulesBegin,
            RulesEnd);
        if (requirements is not null)
        {
            managedSpans.Add(
                new SieveSourceSpan(
                    requirements.Start,
                    requirements.Length));
            conflict |= !requirements.IsValid ||
                !ValidateRequirementsRegion(text, requirements);
        }

        IReadOnlyList<RuleDefinition> managedRules = [];
        if (rules is not null)
        {
            managedSpans.Add(
                new SieveSourceSpan(rules.Start, rules.Length));
            ManagedMetadataResult metadata = ParseManagedMetadata(
                text,
                rules);
            managedRules = metadata.Rules;
            conflict |= metadata.Conflict || !rules.IsValid;
            diagnostics.AddRange(metadata.Diagnostics);
        }

        if (conflict && diagnostics.All(item => item.Code != "ManagedRegionModified"))
        {
            diagnostics.Add(
                Diagnostic(
                    "Error",
                    "ManagedRegionModified",
                    "A Transiever.SieveRuler managed region is duplicated or not terminated."));
        }

        string externalText = BlankSpans(text, managedSpans);
        var parser = new StrictSieveParser(externalText);
        StrictParseResult parsed = parser.Parse();
        diagnostics.AddRange(parsed.Diagnostics);

        return new SieveImportResult
        {
            OriginalContent = original,
            ExternalRules = parsed.Rules,
            ManagedSourceRules = managedRules,
            ManagedSpans = managedSpans,
            LeadingRequireSpans = parsed.LeadingRequireSpans,
            DeclaredCapabilities = parsed.DeclaredCapabilities,
            Diagnostics = diagnostics,
            RequirementsInsertionOffset = parsed.RequirementsInsertionOffset,
            ManagedRegionConflict = conflict
        };
    }

    private static ManagedMetadataResult ParseManagedMetadata(
        string text,
        Region region)
    {
        string regionText = text.Substring(region.ContentStart, region.ContentLength);
        string? metadataText = ReadPrefixedLines(regionText, MetadataPrefix);
        string? expectedMetadataHash =
            ReadPrefixedLine(regionText, MetadataHashPrefix);
        string? expectedHash = ReadPrefixedLine(regionText, BodyHashPrefix);
        int bodySeparator = regionText.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        int separatorLength = 4;
        if (bodySeparator < 0)
        {
            bodySeparator = regionText.IndexOf("\n\n", StringComparison.Ordinal);
            separatorLength = 2;
        }
        string body = bodySeparator < 0
            ? string.Empty
            : regionText[(bodySeparator + separatorLength)..];
        body = body.TrimEnd('\r', '\n');

        var diagnostics = new List<ReconciliationDiagnostic>();
        bool conflict = metadataText is null ||
            expectedMetadataHash is null ||
            expectedHash is null;
        List<RuleDefinition> rules = [];

        if (!conflict)
        {
            try
            {
                byte[] metadataBytes = Convert.FromBase64String(metadataText!);
                string actualMetadataHash = Convert.ToHexString(
                    SHA256.HashData(metadataBytes));
                rules = JsonSerializer.Deserialize(
                    metadataBytes,
                    JsonContext.ListRuleDefinition) ?? [];
                string actualHash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(body)));
                conflict =
                    !actualMetadataHash.Equals(
                        expectedMetadataHash,
                        StringComparison.OrdinalIgnoreCase) ||
                    !actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (
                exception is FormatException or JsonException)
            {
                conflict = true;
            }
        }

        if (conflict)
        {
            diagnostics.Add(
                Diagnostic(
                    "Error",
                    "ManagedRegionModified",
                    "The managed region was edited or its metadata is invalid."));
        }

        return new ManagedMetadataResult(rules, conflict, diagnostics);
    }

    private static bool ValidateRequirementsRegion(string text, Region region)
    {
        string regionText = text.Substring(region.ContentStart, region.ContentLength)
            .TrimEnd('\r', '\n');
        string? expectedHash = ReadPrefixedLine(
            regionText,
            RequirementsHashPrefix);
        int firstLineEnd = regionText.IndexOf('\n');
        if (expectedHash is null || firstLineEnd < 0)
            return false;

        string payload = regionText[(firstLineEnd + 1)..]
            .TrimStart('\r')
            .TrimEnd('\r', '\n');
        string actualHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    private static Region? FindRegion(string text, string beginMarker, string endMarker)
    {
        IReadOnlyList<int> begins = FindLineMarkers(text, beginMarker);
        IReadOnlyList<int> ends = FindLineMarkers(text, endMarker);
        if (begins.Count == 0 && ends.Count == 0)
            return null;

        int invalidStart = begins.Concat(ends).Min();
        if (begins.Count != 1 || ends.Count != 1 || ends[0] < begins[0])
        {
            return new Region(
                invalidStart,
                text.Length - invalidStart,
                invalidStart,
                text.Length - invalidStart,
                false);
        }

        int begin = begins[0];
        int endMarkerStart = ends[0];
        int end = endMarkerStart + endMarker.Length;
        if (end < text.Length && text[end] == '\r')
            end++;
        if (end < text.Length && text[end] == '\n')
            end++;

        int contentStart = begin + beginMarker.Length;
        if (contentStart < text.Length && text[contentStart] == '\r')
            contentStart++;
        if (contentStart < text.Length && text[contentStart] == '\n')
            contentStart++;

        return new Region(
            begin,
            end - begin,
            contentStart,
            endMarkerStart - contentStart,
            true);
    }

    private static IReadOnlyList<int> FindLineMarkers(string text, string marker)
    {
        var result = new List<int>();
        int searchFrom = 0;
        while (searchFrom < text.Length)
        {
            int match = text.IndexOf(marker, searchFrom, StringComparison.Ordinal);
            if (match < 0)
                break;

            int after = match + marker.Length;
            bool startsLine = match == 0 || text[match - 1] == '\n';
            bool endsLine = after == text.Length || text[after] is '\r' or '\n';
            if (startsLine && endsLine)
                result.Add(match);
            searchFrom = after;
        }

        return result;
    }

    private static string BlankSpans(string text, IEnumerable<SieveSourceSpan> spans)
    {
        char[] result = text.ToCharArray();
        foreach (SieveSourceSpan span in spans)
        {
            for (int index = span.Start; index < span.End && index < result.Length; index++)
            {
                if (result[index] is not ('\r' or '\n'))
                    result[index] = ' ';
            }
        }

        return new string(result);
    }

    private static string? ReadPrefixedLine(string value, string prefix)
    {
        foreach (string line in value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
                return line[prefix.Length..].Trim();
        }

        return null;
    }

    private static string? ReadPrefixedLines(string value, string prefix)
    {
        string[] parts = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
            .Select(line => line[prefix.Length..].Trim())
            .ToArray();
        return parts.Length == 0 ? null : string.Concat(parts);
    }

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

    private sealed record Region(
        int Start,
        int Length,
        int ContentStart,
        int ContentLength,
        bool IsValid);

    private sealed record ManagedMetadataResult(
        IReadOnlyList<RuleDefinition> Rules,
        bool Conflict,
        IReadOnlyList<ReconciliationDiagnostic> Diagnostics);
}

internal sealed class StrictSieveParser(string text)
{
    private readonly List<SieveToken> tokens = SieveLexer.Tokenize(text);
    private readonly List<ReconciliationDiagnostic> diagnostics = [];
    private readonly HashSet<string> capabilities = new(StringComparer.OrdinalIgnoreCase);
    private int position;

    public StrictParseResult Parse()
    {
        var rules = new List<ImportedSieveRule>();
        int insertionOffset = tokens[0].Start;
        int order = 0;
        bool reportedOpaqueContent = false;
        bool canParseLeadingRequire = true;
        var leadingRequireSpans = new List<SieveSourceSpan>();

        while (!AtEnd)
        {
            int tokenStart = position;
            int commandStart = Current.Start;
            if (canParseLeadingRequire && Current.IsIdentifier("require"))
            {
                try
                {
                    position++;
                    ParseRequire();
                    leadingRequireSpans.Add(
                        new SieveSourceSpan(
                            commandStart,
                            Previous.End - commandStart));
                    insertionOffset = SkipTrivia(Previous.End);
                    continue;
                }
                catch (SieveParseException)
                {
                    position = tokenStart;
                }
            }

            if (Current.IsIdentifier("if"))
            {
                if (TryParseRule(order, out RuleDefinition? rule, out int end))
                {
                    canParseLeadingRequire = false;
                    int sourceStart = IncludeLeadingRuleComments(commandStart);
                    rules.Add(
                        new ImportedSieveRule(
                            rule!,
                            new SieveSourceSpan(sourceStart, end - sourceStart)));
                    order++;
                    continue;
                }

                position = tokenStart;
            }

            canParseLeadingRequire = false;
            if (!reportedOpaqueContent)
            {
                diagnostics.Add(
                    new ReconciliationDiagnostic
                    {
                        Severity = "Info",
                        Code = "OpaqueSieveContentPreserved",
                        Message = "Unsupported or user-authored Sieve content will be preserved without semantic rewriting."
                    });
                reportedOpaqueContent = true;
            }
            SkipTopLevelCommand();
        }

        return new StrictParseResult(
            rules,
            capabilities,
            leadingRequireSpans,
            diagnostics,
            insertionOffset);
    }

    private bool TryParseRule(
        int order,
        out RuleDefinition? rule,
        out int sourceEnd)
    {
        int saved = position;
        rule = null;
        sourceEnd = Current.End;

        try
        {
            ExpectIdentifier("if");
            ParsedTest test = ParseTest(isTopLevel: true);
            Expect(SieveTokenKind.LeftBrace);
            ExpectIdentifier("fileinto");
            bool copy = false;
            var flags = new List<string>();
            while (Current.Kind == SieveTokenKind.Identifier &&
                Current.Value.StartsWith(':'))
            {
                if (Current.Value.Equals(":copy", StringComparison.OrdinalIgnoreCase))
                {
                    copy = true;
                    position++;
                    continue;
                }

                if (Current.Value.Equals(":flags", StringComparison.OrdinalIgnoreCase))
                {
                    position++;
                    flags.AddRange(ParseStringOrList());
                    continue;
                }

                throw new SieveParseException();
            }

            string folder = Expect(SieveTokenKind.String).Value;
            Expect(SieveTokenKind.Semicolon);
            var actions = new List<RuleAction>();
            if (flags.Count > 0)
            {
                actions.Add(
                    new RuleAction
                    {
                        Type = RuleActionType.SetFlags,
                        Values = flags
                    });
                test.RequiredCapabilities.Add("imap4flags");
            }

            actions.Add(
                new RuleAction
                {
                    Type = copy ? RuleActionType.CopyInto : RuleActionType.FileInto,
                    Values = [folder]
                });
            test.RequiredCapabilities.Add("fileinto");
            if (copy)
                test.RequiredCapabilities.Add("copy");

            if (Current.IsIdentifier("stop"))
            {
                position++;
                Expect(SieveTokenKind.Semicolon);
                actions.Add(new RuleAction { Type = RuleActionType.Stop });
            }

            sourceEnd = Expect(SieveTokenKind.RightBrace).End;

            rule = new RuleDefinition
            {
                Id = RuleFingerprint.Create(test.Mode, test.Conditions, folder),
                Name = ReadRuleName(saved),
                TargetFolder = folder,
                Actions = actions,
                ConditionMode = test.Mode,
                Conditions = test.Conditions,
                SourceId = "server",
                Ownership = RuleOwnership.External,
                OriginalOrder = order,
                RequiredCapabilities = test.RequiredCapabilities.ToList()
            };
            return true;
        }
        catch (SieveParseException)
        {
            position = saved;
            return false;
        }
    }

    private ParsedTest ParseTest(bool isTopLevel)
    {
        if (Current.IsIdentifier("allof") || Current.IsIdentifier("anyof"))
        {
            bool isAny = Current.IsIdentifier("anyof");
            if (!isTopLevel && !isAny)
                throw new SieveParseException();

            RuleConditionMode mode = Current.IsIdentifier("anyof")
                ? RuleConditionMode.Any
                : RuleConditionMode.All;
            position++;
            Expect(SieveTokenKind.LeftParenthesis);
            var parts = new List<ParsedTest>();
            do
            {
                parts.Add(ParseTest(isTopLevel: false));
            }
            while (Match(SieveTokenKind.Comma));

            Expect(SieveTokenKind.RightParenthesis);

            if (!isTopLevel)
                return ParseSubjectOrBody(parts);

            var conditions = parts.SelectMany(part => part.Conditions).ToList();
            var required = new HashSet<string>(
                parts.SelectMany(part => part.RequiredCapabilities),
                StringComparer.OrdinalIgnoreCase);
            return new ParsedTest(mode, conditions, required);
        }

        string command = Expect(SieveTokenKind.Identifier).Value;
        ExpectValue(":contains");

        if (command.Equals("address", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<string> headers = ParseStringOrList();
            IReadOnlyList<string> values = ParseStringOrList();
            RuleConditionType type = headers.Count switch
            {
                1 when headers[0].Equals("from", StringComparison.OrdinalIgnoreCase) =>
                    RuleConditionType.SenderContains,
                2 when headers.Contains("to", StringComparer.OrdinalIgnoreCase) &&
                    headers.Contains("cc", StringComparer.OrdinalIgnoreCase) =>
                    RuleConditionType.ReceiverContains,
                _ => throw new SieveParseException()
            };
            return Atomic(type, values);
        }

        if (command.Equals("header", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<string> headers = ParseStringOrList();
            if (headers.Count != 1 ||
                !headers[0].Equals("subject", StringComparison.OrdinalIgnoreCase))
                throw new SieveParseException();

            return Atomic(RuleConditionType.SubjectContains, ParseStringOrList());
        }

        if (command.Equals("body", StringComparison.OrdinalIgnoreCase))
        {
            ParsedTest body = Atomic(
                RuleConditionType.BodyContains,
                ParseStringOrList());
            body.RequiredCapabilities.Add("body");
            return body;
        }

        throw new SieveParseException();
    }

    private static ParsedTest ParseSubjectOrBody(IReadOnlyList<ParsedTest> parts)
    {
        if (parts.Count != 2 ||
            parts.Any(part => part.Conditions.Count != 1))
        {
            throw new SieveParseException();
        }

        List<RuleCondition> subject = parts
            .SelectMany(part => part.Conditions)
            .Where(condition => condition.Type == RuleConditionType.SubjectContains)
            .ToList();
        List<RuleCondition> body = parts
            .SelectMany(part => part.Conditions)
            .Where(condition => condition.Type == RuleConditionType.BodyContains)
            .ToList();
        if (subject.Count != 1 ||
            body.Count != 1 ||
            !subject[0].Values.SequenceEqual(
                body[0].Values,
                StringComparer.OrdinalIgnoreCase))
        {
            throw new SieveParseException();
        }

        return new ParsedTest(
            RuleConditionMode.All,
            [
                new RuleCondition
                {
                    Type = RuleConditionType.SubjectOrBodyContains,
                    Values = [.. subject[0].Values]
                }
            ],
            new HashSet<string>(["body"], StringComparer.OrdinalIgnoreCase));
    }

    private static ParsedTest Atomic(
        RuleConditionType type,
        IReadOnlyList<string> values) =>
        new(
            RuleConditionMode.All,
            [
                new RuleCondition
                {
                    Type = type,
                    Values = values.ToList()
                }
            ],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private IReadOnlyList<string> ParseStringOrList()
    {
        if (Match(SieveTokenKind.LeftBracket))
        {
            var result = new List<string>();
            do
            {
                result.Add(Expect(SieveTokenKind.String).Value);
            }
            while (Match(SieveTokenKind.Comma));
            Expect(SieveTokenKind.RightBracket);
            return result;
        }

        return [Expect(SieveTokenKind.String).Value];
    }

    private void ParseRequire()
    {
        IReadOnlyList<string> parsedCapabilities = ParseStringOrList();
        Expect(SieveTokenKind.Semicolon);
        foreach (string capability in parsedCapabilities)
            capabilities.Add(capability);
    }

    private void SkipTopLevelCommand()
    {
        int braceDepth = 0;
        while (!AtEnd)
        {
            SieveToken token = tokens[position++];
            if (token.Kind == SieveTokenKind.LeftBrace)
                braceDepth++;
            else if (token.Kind == SieveTokenKind.RightBrace)
            {
                if (braceDepth == 0)
                    return;
                braceDepth--;
                if (braceDepth == 0)
                    return;
            }
            else if (token.Kind == SieveTokenKind.Semicolon && braceDepth == 0)
                return;
        }
    }

    private int IncludeLeadingRuleComments(int commandStart)
    {
        int lineStart = text.LastIndexOf('\n', Math.Max(0, commandStart - 1));
        int candidate = lineStart < 0 ? 0 : lineStart + 1;
        int scan = candidate;
        while (scan > 0)
        {
            int previousEnd = scan - 1;
            if (previousEnd >= 0 && text[previousEnd] == '\n')
                previousEnd--;
            if (previousEnd >= 0 && text[previousEnd] == '\r')
                previousEnd--;
            int previousStart = text.LastIndexOf('\n', Math.Max(0, previousEnd));
            previousStart = previousStart < 0 ? 0 : previousStart + 1;
            string line = text[previousStart..(previousEnd + 1)].Trim();
            if (line.Length > 0 && !line.StartsWith('#'))
                break;
            candidate = previousStart;
            scan = previousStart;
        }

        return candidate;
    }

    private string ReadRuleName(int tokenStart)
    {
        int commandStart = tokens[tokenStart].Start;
        int scanStart = IncludeLeadingRuleComments(commandStart);
        string prefix = text[scanStart..commandStart];
        string? providerName = SieveProviderMetadata.TryReadRuleName(prefix);
        if (providerName is not null)
            return providerName;

        const string marker = "# Rule:";
        int markerIndex = prefix.LastIndexOf(marker, StringComparison.Ordinal);
        return markerIndex < 0
            ? $"Server rule {tokenStart + 1}"
            : prefix[(markerIndex + marker.Length)..].Trim();
    }

    private int SkipTrivia(int offset)
    {
        int current = offset;
        while (current < text.Length)
        {
            if (char.IsWhiteSpace(text[current]))
            {
                current++;
                continue;
            }

            if (text[current] == '#')
            {
                int newline = text.IndexOf('\n', current);
                current = newline < 0 ? text.Length : newline + 1;
                continue;
            }

            break;
        }

        return current;
    }

    private bool MatchIdentifier(string value)
    {
        if (!Current.IsIdentifier(value))
            return false;
        position++;
        return true;
    }

    private void ExpectIdentifier(string value)
    {
        if (!MatchIdentifier(value))
            throw new SieveParseException();
    }

    private void ExpectValue(string value)
    {
        if (!Current.Value.Equals(value, StringComparison.OrdinalIgnoreCase))
            throw new SieveParseException();
        position++;
    }

    private bool Match(SieveTokenKind kind)
    {
        if (Current.Kind != kind)
            return false;
        position++;
        return true;
    }

    private SieveToken Expect(SieveTokenKind kind)
    {
        if (Current.Kind != kind)
            throw new SieveParseException();
        return tokens[position++];
    }

    private SieveToken Current => tokens[position];

    private SieveToken Previous => tokens[position - 1];

    private bool AtEnd => Current.Kind == SieveTokenKind.End;

    private sealed class SieveParseException : Exception;

    private sealed record ParsedTest(
        RuleConditionMode Mode,
        List<RuleCondition> Conditions,
        HashSet<string> RequiredCapabilities);
}

internal sealed record StrictParseResult(
    IReadOnlyList<ImportedSieveRule> Rules,
    IReadOnlySet<string> DeclaredCapabilities,
    IReadOnlyList<SieveSourceSpan> LeadingRequireSpans,
    IReadOnlyList<ReconciliationDiagnostic> Diagnostics,
    int RequirementsInsertionOffset);

internal enum SieveTokenKind
{
    Identifier,
    String,
    LeftBrace,
    RightBrace,
    LeftParenthesis,
    RightParenthesis,
    LeftBracket,
    RightBracket,
    Comma,
    Semicolon,
    Invalid,
    End
}

internal sealed record SieveToken(
    SieveTokenKind Kind,
    string Value,
    int Start,
    int End)
{
    public bool IsIdentifier(string value) =>
        Kind == SieveTokenKind.Identifier &&
        Value.Equals(value, StringComparison.OrdinalIgnoreCase);
}

internal static class SieveLexer
{
    public static List<SieveToken> Tokenize(string text)
    {
        var tokens = new List<SieveToken>();
        int position = 0;
        while (position < text.Length)
        {
            char current = text[position];
            if (char.IsWhiteSpace(current))
            {
                position++;
                continue;
            }

            if (current == '#')
            {
                int newline = text.IndexOf('\n', position);
                position = newline < 0 ? text.Length : newline + 1;
                continue;
            }

            int start = position;
            switch (current)
            {
                case '"':
                    tokens.Add(ReadString(text, ref position));
                    break;
                case '{':
                    tokens.Add(Token(SieveTokenKind.LeftBrace, text, position++));
                    break;
                case '}':
                    tokens.Add(Token(SieveTokenKind.RightBrace, text, position++));
                    break;
                case '(':
                    tokens.Add(Token(SieveTokenKind.LeftParenthesis, text, position++));
                    break;
                case ')':
                    tokens.Add(Token(SieveTokenKind.RightParenthesis, text, position++));
                    break;
                case '[':
                    tokens.Add(Token(SieveTokenKind.LeftBracket, text, position++));
                    break;
                case ']':
                    tokens.Add(Token(SieveTokenKind.RightBracket, text, position++));
                    break;
                case ',':
                    tokens.Add(Token(SieveTokenKind.Comma, text, position++));
                    break;
                case ';':
                    tokens.Add(Token(SieveTokenKind.Semicolon, text, position++));
                    break;
                default:
                    while (position < text.Length &&
                        !char.IsWhiteSpace(text[position]) &&
                        !"{}()[],;\"#".Contains(text[position], StringComparison.Ordinal))
                    {
                        position++;
                    }

                    tokens.Add(
                        new SieveToken(
                            SieveTokenKind.Identifier,
                            text[start..position],
                            start,
                            position));
                    break;
            }
        }

        tokens.Add(new SieveToken(SieveTokenKind.End, "", text.Length, text.Length));
        return tokens;
    }

    private static SieveToken ReadString(string text, ref int position)
    {
        int start = position++;
        var value = new StringBuilder();
        while (position < text.Length)
        {
            char current = text[position++];
            if (current == '"')
                return new SieveToken(SieveTokenKind.String, value.ToString(), start, position);

            if (current == '\\' && position < text.Length)
                current = text[position++];
            value.Append(current);
        }

        return new SieveToken(SieveTokenKind.Invalid, value.ToString(), start, position);
    }

    private static SieveToken Token(SieveTokenKind kind, string text, int position) =>
        new(kind, text[position].ToString(), position, position + 1);
}

internal static class RuleFingerprint
{
    public static string Create(RuleDefinition rule) =>
        Create(
            rule.ConditionMode,
            rule.Conditions,
            rule.Exceptions,
            GetEffectiveActions(rule));

    public static string Create(
        RuleConditionMode mode,
        IEnumerable<RuleCondition> conditions,
        string targetFolder)
    {
        RuleAction[] actions = string.IsNullOrWhiteSpace(targetFolder)
            ? []
            :
            [
                new RuleAction
                {
                    Type = RuleActionType.FileInto,
                    Values = [targetFolder]
                }
            ];

        return Create(mode, conditions, [], actions);
    }

    private static string Create(
        RuleConditionMode mode,
        IEnumerable<RuleCondition> conditions,
        IEnumerable<RuleCondition> exceptions,
        IEnumerable<RuleAction> actions)
    {
        string canonical = string.Join(
            "\n",
            conditions
                .Select(condition => $"condition:{CanonicalCondition(condition)}")
                .Order(StringComparer.Ordinal)) +
            "\n" +
            string.Join(
                "\n",
                exceptions
                    .Select(condition => $"exception:{CanonicalCondition(condition)}")
                    .Order(StringComparer.Ordinal)) +
            "\n" +
            string.Join(
                "\n",
                actions
                    .Select(action => $"action:{CanonicalAction(action)}")
                    .Order(StringComparer.Ordinal)) +
            $"\n{mode}";
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16];
    }

    private static IEnumerable<RuleAction> GetEffectiveActions(RuleDefinition rule)
    {
        if (rule.Actions.Count > 0)
            return rule.Actions;

        return string.IsNullOrWhiteSpace(rule.TargetFolder)
            ? []
            :
            [
                new RuleAction
                {
                    Type = RuleActionType.FileInto,
                    Values = [rule.TargetFolder]
                }
            ];
    }

    private static string CanonicalCondition(RuleCondition condition) =>
        $"{condition.Type}:{CanonicalValues(condition.Values)}";

    private static string CanonicalAction(RuleAction action) =>
        $"{action.Type}:{CanonicalValues(action.Values)}";

    private static string CanonicalValues(IEnumerable<string> values) =>
        string.Join(
            '|',
            values
                .Select(value => value.Trim().ToUpperInvariant())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
}
