namespace Transiever.SieveRuler.Cli;

internal static class SecretOptionSafety
{
    public static void Validate(IReadOnlyList<string> args)
    {
        for (int index = 0; index < args.Count; index++)
        {
            string value = args[index];
            if (!value.StartsWith('-'))
                continue;

            if (value is "--sieve-password-stdin" or "--secret-stdin")
            {
                if (index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal) && args[index + 1] != "-h")
                    throw new ArgumentException("Secret input cannot be supplied through an option value.");
                continue;
            }

            if (value == "--sieve-password")
                throw new ArgumentException("Passwords cannot be supplied through --sieve-password.");

            string name = value.Split('=', 2)[0];
            if (name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("secret", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Secret input cannot be supplied through an option value.");
        }
    }
}
