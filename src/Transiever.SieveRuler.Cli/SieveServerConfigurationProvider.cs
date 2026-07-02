using Transiever.SieveRuler.Services;

namespace Transiever.SieveRuler.Cli;

public interface ISieveServerConfigurationProvider
{
    SieveServerConfiguration GetConfiguration(CommandLineOptions options);
}

public sealed class EnvironmentSieveServerConfigurationProvider
    : ISieveServerConfigurationProvider
{
    public SieveServerConfiguration GetConfiguration(CommandLineOptions options)
    {
        string host = options.SieveHost ?? Required("HOST");
        string userName = options.SieveUserName ?? Required("USERNAME");
        string password = options.SievePassword ?? Read("PASSWORD") ?? ReadPassword();
        int port = options.SievePort ?? (int.TryParse(
            Read("PORT"),
            out int configuredPort)
            ? configuredPort
            : SieveServerConfiguration.DefaultPort);
        string? configuredSecurity = Read("SECURITY_MODE");
        if (configuredSecurity?.Equals(
            "PlainText",
            StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new InvalidOperationException(
                "Transiever.SieveRuler does not send credentials over a plaintext ManageSieve connection.");
        }

        SieveConnectionSecurity security = Enum.TryParse(
                configuredSecurity,
                ignoreCase: true,
                out SieveConnectionSecurity configuredMode)
                ? configuredMode
                : SieveConnectionSecurity.StartTlsRequired;
        security = options.SieveSecurity ?? security;

        return new SieveServerConfiguration(
            host,
            port,
            userName,
            password,
            security);
    }

    private static string Required(string suffix)
    {
        return Read(suffix) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"Environment variable TRANSIEVER_SIEVE_{suffix} is required.");
    }

    private static string? Read(string suffix) =>
        Environment.GetEnvironmentVariable($"TRANSIEVER_SIEVE_{suffix}");

    private static string ReadPassword()
    {
        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException(
                "TRANSIEVER_SIEVE_PASSWORD is required when input is redirected.");
        }

        Console.Write("ManageSieve password: ");
        var password = new System.Text.StringBuilder();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
            }
        }

        Console.WriteLine();
        return password.ToString();
    }
}
