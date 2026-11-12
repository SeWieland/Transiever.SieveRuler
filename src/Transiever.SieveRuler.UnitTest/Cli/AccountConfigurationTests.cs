using Transiever.Common.Accounts;
using Transiever.SieveRuler.Services;

namespace Transiever.SieveRuler.Cli.UnitTest;

public sealed class AccountConfigurationTests
{
    private static readonly AccountReference Reference = AccountReference.Parse("account:57ec9b3c-42e7-443f-b5d2-37f94c20c120");

    [Fact]
    public void Selection_UsesExplicitReferenceAndStoredConnection()
    {
        var profiles = new FakeProfiles(Profile());
        var credentials = new FakeCredentials();
        credentials.Set(Reference, CredentialKind.Password, "stored-password");
        var provider = Provider(profiles, credentials, new() { ["TRANSIEVER_ACCOUNT"] = "invalid" });

        var result = provider.GetConfiguration(CommandLineOptions.Parse(["preview", "--account", Reference.ToString()]));

        Assert.Equal("profile.test", result.Host);
        Assert.Equal("profile-user", result.UserName);
        Assert.Equal(4192, result.Port);
        Assert.Equal(SieveConnectionSecurity.ImplicitTls, result.Security);
        Assert.Equal("stored-password", result.Password);
    }

    [Fact]
    public void Selection_PreservesOverrideAndEnvironmentPrecedence()
    {
        var credentials = new FakeCredentials();
        credentials.Set(Reference, CredentialKind.Password, "stored-password");
        var input = new FakeInput { StandardInputSecret = "stdin-password" };
        var provider = Provider(new FakeProfiles(Profile()), credentials, new()
        {
            ["TRANSIEVER_ACCOUNT"] = Reference.ToString(),
            ["TRANSIEVER_SIEVE_HOST"] = "env.test",
            ["TRANSIEVER_SIEVE_USERNAME"] = "env-user",
            ["TRANSIEVER_SIEVE_PASSWORD"] = "env-password",
            ["TRANSIEVER_SIEVE_PORT"] = "4193",
            ["TRANSIEVER_SIEVE_SECURITY_MODE"] = "StartTlsRequired"
        }, input);

        var result = provider.GetConfiguration(CommandLineOptions.Parse(
            ["preview", "--sieve-host", "cli.test", "--sieve-password-stdin"]));

        Assert.Equal("cli.test", result.Host);
        Assert.Equal("env-user", result.UserName);
        Assert.Equal(4193, result.Port);
        Assert.Equal(SieveConnectionSecurity.StartTlsRequired, result.Security);
        Assert.Equal("stdin-password", result.Password);
        Assert.Equal(0, credentials.Reads);
    }

    [Fact]
    public void Selection_EnvironmentPasswordDoesNotOpenCredentialStore()
    {
        var provider = new EnvironmentSieveServerConfigurationProvider(
            () => new FakeProfiles(Profile()), () => throw new InvalidOperationException("Credential store accessed"),
            key => key switch { "TRANSIEVER_ACCOUNT" => Reference.ToString(), "TRANSIEVER_SIEVE_PASSWORD" => "env-password", _ => null },
            new FakeInput());
        Assert.Equal("env-password", provider.GetConfiguration(CommandLineOptions.Parse(["preview"])).Password);
    }

    [Fact]
    public void InvalidSelection_FailsBeforeOpeningEitherStore()
    {
        var provider = new EnvironmentSieveServerConfigurationProvider(
            () => throw new InvalidOperationException("Profile store accessed"),
            () => throw new InvalidOperationException("Credential store accessed"), _ => null, new FakeInput());
        Assert.Throws<ArgumentException>(() => provider.GetConfiguration(
            CommandLineOptions.Parse(["preview", "--account", "not-an-account"])));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void EmptyExplicitSelection_IsRejectedInsteadOfFallingBack(string selection)
    {
        var provider = Provider(new FakeProfiles(Profile()), new FakeCredentials(), new()
        {
            ["TRANSIEVER_SIEVE_HOST"] = "env.test", ["TRANSIEVER_SIEVE_USERNAME"] = "env-user", ["TRANSIEVER_SIEVE_PASSWORD"] = "env-password"
        });
        Assert.Throws<ArgumentException>(() => provider.GetConfiguration(
            CommandLineOptions.Parse(["preview", "--account", selection])));
    }

    [Fact]
    public void NoSelection_DoesNotOpenStores()
    {
        var environment = new Dictionary<string, string?>
        {
            ["TRANSIEVER_SIEVE_HOST"] = "env.test", ["TRANSIEVER_SIEVE_USERNAME"] = "user", ["TRANSIEVER_SIEVE_PASSWORD"] = "one-run"
        };
        var provider = new EnvironmentSieveServerConfigurationProvider(
            () => throw new InvalidOperationException("Profile store accessed"),
            () => throw new InvalidOperationException("Credential store accessed"),
            key => environment.GetValueOrDefault(key), new FakeInput());

        Assert.Equal("one-run", provider.GetConfiguration(CommandLineOptions.Parse(["preview"])).Password);
    }

    [Theory]
    [InlineData(CredentialStatus.Expired)]
    [InlineData(CredentialStatus.Rejected)]
    [InlineData(CredentialStatus.Unavailable)]
    [InlineData(CredentialStatus.Missing)]
    public void HeadlessSelection_DoesNotReuseUnusableCredentialsOrPrompt(CredentialStatus status)
    {
        var credentials = new FakeCredentials { Result = new(status, "unsafe-secret-sentinel") };
        var provider = Provider(new FakeProfiles(Profile()), credentials);

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetConfiguration(
            CommandLineOptions.Parse(["preview", "--account", Reference.ToString()])));

        Assert.Contains(status.ToString(), exception.Message);
        Assert.DoesNotContain("unsafe-secret-sentinel", exception.Message);
    }

