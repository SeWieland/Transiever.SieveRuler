using Transiever.SieveRuler.Models;
using Transiever.SieveRuler.Services;

namespace Transiever.SieveRuler.UnitTest;

public sealed class RuleOptimizerTests
{
    [Fact]
    public void Optimize_MergesEquivalentSingleConditionRules()
    {
        RuleDefinition[] rules =
        [
            CreateSenderRule("First", "Inbox/Development", "first@example.com"),
            CreateSenderRule("Second", "Inbox/Development", "second@example.com")
        ];

        RuleOptimizationResult result = new RuleOptimizer().Optimize(
            rules,
            RuleOptimizationMode.Conservative);

        RuleDefinition optimizedRule = Assert.Single(result.Rules);
        RuleCondition condition = Assert.Single(optimizedRule.Conditions);

        Assert.Equal(2, result.OriginalRuleCount);
        Assert.Equal(
            ["first@example.com", "second@example.com"],
            condition.GetValues());
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Action == "MergedEquivalentRules");
    }

    [Fact]
    public void Optimize_MergesExplicitOutlookActionsAndPreservesActions()
    {
        RuleAction[] actions = MarkReadFileIntoAndStop("Inbox/Development");
        RuleDefinition[] rules =
        [
            CreateSenderRule(
                "First",
                "Inbox/Development",
                "first@example.com",
                actions),
            CreateSenderRule(
                "Second",
                "Inbox/Development",
                "second@example.com",
                actions)
        ];

        RuleOptimizationResult result = new RuleOptimizer().Optimize(
            rules,
            RuleOptimizationMode.Conservative);

        RuleDefinition optimizedRule = Assert.Single(result.Rules);
        RuleCondition condition = Assert.Single(optimizedRule.Conditions);

        Assert.Equal(
            ["first@example.com", "second@example.com"],
            condition.GetValues());
        Assert.Collection(
            optimizedRule.Actions,
            action =>
            {
                Assert.Equal(RuleActionType.SetFlags, action.Type);
                Assert.Equal(["\\Seen"], action.GetValues());
            },
            action =>
            {
                Assert.Equal(RuleActionType.FileInto, action.Type);
                Assert.Equal(["Inbox/Development"], action.GetValues());
            },
            action => Assert.Equal(RuleActionType.Stop, action.Type));
    }

    [Fact]
    public void Optimize_MergesTargetFolderShortcutWithEquivalentExplicitFileInto()
    {
        RuleDefinition[] rules =
        [
            CreateSenderRule("Shortcut", "Inbox/Development", "first@example.com"),
            CreateSenderRule(
                "Explicit",
                "Inbox/Development",
                "second@example.com",
                [FileInto("Inbox/Development")])
        ];

        RuleOptimizationResult result = new RuleOptimizer().Optimize(
            rules,
            RuleOptimizationMode.Conservative);

        RuleDefinition optimizedRule = Assert.Single(result.Rules);
        RuleCondition condition = Assert.Single(optimizedRule.Conditions);

        Assert.Equal(
            ["first@example.com", "second@example.com"],
            condition.GetValues());
        RuleAction action = Assert.Single(optimizedRule.Actions);
        Assert.Equal(RuleActionType.FileInto, action.Type);
        Assert.Equal(["Inbox/Development"], action.GetValues());
    }

    [Fact]
    public void Optimize_DoesNotMergeRulesForDifferentFolders()
    {
        RuleDefinition[] rules =
        [
            CreateSenderRule("First", "Inbox/First", "sender@example.com"),
            CreateSenderRule("Second", "Inbox/Second", "sender@example.com")
        ];

        RuleOptimizationResult result = new RuleOptimizer().Optimize(
            rules,
            RuleOptimizationMode.Conservative);

        Assert.Equal(2, result.Rules.Count);
    }

    [Fact]
    public void Optimize_ConservativeDoesNotMergeDifferentConditionTypes()
    {
        RuleDefinition[] rules =
        [
            CreateSenderRule("Sender", "Inbox/Development", "sender@example.com"),
            CreateSubjectRule("Subject", "Inbox/Development", "invoice")
        ];

        RuleOptimizationResult result = new RuleOptimizer().Optimize(
            rules,
            RuleOptimizationMode.Conservative);

        Assert.Equal(2, result.Rules.Count);
    }

    [Fact]
    public void Optimize_BalancedMergesDifferentConditionTypesWithSameActions()
    {
        RuleDefinition[] rules =
        [
            CreateSenderRule("Sender", "Inbox/Development", "sender@example.com"),
            CreateSubjectRule("Subject", "Inbox/Development", "invoice")
        ];

        RuleOptimizationResult result = new RuleOptimizer().Optimize(
            rules,
            RuleOptimizationMode.Balanced);

        RuleDefinition optimizedRule = Assert.Single(result.Rules);

        Assert.Equal(RuleConditionMode.Any, optimizedRule.ConditionMode);
        Assert.Collection(
            optimizedRule.Conditions.OrderBy(condition => condition.Type.ToString()),
            condition =>
            {
                Assert.Equal(RuleConditionType.SenderContains, condition.Type);
                Assert.Equal(["sender@example.com"], condition.GetValues());
            },
            condition =>
            {
                Assert.Equal(RuleConditionType.SubjectContains, condition.Type);
                Assert.Equal(["invoice"], condition.GetValues());
            });
    }

    [Fact]
    public void Optimize_MergesHasAttachmentRulesWithoutValues()
    {
        RuleDefinition[] rules =
        [
            CreateHasAttachmentRule("First", "Inbox/Attachments"),
            CreateHasAttachmentRule("Second", "Inbox/Attachments")
        ];

        RuleOptimizationResult result = new RuleOptimizer().Optimize(
            rules,
            RuleOptimizationMode.Conservative);

        RuleDefinition optimizedRule = Assert.Single(result.Rules);
        RuleCondition condition = Assert.Single(optimizedRule.Conditions);

        Assert.Equal(RuleConditionType.HasAttachment, condition.Type);
        Assert.Empty(condition.GetValues());
    }

    [Fact]
    public void Optimize_DoesNotMergeRulesWithDifferentActions()
    {
        RuleDefinition[] rules =
        [
            CreateSenderRule(
                "Move",
                "Inbox/Development",
                "sender@example.com",
                [FileInto("Inbox/Development")]),
            CreateSenderRule(
                "Mark read and move",
                "Inbox/Development",
                "other@example.com",
                MarkReadFileIntoAndStop("Inbox/Development"))
        ];

        RuleOptimizationResult result = new RuleOptimizer().Optimize(
            rules,
            RuleOptimizationMode.Aggressive);

        Assert.Equal(2, result.Rules.Count);
    }

    [Fact]
    public void Optimize_DoesNotMergeRulesWithDifferentExceptions()
    {
        RuleDefinition[] rules =
        [
            CreateSenderRule(
                "First",
                "Inbox/Development",
                "first@example.com",
                exceptions:
                [
                    new RuleCondition
                    {
                        Type = RuleConditionType.BodyContains,
                        Values = ["internal"]
                    }
                ]),
            CreateSenderRule(
                "Second",
                "Inbox/Development",
                "second@example.com",
                exceptions:
                [
                    new RuleCondition
                    {
                        Type = RuleConditionType.BodyContains,
                        Values = ["external"]
                    }
                ])
        ];

        RuleOptimizationResult result = new RuleOptimizer().Optimize(
            rules,
            RuleOptimizationMode.Aggressive);

        Assert.Equal(2, result.Rules.Count);
    }

    [Fact]
    public void Optimize_DoesNotMergeRedirectOnlyRules()
    {
        RuleDefinition[] rules =
        [
            CreateSenderRule(
                "First",
                "",
                "first@example.com",
                [Redirect("archive@example.com")]),
            CreateSenderRule(
                "Second",
                "",
                "second@example.com",
                [Redirect("archive@example.com")])
        ];

        RuleOptimizationResult result = new RuleOptimizer().Optimize(
            rules,
            RuleOptimizationMode.Aggressive);

        Assert.Equal(2, result.Rules.Count);
    }

    [Fact]
    public void Optimize_ConservativePreservesIndividualSenderAddresses()
    {
        RuleDefinition[] rules =
        [
            CreateSenderRule("First", "Inbox/Development", "first@example.com"),
            CreateSenderRule("Second", "Inbox/Development", "second@example.com"),
            CreateSenderRule("Third", "Inbox/Development", "third@example.com")
        ];

        RuleOptimizationResult result = new RuleOptimizer().Optimize(
            rules,
            RuleOptimizationMode.Conservative);

        RuleCondition condition = Assert.Single(Assert.Single(result.Rules).Conditions);

        Assert.Equal(
            ["first@example.com", "second@example.com", "third@example.com"],
            condition.GetValues());
    }

    [Fact]
    public void Optimize_BalancedInfersExactDomainForThreeSenders()
    {
        RuleDefinition[] rules =
        [
            CreateSenderRule("First", "Inbox/Development", "first@example.com"),
            CreateSenderRule("Second", "Inbox/Development", "second@example.com"),
            CreateSenderRule("Third", "Inbox/Development", "third@example.com")
        ];

        RuleOptimizationResult result = new RuleOptimizer().Optimize(
            rules,
            RuleOptimizationMode.Balanced);

        RuleCondition condition = Assert.Single(Assert.Single(result.Rules).Conditions);

        Assert.Equal(["@example.com"], condition.GetValues());
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Action == "InferredSenderDomain"
                && diagnostic.Severity == "Warning");
    }

    [Fact]
    public void Optimize_AggressiveInfersDomainForExplicitFileIntoActions()
    {
        RuleDefinition[] rules =
        [
            CreateSenderRule(
                "First",
                "Inbox/Development",
                "first@example.com",
                [FileInto("Inbox/Development")]),
            CreateSenderRule(
                "Second",
                "Inbox/Development",
                "second@example.com",
                [FileInto("Inbox/Development")])
        ];

        RuleOptimizationResult result = new RuleOptimizer().Optimize(
            rules,
            RuleOptimizationMode.Aggressive);

        RuleCondition condition = Assert.Single(Assert.Single(result.Rules).Conditions);

        Assert.Equal(["@example.com"], condition.GetValues());
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Action == "InferredSenderDomain"
                && diagnostic.Severity == "Warning");
    }

    [Fact]
    public void Optimize_BalancedDoesNotInferDomainBelowThreshold()
    {
        RuleDefinition[] rules =
        [
            CreateSenderRule("First", "Inbox/Development", "first@example.com"),
            CreateSenderRule("Second", "Inbox/Development", "second@example.com")
        ];

        RuleOptimizationResult result = new RuleOptimizer().Optimize(
            rules,
            RuleOptimizationMode.Balanced);

        RuleCondition condition = Assert.Single(Assert.Single(result.Rules).Conditions);

        Assert.Equal(
            ["first@example.com", "second@example.com"],
            condition.GetValues());
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Action == "InferredSenderDomain");
    }

    [Fact]
    public void Optimize_BalancedDoesNotInferRoleSenderDomain()
    {
        RuleDefinition rule = CreateSenderRule(
            "Newsletter",
            "Inbox/Newsletters",
            "newsletter@example.com");

        RuleOptimizationResult result = new RuleOptimizer().Optimize(
            [rule],
            RuleOptimizationMode.Balanced);

        RuleCondition condition = Assert.Single(Assert.Single(result.Rules).Conditions);

        Assert.Equal(["newsletter@example.com"], condition.GetValues());
    }

    [Fact]
    public void Optimize_AggressiveInfersExactDomainForTwoSenders()
    {
        RuleDefinition[] rules =
        [
            CreateSenderRule("First", "Inbox/Development", "first@example.com"),
            CreateSenderRule("Second", "Inbox/Development", "second@example.com")
        ];

        RuleOptimizationResult result = new RuleOptimizer().Optimize(
            rules,
            RuleOptimizationMode.Aggressive);

        RuleCondition condition = Assert.Single(Assert.Single(result.Rules).Conditions);

        Assert.Equal(["@example.com"], condition.GetValues());
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Action == "InferredSenderDomain"
                && diagnostic.Severity == "Warning");
    }

    [Theory]
    [InlineData("newsletter@example.com")]
    [InlineData("no-reply@example.com")]
    [InlineData("do_not_reply@example.com")]
    [InlineData("billing@example.com")]
    [InlineData("info@example.com")]
    [InlineData("notifications+weekly@example.com")]
    [InlineData("orders@example.com")]
    [InlineData("receipts@example.com")]
    [InlineData("mailer-daemon@example.com")]
    [InlineData("payments@example.com")]
    [InlineData("security@example.com")]
    [InlineData("subscriptions@example.com")]
    [InlineData("support@example.com")]
    [InlineData("updates@example.com")]
    public void Optimize_AggressiveInfersDomainForRoleSender(string sender)
    {
        RuleDefinition rule = CreateSenderRule(
            "Role sender",
            "Inbox/Automated",
            sender);

        RuleOptimizationResult result = new RuleOptimizer().Optimize(
            [rule],
            RuleOptimizationMode.Aggressive);

        RuleCondition condition = Assert.Single(Assert.Single(result.Rules).Conditions);

        Assert.Equal(["@example.com"], condition.GetValues());
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Action == "InferredRoleSenderDomain"
                && diagnostic.Severity == "Warning");
    }

    [Fact]
    public void Optimize_AggressiveCollapsesSiblingSubdomains()
    {
        RuleDefinition[] rules =
        [
            CreateSenderRule(
                "First",
                "Inbox/Development",
                "developer@notifications.example.com"),
            CreateSenderRule(
                "Second",
                "Inbox/Development",
                "release@updates.example.com")
        ];

        RuleOptimizationResult result = new RuleOptimizer().Optimize(
            rules,
            RuleOptimizationMode.Aggressive);

        RuleCondition condition = Assert.Single(Assert.Single(result.Rules).Conditions);

        Assert.Equal(["example.com"], condition.GetValues());
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Action == "CollapsedSenderSubdomains"
                && diagnostic.Severity == "Warning");
    }

    [Fact]
    public void Optimize_AggressiveCollapsesExactDomainAndItsSubdomain()
    {
        RuleDefinition[] rules =
        [
            CreateSenderRule("Info", "Posteingang/XYZ", "info@xyz.de"),
            CreateSenderRule("Noreply", "Posteingang/XYZ", "noreply@info.xyz.de")
        ];

        RuleOptimizationResult result = new RuleOptimizer().Optimize(
            rules,
            RuleOptimizationMode.Aggressive);

        RuleCondition condition = Assert.Single(Assert.Single(result.Rules).Conditions);

        Assert.Equal(["xyz.de"], condition.GetValues());
        Assert.DoesNotContain(
            condition.GetValues(),
            value => value.Contains("info.xyz.de", StringComparison.OrdinalIgnoreCase));
        Assert.Collection(
            result.Diagnostics,
            diagnostic => Assert.Equal("MergedEquivalentRules", diagnostic.Action),
            diagnostic => Assert.Equal("InferredRoleSenderDomain", diagnostic.Action),
            diagnostic => Assert.Equal("InferredRoleSenderDomain", diagnostic.Action),
            diagnostic => Assert.Equal("CollapsedSenderSubdomains", diagnostic.Action));
    }

    [Fact]
    public void Optimize_AggressiveDoesNotCollapseToKnownPublicSuffix()
    {
        RuleDefinition[] rules =
        [
            CreateSenderRule("First", "Inbox/Development", "person@first.co.uk"),
            CreateSenderRule("Second", "Inbox/Development", "person@second.co.uk")
        ];

        RuleOptimizationResult result = new RuleOptimizer().Optimize(
            rules,
            RuleOptimizationMode.Aggressive);

        RuleCondition condition = Assert.Single(Assert.Single(result.Rules).Conditions);

        Assert.Equal(
            ["person@first.co.uk", "person@second.co.uk"],
            condition.GetValues());
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Action == "CollapsedSenderSubdomains");
    }

    [Fact]
    public void Optimize_AggressiveDoesNotCollapseSubdomainsAcrossFolders()
    {
        RuleDefinition[] rules =
        [
            CreateSenderRule(
                "First",
                "Inbox/First",
                "person@alerts.example.com"),
            CreateSenderRule(
                "Second",
                "Inbox/Second",
                "person@news.example.com")
        ];

        RuleOptimizationResult result = new RuleOptimizer().Optimize(
            rules,
            RuleOptimizationMode.Aggressive);

        Assert.Equal(2, result.Rules.Count);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Action == "CollapsedSenderSubdomains");
    }

    private static RuleDefinition CreateSenderRule(
        string name,
        string targetFolder,
        string sender,
        IReadOnlyList<RuleAction>? actions = null,
        IReadOnlyList<RuleCondition>? exceptions = null)
    {
        return new RuleDefinition
        {
            Name = name,
            TargetFolder = targetFolder,
            Actions = actions?.ToList() ?? [],
            Conditions =
            [
                new RuleCondition
                {
                    Type = RuleConditionType.SenderContains,
                    Values = [sender]
                }
            ],
            Exceptions = exceptions?.ToList() ?? []
        };
    }

    private static RuleDefinition CreateSubjectRule(
        string name,
        string targetFolder,
        string subject) =>
        new()
        {
            Name = name,
            TargetFolder = targetFolder,
            Conditions =
            [
                new RuleCondition
                {
                    Type = RuleConditionType.SubjectContains,
                    Values = [subject]
                }
            ]
        };

    private static RuleDefinition CreateHasAttachmentRule(
        string name,
        string targetFolder) =>
        new()
        {
            Name = name,
            TargetFolder = targetFolder,
            Conditions =
            [
                new RuleCondition
                {
                    Type = RuleConditionType.HasAttachment
                }
            ]
        };

    private static RuleAction[] MarkReadFileIntoAndStop(string targetFolder) =>
    [
        new RuleAction
        {
            Type = RuleActionType.SetFlags,
            Values = ["\\Seen"]
        },
        FileInto(targetFolder),
        new RuleAction
        {
            Type = RuleActionType.Stop
        }
    ];

    private static RuleAction FileInto(string targetFolder) =>
        new()
        {
            Type = RuleActionType.FileInto,
            Values = [targetFolder]
        };

    private static RuleAction Redirect(string address) =>
        new()
        {
            Type = RuleActionType.Redirect,
            Values = [address]
        };
}
