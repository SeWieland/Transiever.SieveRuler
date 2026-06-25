using System.Security.Cryptography;
using Transiever.ManageSieve;

namespace Transiever.SieveRuler.Services;

public enum SieveConnectionSecurity
{
    StartTlsRequired,
    ImplicitTls
}

public sealed record SieveServerConfiguration(
    string Host,
    int Port,
    string UserName,
    string Password,
    SieveConnectionSecurity Security)
{
    public const int DefaultPort = 4190;
}

public sealed record RemoteSieveState
{
    public required string ActiveScriptName { get; init; }

    public required byte[] ActiveContent { get; init; }

    public required string ActiveContentSha256 { get; init; }

    public IReadOnlyList<ManageSieveScriptInfo> Scripts { get; init; } = [];

    public required ManageSieveCapabilities Capabilities { get; init; }
}

public interface ISieveServerConnection : IAsyncDisposable
{
    Task<RemoteSieveState> ReadStateAsync(CancellationToken cancellationToken);

    Task CheckScriptAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);

    Task<bool> HaveSpaceAsync(
        string scriptName,
        long contentLength,
        CancellationToken cancellationToken);

    Task<byte[]> GetScriptAsync(
        string scriptName,
        CancellationToken cancellationToken);

    Task PutScriptAsync(
        string scriptName,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);

    Task ActivateAsync(string? scriptName, CancellationToken cancellationToken);

    Task DeleteScriptAsync(string scriptName, CancellationToken cancellationToken);
}

public interface ISieveServerConnectionFactory
{
    Task<ISieveServerConnection> ConnectAsync(
        SieveServerConfiguration configuration,
        CancellationToken cancellationToken);
}

public sealed class ManageSieveServerConnectionFactory(
    IManageSieveClientFactory clientFactory) : ISieveServerConnectionFactory
{
    public ManageSieveServerConnectionFactory()
        : this(new ManageSieveClientFactory())
    {
    }

    public async Task<ISieveServerConnection> ConnectAsync(
        SieveServerConfiguration configuration,
        CancellationToken cancellationToken)
    {
        IManageSieveClient client = clientFactory.CreateClient(
            new ManageSieveClientOptions
            {
                Host = configuration.Host,
                Port = configuration.Port,
                SecurityMode = ToTransieverSecurityMode(configuration.Security)
            });

        try
        {
            await client.ConnectAsync(cancellationToken);
            if (configuration.Security == SieveConnectionSecurity.StartTlsRequired)
                await client.StartTlsAsync(cancellationToken);

            await client.AuthenticateAsync(
                new ManageSievePlainAuthenticator(
                    configuration.UserName,
                    configuration.Password),
                cancellationToken);
            return new ManageSieveServerConnection(client);
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    private static ManageSieveSecurityMode ToTransieverSecurityMode(
        SieveConnectionSecurity security) =>
        security switch
        {
            SieveConnectionSecurity.StartTlsRequired =>
                ManageSieveSecurityMode.StartTlsRequired,
            SieveConnectionSecurity.ImplicitTls =>
                ManageSieveSecurityMode.ImplicitTls,
            _ => throw new ArgumentOutOfRangeException(
                nameof(security),
                security,
                "Unsupported Sieve connection security mode.")
        };
}

internal sealed class ManageSieveServerConnection(IManageSieveClient client)
    : ISieveServerConnection
{
    public async Task<RemoteSieveState> ReadStateAsync(
        CancellationToken cancellationToken)
    {
        ManageSieveCapabilities capabilities =
            await client.RefreshCapabilitiesAsync(cancellationToken);
        IReadOnlyList<ManageSieveScriptInfo> scripts =
            await client.ListScriptsAsync(cancellationToken);
        ManageSieveScriptInfo? active = scripts.SingleOrDefault(script => script.IsActive);
        byte[] content = active is null
            ? []
            : (await client.GetScriptAsync(active.Name, cancellationToken)).Content.ToArray();

        return new RemoteSieveState
        {
            ActiveScriptName = active?.Name ?? "",
            ActiveContent = content,
            ActiveContentSha256 = Convert.ToHexString(SHA256.HashData(content)),
            Scripts = scripts,
            Capabilities = capabilities
        };
    }

    public async Task CheckScriptAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        await client.CheckScriptAsync(content, cancellationToken);
    }

    public async Task<bool> HaveSpaceAsync(
        string scriptName,
        long contentLength,
        CancellationToken cancellationToken) =>
        (await client.HaveSpaceAsync(
            scriptName,
            contentLength,
            cancellationToken)).HasSpace;

    public async Task PutScriptAsync(
        string scriptName,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        await client.PutScriptAsync(scriptName, content, cancellationToken);
    }

    public async Task<byte[]> GetScriptAsync(
        string scriptName,
        CancellationToken cancellationToken) =>
        (await client.GetScriptAsync(scriptName, cancellationToken)).Content.ToArray();

    public async Task ActivateAsync(
        string? scriptName,
        CancellationToken cancellationToken)
    {
        await client.SetActiveScriptAsync(
            string.IsNullOrEmpty(scriptName) ? null : scriptName,
            cancellationToken);
    }

    public async Task DeleteScriptAsync(
        string scriptName,
        CancellationToken cancellationToken)
    {
        await client.DeleteScriptAsync(scriptName, cancellationToken);
    }

    public ValueTask DisposeAsync() => client.DisposeAsync();
}
