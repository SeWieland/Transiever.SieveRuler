namespace Transiever.SieveRuler.Models;

public sealed record RuleOptimizationDiagnostic
{
    public string Severity { get; init; } = "Info";

    public string Action { get; init; } = "";

    public string Message { get; init; } = "";

    public string? Detail { get; init; }
}