    [Theory]
    [InlineData("XOAUTH2", TransportSecurity.ImplicitTls)]
    [InlineData(null, TransportSecurity.PlainText)]
    public void UnsupportedProfilePolicy_FailsBeforeRetrievingCredential(string? authentication, TransportSecurity security)
    {
        var credentials = new FakeCredentials();
        var provider = Provider(new FakeProfiles(Profile() with { PreferredAuthentication = authentication, Security = security }), credentials);

        Assert.Throws<InvalidOperationException>(() => provider.GetConfiguration(
            CommandLineOptions.Parse(["preview", "--account", Reference.ToString()])));
        Assert.Equal(0, credentials.Reads);
    }

    [Fact]
    public void MissingProfile_FailsWithoutReadingCredentials()
    {
        var credentials = new FakeCredentials();
        var provider = Provider(new FakeProfiles(), credentials);
        Assert.Throws<InvalidOperationException>(() => provider.GetConfiguration(
            CommandLineOptions.Parse(["preview", "--account", Reference.ToString()])));
        Assert.Equal(0, credentials.Reads);
    }

    [Fact]
    public void InteractiveSelection_CanUseOneRunSecretWhenStoreUnavailable()
    {
        var provider = Provider(new FakeProfiles(Profile()), new FakeCredentials { Result = new(CredentialStatus.Unavailable, null) },
            input: new FakeInput { IsInteractive = true, Secret = "prompt-password" });
        Assert.Equal("prompt-password", provider.GetConfiguration(CommandLineOptions.Parse(
            ["preview", "--account", Reference.ToString()])).Password);
    }

    private static EnvironmentSieveServerConfigurationProvider Provider(FakeProfiles profiles, FakeCredentials credentials,
        Dictionary<string, string?>? environment = null, FakeInput? input = null) =>
        new(() => profiles, () => credentials, key => environment?.GetValueOrDefault(key), input ?? new FakeInput());

    internal static AccountProfile Profile() => new()
    {
        Reference = Reference, DisplayName = "Test account", Provider = "managesieve", AccountId = "identity",
        Host = "profile.test", Username = "profile-user", Port = 4192, Security = TransportSecurity.ImplicitTls
    };
}

internal sealed class FakeProfiles(params AccountProfile[] profiles) : IAccountProfileStore
{
    private readonly Dictionary<AccountReference, AccountProfile> entries = profiles.ToDictionary(profile => profile.Reference);
    public AccountProfile Create(AccountProfile profile)
    {
        profile = profile with { Reference = new AccountReference(Guid.NewGuid()) };
        entries.Add(profile.Reference, profile);
        return profile;
    }
    public AccountProfile? Get(AccountReference reference) => entries.GetValueOrDefault(reference);
    public IReadOnlyList<AccountProfile> List() => entries.Values.ToArray();
    public AccountProfile Update(AccountProfile profile) => entries[profile.Reference] = profile;
    public void Delete(AccountReference reference) => entries.Remove(reference);
}

internal sealed class FakeCredentials : ICredentialStore
{
    private readonly Dictionary<(AccountReference, CredentialKind), CredentialResult> entries = new();
    public CredentialResult? Result { get; init; }
    public bool FailRemoval { get; init; }
    public int Reads { get; private set; }
    public CredentialResult Get(AccountReference reference, CredentialKind kind)
    {
        Reads++;
        return Result ?? entries.GetValueOrDefault((reference, kind), new(CredentialStatus.Missing, null));
    }
    public CredentialStatus Set(AccountReference reference, CredentialKind kind, string secret, DateTimeOffset? expiresAt = null)
    {
        entries[(reference, kind)] = new(CredentialStatus.Available, secret);
        return CredentialStatus.Available;
    }
    public CredentialStatus Remove(AccountReference reference, CredentialKind kind)
    {
        if (FailRemoval) return CredentialStatus.Unavailable;
        entries.Remove((reference, kind));
        return CredentialStatus.Missing;
    }
    public CredentialStatus GetStatus(AccountReference reference, CredentialKind kind) =>
        entries.GetValueOrDefault((reference, kind), new(CredentialStatus.Missing, null)).Status;
    public CredentialStatus MarkOutcome(AccountReference reference, CredentialKind kind, CredentialStatus status)
    {
        entries[(reference, kind)] = new(status, entries.GetValueOrDefault((reference, kind))?.Secret);
        return status;
    }
}

internal sealed class FakeInput : ICliInput
{
    public bool IsInteractive { get; init; }
    public string Secret { get; init; } = "prompt-secret";
    public string StandardInputSecret { get; init; } = "stdin-secret";
    public Queue<string> Lines { get; } = new();
    public string ReadRequired(string prompt) => IsInteractive ? Lines.Dequeue() : throw new InvalidOperationException("Unexpected prompt");
    public string ReadSecret(string prompt) => IsInteractive ? Secret : throw new InvalidOperationException("Unexpected prompt");
    public string ReadStandardInputSecret() => StandardInputSecret;
}
