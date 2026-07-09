using System.Security.Cryptography;
using System.Text.Json;
using Transiever.ManageSieve;
using Transiever.SieveRuler.Application;
using Transiever.SieveRuler.Models;
using Transiever.SieveRuler.Services;

namespace Transiever.SieveRuler.UnitTest;

public sealed class SieveSynchronizationWorkflowTests
{
    private static readonly JsonSerializerOptions PlanOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [Fact]
    public async Task Preview_WritesCandidateRulesRenderedWithOptimization()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"Transiever.SieveRuler-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            var serializer = new JsonRuleSerializer();
            string rulesFile = Path.Combine(directory, "rules.json");
            string reconciledRulesFile = Path.Combine(directory, "reconciled-rules.json");
            string candidateRulesFile = Path.Combine(directory, "candidate-rules.json");

            await serializer.SaveDocumentAsync(
                new RuleDocument
                {
                    SourceId = "outlook",
                    Rules =
                    [
                        CreateSenderRule("First", "first@example.com"),
                        CreateSenderRule("Second", "second@example.com")
                    ]
                },
                rulesFile,
                cancellationToken);

            var importer = new SieveImporter();
            var optimizer = new RuleOptimizer();
            var workflow = new SieveSynchronizationWorkflow(
                serializer,
                importer,
                new RuleReconciler(optimizer),
                new SieveScriptComposer(importer, new SieveGenerator()),
                new FakeConnectionFactory(FakeConnection.Empty()),
                new TestInteraction());

            PreviewSynchronizationResult result = await workflow.PreviewAsync(
                new PreviewSynchronizationRequest(
                    new SieveServerConfiguration(
                        "localhost",
                        SieveServerConfiguration.DefaultPort,
                        "user",
                        "password",
                        SieveConnectionSecurity.StartTlsRequired),
                    rulesFile,
                    reconciledRulesFile,
                    candidateRulesFile,
                    Path.Combine(directory, "server.sieve"),
                    Path.Combine(directory, "candidate.sieve"),
                    Path.Combine(directory, "plan.json"),
                    AdoptCompatible: false,
                    OptimizationMode: RuleOptimizationMode.Conservative),
                cancellationToken);

            RuleDocument reconciled = await serializer.LoadDocumentAsync(
                reconciledRulesFile,
                cancellationToken);
            RuleDocument candidateRules = await serializer.LoadDocumentAsync(
                candidateRulesFile,
                cancellationToken);

