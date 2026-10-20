namespace Transiever.SieveRuler.Cli.UnitTest;

[Collection("Environment")]
public sealed class ProgramTests
{
    [Theory]
    [InlineData("--sieve-password", "cli-secret-sentinel")]
    [InlineData("--sieve-password=cli-secret-sentinel")]
    [InlineData("--sieve-password-stdin=cli-secret-sentinel")]
    [InlineData("--sieve-password-stdin", "cli-secret-sentinel")]
    [InlineData("--sieve-password-stdin", "-cli-secret-sentinel")]
    public async Task Main_RejectsUnsafePasswordBeforeWorkflowWithoutReadingOrDisplayingSecrets(params string[] options)
    {
        TextReader originalInput = Console.In;
        TextWriter originalOutput = Console.Out;
        TextWriter originalError = Console.Error;
        using var input = new StringReader("stdin-sentinel\n");
        using var output = new StringWriter();
        using var error = new StringWriter();
        Console.SetIn(input);
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            int code = await Program.Main(["preview", ..options]);

            Assert.Equal(1, code);
            Assert.Contains("cannot be supplied", error.ToString());
            Assert.DoesNotContain("cli-secret-sentinel", error.ToString());
            Assert.DoesNotContain("cli-secret-sentinel", output.ToString());
            Assert.DoesNotContain("ManageSieve password:", output.ToString());
            Assert.Contains("--sieve-password-stdin", output.ToString());
            Assert.Equal("stdin-sentinel", input.ReadLine());
        }
        finally
        {
            Console.SetIn(originalInput);
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task Main_WithoutArguments_ReturnsHelpWithoutExternalAccess()
    {
        Assert.Equal(0, await global::Transiever.SieveRuler.Cli.Program.Main([]));
    }
}
