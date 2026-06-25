using Transiever.SieveRuler.Services;

namespace Transiever.SieveRuler.Cli;

public interface ISieveServerConfigurationProvider
{
    SieveServerConfiguration GetConfiguration();
}

public sealed class EnvironmentSieveServerConfigurationProvider
    : ISieveServerConfigurationProvider
{
    public SieveServerConfiguration GetConfiguration()
    {
        string host = Required("SIEVERULER_SIEVE_HOST");
        string userName = Required("SIEVERULER_SIEVE_USERNAME");
        string password = Environment.GetEnvironmentVariable(
            "SIEVERULER_SIEVE_PASSWORD") ?? ReadPassword();
        int port = int.TryParse(
            Environment.GetEnvironmentVariable("SIEVERULER_SIEVE_PORT"),
            out int configuredPort)
            ? configuredPort
            : SieveServerConfiguration.DefaultPort;
        string? configuredSecurity =
            Environment.GetEnvironmentVariable("SIEVERULER_SIEVE_SECURITY_MODE");
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

        return new SieveServerConfiguration(
            host,
            port,
            userName,
            password,
            security);
    }

    private static string Required(string name)
    {
        return Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"Environment variable {name} is required.");
    }

    private static string ReadPassword()
    {
        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException(
                "SIEVERULER_SIEVE_PASSWORD is required when input is redirected.");
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
