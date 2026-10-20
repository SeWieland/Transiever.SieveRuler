using Transiever.Common.Accounts;

namespace Transiever.SieveRuler.Cli.UnitTest;

public sealed class AccountCommandTests
{
    [Fact]
    public void CreateThenUpdate_StoresOnlyProfileAndPreservesReference()
    {
        var profiles = new FakeProfiles();
        var credentials = new FakeCredentials();
        using var output = new StringWriter();
        var app = Application(profiles, credentials, output);
        Assert.Equal(0, app.Run(AccountCommandOptions.Parse(
            ["create", "--name", "Work", "--provider", "test", "--account-id", "identity", "--sieve-host", "sieve.test", "--sieve-username", "user"])));
        var profile = Assert.Single(profiles.List());
        Assert.Equal("Work", profile.DisplayName);
        Assert.Equal("identity", profile.AccountId);
        Assert.Equal("test", profile.Provider);
        Assert.Equal("sieve.test", profile.Host);
        Assert.Equal(CredentialStatus.Missing, credentials.GetStatus(profile.Reference, CredentialKind.Password));

        app.Run(AccountCommandOptions.Parse(["update", profile.Reference.ToString(), "--name", "Renamed", "--sieve-host", "new.test"]));
        var updated = Assert.Single(profiles.List());
        Assert.Equal(profile.Reference, updated.Reference);
        Assert.Equal("Renamed", updated.DisplayName);
        Assert.Equal("new.test", updated.Host);
        Assert.Equal("user", updated.Username);
    }

    [Fact]
    public void Create_UsesGuidedNonSecretPromptsOnlyOnTerminal()
    {
        var profiles = new FakeProfiles();
        var input = new FakeInput { IsInteractive = true };
        foreach (string value in new[] { "Personal", "sieve.test", "user" }) input.Lines.Enqueue(value);
        Application(profiles, new FakeCredentials(), new StringWriter(), input).Run(AccountCommandOptions.Parse(["create"]));

        var profile = Assert.Single(profiles.List());
        Assert.Equal("Personal", profile.DisplayName);
        Assert.Equal("sieve.test", profile.Host);
        Assert.Equal("user", profile.Username);
        Assert.Equal("user", profile.AccountId);
        Assert.Equal("managesieve", profile.Provider);
    }

    [Fact]
    public void HeadlessCreate_RequiresFieldsWithoutPersistingPartialProfile()
    {
        var profiles = new FakeProfiles();
        Assert.Throws<InvalidOperationException>(() => Application(profiles, new FakeCredentials(), new StringWriter()).Run(
            AccountCommandOptions.Parse(["create", "--name", "Work"])));
        Assert.Empty(profiles.List());
    }

    [Fact]
    public void LoginReplacementAndLogout_ChangeLocalSecretWithoutPrintingIt()
    {
        var profile = AccountConfigurationTests.Profile();
        var credentials = new FakeCredentials();
        using var output = new StringWriter();
        var input = new FakeInput { StandardInputSecret = "secret-sentinel" };
        var app = Application(new FakeProfiles(profile), credentials, output, input);
        app.Run(AccountCommandOptions.Parse(["login", profile.Reference.ToString(), "--secret-stdin"]));
        Assert.Equal("secret-sentinel", credentials.Get(profile.Reference, CredentialKind.Password).Secret);
        credentials.MarkOutcome(profile.Reference, CredentialKind.Password, CredentialStatus.Rejected);
        app.Run(AccountCommandOptions.Parse(["login", profile.Reference.ToString(), "--kind", "OAuthRefreshToken", "--secret-stdin"]));
        Assert.Equal("secret-sentinel", credentials.Get(profile.Reference, CredentialKind.OAuthRefreshToken).Secret);
        app.Run(AccountCommandOptions.Parse(["login", profile.Reference.ToString(), "--secret-stdin"]));
        Assert.Equal(CredentialStatus.Available, credentials.GetStatus(profile.Reference, CredentialKind.Password));
        app.Run(AccountCommandOptions.Parse(["status", profile.Reference.ToString()]));
        app.Run(AccountCommandOptions.Parse(["logout", profile.Reference.ToString()]));
        Assert.Equal(CredentialStatus.Missing, credentials.GetStatus(profile.Reference, CredentialKind.Password));
        Assert.Equal(CredentialStatus.Missing, credentials.GetStatus(profile.Reference, CredentialKind.OAuthRefreshToken));
        Assert.DoesNotContain("secret-sentinel", output.ToString());
        Assert.Contains("provider", output.ToString());
        Assert.Contains("local", output.ToString());
    }

