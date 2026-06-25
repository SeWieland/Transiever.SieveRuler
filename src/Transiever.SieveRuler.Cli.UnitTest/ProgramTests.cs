namespace Transiever.SieveRuler.Cli.UnitTest;

public sealed class ProgramTests
{
    [Fact]
    public async Task Main_WithoutArguments_ReturnsHelpWithoutExternalAccess()
    {
        Assert.Equal(0, await global::Transiever.SieveRuler.Cli.Program.Main([]));
    }
}
