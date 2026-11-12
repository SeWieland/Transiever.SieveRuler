using Transiever.Common.Accounts;
using Transiever.SieveRuler.Services;

namespace Transiever.SieveRuler.Cli;

public interface ISieveServerConfigurationProvider
{
    SieveServerConfiguration GetConfiguration(CommandLineOptions options);
}

public sealed class EnvironmentSieveServerConfigurationProvider : ISieveServerConfigurationProvider
{
    private readonly Func<IAccountProfileStore> profiles;
    private readonly Func<ICredentialStore> credentials;
    private readonly Func<string, string?> environment;
    private readonly ICliInput input;

    public EnvironmentSieveServerConfigurationProvider()
        : this(() => new JsonAccountProfileStore(), CredentialStores.CreateDefault,
            Environment.GetEnvironmentVariable, new ConsoleCliInput()) { }

    internal EnvironmentSieveServerConfigurationProvider(
        Func<IAccountProfileStore> profiles, Func<ICredentialStore> credentials,
        Func<string, string?> environment, ICliInput input)
    {
        this.profiles = profiles;
        this.credentials = credentials;
        this.environment = environment;
        this.input = input;
    }

    public SieveServerConfiguration GetConfiguration(CommandLineOptions options)
    {
        AccountProfile? profile = SelectedProfile(options.Account);
        ValidateAuthentication(profile);
        string host = options.SieveHost ?? Read("HOST") ?? profile?.Host ?? input.ReadRequired("ManageSieve host");
        string userName = options.SieveUserName ?? Read("USERNAME") ?? profile?.Username ?? input.ReadRequired("ManageSieve username");
        int port = options.SievePort ?? ReadPort() ?? profile?.Port ?? SieveServerConfiguration.DefaultPort;
        SieveConnectionSecurity security = ResolveSecurity(options.SieveSecurity, profile?.Security);
        string password = options.SievePasswordStdin
            ? input.ReadStandardInputSecret()
            : Read("PASSWORD") ?? ReadStoredOrPromptedPassword(profile);
        return new SieveServerConfiguration(host, port, userName, password, security);
    }

    private AccountProfile? SelectedProfile(string? explicitAccount)
    {
        string? selection = explicitAccount ?? environment("TRANSIEVER_ACCOUNT");
        if (explicitAccount is null && string.IsNullOrWhiteSpace(selection)) return null;
        AccountReference reference = AccountReference.Parse(selection!);
        return profiles().Get(reference) ?? throw new InvalidOperationException("The selected account profile does not exist.");
    }

    private static void ValidateAuthentication(AccountProfile? profile)
    {
        if (!string.IsNullOrWhiteSpace(profile?.PreferredAuthentication) &&
            !profile.PreferredAuthentication.Equals("PLAIN", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected account requests an authentication mechanism unsupported by srtx; only PLAIN over TLS is supported.");
    }

    private string ReadStoredOrPromptedPassword(AccountProfile? profile)
    {
        CredentialStatus status = CredentialStatus.Missing;
        if (profile is not null)
        {
            CredentialResult result = credentials().Get(profile.Reference, CredentialKind.Password);
            status = result.Status;
            if (status == CredentialStatus.Available && !string.IsNullOrEmpty(result.Secret)) return result.Secret;
        }
        if (!input.IsInteractive)
            throw new InvalidOperationException($"Password is {status}. Use --sieve-password-stdin or TRANSIEVER_SIEVE_PASSWORD in headless execution.");
        return input.ReadSecret("ManageSieve password");
    }

    private int? ReadPort()
    {
        string? value = Read("PORT");
        if (value is null) return null;
        return int.TryParse(value, out int port) && port is >= 1 and <= 65535
            ? port
            : throw new InvalidOperationException("TRANSIEVER_SIEVE_PORT must be a TCP port from 1 to 65535.");
    }

    private SieveConnectionSecurity ResolveSecurity(SieveConnectionSecurity? explicitSecurity, TransportSecurity? profileSecurity)
    {
        if (explicitSecurity is { } security) return security;
        string value = Read("SECURITY_MODE") ?? profileSecurity?.ToString() ?? "StartTlsRequired";
        if (value.Equals("PlainText", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Transiever.SieveRuler does not send credentials over a plaintext ManageSieve connection.");
        return Enum.TryParse(value, true, out SieveConnectionSecurity mode) && Enum.IsDefined(mode)
            ? mode
            : throw new InvalidOperationException("Unsupported ManageSieve security mode.");
    }

    private string? Read(string suffix) => environment($"TRANSIEVER_SIEVE_{suffix}") is { Length: > 0 } value ? value : null;

    internal static string ReadStandardInputPassword(bool inputIsRedirected)
    {
        if (!inputIsRedirected)
            throw new InvalidOperationException("--sieve-password-stdin requires redirected standard input.");
        return Console.ReadLine() is { Length: > 0 } password
            ? password
            : throw new InvalidOperationException("Standard input did not contain a Sieve password.");
    }
}