    [Fact]
    public void Login_EnvironmentSecretWorksHeadlesslyAndStdinTakesPrecedence()
    {
        var profile = AccountConfigurationTests.Profile();
        var credentials = new FakeCredentials();
        using var output = new StringWriter();
        var app = Application(new FakeProfiles(profile), credentials, output, environment: key => key == "TRANSIEVER_SIEVE_PASSWORD" ? "env-secret" : null);
        app.Run(AccountCommandOptions.Parse(["login", profile.Reference.ToString()]));
        Assert.Equal("env-secret", credentials.Get(profile.Reference, CredentialKind.Password).Secret);
        app.Run(AccountCommandOptions.Parse(["login", profile.Reference.ToString(), "--secret-stdin"]));
        Assert.Equal("stdin-secret", credentials.Get(profile.Reference, CredentialKind.Password).Secret);
    }

    [Fact]
    public void Login_UnavailableStoreReportsFailureAndDoesNotClaimProviderLogin()
    {
        var profile = AccountConfigurationTests.Profile();
        using var output = new StringWriter();
        var app = new AccountCommandApplication(() => new FakeProfiles(profile), CredentialStores.CreateUnavailable,
            _ => "one-run-secret", new FakeInput(), output);
        Assert.Equal(1, app.Run(AccountCommandOptions.Parse(["login", profile.Reference.ToString()])));
        Assert.Contains("Unavailable", output.ToString());
        Assert.DoesNotContain("one-run-secret", output.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Forget_PreservesProfileWhenCredentialsCannotBeDeleted(bool failure)
    {
        var profile = AccountConfigurationTests.Profile();
        var profiles = new FakeProfiles(profile);
        using var output = new StringWriter();
        int result = Application(profiles, new FakeCredentials { FailRemoval = failure }, output).Run(
            AccountCommandOptions.Parse(["forget", profile.Reference.ToString()]));
        Assert.Equal(failure ? 1 : 0, result);
        Assert.Equal(failure, profiles.Get(profile.Reference) is not null);
    }

    [Fact]
    public void ListAndShow_ReadProfilesWithoutOpeningCredentialStore()
    {
        var profile = AccountConfigurationTests.Profile();
        using var output = new StringWriter();
        var app = new AccountCommandApplication(() => new FakeProfiles(profile),
            () => throw new InvalidOperationException("Credential store accessed"), _ => null, new FakeInput(), output);
        app.Run(AccountCommandOptions.Parse(["list"]));
        app.Run(AccountCommandOptions.Parse(["show", profile.Reference.ToString()]));
        Assert.Contains(profile.Reference.ToString(), output.ToString());
        Assert.Contains("profile.test", output.ToString());
    }

    [Fact]
    public void Help_DoesNotReadInputEnvironmentOrStores()
    {
        var app = new AccountCommandApplication(
            () => throw new InvalidOperationException("Profile store accessed"),
            () => throw new InvalidOperationException("Credential store accessed"),
            _ => throw new InvalidOperationException("Environment accessed"), new FakeInput(), new StringWriter());
        Assert.Equal(0, app.Run(AccountCommandOptions.Parse([])));
    }

    [Theory]
    [InlineData(CredentialStatus.Missing)]
    [InlineData(CredentialStatus.Expired)]
    [InlineData(CredentialStatus.Rejected)]
    [InlineData(CredentialStatus.Unavailable)]
    public void Status_ReportsMetadataWithoutRetrievingSecrets(CredentialStatus status)
    {
        var profile = AccountConfigurationTests.Profile();
        var credentials = new FakeCredentials();
        credentials.MarkOutcome(profile.Reference, CredentialKind.Password, status);
        using var output = new StringWriter();
        Application(new FakeProfiles(profile), credentials, output).Run(
            AccountCommandOptions.Parse(["status", profile.Reference.ToString()]));
        Assert.Contains($"Password: {status}", output.ToString());
        Assert.Equal(0, credentials.Reads);
    }

    [Fact]
    public void Logout_RemovalFailureReturnsFailureAndKeepsProfile()
    {
        var profile = AccountConfigurationTests.Profile();
        var profiles = new FakeProfiles(profile);
        Assert.Equal(1, Application(profiles, new FakeCredentials { FailRemoval = true }, new StringWriter()).Run(
            AccountCommandOptions.Parse(["logout", profile.Reference.ToString()])));
        Assert.NotNull(profiles.Get(profile.Reference));
    }

    [Fact]
    public void Logout_SelectedKindPreservesOtherCredential()
    {
        var profile = AccountConfigurationTests.Profile();
        var credentials = new FakeCredentials();
        credentials.Set(profile.Reference, CredentialKind.Password, "retained-password");
        credentials.Set(profile.Reference, CredentialKind.OAuthRefreshToken, "removed-refresh-token");
        Application(new FakeProfiles(profile), credentials, new StringWriter()).Run(
            AccountCommandOptions.Parse(["logout", profile.Reference.ToString(), "--kind", "OAuthRefreshToken"]));
        Assert.Equal("retained-password", credentials.Get(profile.Reference, CredentialKind.Password).Secret);
        Assert.Equal(CredentialStatus.Missing, credentials.GetStatus(profile.Reference, CredentialKind.OAuthRefreshToken));
    }

    [Fact]
    public void Status_SelectedKindDoesNotReportOtherCredential()
    {
        var profile = AccountConfigurationTests.Profile();
        using var output = new StringWriter();
        Application(new FakeProfiles(profile), new FakeCredentials(), output).Run(
            AccountCommandOptions.Parse(["status", profile.Reference.ToString(), "--kind", "OAuthRefreshToken"]));
        Assert.Contains("OAuthRefreshToken: Missing", output.ToString());
        Assert.DoesNotContain("Password:", output.ToString());
    }

    [Theory]
    [InlineData("create", "--kind", "Password")]
    [InlineData("forget", "--kind", "OAuthRefreshToken")]
    [InlineData("create", "--secret-stdin")]
    [InlineData("list", "--name", "ignored")]
    public void Parse_RejectsOptionsThatWouldBeIgnored(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => AccountCommandOptions.Parse(args));
    }

    [Fact]
    public void Login_WithoutExplicitInputFailsHeadlesslyAndDoesNotStoreCredential()
    {
        var profile = AccountConfigurationTests.Profile();
        var credentials = new FakeCredentials();
        Assert.Throws<InvalidOperationException>(() => Application(new FakeProfiles(profile), credentials, new StringWriter()).Run(
            AccountCommandOptions.Parse(["login", profile.Reference.ToString()])));
        Assert.Equal(CredentialStatus.Missing, credentials.GetStatus(profile.Reference, CredentialKind.Password));
    }

    [Theory]
    [InlineData("--secret=secret-sentinel")]
    [InlineData("--password", "secret-sentinel")]
    [InlineData("--secret-stdin", "secret-sentinel")]
    [InlineData("--refresh-token=secret-sentinel")]
    public void Parse_RedactsUnsafeSecretValues(params string[] args)
    {
        Assert.DoesNotContain("secret-sentinel", Assert.Throws<ArgumentException>(() =>
            AccountCommandOptions.Parse(["login", ..args])).Message);
    }

    [Fact]
    public void Parse_RejectsInvalidPortsAndUnknownCredentialKinds()
    {
        Assert.Throws<ArgumentException>(() => AccountCommandOptions.Parse(["create", "--sieve-port", "70000"]));
        Assert.Throws<ArgumentException>(() => AccountCommandOptions.Parse(["login", "--kind", "AccessToken"]));
    }

    private static AccountCommandApplication Application(FakeProfiles profiles, FakeCredentials credentials, TextWriter output,
        FakeInput? input = null, Func<string, string?>? environment = null) =>
        new(() => profiles, () => credentials, environment ?? (_ => null), input ?? new FakeInput(), output);
}
