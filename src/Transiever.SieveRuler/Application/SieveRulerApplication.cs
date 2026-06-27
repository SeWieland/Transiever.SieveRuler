using Transiever.SieveRuler.Models;
using Transiever.SieveRuler.Services;

namespace Transiever.SieveRuler.Application;

/// <summary>
/// Entry point for local rule inspection, optimization, and Sieve generation workflows.
/// </summary>
public sealed class SieveRulerApplication(
    IRuleSerializer serializer,
    IRuleOptimizer optimizer,
    ISieveGenerator sieveGenerator)
{
    public async Task<GenerateSieveResult> GenerateAsync(
        GenerateSieveRequest request,
        CancellationToken cancellationToken = default)
    {
        RuleDocument document = await serializer.LoadDocumentAsync(
            request.RulesFile,
            cancellationToken);
        return await GenerateDocumentAsync(document, request, cancellationToken);
    }

    public async Task<GenerateSieveResult> GenerateDocumentAsync(
        RuleDocument document,
        GenerateSieveRequest request,
        CancellationToken cancellationToken = default)
    {
        PreparedRules prepared = await OptimizeIfRequestedAsync(
            document,
            request.OutputFile,
            request.OptimizationMode,
            request.DryRun,
            cancellationToken);
        string content = sieveGenerator.Generate(
            prepared.Document.Rules,
            prepared.SourceDescription);

        if (!request.DryRun)
        {
            await File.WriteAllTextAsync(
                request.SieveFile,
                content,
                cancellationToken);
        }

        return new GenerateSieveResult(
            prepared.Document.Rules.Count,
            request.SieveFile,
            prepared.Optimization,
            !request.DryRun);
    }

    public async Task<InspectRulesResult> InspectAsync(
        InspectRulesRequest request,
        CancellationToken cancellationToken = default) =>
        new(
            await serializer.LoadDocumentAsync(
                request.RulesFile,
                cancellationToken),
            request.RulesFile);

    public async Task<OptimizeRulesResult> OptimizeAsync(
        OptimizeRulesRequest request,
        CancellationToken cancellationToken = default)
    {
        RuleDocument source = await serializer.LoadDocumentAsync(
            request.RulesFile,
            cancellationToken);
        RuleOptimizationResult result = optimizer.Optimize(
            source.Rules,
            request.OptimizationMode);
        RuleDocument output = source with
        {
            Rules = result.Rules.ToList()
        };

        if (!request.DryRun)
        {
            await serializer.SaveDocumentAsync(
                output,
                request.OutputFile,
                cancellationToken);
        }

        return new OptimizeRulesResult(
            output,
            result,
            request.OutputFile,
            !request.DryRun);
    }

    private async Task<PreparedRules> OptimizeIfRequestedAsync(
        RuleDocument document,
        string outputFile,
        RuleOptimizationMode? optimizationMode,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (optimizationMode is null)
        {
            return new PreparedRules(document, "rules document", null);
        }

        RuleOptimizationResult result = optimizer.Optimize(
            document.Rules,
            optimizationMode.Value);
        RuleDocument optimized = document with
        {
            Rules = result.Rules.ToList()
        };
        if (!dryRun)
        {
            await serializer.SaveDocumentAsync(
                optimized,
                outputFile,
                cancellationToken);
        }

        return new PreparedRules(
            optimized,
            dryRun ? "rules document (optimized in memory)" : outputFile,
            result);
    }

    private sealed record PreparedRules(
        RuleDocument Document,
        string SourceDescription,
        RuleOptimizationResult? Optimization);
}
