using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Transiever.SieveRuler.Models;
using Transiever.SieveRuler.Services;

namespace Transiever.SieveRuler.UnitTest;

public sealed class SieveImporterAndCompositionTests
{
    [Fact]
    public void Import_RecognizesGeneratedCompatibleSubsetAndPreservesOpaqueRules()
    {
        byte[] script = Encoding.UTF8.GetBytes(
            """
            require ["fileinto", "body"];

            if header :matches "subject" "*" {
                discard;
            }

            # Rule: Billing
            if allof(
                address :contains "from" ["billing@example.com"],
                anyof(header :contains "subject" ["invoice"], body :contains ["invoice"])
            ) {
                fileinto "INBOX/Billing";
                stop;
            }
            """);

        SieveImportResult result = new SieveImporter().Import(script);

        ImportedSieveRule imported = Assert.Single(result.ExternalRules);
        Assert.Equal("Billing", imported.Rule.Name);
        Assert.Equal("INBOX/Billing", imported.Rule.TargetFolder);
        Assert.Collection(
            imported.Rule.Conditions,
            condition => Assert.Equal(RuleConditionType.SenderContains, condition.Type),
            condition => Assert.Equal(RuleConditionType.SubjectOrBodyContains, condition.Type));
        Assert.Contains("body", imported.Rule.RequiredCapabilities);
        Assert.Contains("fileinto", result.DeclaredCapabilities);
    }

    [Fact]
    public void Import_UsesProviderFlagRuleNameBeforeRuleComment()
    {
        byte[] script = Encoding.UTF8.GetBytes(
            """
            require ["fileinto"];

            # Rule: Fallback name
            ## Flag: |UniqueId:1234|Rulename: Provider name|LastModified: 2026-06-26T00:00:00Z|ModifiedBy: mail.example
            if header :contains "subject" "invoice" {
                fileinto "INBOX/Billing";
                stop;
            }
            """);

        ImportedSieveRule imported = Assert.Single(
            new SieveImporter().Import(script).ExternalRules);

        Assert.Equal("Provider name", imported.Rule.Name);
    }

    [Fact]
    public void Import_RecognizesProviderRuleWithoutStop()
    {
        byte[] script = Encoding.UTF8.GetBytes(
            """
            require [ "fileinto" ];

            ## Flag: |UniqueId:6|Rulename: openprovider|LastModified: 2026-01-24T20:00:30Z|ModifiedBy: 95.114.211.166
            if address :contains "from" "@openprovider.nl"
            {
            fileinto "INBOX/Openprovider" ;
            }
            """);

        ImportedSieveRule imported = Assert.Single(
            new SieveImporter().Import(script).ExternalRules);

        Assert.Equal("openprovider", imported.Rule.Name);
        Assert.Equal("INBOX/Openprovider", imported.Rule.TargetFolder);
    }

