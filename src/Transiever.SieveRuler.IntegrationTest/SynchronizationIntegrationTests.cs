using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using System.Diagnostics;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Transiever.ManageSieve;
using Transiever.SieveRuler.Application;
using Transiever.SieveRuler.Models;
using Transiever.SieveRuler.Services;

namespace Transiever.SieveRuler.IntegrationTest;

public sealed class SynchronizationIntegrationTests
{
    [DockerFact]
    public async Task PreviewDeployAndRollback_PreservesActiveScriptName()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string dockerDirectory = Path.Combine(AppContext.BaseDirectory, "docker");
        IFutureDockerImage image = new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(dockerDirectory)
            .WithDockerfile("Dockerfile")
            .WithName("srtx-integration-dovecot:2.3.21.1")
            .WithCleanUp(true)
            .Build();
        await image.CreateAsync(cancellationToken);
        await using (image)
        {
            IContainer container = new ContainerBuilder(image)
                .WithPortBinding(4190, true)
                .WithWaitStrategy(
                    Wait.ForUnixContainer().UntilExternalTcpPortIsAvailable(4190))
                .WithCleanUp(true)
                .Build();
            await container.StartAsync(cancellationToken);
            await using (container)
            {
                byte[] certificateHash =
                    await ReadCertificateHashAsync(container, cancellationToken);
                var configuration = new SieveServerConfiguration(
                    "localhost",
                    container.GetMappedPublicPort(4190),
                    "srtx",
                    "srtx-password",
                    SieveConnectionSecurity.StartTlsRequired);
                var connectionFactory = new ManageSieveServerConnectionFactory(
                    new TestClientFactory(certificateHash));

                byte[] originalContent = await SeedActiveScriptAsync(
                    connectionFactory,
                    configuration,
                    cancellationToken);
                await SeedHistoryScriptsAsync(
                    connectionFactory,
                    configuration,
                    cancellationToken);

                string directory = Path.Combine(
                    Path.GetTempPath(),
                    $"Transiever.SieveRuler-{Guid.NewGuid():N}");
                Directory.CreateDirectory(directory);
                try
                {
                    string rulesFile = Path.Combine(directory, "rules.json");
                    var serializer = new JsonRuleSerializer();
                    await serializer.SaveDocumentAsync(
                        new RuleDocument
                        {
                            SourceId = "integration",
                            Rules =
                            [
                                new RuleDefinition
                                {
                                    Name = "Invoices",
                                    TargetFolder = "INBOX/Billing",
                                    Conditions =
                                    [
                                        new RuleCondition
                                        {
                                            Type = RuleConditionType.SubjectContains,
                                            Values = ["invoice"]
                                        }
                                    ]
                                }
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
                        connectionFactory,
                        new TestInteraction());
                    var preview = new PreviewSynchronizationRequest(
                        configuration,
                        rulesFile,
                        Path.Combine(directory, "reconciled-rules.json"),
                        Path.Combine(directory, "candidate-rules.json"),
                        Path.Combine(directory, "server.sieve"),
                        Path.Combine(directory, "candidate.sieve"),
                        Path.Combine(directory, "plan.json"),
                        AdoptCompatible: false);

                    PreviewSynchronizationResult previewResult =
                        await workflow.PreviewAsync(preview, cancellationToken);
                    Assert.Equal(
                        PreviewSynchronizationStatus.Prepared,
                        previewResult.Status);
                    Assert.Equal("original", previewResult.TargetScriptName);
                    Assert.True(previewResult.ReplacesActiveScript);

                    DeploySynchronizationResult deployResult =
                        await workflow.DeployAsync(
                            new DeploySynchronizationRequest(
                                configuration,
                                preview.PlanFile),
                            cancellationToken);
                    Assert.Equal(
                        DeploySynchronizationStatus.ReplacedActive,
                        deployResult.Status);

                    await using ISieveServerConnection connection =
                        await connectionFactory.ConnectAsync(
                            configuration,
                            cancellationToken);
                    RemoteSieveState final =
                        await connection.ReadStateAsync(cancellationToken);
                    Assert.Equal("original", final.ActiveScriptName);
                    Assert.Contains(final.Scripts, script => script.Name == "original");
                    Assert.Contains(
                        final.Scripts,
                        script => script.Name == deployResult.BackupScriptName);
                    Assert.Contains(
                        final.Scripts,
                        script => script.Name == "srtx-backup-20240101000000-original-copy");
                    Assert.Contains(
                        final.Scripts,
                        script => script.Name == "srtx-20240901000000-keep");
                    Assert.DoesNotContain(
                        final.Scripts,
                        script => script.Name == "srtx-backup-20240201000000-old");
                    Assert.DoesNotContain(
                        final.Scripts,
                        script => script.Name == "srtx-20240301000000-old");
                    Assert.DoesNotContain(
                        final.Scripts,
                        script => script.Name == "srtx-20240401000000-old");

                    RollbackSynchronizationResult rollbackResult =
                        await workflow.RollbackAsync(
                            new RollbackSynchronizationRequest(
                                configuration,
                                preview.PlanFile),
                            cancellationToken);
                    Assert.Equal(
                        RollbackSynchronizationStatus.RestoredBackup,
                        rollbackResult.Status);

                    RemoteSieveState rolledBack =
                        await connection.ReadStateAsync(cancellationToken);
                    Assert.Equal("original", rolledBack.ActiveScriptName);
                    Assert.Equal(
                        Convert.ToHexString(SHA256.HashData(originalContent)),
                        rolledBack.ActiveContentSha256);
                    Assert.Contains(
                        rolledBack.Scripts,
                        script => script.Name == deployResult.BackupScriptName);

                    HistoryListResult history =
                        await workflow.ListHistoryAsync(
                            new HistoryListRequest(configuration),
                            cancellationToken);
                    Assert.Contains(
                        history.Entries,
                        entry => entry.Name == deployResult.BackupScriptName &&
                            entry.Kind == SieveHistoryEntryKind.Backup);

                    HistoryShowResult shownBackup =
                        await workflow.ShowHistoryAsync(
                            new HistoryShowRequest(
                                configuration,
                                deployResult.BackupScriptName!),
                            cancellationToken);
                    Assert.Equal(originalContent, shownBackup.Content);

                    HistoryRestoreResult historyRestore =
                        await workflow.RestoreHistoryAsync(
                            new HistoryRestoreRequest(
                                configuration,
                                "srtx-20240901000000-keep"),
                            cancellationToken);
                    Assert.Equal(
                        HistoryRestoreStatus.RestoredScript,
                        historyRestore.Status);
                    RemoteSieveState historyRestored =
                        await connection.ReadStateAsync(cancellationToken);
                    Assert.Equal("original", historyRestored.ActiveScriptName);
                    Assert.Equal(
                        Convert.ToHexString(
                            SHA256.HashData(
                                "require [\"fileinto\"];\r\nkeep;\r\n"u8.ToArray())),
                        historyRestored.ActiveContentSha256);
                }
                finally
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }
    }

    private static async Task<byte[]> SeedActiveScriptAsync(
        ISieveServerConnectionFactory connectionFactory,
        SieveServerConfiguration configuration,
        CancellationToken cancellationToken)
    {
        await using ISieveServerConnection connection =
            await connectionFactory.ConnectAsync(configuration, cancellationToken);
        byte[] original =
            "require [\"fileinto\"];\r\n# User content\r\nif true { keep; }\r\n"u8.ToArray();
        await connection.CheckScriptAsync(original, cancellationToken);
        await connection.PutScriptAsync("original", original, cancellationToken);
        await connection.ActivateAsync("original", cancellationToken);
        return original;
    }

    private static async Task SeedHistoryScriptsAsync(
        ISieveServerConnectionFactory connectionFactory,
        SieveServerConfiguration configuration,
        CancellationToken cancellationToken)
    {
        await using ISieveServerConnection connection =
            await connectionFactory.ConnectAsync(configuration, cancellationToken);
        byte[] content = "require [\"fileinto\"];\r\nkeep;\r\n"u8.ToArray();
        foreach (string scriptName in new[]
        {
            "srtx-backup-20240101000000-original-copy",
            "srtx-backup-20240201000000-old",
            "srtx-20240301000000-old",
            "srtx-20240401000000-old",
            "srtx-20240501000000-keep",
            "srtx-20240601000000-keep",
            "srtx-20240701000000-keep",
            "srtx-20240801000000-keep",
            "srtx-20240901000000-keep"
        })
        {
            await connection.PutScriptAsync(scriptName, content, cancellationToken);
        }
    }

    private static async Task<byte[]> ReadCertificateHashAsync(
        IContainer container,
        CancellationToken cancellationToken)
    {
        ExecResult certificate = await container.ExecAsync(
            ["sh", "-c", "base64 -w0 /etc/dovecot/cert.pem"],
            cancellationToken);
        using X509Certificate2 parsed = X509Certificate2.CreateFromPem(
            System.Text.Encoding.ASCII.GetString(
                Convert.FromBase64String(certificate.Stdout.Trim())));
        return SHA256.HashData(parsed.RawData);
    }

    private sealed class TestClientFactory(byte[] certificateHash)
        : IManageSieveClientFactory
    {
        public IManageSieveClient CreateClient(ManageSieveClientOptions options) =>
            new ManageSieveClient(
                options,
                new TcpManageSieveTransportFactory(Validate));

        private bool Validate(
            object sender,
            X509Certificate? certificate,
            X509Chain? chain,
            SslPolicyErrors errors) =>
            certificate is not null &&
            CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(certificate.GetRawCertData()),
                certificateHash);
    }

    private sealed class TestInteraction : ISynchronizationInteraction
    {
        public bool ResolveAdoption(bool? explicitChoice, int compatibleRuleCount) =>
            explicitChoice ?? false;
    }
}

internal sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute(
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
        try
        {
            using Process? process = Process.Start(
                new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "version --format {{.Server.Version}}",
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                });
            if (process is null ||
                !process.WaitForExit(5_000) ||
                process.ExitCode != 0)
            {
                Skip = "Docker-backed synchronization tests require a running Docker daemon.";
            }
        }
        catch
        {
            Skip = "Docker-backed synchronization tests require the Docker CLI and daemon.";
        }
    }
}
