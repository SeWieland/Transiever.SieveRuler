using Transiever.SieveRuler.Application;
using Transiever.SieveRuler.Services;
using Transiever.Common.Accounts;

namespace Transiever.SieveRuler.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("account", StringComparison.OrdinalIgnoreCase))
            return RunAccount(args[1..]);

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

    private static int RunAccount(string[] args)
    {
        try
        {
            AccountCommandOptions options = AccountCommandOptions.Parse(args);
            return new AccountCommandApplication(
                () => new JsonAccountProfileStore(), CredentialStores.CreateDefault,
                Environment.GetEnvironmentVariable, new ConsoleCliInput(), Console.Out).Run(options);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
