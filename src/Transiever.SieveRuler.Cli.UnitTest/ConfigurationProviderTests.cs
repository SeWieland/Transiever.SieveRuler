using Transiever.SieveRuler.Services;
using CommandLineOptions = global::Transiever.SieveRuler.Cli.CommandLineOptions;
using Provider = global::Transiever.SieveRuler.Cli.EnvironmentSieveServerConfigurationProvider;

namespace Transiever.SieveRuler.Cli.UnitTest;

[Collection("Environment")]
public sealed class ConfigurationProviderTests
{
    [Fact]
    public void Provider_ReadsTransieverEnvironment()
    {
        Clear();
        Set("HOST", "sieve.test");
        Set("USERNAME", "user");
        Set("PASSWORD", "password");
        try
        {
            SieveServerConfiguration configuration =
                new Provider().GetConfiguration(CommandLineOptions.Parse(["preview"]));

            Assert.Equal("sieve.test", configuration.Host);
            Assert.Equal("user", configuration.UserName);
        }
        finally
        {
            Clear();
        }
    }

    [Fact]
    public void Provider_PrefersCommandLineOptionsOverEnvironment()
    {
        Clear();
        Set("HOST", "env.test");
        Set("USERNAME", "env-user");
        Set("PASSWORD", "env-password");
        try
        {
            CommandLineOptions options = CommandLineOptions.Parse(
                [
                    "preview",
                    "--sieve-host", "cli.test",
                    "--sieve-port", "4191",
                    "--sieve-username", "cli-user",
                    "--sieve-password", "cli-password",
                    "--sieve-security-mode", "ImplicitTls"
                ]);

            SieveServerConfiguration configuration =
                new Provider().GetConfiguration(options);

            Assert.Equal("cli.test", configuration.Host);
            Assert.Equal(4191, configuration.Port);
            Assert.Equal("cli-user", configuration.UserName);
            Assert.Equal("cli-password", configuration.Password);
            Assert.Equal(SieveConnectionSecurity.ImplicitTls, configuration.Security);
        }
        finally
        {
            Clear();
        }
    }

    private static void Set(string suffix, string value) =>
        Environment.SetEnvironmentVariable($"TRANSIEVER_SIEVE_{suffix}", value);

    private static void Clear()
    {
        foreach (string suffix in new[]
        {
            "HOST",
            "USERNAME",
            "PASSWORD",
            "PORT",
            "SECURITY_MODE"
        })
        {
            Environment.SetEnvironmentVariable(
                $"TRANSIEVER_SIEVE_{suffix}",
                null);
        }
    }
}

[CollectionDefinition("Environment", DisableParallelization = true)]
public sealed class EnvironmentCollection;
