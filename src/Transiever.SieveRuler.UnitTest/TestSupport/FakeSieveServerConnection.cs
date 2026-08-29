using System.Security.Cryptography;
using Transiever.ManageSieve;
using Transiever.SieveRuler.Services;

namespace Transiever.SieveRuler.UnitTest;

internal enum FakeSieveOperationKind
{
    ReadState,
    CheckScript,
    HaveSpace,
    GetScript,
    PutScript,
    Activate,
    DeleteScript
}

internal sealed record FakeSieveOperation(
    FakeSieveOperationKind Kind,
    string? ScriptName = null,
    byte[]? Content = null)
{
    public bool IsMutation => Kind is
        FakeSieveOperationKind.PutScript or
        FakeSieveOperationKind.Activate or
        FakeSieveOperationKind.DeleteScript;
}

internal sealed class FakeSieveServerConnectionFactory(FakeSieveServerConnection connection)
    : ISieveServerConnectionFactory
{
    public Task<ISieveServerConnection> ConnectAsync(
        SieveServerConfiguration configuration,
        CancellationToken cancellationToken) =>
        Task.FromResult<ISieveServerConnection>(connection);
}

internal sealed class FakeSieveServerConnection : ISieveServerConnection
{
    private readonly Dictionary<string, byte[]> scripts;
    private readonly Queue<bool> haveSpaceResponses = [];
    private readonly List<string> deletedScripts = [];
    private readonly List<FakeSieveOperation> operations = [];
    private readonly List<Failure> failures = [];
    private readonly List<ContentOverride> storedContentOnPut = [];
    private (int Occurrence, string ScriptName)? activeScriptBeforeRead;

    private FakeSieveServerConnection(string activeScriptName, Dictionary<string, byte[]> scripts)
    {
        ActiveScriptName = activeScriptName;
        this.scripts = scripts;
    }

    public string ActiveScriptName { get; private set; }
    public int HaveSpaceCalls { get; private set; }
    public IReadOnlyList<string> DeletedScripts => deletedScripts;
    public IReadOnlyList<FakeSieveOperation> Operations => operations;

    public static FakeSieveServerConnection Empty() => new("", []);

    public static FakeSieveServerConnection WithScripts(
        string activeScriptName,
        params (string Name, byte[] Content)[] scripts) =>
        new(activeScriptName, scripts.ToDictionary(
            script => script.Name,
            script => script.Content.ToArray(),
            StringComparer.Ordinal));

    public FakeSieveServerConnection WithHaveSpaceResponses(params bool[] responses)
    {
        foreach (bool response in responses)
            haveSpaceResponses.Enqueue(response);
        return this;
    }

    public FakeSieveServerConnection WithFailure(FakeSieveOperationKind kind, int occurrence, Exception exception)
    {
        failures.Add(new(kind, occurrence, exception));
        return this;
    }

    public FakeSieveServerConnection WithStoredContentOnPut(string scriptName, int occurrence, byte[] content)
    {
        storedContentOnPut.Add(new(scriptName, occurrence, content.ToArray()));
        return this;
    }

    public FakeSieveServerConnection WithActiveScriptBeforeRead(int occurrence, string activeScriptName)
    {
        activeScriptBeforeRead = (occurrence, activeScriptName);
        return this;
    }

    public bool ContainsScript(string scriptName) => scripts.ContainsKey(scriptName);
    public byte[] GetContent(string scriptName) => scripts[scriptName].ToArray();

    public Task<RemoteSieveState> ReadStateAsync(CancellationToken cancellationToken)
    {
        Record(FakeSieveOperationKind.ReadState);
        if (activeScriptBeforeRead is { } before && Count(FakeSieveOperationKind.ReadState) == before.Occurrence)
            ActiveScriptName = before.ScriptName;
        ThrowIfConfigured(FakeSieveOperationKind.ReadState);
        byte[] activeContent = ActiveScriptName.Length == 0 ? [] : scripts[ActiveScriptName];
        return Task.FromResult(new RemoteSieveState
        {
            ActiveScriptName = ActiveScriptName,
            ActiveContent = activeContent.ToArray(),
            ActiveContentSha256 = Convert.ToHexString(SHA256.HashData(activeContent)),
            Scripts = scripts.Keys.Select(name => new ManageSieveScriptInfo(
                name, name.Equals(ActiveScriptName, StringComparison.Ordinal))).ToArray(),
            Capabilities = new ManageSieveCapabilities
            {
                SieveExtensions = new HashSet<string>(["fileinto"], StringComparer.OrdinalIgnoreCase)
            }
        });
    }

