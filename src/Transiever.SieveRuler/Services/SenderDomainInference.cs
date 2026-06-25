using Transiever.SieveRuler.Models;

namespace Transiever.SieveRuler.Services;

internal static class SenderDomainInference
{
    private const int BalancedDomainGroupSize = 3;
    private const int AggressiveDomainGroupSize = 2;

    private static readonly HashSet<string> RoleLocalParts =
    [
        "account",
        "accounting",
        "admin",
        "alert",
        "alerts",
        "automated",
        "billing",
        "bounce",
        "bounces",
        "campaign",
        "campaigns",
        "contact",
        "customer",
        "customerservice",
        "delivery",
        "digest",
        "donotreply",
        "donotrespond",
        "email",
        "hello",
        "help",
        "helpdesk",
        "info",
        "invoice",
        "invoices",
        "mail",
        "mailer",
        "mailerdaemon",
        "marketing",
        "message",
        "messages",
        "news",
        "newsletter",
        "newsletters",
        "noresponse",
        "noreply",
        "notification",
        "notifications",
        "notify",
        "order",
        "orders",
        "payment",
        "payments",
        "postmaster",
        "receipt",
        "receipts",
        "sales",
        "security",
        "service",
        "shipping",
        "statement",
        "statements",
        "subscription",
        "subscriptions",
        "support",
        "system",
        "team",
        "transaction",
        "transactions",
        "update",
        "updates",
        "verification",
        "verify",
        "welcome"
    ];

    private static readonly HashSet<string> CommonMultiLabelPublicSuffixes =
    [
        "ac.uk",
        "co.in",
        "co.jp",
        "co.kr",
        "co.nz",
        "co.uk",
        "co.za",
        "com.au",
        "com.br",
        "com.cn",
        "com.hk",
        "com.mx",
        "com.sg",
        "com.tr",
        "com.tw",
        "edu.au",
        "firm.in",
        "gov.au",
        "gov.uk",
        "me.uk",
        "net.au",
        "net.in",
        "net.uk",
        "org.au",
        "org.in",
        "org.uk"
    ];

