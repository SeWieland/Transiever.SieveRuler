using System.Text;
using Transiever.SieveRuler.Models;
using Transiever.SieveRuler.Services;

namespace Transiever.SieveRuler.UnitTest;

public sealed class BaselineSieveGoldenTests
{
    [Fact]
    public async Task Out001_GeneratesExactSiv001Golden()
    {
        string fixtureDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "SieveV1");
        string inputPath = Path.Combine(fixtureDirectory, "OUT-001.rules.json");
        string goldenPath = Path.Combine(fixtureDirectory, "SIV-001.sieve");

        RuleDocument document = await new JsonRuleSerializer().LoadDocumentAsync(
            inputPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(RuleDocument.SchemaId, document.Schema);
        Assert.Equal(1, document.SchemaVersion);
        Assert.Equal("outlook", document.SourceId);
        RuleDefinition rule = Assert.Single(document.Rules);
        Assert.Equal("Project invoices", rule.Name);

        Assert.Collection(
            rule.Actions,
            action => Assert.Equal(RuleActionType.SetFlags, action.Type),
            action => Assert.Equal(RuleActionType.FileInto, action.Type),
            action => Assert.Equal(RuleActionType.CopyInto, action.Type),
            action => Assert.Equal(RuleActionType.Redirect, action.Type),
            action => Assert.Equal(RuleActionType.Stop, action.Type));
        Assert.Collection(
            rule.Exceptions,
            exception =>
            {
                Assert.Equal(RuleConditionType.BodyContains, exception.Type);
                Assert.Equal(["internal"], exception.Values);
            });

        string actualText = new SieveGenerator().Generate(
            document.Rules,
            "OUT-001.rules.json");
        byte[] actual = Encoding.UTF8.GetBytes(actualText);

        Assert.DoesNotContain((byte)'\r', actual);
        Assert.DoesNotContain(new byte[] { 0xEF, 0xBB, 0xBF }, actual);
        Assert.Equal((byte)'\n', actual[^1]);
        Assert.NotEqual((byte)'\n', actual[^2]);

        Assert.Contains("require [\"body\", \"copy\", \"fileinto\", \"imap4flags\", \"mime\"];", actualText);
        Assert.Contains("header :contains \"Subject\"", actualText);
        Assert.DoesNotContain("header :contains \"subject\"", actualText);
        Assert.Contains("not body :contains \"internal\"", actualText);
        Assert.Contains("addflag \"\\\\Seen\" ;", actualText);
        Assert.Contains("fileinto \"INBOX/Projects\" ;", actualText);
        Assert.Contains("fileinto :copy \"Archive/Projects\" ;", actualText);
        Assert.Contains("redirect \"archive@example.test\" ;", actualText);
        Assert.Contains("stop ;", actualText);
        Assert.True(
            actualText.IndexOf("addflag ", StringComparison.Ordinal) <
            actualText.IndexOf("fileinto \"INBOX/Projects\" ;", StringComparison.Ordinal));
        Assert.True(
            actualText.IndexOf("fileinto \"INBOX/Projects\" ;", StringComparison.Ordinal) <
            actualText.IndexOf("fileinto :copy", StringComparison.Ordinal));
        Assert.True(
            actualText.IndexOf("fileinto :copy", StringComparison.Ordinal) <
            actualText.IndexOf("redirect ", StringComparison.Ordinal));
        Assert.True(
            actualText.IndexOf("redirect ", StringComparison.Ordinal) <
            actualText.IndexOf("stop ;", StringComparison.Ordinal));

        int flagIndex = actualText.IndexOf(
            "## Flag: ",
            StringComparison.Ordinal);
        int ifIndex = actualText.IndexOf(
            "if ",
            flagIndex,
            StringComparison.Ordinal);
        int flagLineEnd = actualText.IndexOf('\n', flagIndex);
        Assert.True(flagIndex >= 0 && ifIndex == flagLineEnd + 1);
        string flagLine = actualText[flagIndex..flagLineEnd];
        Assert.Contains("Rulename: Project invoices", flagLine);
        Assert.DoesNotContain("LastModified", flagLine);
        Assert.DoesNotContain("ModifiedBy", flagLine);

        if (!File.Exists(goldenPath))
        {
            string actualPath = Path.Combine(
                Path.GetTempPath(),
                $"SieveRuler-{Guid.NewGuid():N}-SIV-001.actual.sieve");
            await File.WriteAllBytesAsync(
                actualPath,
                actual,
                TestContext.Current.CancellationToken);
            Assert.Fail($"Missing SIV-001 golden. Actual bytes were written to '{actualPath}'.");
        }

        byte[] expected = await File.ReadAllBytesAsync(
            goldenPath,
            TestContext.Current.CancellationToken);
        Assert.Equal(expected, actual);
    }
}
