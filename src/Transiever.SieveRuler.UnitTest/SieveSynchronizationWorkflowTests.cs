using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Transiever.ManageSieve;
using Transiever.SieveRuler.Application;
using Transiever.SieveRuler.Models;
using Transiever.SieveRuler.Services;

namespace Transiever.SieveRuler.UnitTest;

public sealed class SieveSynchronizationWorkflowTests
{
    private static readonly byte[] NoActiveOriginalMarkerContent =
        "# SieveRuler original state marker: no active script\r\nkeep;\r\n"u8.ToArray();

    public enum DeploymentFailureStage
    {
        CheckScript,
        StaleState,
        BackupCollision
    }

    public enum RollbackIntegrityFailure
    {
        InvalidCandidate,
        InvalidSource,
        InvalidBackup
    }

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
                new FakeSieveServerConnectionFactory(FakeSieveServerConnection.Empty()),
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
                new FakeSieveServerConnectionFactory(
                    FakeSieveServerConnection.WithScripts(
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

            PreviewSynchronizationResult result = await CreateWorkflow(FakeSieveServerConnection.Empty())
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
    public async Task Preview_RecordsNoMutatingServerOperation()
    {
        FakeSieveServerConnection connection = FakeSieveServerConnection.Empty();

        PreviewSynchronizationResult result = await CreateWorkflow(connection)
            .PreviewAsync(
                new PreviewSynchronizationRequest(
                    TestConfiguration(),
                    "unused.json",
                    "reconciled.json",
                    "candidate.json",
                    "server.sieve",
                    "candidate.sieve",
                    "plan.json",
                    AdoptCompatible: false,
                    SourceDocument: new RuleDocument
                    {
                        SourceId = "outlook",
                        Rules = [CreateSenderRule("First", "first@example.com")]
                    },
                    WriteArtifacts: false),
                TestContext.Current.CancellationToken);

        Assert.Equal(PreviewSynchronizationStatus.Prepared, result.Status);
        Assert.DoesNotContain(connection.Operations, operation => operation.IsMutation);
    }

    [Fact]
    public async Task Deploy_ActiveReplacementMaintainsSafetyOrdering()
    {
        byte[] source = "keep;\r\n"u8.ToArray();
        byte[] candidate = "stop;\r\n"u8.ToArray();
        const string sourceName = "source";
        const string targetName = sourceName;
        const string backupName = "srtx-backup-test";
        FakeSieveServerConnection connection = FakeSieveServerConnection.WithScripts(
            sourceName,
            (sourceName, source),
            ("srtx-20240101000000-old", source));

        DeploySynchronizationResult result = await CreateWorkflow(connection)
            .DeployAsync(
                new DeploySynchronizationRequest(
                    TestConfiguration(),
                    "unused.json",
                    HistoryLimit: 0,
                    Plan: Plan(candidate, targetName, sourceName, source, backupName)),
                TestContext.Current.CancellationToken);

        Assert.Equal(DeploySynchronizationStatus.ReplacedActive, result.Status);
        AssertOccursBefore(connection.Operations, FakeSieveOperationKind.CheckScript, FakeSieveOperationKind.PutScript);

        int backupPut = connection.Operations.ToList().FindIndex(operation =>
            operation.Kind == FakeSieveOperationKind.PutScript && operation.ScriptName == backupName);
        int backupGet = connection.Operations.ToList().FindIndex(operation =>
            operation.Kind == FakeSieveOperationKind.GetScript && operation.ScriptName == backupName);
        int targetPut = connection.Operations.ToList().FindIndex(operation =>
            operation.Kind == FakeSieveOperationKind.PutScript && operation.ScriptName == targetName);
        int verificationRead = connection.Operations.ToList().FindIndex(targetPut + 1, operation =>
            operation.Kind == FakeSieveOperationKind.ReadState);

        Assert.True(targetPut > backupPut, "The target was written before the backup.");
        AssertOccursBefore(
            connection.Operations.Skip(backupPut).ToList(),
            FakeSieveOperationKind.PutScript,
            FakeSieveOperationKind.GetScript);
        AssertOccursBefore(
            connection.Operations.Skip(backupGet).ToList(),
            FakeSieveOperationKind.GetScript,
            FakeSieveOperationKind.PutScript);
        AssertOccursBefore(
            connection.Operations.Skip(targetPut).ToList(),
            FakeSieveOperationKind.PutScript,
            FakeSieveOperationKind.ReadState);
        AssertOccursBefore(
            connection.Operations.Skip(verificationRead).ToList(),
            FakeSieveOperationKind.ReadState,
            FakeSieveOperationKind.DeleteScript);
    }

    [Fact]
    public async Task Deploy_InactiveCandidateMaintainsSafetyOrdering()
    {
        byte[] source = "keep;\r\n"u8.ToArray();
        byte[] candidate = "stop;\r\n"u8.ToArray();
        const string sourceName = "source";
        const string targetName = "srtx-candidate";
        FakeSieveServerConnection connection = FakeSieveServerConnection.WithScripts(
            sourceName,
            (sourceName, source),
            ("srtx-20240101000000-old", source));

        DeploySynchronizationResult result = await CreateWorkflow(connection)
            .DeployAsync(
                new DeploySynchronizationRequest(
                    TestConfiguration(),
                    "unused.json",
                    HistoryLimit: 0,
                    Plan: Plan(candidate, targetName, sourceName, source)),
                TestContext.Current.CancellationToken);

        Assert.Equal(DeploySynchronizationStatus.Activated, result.Status);
        int targetPut = connection.Operations.ToList().FindIndex(operation =>
            operation.Kind == FakeSieveOperationKind.PutScript && operation.ScriptName == targetName);
        int activation = connection.Operations.ToList().FindIndex(targetPut + 1, operation =>
            operation.Kind == FakeSieveOperationKind.Activate);
        int verificationRead = connection.Operations.ToList().FindIndex(activation + 1, operation =>
            operation.Kind == FakeSieveOperationKind.ReadState);

        AssertOccursBefore(connection.Operations, FakeSieveOperationKind.CheckScript, FakeSieveOperationKind.PutScript);
        AssertOccursBefore(
            connection.Operations.Skip(targetPut).ToList(),
            FakeSieveOperationKind.PutScript,
            FakeSieveOperationKind.Activate);
        AssertOccursBefore(
            connection.Operations.Skip(activation).ToList(),
            FakeSieveOperationKind.Activate,
            FakeSieveOperationKind.ReadState);
        AssertOccursBefore(
            connection.Operations.Skip(verificationRead).ToList(),
            FakeSieveOperationKind.ReadState,
            FakeSieveOperationKind.DeleteScript);
    }

    [Theory]
    [InlineData(DeploymentFailureStage.CheckScript)]
    [InlineData(DeploymentFailureStage.StaleState)]
    [InlineData(DeploymentFailureStage.BackupCollision)]
    public async Task Deploy_PreMutationFailureDoesNotWriteCandidate(
        DeploymentFailureStage stage)
    {
        byte[] source = "keep;\r\n"u8.ToArray();
        byte[] candidate = "stop;\r\n"u8.ToArray();
        const string sourceName = "source";
        const string targetName = sourceName;
        const string backupName = "srtx-backup-test";
        FakeSieveServerConnection connection = stage switch
        {
            DeploymentFailureStage.CheckScript =>
                FakeSieveServerConnection.WithScripts(sourceName, (sourceName, source))
                    .WithFailure(
                        FakeSieveOperationKind.CheckScript,
                        1,
                        new InvalidOperationException("check rejected")),
            DeploymentFailureStage.StaleState =>
                FakeSieveServerConnection.WithScripts(
                    sourceName,
                    (sourceName, source),
                    ("changed", "discard;\r\n"u8.ToArray()))
                    .WithActiveScriptBeforeRead(1, "changed"),
            DeploymentFailureStage.BackupCollision =>
                FakeSieveServerConnection.WithScripts(
                    sourceName,
                    (sourceName, source),
                    (backupName, source)),
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
        };

        string expectedMessage = stage switch
        {
            DeploymentFailureStage.CheckScript =>
                "check rejected",
            DeploymentFailureStage.StaleState =>
                "active server script changed",
            DeploymentFailureStage.BackupCollision =>
                "already exists",
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
        };

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateWorkflow(connection).DeployAsync(
                new DeploySynchronizationRequest(
                    TestConfiguration(),
                    "unused.json",
                    Plan: Plan(candidate, targetName, sourceName, source, backupName)),
                TestContext.Current.CancellationToken));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(connection.Operations, operation => operation.Kind == FakeSieveOperationKind.CheckScript);
        if (stage is DeploymentFailureStage.StaleState or DeploymentFailureStage.BackupCollision)
        {
            AssertOccursBefore(connection.Operations, FakeSieveOperationKind.CheckScript, FakeSieveOperationKind.ReadState);
        }
        Assert.DoesNotContain(
            connection.Operations,
            operation => operation.Kind == FakeSieveOperationKind.PutScript &&
                operation.ScriptName == targetName);
        Assert.DoesNotContain(
            connection.Operations,
            operation => operation.Kind == FakeSieveOperationKind.Activate);
        Assert.Equal(
            stage == DeploymentFailureStage.StaleState ? "changed" : sourceName,
            connection.ActiveScriptName);
        Assert.Equal(source, connection.GetContent(sourceName));
    }

    [Fact]
    public async Task Deploy_HaveSpaceSecondCheckFailurePrunesProtectedHistoryAndDoesNotWriteCandidate()
    {
        byte[] source = "keep;\r\n"u8.ToArray();
        byte[] candidate = "stop;\r\n"u8.ToArray();
        const string sourceName = "srtx-20240601000000-active";
        const string targetName = sourceName;
        const string backupName = "srtx-backup-20240601000000-current";
        FakeSieveServerConnection connection = FakeSieveServerConnection.WithScripts(
                sourceName,
                (sourceName, source),
                ("srtx-backup-20230101000000-original", source),
                ("srtx-user-script", source),
                ("srtx-20240101000000-old", source))
            .WithHaveSpaceResponses(true, false, true, false);

        DeploySynchronizationResult result = await CreateWorkflow(connection)
            .DeployAsync(
                new DeploySynchronizationRequest(
                    TestConfiguration(),
                    "unused.json",
                    HistoryLimit: 0,
                    Plan: Plan(candidate, targetName, sourceName, source, backupName)),
                TestContext.Current.CancellationToken);

        Assert.Equal(DeploySynchronizationStatus.InsufficientSpace, result.Status);
        Assert.Equal(4, connection.HaveSpaceCalls);
        Assert.Equal(["srtx-20240101000000-old"], result.DeletedHistoryScriptNames);
        Assert.True(connection.ContainsScript(sourceName));
        Assert.True(connection.ContainsScript("srtx-backup-20230101000000-original"));
        Assert.True(connection.ContainsScript("srtx-user-script"));
        Assert.False(connection.ContainsScript("srtx-20240101000000-old"));
        Assert.Equal(
            2,
            connection.Operations.Count(
                operation => operation.Kind == FakeSieveOperationKind.ReadState));
        Assert.Equal(
            2,
            connection.Operations.Count(
                operation => operation.Kind == FakeSieveOperationKind.HaveSpace &&
                    operation.ScriptName == targetName));
        Assert.Equal(
            2,
            connection.Operations.Count(
                operation => operation.Kind == FakeSieveOperationKind.HaveSpace &&
                    operation.ScriptName == backupName));
        Assert.DoesNotContain(
            connection.Operations,
            operation => operation.Kind == FakeSieveOperationKind.PutScript &&
                operation.ScriptName == targetName);
        Assert.DoesNotContain(
            connection.Operations,
            operation => operation.Kind == FakeSieveOperationKind.Activate);
        Assert.Equal(sourceName, connection.ActiveScriptName);
        Assert.Equal(source, connection.GetContent(sourceName));
    }

    [Fact]
    public async Task Deploy_InactiveTargetVerificationFailurePreservesCandidateAndActiveState()
    {
        byte[] source = "keep;\r\n"u8.ToArray();
        byte[] candidate = "stop;\r\n"u8.ToArray();
        const string sourceName = "source";
        const string targetName = "srtx-candidate";
        FakeSieveServerConnection connection = FakeSieveServerConnection.WithScripts(
                sourceName,
                (sourceName, source))
            .WithFailure(
                FakeSieveOperationKind.ReadState,
                2,
                new InvalidOperationException("post-upload verification failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateWorkflow(connection).DeployAsync(
                new DeploySynchronizationRequest(
                    TestConfiguration(),
                    "unused.json",
                    Plan: Plan(candidate, targetName, sourceName, source)),
                TestContext.Current.CancellationToken));

        Assert.Equal(sourceName, connection.ActiveScriptName);
        Assert.Equal(source, connection.GetContent(sourceName));
        Assert.Equal(candidate, connection.GetContent(targetName));
        Assert.DoesNotContain(
            connection.Operations,
            operation => operation.Kind == FakeSieveOperationKind.Activate);
    }

    [Fact]
    public async Task Deploy_NoActiveMarkerVerificationFailurePreservesMarkerAndActiveState()
    {
        byte[] candidate = "stop;\r\n"u8.ToArray();
        const string targetName = "srtx-candidate";
        FakeSieveServerConnection connection = FakeSieveServerConnection.Empty()
            .WithFailure(
                FakeSieveOperationKind.ReadState,
                2,
                new InvalidOperationException("post-upload verification failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateWorkflow(connection).DeployAsync(
                new DeploySynchronizationRequest(
                    TestConfiguration(),
                    "unused.json",
                    Plan: Plan(candidate, targetName)),
                TestContext.Current.CancellationToken));

        FakeSieveOperation markerPut = Assert.Single(
            connection.Operations,
            operation => operation.Kind == FakeSieveOperationKind.PutScript &&
                operation.ScriptName != targetName);
        Assert.Equal(NoActiveOriginalMarkerContent, markerPut.Content);
        Assert.Equal(NoActiveOriginalMarkerContent, connection.GetContent(markerPut.ScriptName!));
        Assert.Equal(candidate, connection.GetContent(targetName));
        Assert.Equal("", connection.ActiveScriptName);
        Assert.DoesNotContain(
            connection.Operations,
            operation => operation.Kind == FakeSieveOperationKind.Activate);
    }

    [Fact]
    public async Task Deploy_BackupVerificationFailureDoesNotWriteTarget()
    {
        byte[] source = "keep;\r\n"u8.ToArray();
        byte[] candidate = "stop;\r\n"u8.ToArray();
        byte[] corrupted = "corrupted;\r\n"u8.ToArray();
        const string sourceName = "source";
        const string targetName = sourceName;
        const string backupName = "srtx-backup-test";
        FakeSieveServerConnection connection = FakeSieveServerConnection.WithScripts(
                sourceName,
                (sourceName, source))
            .WithStoredContentOnPut(backupName, 1, corrupted);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => CreateWorkflow(connection).DeployAsync(
                new DeploySynchronizationRequest(
                    TestConfiguration(),
                    "unused.json",
                    Plan: Plan(candidate, targetName, sourceName, source, backupName)),
                TestContext.Current.CancellationToken));

        Assert.Equal(sourceName, connection.ActiveScriptName);
        Assert.Equal(source, connection.GetContent(sourceName));
        Assert.Equal(corrupted, connection.GetContent(backupName));
        Assert.DoesNotContain(
            connection.Operations,
            operation => operation.Kind == FakeSieveOperationKind.PutScript &&
                operation.ScriptName == targetName);
        Assert.DoesNotContain(
            connection.Operations,
            operation => operation.Kind == FakeSieveOperationKind.Activate);
    }