    public static SenderDomainInferenceResult Apply(
        IEnumerable<string> sourceValues,
        RuleOptimizationMode mode,
        string targetFolder)
    {
        var values = new HashSet<string>(
            sourceValues,
            StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<RuleOptimizationDiagnostic>();

        if (mode == RuleOptimizationMode.Balanced)
        {
            InferExactDomains(
                values,
                BalancedDomainGroupSize,
                "Balanced",
                targetFolder,
                diagnostics);
        }
        else if (mode == RuleOptimizationMode.Aggressive)
        {
            InferRoleDomains(values, targetFolder, diagnostics);
            InferExactDomains(
                values,
                AggressiveDomainGroupSize,
                "Aggressive",
                targetFolder,
                diagnostics);
            CollapseSubdomains(values, targetFolder, diagnostics);
        }

        return new SenderDomainInferenceResult(values, diagnostics);
    }

    private static void InferExactDomains(
        HashSet<string> values,
        int minimumAddresses,
        string modeName,
        string targetFolder,
        List<RuleOptimizationDiagnostic> diagnostics)
    {
        var addressesByDomain = values
            .Select(value => (Value: value, Domain: ParseExactSenderAddress(value)?.Domain))
            .Where(value => value.Domain is not null)
            .GroupBy(
                value => value.Domain!,
                StringComparer.OrdinalIgnoreCase)
            .Where(domainGroup => domainGroup.Count() >= minimumAddresses)
            .ToArray();

        foreach (var domainGroup in addressesByDomain)
        {
            var addresses = domainGroup
                .Select(value => value.Value)
                .ToArray();
            var domainPattern = $"@{domainGroup.Key}";

            ReplaceValues(values, addresses, domainPattern);
            diagnostics.Add(new RuleOptimizationDiagnostic
            {
                Severity = "Warning",
                Action = "InferredSenderDomain",
                Message =
                    $"Replaced {addresses.Length} sender addresses with '{domainPattern}' for '{targetFolder}'.",
                Detail = $"{modeName} optimization broadens the rule to every sender at this exact domain."
            });
        }
    }

    private static void InferRoleDomains(
        HashSet<string> values,
        string targetFolder,
        List<RuleOptimizationDiagnostic> diagnostics)
    {
        var roleAddresses = values
            .Select(value => (Value: value, Address: ParseExactSenderAddress(value)))
            .Where(value => value.Address is not null
                && IsRoleLocalPart(value.Address.Value.LocalPart))
            .ToArray();

        foreach (var roleAddress in roleAddresses)
        {
            var domainPattern = $"@{roleAddress.Address!.Value.Domain}";

            ReplaceValues(values, [roleAddress.Value], domainPattern);
            diagnostics.Add(new RuleOptimizationDiagnostic
            {
                Severity = "Warning",
                Action = "InferredRoleSenderDomain",
                Message =
                    $"Replaced role sender '{roleAddress.Value}' with '{domainPattern}' for '{targetFolder}'.",
                Detail = "Aggressive optimization treats common automated and organizational local parts as domain-wide senders."
            });
        }
    }

    private static void CollapseSubdomains(
        HashSet<string> values,
        string targetFolder,
        List<RuleOptimizationDiagnostic> diagnostics)
    {
        while (TryFindSubdomainCollapse(values, out var collapse))
        {
            ReplaceValues(values, collapse.Values, collapse.ParentDomain);
            diagnostics.Add(new RuleOptimizationDiagnostic
            {
                Severity = "Warning",
                Action = "CollapsedSenderSubdomains",
                Message =
                    $"Collapsed {collapse.Domains.Count} sender domains to parent pattern '{collapse.ParentDomain}' for '{targetFolder}'.",
                Detail =
                    $"Aggressive optimization broadened these domains: {string.Join(", ", collapse.Domains)}."
            });
        }
    }

    private static bool TryFindSubdomainCollapse(
        IReadOnlyCollection<string> values,
        out SubdomainCollapse collapse)
    {
        var domainValues = values
            .Select(value => (Value: value, Domain: GetSenderDomain(value)))
            .Where(value => value.Domain is not null)
            .Select(value => (value.Value, Domain: value.Domain!))
            .ToArray();

        var candidate = domainValues
            .SelectMany(domainValue => GetParentDomains(domainValue.Domain))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(parent => new
            {
                Parent = parent,
                Matches = domainValues
                    .Where(value => IsDomainOrSubdomain(value.Domain, parent))
                    .ToArray()
            })
            .Where(value => value.Matches
                .Select(match => match.Domain)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() >= 2)
            .OrderBy(value => value.Parent.Count(character => character == '.'))
            .ThenBy(value => value.Parent, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (candidate is null)
        {
            collapse = default;
            return false;
        }

        collapse = new SubdomainCollapse(
            candidate.Parent,
            candidate.Matches.Select(match => match.Value).ToArray(),
            candidate.Matches
                .Select(match => match.Domain)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(domain => domain, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        return true;
    }

    private static IEnumerable<string> GetParentDomains(string domain)
    {
        var labels = domain.Split('.');

        for (var index = 1; index < labels.Length - 1; index++)
        {
            var candidate = string.Join('.', labels[index..]);

            if (!IsPublicSuffix(candidate))
                yield return candidate;
        }
    }

    private static bool IsDomainOrSubdomain(string domain, string parent)
    {
        return domain.Equals(parent, StringComparison.OrdinalIgnoreCase)
            || domain.EndsWith($".{parent}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPublicSuffix(string domain)
    {
        return !domain.Contains('.')
            || CommonMultiLabelPublicSuffixes.Contains(domain);
    }

    private static (string LocalPart, string Domain)? ParseExactSenderAddress(string value)
    {
        var separator = value.IndexOf('@');

        if (separator <= 0
            || separator != value.LastIndexOf('@')
            || separator == value.Length - 1)
        {
            return null;
        }

        var localPart = value[..separator];
        var domain = value[(separator + 1)..];

        if (localPart.Any(char.IsWhiteSpace)
            || domain.Any(char.IsWhiteSpace)
            || Uri.CheckHostName(domain) == UriHostNameType.Unknown)
        {
            return null;
        }

        return (localPart, domain.ToLowerInvariant());
    }

    private static string? GetSenderDomain(string value)
    {
        if (value.StartsWith('@'))
        {
            var domain = value[1..];

            return Uri.CheckHostName(domain) == UriHostNameType.Unknown
                ? null
                : domain.ToLowerInvariant();
        }

        return ParseExactSenderAddress(value)?.Domain;
    }

    private static bool IsRoleLocalPart(string localPart)
    {
        var untaggedLocalPart = localPart.Split('+', 2)[0];
        var normalizedLocalPart = new string(
            untaggedLocalPart
                .Where(character => character is not '.' and not '-' and not '_')
                .Select(char.ToLowerInvariant)
                .ToArray());

        return RoleLocalParts.Contains(normalizedLocalPart);
    }

    private static void ReplaceValues(
        HashSet<string> values,
        IEnumerable<string> valuesToRemove,
        string replacement)
    {
        foreach (var value in valuesToRemove)
            values.Remove(value);

        values.Add(replacement);
    }

    private readonly record struct SubdomainCollapse(
        string ParentDomain,
        IReadOnlyCollection<string> Values,
        IReadOnlyCollection<string> Domains);
}

internal sealed record SenderDomainInferenceResult(
    IReadOnlyCollection<string> Values,
    IReadOnlyCollection<RuleOptimizationDiagnostic> Diagnostics);
