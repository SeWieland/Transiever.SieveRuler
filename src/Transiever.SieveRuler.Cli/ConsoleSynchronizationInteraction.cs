using Transiever.SieveRuler.Application;

namespace Transiever.SieveRuler.Cli;

public sealed class ConsoleSynchronizationInteraction : ISynchronizationInteraction
{
    public bool ResolveAdoption(bool? explicitChoice, int compatibleRuleCount)
    {
        if (compatibleRuleCount == 0)
        {
            return false;
        }

        if (explicitChoice is { } choice)
        {
            return choice;
        }

        if (Console.IsInputRedirected)
        {
            Console.WriteLine(
                $"Preserving {compatibleRuleCount} compatible external rules because input is redirected.");
            return false;
        }

        Console.Write(
            $"Adopt {compatibleRuleCount} compatible server rules into the managed region? [Y/n] ");
        string? answer = Console.ReadLine();
        return string.IsNullOrWhiteSpace(answer) ||
            answer.Equals("y", StringComparison.OrdinalIgnoreCase) ||
            answer.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

}
