using Transiever.SieveRuler.Models;

namespace Transiever.SieveRuler.Services;

public interface IRuleSerializer
{
    Task SaveAsync(
        IEnumerable<RuleDefinition> rules,
        string file,
        string sourceId = "generic",
        CancellationToken cancellationToken = default);

    Task<List<RuleDefinition>> LoadAsync(
        string file,
        CancellationToken cancellationToken = default);

    Task SaveDocumentAsync(
        RuleDocument document,
        string file,
        CancellationToken cancellationToken = default);

    Task SaveDocumentAsync(
        RuleDocument document,
        Stream destination,
        CancellationToken cancellationToken = default);

    Task<RuleDocument> LoadDocumentAsync(
        string file,
        CancellationToken cancellationToken = default);

    Task<RuleDocument> LoadDocumentAsync(
        Stream source,
        CancellationToken cancellationToken = default);
}
