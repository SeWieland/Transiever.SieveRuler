using Transiever.SieveRuler.Services;
using Provider = global::Transiever.SieveRuler.Cli.EnvironmentSieveServerConfigurationProvider;

namespace Transiever.SieveRuler.Cli.UnitTest;

[Collection("Environment")]
public sealed class ConfigurationProviderTests
{
    [Fact]
    public void Provider_ReadsSieveRulerEnvironment()
    {
        Set("HOST", "sieve.test");
        Set("USERNAME", "user");
        Set("PASSWORD", "password");
        try
        {
            SieveServerConfiguration configuration =
                new Provider().GetConfiguration();

            Assert.Equal("sieve.test", configuration.Host);
            Assert.Equal("user", configuration.UserName);
        }
        finally
        {
            Clear();
        }
    }

    private static void Set(string suffix, string value) =>
        Environment.SetEnvironmentVariable($"SIEVERULER_SIEVE_{suffix}", value);

    private static void Clear()
    {
        foreach (string suffix in new[] { "HOST", "USERNAME", "PASSWORD" })
        {
            Environment.SetEnvironmentVariable(
                $"SIEVERULER_SIEVE_{suffix}",
                null);
        }
    }
}

[CollectionDefinition("Environment", DisableParallelization = true)]
public sealed class EnvironmentCollection;
