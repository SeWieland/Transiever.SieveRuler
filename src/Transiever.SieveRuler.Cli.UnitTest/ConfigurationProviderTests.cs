using Transiever.SieveRuler.Services;
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
        TextReader originalInput = Console.In;
        Console.SetIn(new StringReader("stdin-sentinel\n"));
        try
        {
            SieveServerConfiguration configuration =
                new Provider().GetConfiguration(CommandLineOptions.Parse(["preview"]));

            Assert.Equal("sieve.test", configuration.Host);
            Assert.Equal("user", configuration.UserName);
            Assert.Equal("stdin-sentinel", Console.In.ReadLine());
        }
        finally
        {
            Console.SetIn(originalInput);
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
                    "--sieve-security-mode", "ImplicitTls"
                ]);

            SieveServerConfiguration configuration =
                new Provider().GetConfiguration(options);

            Assert.Equal("cli.test", configuration.Host);
            Assert.Equal(4191, configuration.Port);
            Assert.Equal("cli-user", configuration.UserName);
            Assert.Equal("env-password", configuration.Password);
            Assert.Equal(SieveConnectionSecurity.ImplicitTls, configuration.Security);
        }
        finally
        {
            Clear();
        }
    }

    [Fact]
    public void Provider_UsesExplicitStandardInputPasswordBeforeEnvironment()
    {
        Clear();
        Set("HOST", "sieve.test");
        Set("USERNAME", "user");
        Set("PASSWORD", "environment-password");
        TextReader originalInput = Console.In;
        Console.SetIn(new StringReader("stdin-password\nunused\n"));
        try
        {
            SieveServerConfiguration configuration = new Provider().GetConfiguration(
                CommandLineOptions.Parse(["preview", "--sieve-password-stdin"]));

            Assert.Equal("stdin-password", configuration.Password);
            Assert.Equal("unused", Console.In.ReadLine());
        }
        finally
        {
            Console.SetIn(originalInput);
            Clear();
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    public void Provider_RejectsMissingExplicitStandardInputPassword(string input)
    {
        Clear(); Set("HOST", "sieve.test"); Set("USERNAME", "user");
        TextReader originalInput = Console.In; Console.SetIn(new StringReader(input));
        try
        {
            Assert.Throws<InvalidOperationException>(() => new Provider().GetConfiguration(
                CommandLineOptions.Parse(["preview", "--sieve-password-stdin"])));
        }
        finally { Console.SetIn(originalInput); Clear(); }
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
