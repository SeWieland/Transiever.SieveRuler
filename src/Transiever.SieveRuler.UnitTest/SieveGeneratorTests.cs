using Transiever.SieveRuler.Models;
using Transiever.SieveRuler.Services;

namespace Transiever.SieveRuler.UnitTest;

public sealed class SieveGeneratorTests
{
    [Fact]
    public void Generate_ProducesProviderNeutralReviewNotice()
    {
        RuleDefinition rule = new()
        {
            Name = "Invoices",
            TargetFolder = "INBOX/Finance",
            Conditions =
            [
                new RuleCondition
                {
                    Type = RuleConditionType.SubjectContains,
                    Values = ["Invoice"]
                }
            ]
        };

        string script = new SieveGenerator().Generate([rule]);

        Assert.Contains("Sieve-compatible mail server", script);
        Assert.DoesNotContain("mailbox.org", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fileinto \"INBOX/Finance\" ;", script);
    }

    [Fact]
    public void Generate_EmitsProviderFlagImmediatelyBeforeRuleCommand()
    {
        RuleDefinition rule = new()
        {
            Id = "stable-rule-id",
            Name = "Invoices|Production\r\nUnsafe",
            TargetFolder = "INBOX/Finance",
            Conditions =
            [
                new RuleCondition
                {
                    Type = RuleConditionType.SubjectContains,
                    Values = ["Invoice"]
                }
            ]
        };

        string first = new SieveGenerator().GenerateRuleBody([rule]);
        string second = new SieveGenerator().GenerateRuleBody([rule]);
        string flagLine = first
            .Split('\n')
            .Single(line => line.StartsWith("## Flag:", StringComparison.Ordinal))
            .TrimEnd('\r');
        string secondFlagLine = second
            .Split('\n')
            .Single(line => line.StartsWith("## Flag:", StringComparison.Ordinal))
            .TrimEnd('\r');

        Assert.Matches(@"^## Flag: \|UniqueId:[1-9][0-9]{0,9}\|Rulename: Invoices Production  Unsafe$", flagLine);
        Assert.DoesNotContain("LastModified", flagLine);
        Assert.DoesNotContain("ModifiedBy", flagLine);
        Assert.Equal(
            ReadField(flagLine, "UniqueId:"),
            ReadField(secondFlagLine, "UniqueId:"));
        Assert.DoesNotContain("Invoices|Production", flagLine);
        Assert.Contains(
            flagLine + "\r\n" +
            "if header :contains \"subject\" \"Invoice\"\r\n" +
            "{\r\n" +
            "fileinto \"INBOX/Finance\" ;\r\n" +
            "}",
            first);
    }

    [Fact]
    public void Generate_RequiresBodyExtensionWhenNeeded()
    {
        RuleDefinition rule = new()
        {
            TargetFolder = "INBOX/Content",
            Conditions =
            [
                new RuleCondition
                {
                    Type = RuleConditionType.BodyContains,
                    Values = ["keyword"]
                }
            ]
        };

        string script = new SieveGenerator().Generate([rule]);

        Assert.Contains("require [\"body\", \"fileinto\"];", script);
    }

    private static string ReadField(string flagLine, string fieldName) =>
        flagLine
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Single(part => part.StartsWith(fieldName, StringComparison.Ordinal))
            [fieldName.Length..]
            .Trim();
}
