using System.Text;
using Transiever.SieveRuler.Models;
using Transiever.SieveRuler.Services;

namespace Transiever.SieveRuler.UnitTest;

public sealed class BaselineReconciliationGoldenTests
{
    [Fact]
    public void REC001_ReplacesOnlyAuthoritativeRulesAndPreservesOpaqueContent()
    {
        var importer = new SieveImporter();
        var composer = new SieveScriptComposer(importer, new SieveGenerator());
        SieveImportResult imported = importer.Import(
            ReadBase64Fixture("REC-001.active.base64"));
        Assert.Contains(
            imported.ManagedSourceRules,
            rule => rule.Name == "Obsolete Outlook");
        Assert.Contains(
            imported.ManagedSourceRules,
            rule => rule.Name == "Retained Thunderbird");
        RuleDefinition currentOutlook = CreateRule(
            "Current Outlook",
            "outlook",
            "current",
            "INBOX/Current");

        RuleReconciliationResult reconciliation = new RuleReconciler(
            new RuleOptimizer()).Reconcile(
                "outlook",
                [currentOutlook],
                imported,
                adoptCompatible: false,
                optimizationMode: null);

        Assert.Contains(
            reconciliation.OwnedSourceRules,
            rule => rule.Name == "Retained Thunderbird");
        Assert.Contains(
            reconciliation.OwnedSourceRules,
            rule => rule.Name == "Current Outlook");
        Assert.DoesNotContain(
            reconciliation.OwnedSourceRules,
            rule => rule.Name == "Obsolete Outlook");
        Assert.Contains(
            reconciliation.Diagnostics,
            diagnostic => diagnostic.Code == "ObsoleteManagedSourceRuleRemoved");

        SieveCompositionResult result = composer.Compose(imported, reconciliation);

        Assert.False(result.IsBlocked);
        Assert.Equal(["fileinto", "imap4flags"], result.RequiredCapabilities);
        string goldenPath = GetFixturePath("REC-001.candidate.base64");
        if (!File.Exists(goldenPath))
        {
            string actualPath = Path.Combine(
                Path.GetTempPath(),
                "Transiever-M0-04-REC-001.actual.sieve");
            File.WriteAllBytes(actualPath, result.Content);
            Assert.Fail($"Missing REC-001 golden. Actual bytes were written to '{actualPath}'.");
        }
        Assert.Equal(
            Convert.FromBase64String(File.ReadAllText(goldenPath).Trim()),
            result.Content);
        Assert.True(ContainsBytes(
            result.Content,
            Encoding.UTF8.GetBytes("## Flag: |UniqueId:999|Rulename: Opaque provider rule|ProviderOpaque: value|")));
        Assert.True(ContainsBytes(
            result.Content,
            Encoding.UTF8.GetBytes("notify :message \"opaque\";")));
        Assert.Contains("Current Outlook", Encoding.UTF8.GetString(result.Content));
        Assert.Contains("Retained Thunderbird", Encoding.UTF8.GetString(result.Content));
        Assert.DoesNotContain("Obsolete Outlook", Encoding.UTF8.GetString(result.Content));
    }

    [Fact]
    public void REC002_ExternalEquivalentSuppressesGeneratedDuplicate()
    {
        RuleDefinition external = CreateRule(
            "External equivalent",
            "server",
            "same");
        var imported = new SieveImportResult
        {
            OriginalContent = [],
            ExternalRules =
            [
                new ImportedSieveRule(
                    external,
                    new SieveSourceSpan(0, 0))
            ]
        };

        RuleReconciliationResult result = new RuleReconciler(new RuleOptimizer())
            .Reconcile(
                "outlook",
                [CreateRule("Outlook duplicate", "outlook", "same")],
                imported,
                adoptCompatible: false,
                optimizationMode: null);

        Assert.Empty(result.OwnedSourceRules);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == "DuplicateSuppressedByExternalRule");
    }

    [Fact]
    public void REC002_UnconditionalTopLevelStopBlocksComposition()
    {
        var importer = new SieveImporter();
        SieveCompositionResult result = new SieveScriptComposer(
            importer,
            new SieveGenerator()).Compose(
                importer.Import("stop;\r\n"u8.ToArray()),
                new RuleReconciliationResult());

        Assert.True(result.IsBlocked);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == "ManagedRegionUnreachable");
    }

    private static RuleDefinition CreateRule(
        string name,
        string source,
        string value,
        string targetFolder = "INBOX/Billing") =>
        new()
        {
            Name = name,
            TargetFolder = targetFolder,
            SourceId = source,
            Conditions =
            [
                new RuleCondition
                {
                    Type = RuleConditionType.SubjectContains,
                    Values = [value]
                }
            ],
            Actions =
            [
                new RuleAction
                {
                    Type = RuleActionType.FileInto,
                    Values = [targetFolder]
                },
                new RuleAction
                {
                    Type = RuleActionType.Stop
                }
            ]
        };

    private static string GetFixturePath(string fileName) => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "SieveV1",
        fileName);

    private static byte[] ReadBase64Fixture(string fileName) =>
        Convert.FromBase64String(File.ReadAllText(GetFixturePath(fileName)).Trim());

    private static bool ContainsBytes(byte[] haystack, byte[] needle)
    {
        for (int start = 0; start <= haystack.Length - needle.Length; start++)
        {
            if (haystack.AsSpan(start, needle.Length).SequenceEqual(needle))
                return true;
        }

        return false;
    }
}