    [Fact]
    public void Import_TreatsMalformedRequireAsOpaqueInsteadOfFailing()
    {
        SieveImportResult result = new SieveImporter().Import(
            "require [\"fileinto\";\r\nkeep;\r\n"u8.ToArray());

        Assert.Empty(result.ExternalRules);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == "OpaqueSieveContentPreserved");
    }

    [Fact]
    public void Compose_PreservesUnownedContentAndRemovesOnlyAdoptedRuleSpan()
    {
        byte[] script = Encoding.UTF8.GetBytes(
            """
            require ["fileinto"];

            # User-owned opaque rule
            if header :matches "subject" "*" {
                keep;
            }

            # Rule: Existing
            if header :contains "subject" "invoice" {
                fileinto "INBOX/Billing";
                stop;
            }
            """);
        var importer = new SieveImporter();
        SieveImportResult imported = importer.Import(script);
        var reconciler = new RuleReconciler(new RuleOptimizer());
        RuleReconciliationResult reconciliation = reconciler.Reconcile(
            "outlook",
            [],
            imported,
            adoptCompatible: true,
            optimizationMode: null);
        var composer = new SieveScriptComposer(importer, new SieveGenerator());

        SieveCompositionResult result = composer.Compose(imported, reconciliation);
        string candidate = Encoding.UTF8.GetString(result.Content);

        Assert.False(result.IsBlocked);
        Assert.Contains("# User-owned opaque rule", candidate);
        Assert.Contains("header :matches", candidate);
        Assert.Equal(1, Count(candidate, "header :contains \"Subject\" \"invoice\""));
        Assert.Contains(SieveImporter.RulesBegin, candidate);
        Assert.DoesNotContain(SieveImporter.RequirementsBegin, candidate);
        Assert.Equal(1, Count(candidate, "require "));
    }

    [Fact]
    public void ManagedMetadata_RoundTripsAndDetectsBodyEdits()
    {
        var importer = new SieveImporter();
        SieveImportResult empty = importer.Import(Array.Empty<byte>());
        RuleDefinition rule = CreateRule("Server", "server");
        var reconciliation = new RuleReconciliationResult
        {
            OwnedSourceRules = [rule],
            RenderedRules = [rule]
        };
        var composer = new SieveScriptComposer(importer, new SieveGenerator());

        SieveCompositionResult composed = composer.Compose(empty, reconciliation);
        string composedText = Encoding.UTF8.GetString(composed.Content);
        var reflectionOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters =
            {
                new JsonStringEnumConverter<RuleConditionMode>(allowIntegerValues: false),
                new JsonStringEnumConverter<RuleConditionType>(allowIntegerValues: false),
                new JsonStringEnumConverter<RuleActionType>(allowIntegerValues: false),
                new JsonStringEnumConverter<RuleOwnership>(allowIntegerValues: false)
            }
        };
        byte[] expectedMetadata = JsonSerializer.SerializeToUtf8Bytes(
            new List<RuleDefinition> { rule },
            reflectionOptions);
        string expectedMetadataText = Convert.ToBase64String(expectedMetadata);
        string actualMetadataText = string.Concat(
            composedText.Split("\r\n")
                .Where(line => line.StartsWith(SieveImporter.MetadataPrefix, StringComparison.Ordinal))
                .Select(line => line[SieveImporter.MetadataPrefix.Length..]));
        string expectedMetadataHash = Convert.ToHexString(SHA256.HashData(expectedMetadata));

        Assert.Equal(expectedMetadataText, actualMetadataText);
        Assert.Contains(
            $"{SieveImporter.MetadataHashPrefix}{expectedMetadataHash}\r\n",
            composedText);

        SieveImportResult roundTrip = importer.Import(composed.Content);
        Assert.False(roundTrip.ManagedRegionConflict);
        Assert.Equal("server", Assert.Single(roundTrip.ManagedSourceRules).SourceId);

        string changed = composedText
            .Replace("\"invoice\"", "\"changed\"", StringComparison.Ordinal);
        SieveImportResult tampered = importer.Import(Encoding.UTF8.GetBytes(changed));

        Assert.True(tampered.ManagedRegionConflict);
        Assert.Contains(
            tampered.Diagnostics,
            diagnostic => diagnostic.Code == "ManagedRegionModified");

        string orphaned = composedText
            .Replace(SieveImporter.RulesBegin, "# CHANGED SIEVERULER RULES v1");
        Assert.True(
            importer.Import(Encoding.UTF8.GetBytes(orphaned))
                .ManagedRegionConflict);
    }

    [Fact]
    public void Reconcile_PreservesAdoptedServerRulesAndDropsObsoleteOutlookRules()
    {
        var importer = new SieveImporter();
        RuleDefinition serverRule = CreateRule("Server", "server");
        RuleDefinition oldOutlook = CreateRule("Old Outlook", "outlook", "old");
        var existing = new SieveImportResult
        {
            OriginalContent = [],
            ManagedSourceRules = [serverRule, oldOutlook]
        };
        RuleDefinition current = CreateRule("Current", "outlook", "current");

        RuleReconciliationResult result = new RuleReconciler(new RuleOptimizer())
            .Reconcile("outlook", [current], existing, false, RuleOptimizationMode.Conservative);

        Assert.Contains(result.OwnedSourceRules, rule => rule.Name == "Server");
        Assert.Contains(result.OwnedSourceRules, rule => rule.Name == "Current");
        Assert.DoesNotContain(result.OwnedSourceRules, rule => rule.Name == "Old Outlook");
    }

    [Fact]
    public void Reconcile_ExternalEquivalentRuleSuppressesGeneratedDuplicate()
    {
        RuleDefinition external = CreateRule("External", "server");
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
                [CreateRule("Outlook", "outlook")],
                imported,
                adoptCompatible: false,
                optimizationMode: null);

        Assert.Empty(result.OwnedSourceRules);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == "DuplicateSuppressedByExternalRule");
    }

    [Fact]
    public void Reconcile_DoesNotRetireRulesOwnedByAnotherSource()
    {
        var imported = new SieveImportResult
        {
            OriginalContent = [],
            ManagedSourceRules =
            [
                CreateRule("Outlook", "outlook"),
                CreateRule("Thunderbird", "thunderbird")
            ]
        };

        RuleReconciliationResult result = new RuleReconciler(new RuleOptimizer())
            .Reconcile(
                "outlook",
                [],
                imported,
                adoptCompatible: false,
                optimizationMode: null);

        Assert.DoesNotContain(
            result.OwnedSourceRules,
            rule => rule.SourceId == "outlook");
        Assert.Contains(
            result.OwnedSourceRules,
            rule => rule.SourceId == "thunderbird");
    }

    [Fact]
    public void Reconcile_PreservesPerRuleSourceOverride()
    {
        RuleDefinition importedRule = CreateRule("Imported", "thunderbird");

        RuleReconciliationResult result = new RuleReconciler(new RuleOptimizer())
            .Reconcile(
                "outlook",
                [importedRule],
                new SieveImportResult { OriginalContent = [] },
                adoptCompatible: false,
                optimizationMode: null);

        Assert.Equal(
            "thunderbird",
            Assert.Single(result.OwnedSourceRules).SourceId);
    }

    [Fact]
    public void Compose_BlocksCandidateAfterUnconditionalTopLevelStop()
    {
        var importer = new SieveImporter();
        SieveImportResult imported = importer.Import("stop;\r\n"u8.ToArray());
        var composer = new SieveScriptComposer(importer, new SieveGenerator());

        SieveCompositionResult result = composer.Compose(
            imported,
            new RuleReconciliationResult());

        Assert.True(result.IsBlocked);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == "ManagedRegionUnreachable");
    }

    [Fact]
    public void ManagedMetadata_IsChunkedAndStillRoundTrips()
    {
        var importer = new SieveImporter();
        RuleDefinition[] rules = Enumerable.Range(0, 100)
            .Select(index => CreateRule(
                $"Server rule {index:D3} with a deliberately long name",
                "server",
                $"invoice-{index:D3}@example.com"))
            .ToArray();
        var composer = new SieveScriptComposer(importer, new SieveGenerator());

        SieveCompositionResult composed = composer.Compose(
            importer.Import(Array.Empty<byte>()),
            new RuleReconciliationResult
            {
                OwnedSourceRules = rules,
                RenderedRules = rules
            });
        string text = Encoding.UTF8.GetString(composed.Content);

        Assert.True(
            text.Split(SieveImporter.MetadataPrefix, StringSplitOptions.None).Length > 2);
        Assert.Equal(100, importer.Import(composed.Content).ManagedSourceRules.Count);
    }

    [Fact]
    public void Compose_MergesMultipleLeadingRequireStatementsIntoOne()
    {
        byte[] script = Encoding.UTF8.GetBytes(
            """
            require ["imap4flags"];
            require ["fileinto"];

            if true {
                keep;
            }
            """);
        var importer = new SieveImporter();
        SieveImportResult imported = importer.Import(script);
        RuleDefinition rule = CreateRule("Body", "outlook", "invoice");
        rule.Conditions[0] = new RuleCondition
        {
            Type = RuleConditionType.BodyContains,
            Values = ["invoice"]
        };

        SieveCompositionResult composed = new SieveScriptComposer(
            importer,
            new SieveGenerator()).Compose(
                imported,
                new RuleReconciliationResult
                {
                    OwnedSourceRules = [rule],
                    RenderedRules = [rule]
                });
        string candidate = Encoding.UTF8.GetString(composed.Content);

        Assert.Equal(1, Count(candidate, "require "));
        Assert.Contains(
            "require [\"body\", \"fileinto\", \"imap4flags\"];",
            candidate);
    }

    [Fact]
    public void Compose_PreservesOpaqueDeclaredCapabilitiesAfterMerge()
    {
        byte[] script = Encoding.UTF8.GetBytes(
            """
            # Existing provider feature
            require ["imap4flags"];

            if true {
                keep;
            }
            """);
        var importer = new SieveImporter();
        SieveImportResult imported = importer.Import(script);
        RuleDefinition rule = CreateRule("Subject", "outlook");

        SieveCompositionResult composed = new SieveScriptComposer(
            importer,
            new SieveGenerator()).Compose(
                imported,
                new RuleReconciliationResult
                {
                    OwnedSourceRules = [rule],
                    RenderedRules = [rule]
                });
        string candidate = Encoding.UTF8.GetString(composed.Content);

        Assert.Equal(1, Count(candidate, "require "));
        Assert.Contains("\"imap4flags\"", candidate);
        Assert.Contains("if true", candidate);
    }

    [Fact]
    public void Compose_PreservesOpaqueProviderFlagsOutsideAdoptedSpans()
    {
        const string opaqueFlag =
            "## Flag: |UniqueId:999|Rulename: Existing provider rule|ProviderOpaque: value|";
        byte[] script = Encoding.UTF8.GetBytes(
            $$"""
            require ["fileinto"];

            {{opaqueFlag}}
            if true {
                keep;
            }
            """);
        var importer = new SieveImporter();
        SieveImportResult imported = importer.Import(script);
        RuleDefinition rule = CreateRule("Managed", "outlook");

        SieveCompositionResult composed = new SieveScriptComposer(
            importer,
            new SieveGenerator()).Compose(
                imported,
                new RuleReconciliationResult
                {
                    OwnedSourceRules = [rule],
                    RenderedRules = [rule]
                });
        string candidate = Encoding.UTF8.GetString(composed.Content);

        Assert.Contains(opaqueFlag, candidate);
    }

    private static RuleDefinition CreateRule(
        string name,
        string source,
        string value = "invoice") =>
        new()
        {
            Name = name,
            TargetFolder = "INBOX/Billing",
            SourceId = source,
            Conditions =
            [
                new RuleCondition
                {
                    Type = RuleConditionType.SubjectContains,
                    Values = [value]
                }
            ]
        };

    private static int Count(string value, string search)
    {
        int count = 0;
        int position = 0;
        while ((position = value.IndexOf(search, position, StringComparison.Ordinal)) >= 0)
        {
            count++;
            position += search.Length;
        }

        return count;
    }
}
