using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Transiever.SieveRuler.Models;

namespace Transiever.SieveRuler.Services;

internal static class SieveProviderMetadata
{
    public const string FlagPrefix = "## Flag:";

    public static string RenderFlag(RuleDefinition rule)
    {
        string name = SanitizeCommentField(rule.Name);

        return $"{FlagPrefix} |UniqueId:{CreateUniqueId(rule)}|Rulename: {name}";
    }

    public static string SanitizeCommentField(string? value) =>
        (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("|", " ", StringComparison.Ordinal)
            .Trim();

    public static string? TryReadRuleName(string leadingComments)
    {
        string[] lines = leadingComments
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');

        for (int index = lines.Length - 1; index >= 0; index--)
        {
            string line = lines[index].Trim();
            if (!line.StartsWith(FlagPrefix, StringComparison.Ordinal))
                continue;

            string? name = ReadFlagField(line[FlagPrefix.Length..], "Rulename:");
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();
        }

        return null;
    }

    private static string CreateUniqueId(RuleDefinition rule)
    {
        string identity = string.IsNullOrWhiteSpace(rule.Id)
            ? RuleFingerprint.Create(rule)
            : rule.Id;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        uint value = BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(0, sizeof(uint))) &
            0x7FFF_FFFFU;
        if (value == 0)
            value = 1;

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string? ReadFlagField(string value, string fieldName)
    {
        foreach (string part in value.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = part.Trim();
            if (trimmed.StartsWith(fieldName, StringComparison.OrdinalIgnoreCase))
                return trimmed[fieldName.Length..].Trim();
        }

        return null;
    }
}
