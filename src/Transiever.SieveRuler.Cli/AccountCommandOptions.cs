using Transiever.Common.Accounts;

namespace Transiever.SieveRuler.Cli;

internal enum AccountAction { List, Show, Create, Update, Status, Login, Logout, Forget }

internal sealed class AccountCommandOptions
{
    public AccountAction Action { get; private init; }
    public bool ShowHelp { get; private init; }
    public string? Account { get; private set; }
    public string? Name { get; private set; }
    public string? Provider { get; private set; }
    public string? AccountId { get; private set; }
    public string? Authentication { get; private set; }
    public string? Host { get; private set; }
    public string? Username { get; private set; }
    public int? Port { get; private set; }
    public TransportSecurity? Security { get; private set; }
    public CredentialKind Kind { get; private set; } = CredentialKind.Password;
    public bool KindSpecified { get; private set; }
    public bool SecretStdin { get; private set; }

    public static AccountCommandOptions Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args.Any(value => value is "--help" or "-h" or "help"))
            return new() { ShowHelp = true };
        SecretOptionSafety.Validate(args);
        if (!Enum.TryParse(args[0], true, out AccountAction action) || !Enum.IsDefined(action) || int.TryParse(args[0], out _))
            throw new ArgumentException("Unknown account command. Use list, show, create, update, status, login, logout, or forget.");
        var result = new AccountCommandOptions { Action = action };
        int index = 1;
        if (index < args.Count && !args[index].StartsWith('-')) result.Account = args[index++];
        if (result.Account is not null && action is AccountAction.List or AccountAction.Create)
            throw new ArgumentException("This account command does not accept an existing account reference.");
        for (; index < args.Count; index++)
        {
            string option = args[index];
            ValidateOptionAction(option, action);
            if (option is "--secret-stdin" or "--sieve-password-stdin")
            {
                result.SecretStdin = true;
                continue;
            }
            if (++index >= args.Count || args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"{option} requires a value.");
            string value = args[index];
            switch (option)
            {
                case "--account": result.Account = value; break;
                case "--name": result.Name = value; break;
                case "--provider": result.Provider = value; break;
                case "--account-id": result.AccountId = value; break;
                case "--authentication": result.Authentication = value; break;
                case "--sieve-host": result.Host = value; break;
                case "--sieve-username": result.Username = value; break;
                case "--sieve-port":
                    result.Port = int.TryParse(value, out int port) && port is >= 1 and <= 65535
                        ? port : throw new ArgumentException("--sieve-port must be a TCP port from 1 to 65535.");
                    break;
                case "--sieve-security-mode":
                    result.Security = Enum.TryParse(value, true, out TransportSecurity security) && Enum.IsDefined(security)
                        ? security : throw new ArgumentException("Unsupported security mode.");
                    break;
                case "--kind":
                    result.Kind = Enum.TryParse(value, true, out CredentialKind kind) && Enum.IsDefined(kind)
                        ? kind : throw new ArgumentException("--kind must be Password or OAuthRefreshToken.");
                    result.KindSpecified = true;
                    break;
            }
        }
        return result;
    }

    private static void ValidateOptionAction(string option, AccountAction action)
    {
        bool allowed = option switch
        {
            "--account" => action is not (AccountAction.List or AccountAction.Create),
            "--kind" => action is AccountAction.Login or AccountAction.Logout or AccountAction.Status,
            "--secret-stdin" or "--sieve-password-stdin" => action == AccountAction.Login,
            "--name" or "--provider" or "--account-id" or "--authentication" or "--sieve-host" or
                "--sieve-username" or "--sieve-port" or "--sieve-security-mode" => action is AccountAction.Create or AccountAction.Update,
            _ => false
        };
        if (!allowed) throw new ArgumentException("The option is not supported by this account command. Use srtx account --help.");
    }
}
