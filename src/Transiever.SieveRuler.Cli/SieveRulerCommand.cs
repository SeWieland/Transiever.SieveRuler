namespace Transiever.SieveRuler.Cli;

public enum SieveRulerCommand
{
    Inspect,
    Optimize,
    Generate,
    Preview,
    Deploy,
    Rollback,
    History
}

public enum SieveRulerHistoryAction
{
    List,
    Show,
    Restore,
    Delete,
    Prune
}
