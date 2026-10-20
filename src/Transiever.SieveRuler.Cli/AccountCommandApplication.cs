using Transiever.Common.Accounts;

namespace Transiever.SieveRuler.Cli;

internal sealed class AccountCommandApplication(
    Func<IAccountProfileStore> profiles, Func<ICredentialStore> credentials,
    Func<string, string?> environment, ICliInput input, TextWriter output)
{
    public int Run(AccountCommandOptions options)
    {
        if (options.ShowHelp) { ConsolePresentation.PrintAccountHelp(); return 0; }
        IAccountProfileStore store = profiles();
        if (options.Action == AccountAction.List)
        {
            foreach (AccountProfile entry in store.List()) output.WriteLine($"{entry.Reference}  {entry.DisplayName}  {entry.Host}");
            return 0;
        }
        if (options.Action == AccountAction.Create)
        {
            AccountProfile created = store.Create(CreateProfile(options));
            output.WriteLine($"Created local profile {created.Reference} ({created.DisplayName}). No credentials were stored.");
            return 0;
        }
        AccountReference reference = AccountReference.Parse(options.Account ?? environment("TRANSIEVER_ACCOUNT") ??
            throw new InvalidOperationException("Select an account with account:<guid>, --account, or TRANSIEVER_ACCOUNT."));
        AccountProfile profile = store.Get(reference) ?? throw new InvalidOperationException("The selected account profile does not exist.");
        return options.Action switch
        {
            AccountAction.Show => Show(profile),
            AccountAction.Update => Update(store, profile, options),
            AccountAction.Status => Status(reference, options),
            AccountAction.Login => Login(reference, options),
            AccountAction.Logout => Logout(reference, options),
            AccountAction.Forget => Forget(store, reference),
            _ => throw new InvalidOperationException("Unsupported account command.")
        };
    }

    private AccountProfile CreateProfile(AccountCommandOptions options)
    {
        string name = options.Name ?? input.ReadRequired("Account name");
        string host = options.Host ?? Read("HOST") ?? input.ReadRequired("ManageSieve host");
        string username = options.Username ?? Read("USERNAME") ?? input.ReadRequired("ManageSieve username");
        return new()
        {
            DisplayName = name, Host = host, Username = username,
            Provider = options.Provider ?? "managesieve", AccountId = options.AccountId ?? username,
            PreferredAuthentication = options.Authentication,
            Port = options.Port ?? EnvironmentPort() ?? 4190,
            Security = options.Security ?? EnvironmentSecurity() ?? TransportSecurity.StartTlsRequired
        };
    }

    private int Update(IAccountProfileStore store, AccountProfile profile, AccountCommandOptions options)
    {
        AccountProfile updated = store.Update(profile with
        {
            DisplayName = options.Name ?? profile.DisplayName, Provider = options.Provider ?? profile.Provider,
            AccountId = options.AccountId ?? profile.AccountId, Host = options.Host ?? Read("HOST") ?? profile.Host,
            Username = options.Username ?? Read("USERNAME") ?? profile.Username,
            Port = options.Port ?? EnvironmentPort() ?? profile.Port,
            Security = options.Security ?? EnvironmentSecurity() ?? profile.Security,
            PreferredAuthentication = options.Authentication ?? profile.PreferredAuthentication
        });
        output.WriteLine($"Updated local profile {updated.Reference}.");
        return 0;
    }

    private int Show(AccountProfile profile)
    {
        output.WriteLine(profile.Reference);
        output.WriteLine($"Name: {profile.DisplayName}");
        output.WriteLine($"Provider: {profile.Provider}");
        output.WriteLine($"Account ID: {profile.AccountId}");
        output.WriteLine($"Host: {profile.Host}");
        output.WriteLine($"Port: {profile.Port}");
        output.WriteLine($"Username: {profile.Username}");
        output.WriteLine($"Security: {profile.Security}");
        output.WriteLine($"Authentication: {profile.PreferredAuthentication ?? "default (PLAIN over TLS)"}");
        return 0;
    }

    private int Status(AccountReference reference, AccountCommandOptions options)
    {
        ICredentialStore store = credentials();
        foreach (CredentialKind kind in SelectedKinds(options)) output.WriteLine($"{kind}: {store.GetStatus(reference, kind)}");
        output.WriteLine("Status describes local credential metadata; no provider authentication was attempted.");
        return 0;
    }

    private int Login(AccountReference reference, AccountCommandOptions options)
    {
        string? environmentSecret = options.Kind == CredentialKind.Password ? Read("PASSWORD") : environment("TRANSIEVER_ACCOUNT_REFRESH_TOKEN");
        string secret = options.SecretStdin ? input.ReadStandardInputSecret() : environmentSecret ?? input.ReadSecret("Local credential");
        if (string.IsNullOrEmpty(secret)) throw new InvalidOperationException("A non-empty secret is required.");
        CredentialStatus status = credentials().Set(reference, options.Kind, secret);
        output.WriteLine($"Local {options.Kind}: {status}. Login stores or replaces a local credential; it does not authenticate with the provider or acquire OAuth tokens.");
        return status == CredentialStatus.Available ? 0 : 1;
    }

    private int Logout(AccountReference reference, AccountCommandOptions options)
    {
        ICredentialStore store = credentials();
        bool failed = false;
        foreach (CredentialKind kind in SelectedKinds(options))
        {
            CredentialStatus status = store.Remove(reference, kind);
            output.WriteLine($"Local {kind}: {status}.");
            failed |= status != CredentialStatus.Missing;
        }
        output.WriteLine("Logout removes the selected local credentials and keeps the profile. Provider credentials are not revoked.");
        return failed ? 1 : 0;
    }

    private int Forget(IAccountProfileStore store, AccountReference reference)
    {
        CredentialStatus status = new AccountManager(store, credentials()).Delete(reference);
        output.WriteLine(status == CredentialStatus.Missing
            ? "Forgot the local account profile and credentials. Provider credentials are not revoked."
            : "Credential removal is Unavailable; the local profile was kept. Provider credentials are not revoked.");
        return status == CredentialStatus.Missing ? 0 : 1;
    }

    private static CredentialKind[] SelectedKinds(AccountCommandOptions options) =>
        options.KindSpecified ? [options.Kind] : Enum.GetValues<CredentialKind>();

    private int? EnvironmentPort()
    {
        if (Read("PORT") is not { } value) return null;
        return int.TryParse(value, out int port) && port is >= 1 and <= 65535
            ? port : throw new InvalidOperationException("TRANSIEVER_SIEVE_PORT must be a TCP port from 1 to 65535.");
    }

    private TransportSecurity? EnvironmentSecurity()
    {
        if (Read("SECURITY_MODE") is not { } value) return null;
        return Enum.TryParse(value, true, out TransportSecurity mode) && Enum.IsDefined(mode)
            ? mode : throw new InvalidOperationException("Unsupported ManageSieve security mode.");
    }

    private string? Read(string suffix) => environment($"TRANSIEVER_SIEVE_{suffix}") is { Length: > 0 } value ? value : null;
}
