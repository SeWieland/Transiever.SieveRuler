using Transiever.SieveRuler.Models;
using Transiever.SieveRuler.Services;

namespace Transiever.SieveRuler.Cli.UnitTest;

public sealed class CommandLineOptionsTests
{
    [Fact]
    public void Parse_WithoutArguments_ShowsHelp()
    {
        Assert.True(CommandLineOptions.Parse([]).ShowHelp);
    }

    [Fact]
    public void Parse_RejectsSourceSpecificCommands()
    {
        Assert.Throws<ArgumentException>(
            () => CommandLineOptions.Parse(["export"]));
        Assert.Throws<ArgumentException>(
            () => CommandLineOptions.Parse(["all"]));
    }

    [Fact]
    public void Parse_PreviewReadsReviewArtifactOptions()
    {
        CommandLineOptions options = CommandLineOptions.Parse(
            [
                "preview",
                "--rules", "source.json",
                "--reconciled-rules", "combined.json",
                "--candidate-rules", "candidate-rules.json",
                "--script-name", "Open-Xchange",
                "--preserve-compatible"
            ]);

        Assert.Equal(SieveRulerCommand.Preview, options.Command);
        Assert.Equal("source.json", options.RulesFile);
        Assert.Equal("combined.json", options.ReconciledRulesFile);
        Assert.Equal("candidate-rules.json", options.CandidateRulesFile);
        Assert.Equal("Open-Xchange", options.ScriptName);
        Assert.False(options.AdoptCompatible);
    }

    [Fact]
    public void Parse_RollbackReadsPlanAndForce()
    {
        CommandLineOptions options = CommandLineOptions.Parse(
            ["rollback", "--plan", "plan.json", "--force"]);

        Assert.Equal(SieveRulerCommand.Rollback, options.Command);
        Assert.Equal("plan.json", options.PlanFile);
        Assert.True(options.Force);
    }

    [Fact]
    public void Parse_DeployReadsHistoryOptions()
    {
        CommandLineOptions options = CommandLineOptions.Parse(
            ["deploy", "--history-limit", "3", "--no-prune-history"]);

        Assert.Equal(SieveRulerCommand.Deploy, options.Command);
        Assert.Equal(3, options.HistoryLimit);
        Assert.False(options.PruneHistory);
    }

    [Fact]
    public void Parse_ReadsSieveConnectionOptions()
    {
        CommandLineOptions options = CommandLineOptions.Parse(
            [
                "preview",
                "--sieve-host", "sieve.test",
                "--sieve-port", "4191",
                "--sieve-username", "user",
                "--sieve-password", "password",
                "--sieve-security-mode", "ImplicitTls"
            ]);

        Assert.Equal("sieve.test", options.SieveHost);
        Assert.Equal(4191, options.SievePort);
        Assert.Equal("user", options.SieveUserName);
        Assert.Equal("password", options.SievePassword);
        Assert.Equal(SieveConnectionSecurity.ImplicitTls, options.SieveSecurity);
    }

    [Fact]
    public void Parse_RejectsInvalidSievePort()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => CommandLineOptions.Parse(["preview", "--sieve-port", "70000"]));

        Assert.Contains("--sieve-port", exception.Message);
    }

    [Fact]
    public void Parse_RejectsNegativeHistoryLimit()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => CommandLineOptions.Parse(["deploy", "--history-limit", "-1"]));

        Assert.Contains("--history-limit", exception.Message);
    }

    [Fact]
    public void Parse_HistoryList()
    {
        CommandLineOptions options = CommandLineOptions.Parse(["history", "list"]);

        Assert.Equal(SieveRulerCommand.History, options.Command);
        Assert.Equal(SieveRulerHistoryAction.List, options.HistoryAction);
    }

    [Fact]
    public void Parse_HistoryShowReadsScriptAndOutput()
    {
        CommandLineOptions options = CommandLineOptions.Parse(
            ["history", "show", "srtx-backup-20240101000000-original", "--sieve", "restore.sieve"]);

        Assert.Equal(SieveRulerCommand.History, options.Command);
        Assert.Equal(SieveRulerHistoryAction.Show, options.HistoryAction);
        Assert.Equal("srtx-backup-20240101000000-original", options.HistoryScriptName);
        Assert.Equal("restore.sieve", options.SieveFile);
        Assert.True(options.SieveFileSpecified);
    }

    [Fact]
    public void Parse_HistoryRestoreReadsOriginalAndForce()
    {
        CommandLineOptions options = CommandLineOptions.Parse(
            ["history", "restore", "original", "--force", "--dry-run"]);

        Assert.Equal(SieveRulerCommand.History, options.Command);
        Assert.Equal(SieveRulerHistoryAction.Restore, options.HistoryAction);
        Assert.Equal("original", options.HistoryScriptName);
        Assert.True(options.Force);
        Assert.True(options.DryRun);
    }

    [Fact]
    public void Parse_HistoryDeleteReadsScriptAndDryRun()
    {
        CommandLineOptions options = CommandLineOptions.Parse(
            ["history", "delete", "srtx-20240101000000-candidate", "--dry-run"]);

        Assert.Equal(SieveRulerCommand.History, options.Command);
        Assert.Equal(SieveRulerHistoryAction.Delete, options.HistoryAction);
        Assert.Equal("srtx-20240101000000-candidate", options.HistoryScriptName);
        Assert.True(options.DryRun);
    }

    [Fact]
    public void Parse_HistoryPruneReadsDryRun()
    {
        CommandLineOptions options = CommandLineOptions.Parse(
            ["history", "prune", "--dry-run"]);

        Assert.Equal(SieveRulerCommand.History, options.Command);
        Assert.Equal(SieveRulerHistoryAction.Prune, options.HistoryAction);
        Assert.True(options.DryRun);
    }

    [Theory]
    [InlineData("-o", RuleOptimizationMode.Conservative)]
    [InlineData("-oo", RuleOptimizationMode.Balanced)]
    [InlineData("-ooo", RuleOptimizationMode.Aggressive)]
    public void Parse_OptimizationAliases(
        string alias,
        RuleOptimizationMode expected)
    {
        CommandLineOptions options =
            CommandLineOptions.Parse(["generate", alias]);

        Assert.Equal(expected, options.OptimizationMode);
    }
}
