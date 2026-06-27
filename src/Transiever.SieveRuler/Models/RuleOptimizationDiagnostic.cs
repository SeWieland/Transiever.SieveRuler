namespace Transiever.SieveRuler.Models;

/// <summary>
/// Diagnostic emitted while optimizing a rule set.
/// </summary>
public sealed record RuleOptimizationDiagnostic
{
    public string Severity { get; init; } = "Info";

    public string Action { get; init; } = "";

    public string Message { get; init; } = "";

    public string? Detail { get; init; }
}