            Assert.Equal(PreviewSynchronizationStatus.Prepared, result.Status);
            Assert.Equal(2, reconciled.Rules.Count);
            RuleDefinition candidateRule = Assert.Single(candidateRules.Rules);
            RuleCondition condition = Assert.Single(candidateRule.Conditions);
            Assert.Equal(
                ["first@example.com", "second@example.com"],
                condition.Values);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Preview_UsesActiveScriptNameAsTargetAndPlansBackup()
    {
        string directory = CreateDirectory();

        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            var serializer = new JsonRuleSerializer();
            string rulesFile = Path.Combine(directory, "rules.json");
            string planFile = Path.Combine(directory, "plan.json");
            byte[] activeContent =
                "require [\"fileinto\"];\r\nif true { keep; }\r\n"u8.ToArray();

            await serializer.SaveDocumentAsync(
                new RuleDocument
                {
                    SourceId = "outlook",
                    Rules = [CreateSenderRule("First", "first@example.com")]
                },
                rulesFile,
                cancellationToken);

            var importer = new SieveImporter();
            var workflow = new SieveSynchronizationWorkflow(
                serializer,
                importer,
                new RuleReconciler(new RuleOptimizer()),
                new SieveScriptComposer(importer, new SieveGenerator()),
                new FakeConnectionFactory(
                    FakeConnection.WithScripts(
                        "Open-Xchange",
                        ("Open-Xchange", activeContent))),
                new TestInteraction());

            PreviewSynchronizationResult result = await workflow.PreviewAsync(
                CreatePreviewRequest(directory, rulesFile, planFile),
                cancellationToken);
            DeploymentPlan plan = JsonSerializer.Deserialize<DeploymentPlan>(
                await File.ReadAllTextAsync(planFile, cancellationToken),
                PlanOptions)!;

            Assert.Equal(PreviewSynchronizationStatus.Prepared, result.Status);
            Assert.True(result.ReplacesActiveScript);
            Assert.Equal("Open-Xchange", result.TargetScriptName);
            Assert.Equal("Open-Xchange", plan.TargetScriptName);
            Assert.StartsWith("srtx-backup-", plan.BackupScriptName);
            Assert.Equal(Hash(activeContent), plan.BackupContentSha256);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Preview_CanReturnPlanWithoutWritingArtifacts()
    {
        string directory = CreateDirectory();

        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            string rulesFile = Path.Combine(directory, "rules.json");
            string planFile = Path.Combine(directory, "plan.json");

            PreviewSynchronizationResult result = await CreateWorkflow(FakeConnection.Empty())
                .PreviewAsync(
                    new PreviewSynchronizationRequest(
                        TestConfiguration(),
                        rulesFile,
                        Path.Combine(directory, "reconciled-rules.json"),
                        Path.Combine(directory, "candidate-rules.json"),
                        Path.Combine(directory, "server.sieve"),
                        Path.Combine(directory, "candidate.sieve"),
                        planFile,
                        AdoptCompatible: false,
                        SourceDocument: new RuleDocument
                        {
                            SourceId = "outlook",
                            Rules = [CreateSenderRule("First", "first@example.com")]
                        },
                        WriteArtifacts: false),
                    cancellationToken);

            Assert.Equal(PreviewSynchronizationStatus.Prepared, result.Status);
            Assert.False(result.FilesWritten);
            Assert.NotNull(result.Plan);
            Assert.False(File.Exists(rulesFile));
            Assert.False(File.Exists(planFile));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Preview_AcceptsCapabilitiesDeclaredByActiveScript()
    {
        string directory = CreateDirectory();

        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            var serializer = new JsonRuleSerializer();
            string rulesFile = Path.Combine(directory, "rules.json");
            byte[] activeContent =
                "require [\"body\", \"fileinto\", \"imap4flags\"];\r\nkeep;\r\n"u8.ToArray();

            await serializer.SaveDocumentAsync(
                new RuleDocument
                {
                    SourceId = "outlook",
                    Rules =
                    [
                        new RuleDefinition
                        {
                            Name = "Read invoices",
                            TargetFolder = "INBOX/Invoices",
                            Conditions =
                            [
                                new RuleCondition
                                {
                                    Type = RuleConditionType.SubjectContains,
                                    Values = ["invoice"]
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
                                    Type = RuleActionType.FileInto,
                                    Values = ["INBOX/Invoices"]
                                }
                            ]
                        }
                    ]
                },
                rulesFile,
                cancellationToken);

            PreviewSynchronizationResult result = await CreateWorkflow(
                    FakeConnection.WithScripts(
                        "Open-Xchange",
                        ("Open-Xchange", activeContent)))
                .PreviewAsync(
                    CreatePreviewRequest(
                        directory,
                        rulesFile,
                        Path.Combine(directory, "plan.json")),
                    cancellationToken);

            Assert.Equal(PreviewSynchronizationStatus.Prepared, result.Status);
            Assert.Empty(result.MissingCapabilities);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Rollback_PlanWithoutBackupReactivatesSourceScript()
    {
        string directory = CreateDirectory();

        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            byte[] source = "require [\"fileinto\"];\r\nkeep;\r\n"u8.ToArray();
            byte[] candidate = "require [\"fileinto\"];\r\nstop;\r\n"u8.ToArray();
            string planFile = await WritePlanAsync(
                directory,
                new DeploymentPlan
                {
                    SchemaVersion = DeploymentPlan.CurrentSchemaVersion,
                    SourceActiveScriptName = "source",
                    SourceContentSha256 = Hash(source),
                    CandidateContentBase64 = Convert.ToBase64String(candidate),
                    CandidateContentSha256 = Hash(candidate),
                    TargetScriptName = "srtx-candidate"
                },
                cancellationToken);
            var connection = FakeConnection.WithScripts(
                "srtx-candidate",
                ("source", source),
                ("srtx-candidate", candidate));

            RollbackSynchronizationResult result = await CreateWorkflow(connection)
                .RollbackAsync(
                    new RollbackSynchronizationRequest(
                        TestConfiguration(),
                        planFile),
                    cancellationToken);

            Assert.Equal(RollbackSynchronizationStatus.ReactivatedSource, result.Status);
            Assert.Equal("source", connection.ActiveScriptName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Rollback_RefusesWhenCurrentActiveDoesNotMatchCandidate()
    {
        string directory = CreateDirectory();

        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            byte[] source = "keep;\r\n"u8.ToArray();
            byte[] candidate = "stop;\r\n"u8.ToArray();
            byte[] changed = "discard;\r\n"u8.ToArray();
            string planFile = await WritePlanAsync(
                directory,
                new DeploymentPlan
                {
                    SchemaVersion = DeploymentPlan.CurrentSchemaVersion,
                    SourceActiveScriptName = "source",
                    SourceContentSha256 = Hash(source),
                    CandidateContentBase64 = Convert.ToBase64String(candidate),
                    CandidateContentSha256 = Hash(candidate),
                    TargetScriptName = "srtx-candidate"
                },
                cancellationToken);
            var connection = FakeConnection.WithScripts(
                "srtx-candidate",
                ("source", source),
                ("srtx-candidate", changed));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateWorkflow(connection).RollbackAsync(
                    new RollbackSynchronizationRequest(TestConfiguration(), planFile),
                    cancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Rollback_ForceBypassesOnlyCurrentActiveMismatch()
    {
        string directory = CreateDirectory();

        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            byte[] source = "keep;\r\n"u8.ToArray();
            byte[] candidate = "stop;\r\n"u8.ToArray();
            byte[] changed = "discard;\r\n"u8.ToArray();
            string planFile = await WritePlanAsync(
                directory,
                new DeploymentPlan
                {
                    SchemaVersion = DeploymentPlan.CurrentSchemaVersion,
                    SourceActiveScriptName = "source",
                    SourceContentSha256 = Hash(source),
                    CandidateContentBase64 = Convert.ToBase64String(candidate),
                    CandidateContentSha256 = Hash(candidate),
                    TargetScriptName = "srtx-candidate"
                },
                cancellationToken);
            var connection = FakeConnection.WithScripts(
                "other",
                ("source", source),
                ("srtx-candidate", changed),
                ("other", changed));

            RollbackSynchronizationResult result = await CreateWorkflow(connection)
                .RollbackAsync(
                    new RollbackSynchronizationRequest(
                        TestConfiguration(),
                        planFile,
                        Force: true),
                    cancellationToken);

            Assert.Equal(RollbackSynchronizationStatus.ReactivatedSource, result.Status);
            Assert.Equal("source", connection.ActiveScriptName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Rollback_VersionThreePlanRestoresServerSideBackup()
    {
        string directory = CreateDirectory();

        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            byte[] backup = "require [\"fileinto\"];\r\nkeep;\r\n"u8.ToArray();
            byte[] candidate = "require [\"fileinto\"];\r\nstop;\r\n"u8.ToArray();
            string planFile = await WritePlanAsync(
                directory,
                new DeploymentPlan
                {
                    SchemaVersion = DeploymentPlan.CurrentSchemaVersion,
                    SourceActiveScriptName = "Open-Xchange",
                    SourceContentSha256 = Hash(backup),
                    CandidateContentBase64 = Convert.ToBase64String(candidate),
                    CandidateContentSha256 = Hash(candidate),
                    TargetScriptName = "Open-Xchange",
                    BackupScriptName = "srtx-backup-test",
                    BackupContentSha256 = Hash(backup)
                },
                cancellationToken);
            var connection = FakeConnection.WithScripts(
                "Open-Xchange",
                ("Open-Xchange", candidate),
                ("srtx-backup-test", backup));

            RollbackSynchronizationResult result = await CreateWorkflow(connection)
                .RollbackAsync(
                    new RollbackSynchronizationRequest(TestConfiguration(), planFile),
                    cancellationToken);

            Assert.Equal(RollbackSynchronizationStatus.RestoredBackup, result.Status);
            Assert.Equal("Open-Xchange", connection.ActiveScriptName);
            Assert.Equal(backup, connection.GetContent("Open-Xchange"));
            Assert.True(connection.ContainsScript("srtx-backup-test"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Rollback_ForceDoesNotBypassBackupHashValidation()
    {
        string directory = CreateDirectory();

        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            byte[] backup = "keep;\r\n"u8.ToArray();
            byte[] candidate = "stop;\r\n"u8.ToArray();
            string wrongHash = Hash("discard;\r\n"u8.ToArray());
            string planFile = await WritePlanAsync(
                directory,
                new DeploymentPlan
                {
                    SchemaVersion = DeploymentPlan.CurrentSchemaVersion,
                    SourceActiveScriptName = "Open-Xchange",
                    SourceContentSha256 = wrongHash,
                    CandidateContentBase64 = Convert.ToBase64String(candidate),
                    CandidateContentSha256 = Hash(candidate),
                    TargetScriptName = "Open-Xchange",
                    BackupScriptName = "srtx-backup-test",
                    BackupContentSha256 = wrongHash
                },
                cancellationToken);
            var connection = FakeConnection.WithScripts(
                "Open-Xchange",
                ("Open-Xchange", candidate),
                ("srtx-backup-test", backup));

            await Assert.ThrowsAsync<InvalidDataException>(
                () => CreateWorkflow(connection).RollbackAsync(
                    new RollbackSynchronizationRequest(
                        TestConfiguration(),
                        planFile,
                        Force: true),
                    cancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Deploy_PrunesOnlyInactiveSieveRulerHistoryBeyondRetention()
    {
        string directory = CreateDirectory();

        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            byte[] source = "keep;\r\n"u8.ToArray();
            byte[] candidate = "stop;\r\n"u8.ToArray();
            string targetName = "srtx-20260626000009-target";
            string planFile = await WritePlanAsync(
                directory,
                new DeploymentPlan
                {
                    SourceActiveScriptName = "source",
                    SourceContentSha256 = Hash(source),
                    CandidateContentBase64 = Convert.ToBase64String(candidate),
                    CandidateContentSha256 = Hash(candidate),
                    TargetScriptName = targetName
                },
                cancellationToken);
            var connection = FakeConnection.WithScripts(
                "source",
                ("source", source),
                ("user-script", source),
                ("srtx-backup-20240101000000-original", source),
                ("srtx-backup-20240201000000-old", source),
                ("srtx-20240301000000-old", source),
                ("srtx-20240401000000-old", source),
                ("srtx-20240501000000-keep", source),
                ("srtx-20240601000000-keep", source));

            DeploySynchronizationResult result = await CreateWorkflow(connection)
                .DeployAsync(
                    new DeploySynchronizationRequest(
                        TestConfiguration(),
                        planFile,
                        HistoryLimit: 2),
                    cancellationToken);

            Assert.Equal(DeploySynchronizationStatus.Activated, result.Status);
            Assert.Equal(targetName, connection.ActiveScriptName);
            Assert.Equal(
                [
                    "srtx-backup-20240201000000-old",
                    "srtx-20240301000000-old",
                    "srtx-20240401000000-old"
                ],
                result.DeletedHistoryScriptNames);
            Assert.Equal(result.DeletedHistoryScriptNames, connection.DeletedScripts);
            Assert.True(connection.ContainsScript("source"));
            Assert.True(connection.ContainsScript("user-script"));
            Assert.True(connection.ContainsScript(targetName));
            Assert.True(connection.ContainsScript("srtx-backup-20240101000000-original"));
            Assert.True(connection.ContainsScript("srtx-20240501000000-keep"));
            Assert.True(connection.ContainsScript("srtx-20240601000000-keep"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Deploy_HaveSpaceFailurePrunesAndRetriesOnce()
    {
        string directory = CreateDirectory();

        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            byte[] source = "keep;\r\n"u8.ToArray();
            byte[] candidate = "stop;\r\n"u8.ToArray();
            string planFile = await WritePlanAsync(
                directory,
                new DeploymentPlan
                {
                    SourceActiveScriptName = "source",
                    SourceContentSha256 = Hash(source),
                    CandidateContentBase64 = Convert.ToBase64String(candidate),
                    CandidateContentSha256 = Hash(candidate),
                    TargetScriptName = "srtx-new"
                },
                cancellationToken);
            var connection = FakeConnection.WithScripts(
                "source",
                ("source", source),
                ("srtx-20240101000000-old", source),
                ("srtx-20240201000000-old", source))
                .WithHaveSpaceResponses(false, true);

            DeploySynchronizationResult result = await CreateWorkflow(connection)
                .DeployAsync(
                    new DeploySynchronizationRequest(
                        TestConfiguration(),
                        planFile,
                        HistoryLimit: 0),
                    cancellationToken);

            Assert.Equal(DeploySynchronizationStatus.Activated, result.Status);
            Assert.Equal("srtx-new", connection.ActiveScriptName);
            Assert.Equal(2, connection.HaveSpaceCalls);
            Assert.Equal(
                ["srtx-20240101000000-old", "srtx-20240201000000-old"],
                result.DeletedHistoryScriptNames);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Deploy_CleanupFailuresSurfaceAsWarnings()
    {
        string directory = CreateDirectory();

        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            byte[] source = "keep;\r\n"u8.ToArray();
            byte[] candidate = "stop;\r\n"u8.ToArray();
            string planFile = await WritePlanAsync(
                directory,
                new DeploymentPlan
                {
                    SourceActiveScriptName = "source",
                    SourceContentSha256 = Hash(source),
                    CandidateContentBase64 = Convert.ToBase64String(candidate),
                    CandidateContentSha256 = Hash(candidate),
                    TargetScriptName = "srtx-new"
                },
                cancellationToken);
            var connection = FakeConnection.WithScripts(
                "source",
                ("source", source),
                ("srtx-20240101000000-old", source))
                .WithDeleteFailure("srtx-20240101000000-old");

            DeploySynchronizationResult result = await CreateWorkflow(connection)
                .DeployAsync(
                    new DeploySynchronizationRequest(
                        TestConfiguration(),
                        planFile,
                        HistoryLimit: 0),
                    cancellationToken);

            Assert.Equal(DeploySynchronizationStatus.Activated, result.Status);
            Assert.Empty(result.DeletedHistoryScriptNames);
            Assert.Contains(
                result.CleanupWarnings,
                warning => warning.Contains(
                    "srtx-20240101000000-old",
                    StringComparison.Ordinal));
            Assert.True(connection.ContainsScript("srtx-20240101000000-old"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task HistoryList_ReturnsSieveRulerHistoryAndMarksOriginal()
    {
        byte[] current = "keep;\r\n"u8.ToArray();
        FakeConnection connection = FakeConnection.WithScripts(
            "Open-Xchange",
            ("Open-Xchange", current),
            ("srtx-backup-20240101000000-original", current),
            ("srtx-backup-20240201000000-newer", current),
            ("srtx-20240301000000-candidate", current),
            ("external", current));

        HistoryListResult result = await CreateWorkflow(connection)
            .ListHistoryAsync(
                new HistoryListRequest(TestConfiguration()),
                TestContext.Current.CancellationToken);

        Assert.Equal("Open-Xchange", result.ActiveScriptName);
        Assert.Equal(
            [
                "srtx-20240301000000-candidate",
                "srtx-backup-20240201000000-newer",
                "srtx-backup-20240101000000-original"
            ],
            result.Entries.Select(entry => entry.Name).ToArray());
        Assert.Equal(SieveHistoryEntryKind.Candidate, result.Entries[0].Kind);
        Assert.True(result.Entries.Single(
            entry => entry.Name == "srtx-backup-20240101000000-original").IsOriginal);
    }

    [Fact]
    public async Task HistoryShow_ReturnsContentHashAndLength()
    {
        byte[] backup = "require [\"fileinto\"];\r\nkeep;\r\n"u8.ToArray();
        FakeConnection connection = FakeConnection.WithScripts(
            "",
            ("srtx-backup-20240101000000-original", backup));

        HistoryShowResult result = await CreateWorkflow(connection)
            .ShowHistoryAsync(
                new HistoryShowRequest(
                    TestConfiguration(),
                    "srtx-backup-20240101000000-original"),
                TestContext.Current.CancellationToken);

        Assert.Equal(backup, result.Content);
        Assert.Equal(Hash(backup), result.Entry.ContentSha256);
        Assert.Equal(backup.Length, result.Entry.ContentLength);
        Assert.True(result.Entry.IsOriginal);
    }

    [Fact]
    public async Task HistoryRestore_RestoresBackupIntoCurrentActiveAndKeepsFreshBackup()
    {
        byte[] current = "keep;\r\n"u8.ToArray();
        byte[] backup = "discard;\r\n"u8.ToArray();
        FakeConnection connection = FakeConnection.WithScripts(
            "Open-Xchange",
            ("Open-Xchange", current),
            ("srtx-backup-20240101000000-original", backup));

        HistoryRestoreResult result = await CreateWorkflow(connection)
            .RestoreHistoryAsync(
                new HistoryRestoreRequest(
                    TestConfiguration(),
                    "srtx-backup-20240101000000-original"),
                TestContext.Current.CancellationToken);

        Assert.Equal(HistoryRestoreStatus.RestoredScript, result.Status);
        Assert.Equal("Open-Xchange", result.TargetScriptName);
        Assert.StartsWith("srtx-backup-", result.BackupScriptName);
        Assert.Equal(backup, connection.GetContent("Open-Xchange"));
        Assert.Equal(current, connection.GetContent(result.BackupScriptName!));
    }

    [Fact]
    public async Task HistoryRestore_LatestRestoresNewestInactiveBackup()
    {
        byte[] current = "keep;\r\n"u8.ToArray();
        byte[] older = "discard;\r\n"u8.ToArray();
        byte[] newer = "require [\"fileinto\"];\r\nfileinto \"INBOX/Previous\";\r\n"u8.ToArray();
        FakeConnection connection = FakeConnection.WithScripts(
            "Open-Xchange",
            ("Open-Xchange", current),
            ("srtx-backup-20240101000000-older", older),
            ("srtx-20240201000000-candidate", "stop;\r\n"u8.ToArray()),
            ("srtx-backup-20240301000000-newer", newer));

        HistoryRestoreResult result = await CreateWorkflow(connection)
            .RestoreHistoryAsync(
                new HistoryRestoreRequest(TestConfiguration(), "latest"),
                TestContext.Current.CancellationToken);

        Assert.Equal(HistoryRestoreStatus.RestoredScript, result.Status);
        Assert.Equal("srtx-backup-20240301000000-newer", result.SourceScriptName);
        Assert.Equal(newer, connection.GetContent("Open-Xchange"));
        Assert.Equal(current, connection.GetContent(result.BackupScriptName!));
    }

    [Fact]
    public async Task HistoryRestore_OriginalNoActiveMarkerDisablesActiveAndBacksUpCurrent()
    {
        byte[] current = "keep;\r\n"u8.ToArray();
        FakeConnection connection = FakeConnection.WithScripts(
            "Open-Xchange",
            ("Open-Xchange", current),
            ("srtx-backup-20240101000000-no-active", "keep;\r\n"u8.ToArray()));

        HistoryRestoreResult result = await CreateWorkflow(connection)
            .RestoreHistoryAsync(
                new HistoryRestoreRequest(TestConfiguration(), "original"),
                TestContext.Current.CancellationToken);

        Assert.Equal(HistoryRestoreStatus.DisabledActive, result.Status);
        Assert.Equal("", connection.ActiveScriptName);
        Assert.StartsWith("srtx-backup-", result.BackupScriptName);
        Assert.Equal(current, connection.GetContent(result.BackupScriptName!));
    }

    [Fact]
    public async Task HistoryDelete_DeletesInactiveHistoryScript()
    {
        byte[] current = "keep;\r\n"u8.ToArray();
        FakeConnection connection = FakeConnection.WithScripts(
            "Open-Xchange",
            ("Open-Xchange", current),
            ("srtx-20240101000000-candidate", current));

        HistoryDeleteResult result = await CreateWorkflow(connection)
            .DeleteHistoryAsync(
                new HistoryDeleteRequest(
                    TestConfiguration(),
                    "srtx-20240101000000-candidate"),
                TestContext.Current.CancellationToken);

        Assert.Equal(HistoryDeleteStatus.Deleted, result.Status);
        Assert.Equal("srtx-20240101000000-candidate", result.ScriptName);
        Assert.False(connection.ContainsScript("srtx-20240101000000-candidate"));
        Assert.Equal(["srtx-20240101000000-candidate"], connection.DeletedScripts);
    }

    [Fact]
    public async Task HistoryDelete_RefusesActiveHistoryScript()
    {
        byte[] current = "keep;\r\n"u8.ToArray();
        FakeConnection connection = FakeConnection.WithScripts(
            "srtx-20240101000000-active",
            ("srtx-20240101000000-active", current),
            ("srtx-backup-20231201000000-original", current));

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateWorkflow(connection).DeleteHistoryAsync(
                    new HistoryDeleteRequest(
                        TestConfiguration(),
                        "srtx-20240101000000-active"),
                    TestContext.Current.CancellationToken));

        Assert.Contains("active", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(connection.ContainsScript("srtx-20240101000000-active"));
        Assert.Empty(connection.DeletedScripts);
    }

    [Fact]
    public async Task HistoryDelete_DoesNotResolveLatestShortcut()
    {
        byte[] current = "keep;\r\n"u8.ToArray();
        FakeConnection connection = FakeConnection.WithScripts(
            "Open-Xchange",
            ("Open-Xchange", current),
            ("srtx-backup-20240101000000-original", current));

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateWorkflow(connection).DeleteHistoryAsync(
                    new HistoryDeleteRequest(
                        TestConfiguration(),
                        "latest"),
                    TestContext.Current.CancellationToken));

        Assert.Contains("latest", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(connection.ContainsScript("srtx-backup-20240101000000-original"));
        Assert.Empty(connection.DeletedScripts);
    }

    [Fact]
    public async Task HistoryPrune_DeletesAllInactiveSieveRulerHistoryExceptActive()
    {
        byte[] current = "keep;\r\n"u8.ToArray();
        FakeConnection connection = FakeConnection.WithScripts(
            "srtx-20240501000000-active",
            ("srtx-20240501000000-active", current),
            ("srtx-backup-20240101000000-original", current),
            ("srtx-backup-20240201000000-no-active", current),
            ("srtx-20240301000000-candidate", current),
            ("external", current));

        HistoryPruneResult result = await CreateWorkflow(connection)
            .PruneHistoryAsync(
                new HistoryPruneRequest(TestConfiguration()),
                TestContext.Current.CancellationToken);

        Assert.Equal(HistoryPruneStatus.Pruned, result.Status);
        Assert.Equal(
            [
                "srtx-backup-20240101000000-original",
                "srtx-backup-20240201000000-no-active",
                "srtx-20240301000000-candidate"
            ],
            result.DeletedScriptNames);
        Assert.Empty(result.Warnings);
        Assert.True(connection.ContainsScript("srtx-20240501000000-active"));
        Assert.True(connection.ContainsScript("external"));
        Assert.False(connection.ContainsScript("srtx-backup-20240101000000-original"));
        Assert.False(connection.ContainsScript("srtx-backup-20240201000000-no-active"));
        Assert.False(connection.ContainsScript("srtx-20240301000000-candidate"));
    }

    [Fact]
    public async Task HistoryPrune_DryRunDoesNotDelete()
    {
        byte[] current = "keep;\r\n"u8.ToArray();
        FakeConnection connection = FakeConnection.WithScripts(
            "Open-Xchange",
            ("Open-Xchange", current),
            ("srtx-backup-20240101000000-original", current),
            ("srtx-20240201000000-candidate", current));

        HistoryPruneResult result = await CreateWorkflow(connection)
            .PruneHistoryAsync(
                new HistoryPruneRequest(TestConfiguration(), DryRun: true),
                TestContext.Current.CancellationToken);

        Assert.Equal(HistoryPruneStatus.PlanValidated, result.Status);
        Assert.Equal(
            [
                "srtx-backup-20240101000000-original",
                "srtx-20240201000000-candidate"
            ],
            result.DeletedScriptNames);
        Assert.True(connection.ContainsScript("srtx-backup-20240101000000-original"));
        Assert.True(connection.ContainsScript("srtx-20240201000000-candidate"));
        Assert.Empty(connection.DeletedScripts);
    }

    [Fact]
    public async Task Deploy_FromNoActiveStateCreatesOriginalNoActiveMarker()
    {
        string directory = CreateDirectory();
        try
        {
            byte[] candidate = "keep;\r\n"u8.ToArray();
            string planFile = await WritePlanAsync(
                directory,
                new DeploymentPlan
                {
                    SourceActiveScriptName = "",
                    SourceContentSha256 = Hash([]),
                    CandidateContentBase64 = Convert.ToBase64String(candidate),
                    CandidateContentSha256 = Hash(candidate),
                    TargetScriptName = "srtx-20240101000000-candidate"
                },
                TestContext.Current.CancellationToken);
            FakeConnection connection = FakeConnection.Empty();

            DeploySynchronizationResult result = await CreateWorkflow(connection)
                .DeployAsync(
                    new DeploySynchronizationRequest(
                        TestConfiguration(),
                        planFile),
                    TestContext.Current.CancellationToken);

            Assert.Equal(DeploySynchronizationStatus.Activated, result.Status);
            Assert.StartsWith("srtx-backup-", result.BackupScriptName);
            Assert.EndsWith("no-active", result.BackupScriptName);
            Assert.True(connection.ContainsScript(result.BackupScriptName!));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static RuleDefinition CreateSenderRule(string name, string value) =>
        new()
        {
            Name = name,
            TargetFolder = "INBOX/Development",
            Conditions =
            [
                new RuleCondition
                {
                    Type = RuleConditionType.SenderContains,
                    Values = [value]
                }
            ]
        };

    private static SieveSynchronizationWorkflow CreateWorkflow(
        FakeConnection connection)
    {
        var serializer = new JsonRuleSerializer();
        var importer = new SieveImporter();
        var optimizer = new RuleOptimizer();
        return new SieveSynchronizationWorkflow(
            serializer,
            importer,
            new RuleReconciler(optimizer),
            new SieveScriptComposer(importer, new SieveGenerator()),
            new FakeConnectionFactory(connection),
            new TestInteraction());
    }

    private static PreviewSynchronizationRequest CreatePreviewRequest(
        string directory,
        string rulesFile,
        string planFile) =>
        new(
            TestConfiguration(),
            rulesFile,
            Path.Combine(directory, "reconciled-rules.json"),
            Path.Combine(directory, "candidate-rules.json"),
            Path.Combine(directory, "server.sieve"),
            Path.Combine(directory, "candidate.sieve"),
            planFile,
            AdoptCompatible: false);

    private static async Task<string> WritePlanAsync(
        string directory,
        DeploymentPlan plan,
        CancellationToken cancellationToken)
    {
        string planFile = Path.Combine(directory, "plan.json");
        await File.WriteAllTextAsync(
            planFile,
            JsonSerializer.Serialize(plan, PlanOptions),
            cancellationToken);
        return planFile;
    }

    private static string CreateDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"Transiever.SieveRuler-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static SieveServerConfiguration TestConfiguration() =>
        new(
            "localhost",
            SieveServerConfiguration.DefaultPort,
            "user",
            "password",
            SieveConnectionSecurity.StartTlsRequired);

    private static string Hash(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content));

    private sealed class FakeConnectionFactory(FakeConnection connection)
        : ISieveServerConnectionFactory
    {
        public Task<ISieveServerConnection> ConnectAsync(
            SieveServerConfiguration configuration,
            CancellationToken cancellationToken) =>
            Task.FromResult<ISieveServerConnection>(connection);
    }

    private sealed class FakeConnection : ISieveServerConnection
    {
        private readonly Dictionary<string, byte[]> scripts;
        private readonly Queue<bool> haveSpaceResponses = [];
        private readonly HashSet<string> deleteFailures = new(StringComparer.Ordinal);
        private readonly List<string> deletedScripts = [];

        private FakeConnection(
            string activeScriptName,
            Dictionary<string, byte[]> scripts)
        {
            ActiveScriptName = activeScriptName;
            this.scripts = scripts;
        }

        public string ActiveScriptName { get; private set; }

        public int HaveSpaceCalls { get; private set; }

        public IReadOnlyList<string> DeletedScripts => deletedScripts;

        public static FakeConnection Empty() =>
            new("", []);

        public static FakeConnection WithScripts(
            string activeScriptName,
            params (string Name, byte[] Content)[] scripts) =>
            new(
                activeScriptName,
                scripts.ToDictionary(
                    script => script.Name,
                    script => script.Content.ToArray(),
                    StringComparer.Ordinal));

        public FakeConnection WithHaveSpaceResponses(params bool[] responses)
        {
            foreach (bool response in responses)
                haveSpaceResponses.Enqueue(response);

            return this;
        }

        public FakeConnection WithDeleteFailure(string scriptName)
        {
            deleteFailures.Add(scriptName);
            return this;
        }

        public bool ContainsScript(string scriptName) =>
            scripts.ContainsKey(scriptName);

        public byte[] GetContent(string scriptName) =>
            scripts[scriptName].ToArray();

        public Task<RemoteSieveState> ReadStateAsync(
            CancellationToken cancellationToken)
        {
            byte[] activeContent = ActiveScriptName.Length == 0
                ? []
                : scripts[ActiveScriptName];
            return Task.FromResult(
                new RemoteSieveState
                {
                    ActiveScriptName = ActiveScriptName,
                    ActiveContent = activeContent.ToArray(),
                    ActiveContentSha256 = Hash(activeContent),
                    Scripts = scripts
                        .Keys
                        .Select(name => new ManageSieveScriptInfo(
                            name,
                            name.Equals(ActiveScriptName, StringComparison.Ordinal)))
                        .ToArray(),
                    Capabilities = new ManageSieveCapabilities
                    {
                        SieveExtensions = new HashSet<string>(
                            ["fileinto"],
                            StringComparer.OrdinalIgnoreCase)
                    }
                });
        }

        public Task CheckScriptAsync(
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<bool> HaveSpaceAsync(
            string scriptName,
            long contentLength,
            CancellationToken cancellationToken)
        {
            HaveSpaceCalls++;
            return Task.FromResult(
                haveSpaceResponses.Count == 0 || haveSpaceResponses.Dequeue());
        }

        public Task<byte[]> GetScriptAsync(
            string scriptName,
            CancellationToken cancellationToken) =>
            Task.FromResult(GetContent(scriptName));

        public Task PutScriptAsync(
            string scriptName,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken)
        {
            scripts[scriptName] = content.ToArray();
            return Task.CompletedTask;
        }

        public Task ActivateAsync(
            string? scriptName,
            CancellationToken cancellationToken)
        {
            ActiveScriptName = scriptName ?? "";
            return Task.CompletedTask;
        }

        public Task DeleteScriptAsync(
            string scriptName,
            CancellationToken cancellationToken)
        {
            if (deleteFailures.Contains(scriptName))
                throw new InvalidOperationException("Delete failed.");

            scripts.Remove(scriptName);
            deletedScripts.Add(scriptName);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestInteraction : ISynchronizationInteraction
    {
        public bool ResolveAdoption(bool? explicitChoice, int compatibleRuleCount) =>
            explicitChoice ?? false;
    }
}
