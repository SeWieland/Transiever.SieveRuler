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
        string normalized = NormalizeNewLines(first);
        Assert.Contains(
            flagLine + "\n" +
            "if header :contains \"Subject\" \"Invoice\"\n" +
            "{\n" +
            "fileinto \"INBOX/Finance\" ;\n" +
            "}",
            normalized);
    }

    [Fact]
    public void Generate_UsesProviderCompatibleSubjectHeaderCaseInBodyCombination()
    {
        RuleDefinition rule = new()
        {
            TargetFolder = "INBOX/Content",
            Conditions =
            [
                new RuleCondition
                {
                    Type = RuleConditionType.SubjectOrBodyContains,
                    Values = ["keyword"]
                }
            ]
        };

        string script = new SieveGenerator().Generate([rule]);

        Assert.Contains("header :contains \"Subject\" \"keyword\"", script);
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

    [Fact]
    public void Generate_RendersActionsExceptionsAndCapabilities()
    {
        RuleDefinition rule = new()
        {
            Name = "Forward invoice copies",
            Conditions =
            [
                new RuleCondition
                {
                    Type = RuleConditionType.SenderContains,
                    Values = ["billing@example.com"]
                }
            ],
            Exceptions =
            [
                new RuleCondition
                {
                    Type = RuleConditionType.SubjectContains,
                    Values = ["internal"]
                }
            ],
            Actions =
            [
                new RuleAction
                {
                    Type = RuleActionType.SetFlags,
                    Values = ["\\Seen"]
                },
                new RuleAction
                {
                    Type = RuleActionType.CopyInto,
                    Values = ["INBOX/Copies"]
                },
                new RuleAction
                {
                    Type = RuleActionType.Redirect,
                    Values = ["archive@example.com"]
                },
                new RuleAction
                {
                    Type = RuleActionType.Stop
                }
            ]
        };

        string script = new SieveGenerator().Generate([rule]);

        Assert.Contains(
            "require [\"copy\", \"fileinto\", \"imap4flags\"];",
            script);
        Assert.Contains(
            "if allof ( address :contains \"from\" \"billing@example.com\" , not header :contains \"Subject\" \"internal\" )",
            script);
        Assert.Contains("addflag \"\\\\Seen\" ;", script);
        Assert.Contains("fileinto :copy \"INBOX/Copies\" ;", script);
        Assert.Contains("redirect \"archive@example.com\" ;", script);
        Assert.Contains("stop ;", script);
    }

    [Fact]
    public void Generate_RendersAttachmentConditionWithMimeCapability()
    {
        RuleDefinition rule = new()
        {
            TargetFolder = "INBOX/Attachments",
            Conditions =
            [
                new RuleCondition
                {
                    Type = RuleConditionType.HasAttachment
                }
            ]
        };

        string script = new SieveGenerator().Generate([rule]);

        Assert.Contains("require [\"fileinto\", \"mime\"];", script);
        Assert.Contains(
            "header :mime :anychild :contains \"Content-Disposition\" \"attachment\"",
            script);
    }

    private static string ReadField(string flagLine, string fieldName) =>
        flagLine
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Single(part => part.StartsWith(fieldName, StringComparison.Ordinal))
            [fieldName.Length..]
            .Trim();

    private static string NormalizeNewLines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
}