    [Fact]
    public async Task Deploy_PostTargetVerificationFailurePreservesBackupAndFails()
    {
        byte[] source = "keep;\r\n"u8.ToArray();
        byte[] candidate = "stop;\r\n"u8.ToArray();
        byte[] transformed = "discard;\r\n"u8.ToArray();
        const string sourceName = "source";
        const string targetName = sourceName;
        const string backupName = "srtx-backup-test";
        FakeSieveServerConnection connection = FakeSieveServerConnection.WithScripts(
                sourceName,
                (sourceName, source))
            .WithStoredContentOnPut(targetName, 1, transformed);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateWorkflow(connection).DeployAsync(
                new DeploySynchronizationRequest(
                    TestConfiguration(),
                    "unused.json",
                    Plan: Plan(candidate, targetName, sourceName, source, backupName)),
                TestContext.Current.CancellationToken));

        Assert.Equal(sourceName, connection.ActiveScriptName);
        Assert.Equal(source, connection.GetContent(backupName));
        Assert.Equal(transformed, connection.GetContent(targetName));
        Assert.Contains(
            connection.Operations,
            operation => operation.Kind == FakeSieveOperationKind.PutScript &&
                operation.ScriptName == targetName);
        Assert.DoesNotContain(
            connection.Operations,
            operation => operation.Kind == FakeSieveOperationKind.DeleteScript);
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
                    FakeSieveServerConnection.WithScripts(
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
            var connection = FakeSieveServerConnection.WithScripts(
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
            var connection = FakeSieveServerConnection.WithScripts(
                "srtx-candidate",
                ("source", source),
                ("srtx-candidate", changed));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateWorkflow(connection).RollbackAsync(
                    new RollbackSynchronizationRequest(TestConfiguration(), planFile),
                    cancellationToken));

            Assert.DoesNotContain(
                connection.Operations,
                operation => operation.Kind is FakeSieveOperationKind.PutScript or
                    FakeSieveOperationKind.Activate);
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
            var connection = FakeSieveServerConnection.WithScripts(
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
    public async Task Rollback_VersionOnePlanRestoresExactServerSideBackup()
    {
        string directory = CreateDirectory();

        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            byte[] backup = "require [\"fileinto\"];\r\nkeep;\r\n# preserve CRLF\r\n"u8.ToArray();
            byte[] candidate = "require [\"fileinto\"];\r\nstop;\r\n# candidate\r\n"u8.ToArray();
            string planFile = await WritePlanAsync(
                directory,
                new DeploymentPlan
                {
                    SchemaVersion = 1,
                    SourceActiveScriptName = "Open-Xchange",
                    SourceContentSha256 = Hash(backup),
                    CandidateContentBase64 = Convert.ToBase64String(candidate),
                    CandidateContentSha256 = Hash(candidate),
                    TargetScriptName = "Open-Xchange",
                    BackupScriptName = "srtx-backup-test",
                    BackupContentSha256 = Hash(backup)
                },
                cancellationToken);
            var connection = FakeSieveServerConnection.WithScripts(
                "Open-Xchange",
                ("Open-Xchange", candidate),
                ("srtx-backup-test", backup));

            RollbackSynchronizationResult result = await CreateWorkflow(connection)
                .RollbackAsync(
                    new RollbackSynchronizationRequest(TestConfiguration(), planFile),
                    cancellationToken);

            Assert.Equal(RollbackSynchronizationStatus.RestoredBackup, result.Status);
            Assert.Equal(backup, connection.GetContent("Open-Xchange"));
            Assert.Equal("Open-Xchange", connection.ActiveScriptName);
            Assert.Equal(backup, connection.GetContent("srtx-backup-test"));
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
            var connection = FakeSieveServerConnection.WithScripts(
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

            Assert.DoesNotContain(
                connection.Operations,
                operation => operation.Kind is FakeSieveOperationKind.PutScript or
                    FakeSieveOperationKind.Activate);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(RollbackIntegrityFailure.InvalidCandidate)]
    [InlineData(RollbackIntegrityFailure.InvalidSource)]
    [InlineData(RollbackIntegrityFailure.InvalidBackup)]
    public async Task Rollback_ForceNeverBypassesIntegrityFailure(
        RollbackIntegrityFailure failure)
    {
        string directory = CreateDirectory();

        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            byte[] source = "source\r\nkeep;\r\n"u8.ToArray();
            byte[] candidate = "candidate\r\nstop;\r\n"u8.ToArray();
            byte[] changed = "changed\r\ndiscard;\r\n"u8.ToArray();
            string wrongHash = Hash(changed);
            DeploymentPlan plan = failure switch
            {
                RollbackIntegrityFailure.InvalidCandidate => Plan(candidate, "srtx-candidate") with
                {
                    CandidateContentSha256 = wrongHash
                },
                RollbackIntegrityFailure.InvalidSource => Plan(
                    candidate,
                    "srtx-candidate",
                    "source",
                    source) with
                {
                    SourceContentSha256 = wrongHash
                },
                RollbackIntegrityFailure.InvalidBackup => Plan(
                    candidate,
                    "Open-Xchange",
                    "Open-Xchange",
                    source,
                    "srtx-backup-test") with
                {
                    BackupContentSha256 = wrongHash
                },
                _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null)
            };
            string planFile = await WritePlanAsync(directory, plan, cancellationToken);
            FakeSieveServerConnection connection = failure switch
            {
                RollbackIntegrityFailure.InvalidBackup => FakeSieveServerConnection.WithScripts(
                    "Open-Xchange",
                    ("Open-Xchange", candidate),
                    ("srtx-backup-test", source)),
                _ => FakeSieveServerConnection.WithScripts(
                    "srtx-candidate",
                    ("source", source),
                    ("srtx-candidate", candidate))
            };
            byte[]? sourceBefore = failure == RollbackIntegrityFailure.InvalidBackup
                ? null
                : connection.GetContent("source");
            byte[]? candidateBefore = failure == RollbackIntegrityFailure.InvalidBackup
                ? null
                : connection.GetContent("srtx-candidate");
            byte[] activeBefore = failure == RollbackIntegrityFailure.InvalidBackup
                ? connection.GetContent("Open-Xchange")
                : candidateBefore!;
            byte[] backupBefore = failure == RollbackIntegrityFailure.InvalidBackup
                ? connection.GetContent("srtx-backup-test")
                : sourceBefore!;

            await Assert.ThrowsAsync<InvalidDataException>(
                () => CreateWorkflow(connection).RollbackAsync(
                    new RollbackSynchronizationRequest(
                        TestConfiguration(),
                        planFile,
                        Force: true),
                    cancellationToken));

            Assert.DoesNotContain(
                connection.Operations,
                operation => operation.Kind is FakeSieveOperationKind.PutScript or
                    FakeSieveOperationKind.Activate);
            if (failure == RollbackIntegrityFailure.InvalidBackup)
            {
                Assert.Equal(activeBefore, connection.GetContent("Open-Xchange"));
                Assert.Equal(backupBefore, connection.GetContent("srtx-backup-test"));
            }
            else
            {
                Assert.Equal(sourceBefore, connection.GetContent("source"));
                Assert.Equal(candidateBefore, connection.GetContent("srtx-candidate"));
            }
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
            const string sourceName = "srtx-20230201000000-source";
            string targetName = "srtx-20260626000009-target";
            string planFile = await WritePlanAsync(
                directory,
                new DeploymentPlan
                {
                    SourceActiveScriptName = sourceName,
                    SourceContentSha256 = Hash(source),
                    CandidateContentBase64 = Convert.ToBase64String(candidate),
                    CandidateContentSha256 = Hash(candidate),
                    TargetScriptName = targetName
                },
                cancellationToken);
            var connection = FakeSieveServerConnection.WithScripts(
                sourceName,
                (sourceName, source),
                ("user-script", source),
                ("srtx-user-script", source),
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
            Assert.True(connection.ContainsScript(sourceName));
            Assert.True(connection.ContainsScript("user-script"));
            Assert.True(connection.ContainsScript("srtx-user-script"));
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
    public async Task Deploy_RetainsCurrentPlanBackupBeyondHistoryLimit()
    {
        byte[] source = "keep;\r\n"u8.ToArray();
        byte[] candidate = "stop;\r\n"u8.ToArray();
        const string targetName = "srtx-20240601000000-active";
        const string backupName = "srtx-backup-20260601000000-current";
        FakeSieveServerConnection connection = FakeSieveServerConnection.WithScripts(
            targetName,
            (targetName, source),
            ("srtx-backup-20230101000000-original", source),
            ("srtx-20240101000000-old", source));

        DeploySynchronizationResult result = await CreateWorkflow(connection)
            .DeployAsync(
                new DeploySynchronizationRequest(
                    TestConfiguration(),
                    "unused.json",
                    HistoryLimit: 0,
                    Plan: Plan(candidate, targetName, targetName, source, backupName)),
                TestContext.Current.CancellationToken);

        Assert.Equal(DeploySynchronizationStatus.ReplacedActive, result.Status);
        Assert.Equal(["srtx-20240101000000-old"], result.DeletedHistoryScriptNames);
        Assert.Equal(targetName, connection.ActiveScriptName);
        Assert.Equal(candidate, connection.GetContent(targetName));
        Assert.Equal(source, connection.GetContent(backupName));
        Assert.True(connection.ContainsScript("srtx-backup-20230101000000-original"));
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
            var connection = FakeSieveServerConnection.WithScripts(
                "source",
                ("source", source),
                ("srtx-backup-20230101000000-original", source),
                ("srtx-user-script", source),
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
            Assert.True(connection.ContainsScript("source"));
            Assert.True(connection.ContainsScript("srtx-backup-20230101000000-original"));
            Assert.True(connection.ContainsScript("srtx-user-script"));
            int firstSpace = connection.Operations.ToList().FindIndex(
                operation => operation.Kind == FakeSieveOperationKind.HaveSpace);
            int secondSpace = connection.Operations.ToList().FindIndex(
                firstSpace + 1,
                operation => operation.Kind == FakeSieveOperationKind.HaveSpace);
            Assert.Contains(
                connection.Operations.Skip(firstSpace + 1).Take(secondSpace - firstSpace - 1),
                operation => operation.Kind == FakeSieveOperationKind.ReadState);
            Assert.DoesNotContain(
                connection.Operations.Take(secondSpace),
                operation => operation.Kind == FakeSieveOperationKind.PutScript &&
                    operation.ScriptName == "srtx-new");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Deploy_CleanupFailureReturnsSuccessWithStableWarningContext()
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
            var connection = FakeSieveServerConnection.WithScripts(
                "source",
                ("source", source),
                ("srtx-20240101000000-old", source))
                .WithFailure(
                    FakeSieveOperationKind.DeleteScript,
                    1,
                    new InvalidOperationException("synthetic cleanup failure"));

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
    public async Task HistoryList_OriginalSelectionIsDeterministicByOrdinalNameWithoutProvingProvenance()
    {
        byte[] current = "keep;\r\n"u8.ToArray();
        FakeSieveServerConnection connection = FakeSieveServerConnection.WithScripts(
            "Open-Xchange",
            ("Open-Xchange", current),
            ("srtx-backup-20240101000000-a", current),
            ("srtx-backup-20240101000000-b", current),
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
                "srtx-backup-20240101000000-b",
                "srtx-backup-20240101000000-a"
            ],
            result.Entries.Select(entry => entry.Name).ToArray());
        Assert.Equal(SieveHistoryEntryKind.Candidate, result.Entries[0].Kind);
        Assert.True(result.Entries.Single(
            entry => entry.Name == "srtx-backup-20240101000000-a").IsOriginal);
        Assert.False(result.Entries.Single(
            entry => entry.Name == "srtx-backup-20240101000000-b").IsOriginal);
    }

    [Fact]
    public async Task HistoryShow_ReturnsContentHashAndLength()
    {
        byte[] backup = "require [\"fileinto\"];\r\nkeep;\r\n"u8.ToArray();
        FakeSieveServerConnection connection = FakeSieveServerConnection.WithScripts(
            "",
            ("srtx-backup-20240101000000-original", backup));

        HistoryShowResult result = await CreateWorkflow(connection)
            .ShowHistoryAsync(
                new HistoryShowRequest(
                    TestConfiguration(),
                    "srtx-backup-20240101000000-original"),
                TestContext.Current.CancellationToken);

        Assert.Equal(backup, result.Content);
        Assert.Equal(Hash(result.Content), result.Entry.ContentSha256);
        Assert.Equal(backup.Length, result.Entry.ContentLength);
        Assert.True(result.Entry.IsOriginal);
    }

    [Fact]
    public async Task HistoryRestore_RestoresBackupIntoCurrentActiveAndKeepsFreshBackup()
    {
        byte[] current = "keep;\r\n"u8.ToArray();
        byte[] backup = "discard;\r\n"u8.ToArray();
        FakeSieveServerConnection connection = FakeSieveServerConnection.WithScripts(
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
        Assert.Equal("Open-Xchange", connection.ActiveScriptName);
        Assert.Equal(backup, connection.GetContent("Open-Xchange"));
        Assert.Equal(backup, connection.GetContent("srtx-backup-20240101000000-original"));
        Assert.Equal(current, connection.GetContent(result.BackupScriptName!));
    }

    [Fact]
    public async Task HistoryRestore_BackupRefusesChangedActiveStateBeforeMutation()
    {
        byte[] current = "keep;\r\n"u8.ToArray();
        byte[] concurrent = "discard;\r\n"u8.ToArray();
        var connection = FakeSieveServerConnection.WithScripts(
                "current",
                ("current", current),
                ("concurrent", concurrent),
                ("srtx-backup-20240101000000-original", "stop;\r\n"u8.ToArray()))
            .WithActiveScriptBeforeRead(2, "concurrent");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateWorkflow(connection).RestoreHistoryAsync(
                new HistoryRestoreRequest(
                    TestConfiguration(),
                    "srtx-backup-20240101000000-original"),
                TestContext.Current.CancellationToken));

        Assert.DoesNotContain(connection.Operations, operation => operation.IsMutation);
    }

    [Fact]
    public async Task HistoryRestore_LatestSelectionIsDeterministicByOrdinalNameWithoutProvingProvenance()
    {
        byte[] current = "keep;\r\n"u8.ToArray();
        byte[] ordinalFirst = "discard;\r\n"u8.ToArray();
        byte[] ordinalLast = "require [\"fileinto\"];\r\nfileinto \"INBOX/Previous\";\r\n"u8.ToArray();
        FakeSieveServerConnection connection = FakeSieveServerConnection.WithScripts(
            "Open-Xchange",
            ("Open-Xchange", current),
            ("srtx-backup-20240101000000-a", ordinalFirst),
            ("srtx-20240201000000-candidate", "stop;\r\n"u8.ToArray()),
            ("srtx-backup-20240101000000-z", ordinalLast));

        HistoryRestoreResult result = await CreateWorkflow(connection)
            .RestoreHistoryAsync(
                new HistoryRestoreRequest(TestConfiguration(), "latest"),
                TestContext.Current.CancellationToken);

        Assert.Equal(HistoryRestoreStatus.RestoredScript, result.Status);
        Assert.Equal("srtx-backup-20240101000000-z", result.SourceScriptName);
        Assert.Equal(ordinalLast, connection.GetContent("Open-Xchange"));
        Assert.Equal(current, connection.GetContent(result.BackupScriptName!));
    }

    [Fact]
    public async Task HistoryRestore_OriginalNoActiveMarkerDisablesActiveAndBacksUpCurrent()
    {
        byte[] current = "keep;\r\n"u8.ToArray();
        FakeSieveServerConnection connection = FakeSieveServerConnection.WithScripts(
            "Open-Xchange",
            ("Open-Xchange", current),
            ("srtx-backup-20240101000000-no-active", NoActiveOriginalMarkerContent));

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
    public async Task HistoryRestore_NoActiveMarkerRefusesChangedActiveStateBeforeMutation()
    {
        byte[] marker =
            "# SieveRuler original state marker: no active script\r\nkeep;\r\n"u8.ToArray();
        var connection = FakeSieveServerConnection.WithScripts(
                "current",
                ("current", "keep;\r\n"u8.ToArray()),
                ("concurrent", "discard;\r\n"u8.ToArray()),
                ("srtx-backup-20240101000000-no-active", marker))
            .WithActiveScriptBeforeRead(2, "concurrent");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateWorkflow(connection).RestoreHistoryAsync(
                new HistoryRestoreRequest(TestConfiguration(), "original"),
                TestContext.Current.CancellationToken));

        Assert.DoesNotContain(connection.Operations, operation => operation.IsMutation);
    }

    [Theory]
    [InlineData("keep;\r\n")]
    [InlineData("# SieveRuler original state marker: no active script\nkeep;\n")]
    public async Task HistoryRestore_OriginalRejectsNonCanonicalMarkerWithoutMutation(
        string markerText)
    {
        byte[] active = "discard;\r\n"u8.ToArray();
        var connection = FakeSieveServerConnection.WithScripts(
            "Open-Xchange",
            ("Open-Xchange", active),
            ("srtx-backup-20240101000000-no-active",
                Encoding.UTF8.GetBytes(markerText)));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => CreateWorkflow(connection).RestoreHistoryAsync(
                new HistoryRestoreRequest(TestConfiguration(), "original"),
                TestContext.Current.CancellationToken));

        Assert.Equal("Open-Xchange", connection.ActiveScriptName);
        Assert.Equal(active, connection.GetContent("Open-Xchange"));
        Assert.DoesNotContain(connection.Operations, operation => operation.IsMutation);
    }

    [Fact]
    public async Task HistoryDelete_DeletesInactiveHistoryScript()
    {
        byte[] current = "keep;\r\n"u8.ToArray();
        FakeSieveServerConnection connection = FakeSieveServerConnection.WithScripts(
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
        FakeSieveServerConnection connection = FakeSieveServerConnection.WithScripts(
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
        Assert.DoesNotContain(connection.Operations, operation => operation.IsMutation);
    }

    [Fact]
    public async Task HistoryDelete_DoesNotResolveLatestShortcut()
    {
        byte[] current = "keep;\r\n"u8.ToArray();
        FakeSieveServerConnection connection = FakeSieveServerConnection.WithScripts(
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
        Assert.DoesNotContain(connection.Operations, operation => operation.IsMutation);
    }

    [Fact]
    public async Task HistoryDelete_DryRunDoesNotMutate()
    {
        byte[] current = "keep;\r\n"u8.ToArray();
        FakeSieveServerConnection connection = FakeSieveServerConnection.WithScripts(
            "Open-Xchange",
            ("Open-Xchange", current),
            ("srtx-20240101000000-candidate", current));

        HistoryDeleteResult result = await CreateWorkflow(connection)
            .DeleteHistoryAsync(
                new HistoryDeleteRequest(
                    TestConfiguration(),
                    "srtx-20240101000000-candidate",
                    DryRun: true),
                TestContext.Current.CancellationToken);

        Assert.Equal(HistoryDeleteStatus.PlanValidated, result.Status);
        Assert.True(connection.ContainsScript("srtx-20240101000000-candidate"));
        Assert.DoesNotContain(connection.Operations, operation => operation.IsMutation);
    }

    [Theory]
    [InlineData("srtx-user-script")]
    [InlineData("srtx-2024010100000-short")]
    [InlineData("srtx-20240101000000")]
    [InlineData("srtx-backup-20240101000000-")]
    [InlineData("SRTX-20240101000000-candidate")]
    public async Task HistoryPrune_PreservesNamesOutsideReservedGrammar(string name)
    {
        byte[] current = "keep;\r\n"u8.ToArray();
        FakeSieveServerConnection connection = FakeSieveServerConnection.WithScripts(
            "Open-Xchange",
            ("Open-Xchange", current),
            (name, current),
            ("srtx-20240101000000-candidate", current));

        HistoryPruneResult result = await CreateWorkflow(connection)
            .PruneHistoryAsync(
                new HistoryPruneRequest(TestConfiguration()),
                TestContext.Current.CancellationToken);

        Assert.Equal(["srtx-20240101000000-candidate"], result.DeletedScriptNames);
        Assert.True(connection.ContainsScript(name));
        Assert.DoesNotContain(
            connection.Operations,
            operation => operation.Kind == FakeSieveOperationKind.DeleteScript &&
                operation.ScriptName == name);
    }

    [Fact]
    public async Task HistoryPrune_DeletesAllInactiveSieveRulerHistoryExceptActive()
    {
        byte[] current = "keep;\r\n"u8.ToArray();
        FakeSieveServerConnection connection = FakeSieveServerConnection.WithScripts(
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
        FakeSieveServerConnection connection = FakeSieveServerConnection.WithScripts(
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
        Assert.DoesNotContain(connection.Operations, operation => operation.IsMutation);
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
            FakeSieveServerConnection connection = FakeSieveServerConnection.Empty();

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

    [Fact]
    public async Task Deploy_DryRunRecordsNoMutatingServerOperation()
    {
        byte[] candidate = "keep;\r\n"u8.ToArray();
        var connection = FakeSieveServerConnection.Empty();

        DeploySynchronizationResult result = await CreateWorkflow(connection)
            .DeployAsync(
                new DeploySynchronizationRequest(
                    Configuration: null,
                    PlanFile: "unused.json",
                    DryRun: true,
                    Plan: Plan(candidate, targetName: "srtx-candidate")),
                TestContext.Current.CancellationToken);

        Assert.Equal(DeploySynchronizationStatus.PlanValidated, result.Status);
        Assert.DoesNotContain(connection.Operations, operation => operation.IsMutation);
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
        FakeSieveServerConnection connection)
    {
        var serializer = new JsonRuleSerializer();
        var importer = new SieveImporter();
        var optimizer = new RuleOptimizer();
        return new SieveSynchronizationWorkflow(
            serializer,
            importer,
            new RuleReconciler(optimizer),
            new SieveScriptComposer(importer, new SieveGenerator()),
            new FakeSieveServerConnectionFactory(connection),
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

    private static void AssertOccursBefore(
        IReadOnlyList<FakeSieveOperation> operations,
        FakeSieveOperationKind first,
        FakeSieveOperationKind second)
    {
        int firstIndex = operations.ToList().FindIndex(operation => operation.Kind == first);
        int secondIndex = operations.ToList().FindIndex(operation => operation.Kind == second);
        Assert.True(firstIndex >= 0, "The first operation was not recorded.");
        Assert.True(secondIndex > firstIndex, "The operations were out of safety order.");
    }

    private static DeploymentPlan Plan(
        byte[] candidate,
        string targetName,
        string sourceName = "",
        byte[]? source = null,
        string? backupName = null)
    {
        source ??= [];
        return new DeploymentPlan
        {
            SchemaVersion = DeploymentPlan.CurrentSchemaVersion,
            SourceActiveScriptName = sourceName,
            SourceContentSha256 = Hash(source),
            CandidateContentBase64 = Convert.ToBase64String(candidate),
            CandidateContentSha256 = Hash(candidate),
            TargetScriptName = targetName,
            BackupScriptName = backupName,
            BackupContentSha256 = backupName is null ? null : Hash(source)
        };
    }

    private sealed class TestInteraction : ISynchronizationInteraction
    {
        public bool ResolveAdoption(bool? explicitChoice, int compatibleRuleCount) =>
            explicitChoice ?? false;
    }
}
