using Transiever.Common.Cli;

namespace Transiever.SieveRuler.Cli;

internal interface ICliInput
{
    bool IsInteractive { get; }
    string ReadRequired(string prompt);
    string ReadSecret(string prompt);
    string ReadStandardInputSecret();
}

internal sealed class ConsoleCliInput : ICliInput
{
    public bool IsInteractive => !Console.IsInputRedirected && !Console.IsOutputRedirected;

    public string ReadRequired(string prompt)
    {
        if (!IsInteractive)
            throw new InvalidOperationException($"{prompt} is required in headless execution.");
        Console.Write($"{prompt}: ");
        return Console.ReadLine() is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{prompt} is required.");
    }

    public string ReadStandardInputSecret() =>
        EnvironmentSieveServerConfigurationProvider.ReadStandardInputPassword(Console.IsInputRedirected);

    public string ReadSecret(string prompt)
    {
        if (!IsInteractive)
            throw new InvalidOperationException("A secret requires explicit standard input or environment input in headless execution.");
        Console.Write($"{prompt}: ");
        string secret = SecretInput.ReadHidden();
        Console.WriteLine();
        return secret.Length > 0 ? secret : throw new InvalidOperationException("A non-empty secret is required.");
    }
}
