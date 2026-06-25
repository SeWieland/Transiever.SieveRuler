using Transiever.SieveRuler.Application;
using Transiever.SieveRuler.Services;

namespace Transiever.SieveRuler.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        CommandLineOptions options;
        try
        {
            options = CommandLineOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            ConsolePresentation.PrintHelp();
            return 1;
        }

        if (options.ShowHelp)
        {
            ConsolePresentation.PrintHelp();
            return 0;
        }

        IRuleSerializer serializer = new JsonRuleSerializer();
        IRuleOptimizer optimizer = new RuleOptimizer();
        ISieveGenerator generator = new SieveGenerator();
        ISieveImporter importer = new SieveImporter();
        IRuleReconciler reconciler = new RuleReconciler(optimizer);
        ISieveScriptComposer composer = new SieveScriptComposer(importer, generator);
        ISynchronizationInteraction interaction =
            new ConsoleSynchronizationInteraction();
        ISieveSynchronizationWorkflow synchronization =
            new SieveSynchronizationWorkflow(
                serializer,
                importer,
                reconciler,
                composer,
                new ManageSieveServerConnectionFactory(),
                interaction);
        var cli = new SieveRulerCliApplication(
            new SieveRulerApplication(serializer, optimizer, generator),
            synchronization,
            new EnvironmentSieveServerConfigurationProvider());

        try
        {
            return await cli.RunAsync(options);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