    public Task CheckScriptAsync(ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        Record(FakeSieveOperationKind.CheckScript, content: content.ToArray());
        ThrowIfConfigured(FakeSieveOperationKind.CheckScript);
        return Task.CompletedTask;
    }

    public Task<bool> HaveSpaceAsync(string scriptName, long contentLength, CancellationToken cancellationToken)
    {
        HaveSpaceCalls++;
        Record(FakeSieveOperationKind.HaveSpace, scriptName);
        ThrowIfConfigured(FakeSieveOperationKind.HaveSpace, scriptName);
        return Task.FromResult(haveSpaceResponses.Count == 0 || haveSpaceResponses.Dequeue());
    }

    public Task<byte[]> GetScriptAsync(string scriptName, CancellationToken cancellationToken)
    {
        Record(FakeSieveOperationKind.GetScript, scriptName);
        ThrowIfConfigured(FakeSieveOperationKind.GetScript, scriptName);
        return Task.FromResult(GetContent(scriptName));
    }

    public Task PutScriptAsync(string scriptName, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        byte[] recorded = content.ToArray();
        Record(FakeSieveOperationKind.PutScript, scriptName, recorded);
        ThrowIfConfigured(FakeSieveOperationKind.PutScript, scriptName);
        ContentOverride? stored = Find(storedContentOnPut, scriptName, FakeSieveOperationKind.PutScript);
        scripts[scriptName] = (stored?.Content ?? recorded).ToArray();
        return Task.CompletedTask;
    }

    public Task ActivateAsync(string? scriptName, CancellationToken cancellationToken)
    {
        Record(FakeSieveOperationKind.Activate, scriptName);
        ThrowIfConfigured(FakeSieveOperationKind.Activate, scriptName);
        ActiveScriptName = scriptName ?? "";
        return Task.CompletedTask;
    }

    public Task DeleteScriptAsync(string scriptName, CancellationToken cancellationToken)
    {
        Record(FakeSieveOperationKind.DeleteScript, scriptName);
        ThrowIfConfigured(FakeSieveOperationKind.DeleteScript, scriptName);
        scripts.Remove(scriptName);
        deletedScripts.Add(scriptName);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void Record(FakeSieveOperationKind kind, string? scriptName = null, byte[]? content = null) =>
        operations.Add(new(kind, scriptName, content?.ToArray()));

    private int Count(FakeSieveOperationKind kind) => operations.Count(operation => operation.Kind == kind);

    private void ThrowIfConfigured(FakeSieveOperationKind kind, string? scriptName = null)
    {
        Failure? failure = failures.FirstOrDefault(failure => !failure.Fired && failure.Kind == kind &&
            Count(kind) == failure.Occurrence);
        if (failure is null)
            return;
        failure.Fired = true;
        throw failure.Exception;
    }

    private int Count(FakeSieveOperationKind kind, string? scriptName) =>
        operations.Count(operation => operation.Kind == kind && operation.ScriptName == scriptName);

    private ContentOverride? Find(List<ContentOverride> overrides, string scriptName, FakeSieveOperationKind kind)
    {
        ContentOverride? result = overrides.FirstOrDefault(item => !item.Used && item.ScriptName == scriptName &&
            Count(kind, scriptName) == item.Occurrence);
        if (result is not null)
            result.Used = true;
        return result;
    }

    private sealed record Failure(FakeSieveOperationKind Kind, int Occurrence, Exception Exception)
    {
        public bool Fired { get; set; }
    }

    private sealed record ContentOverride(string ScriptName, int Occurrence, byte[] Content)
    {
        public bool Used { get; set; }
    }
}
