using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using StudentDesktop.Models;
using StudentDesktop.Services;

namespace StudentDesktop.Tests;

// Work Item C (SDA/SEK plan): CodeBridge's 'save' path now writes to ILocalStore instead
// of ApiClient — a fake in-memory implementation, same fakeable-by-interface reasoning
// ILocalStore's own doc comment gives for why CodeBridge doesn't depend on
// LocalStoreContext directly.
public sealed class FakeLocalStore : ILocalStore
{
    private readonly Dictionary<Guid, CodeProjectDto> _projects = [];
    public Exception? FailWith { get; set; }

    public Task<IReadOnlyList<CodeProjectSummaryDto>> ListCodeProjectsAsync()
    {
        if (FailWith is not null) throw FailWith;
        return Task.FromResult<IReadOnlyList<CodeProjectSummaryDto>>(
            _projects.Values.Select(p => new CodeProjectSummaryDto(p.Id, p.Name, p.UpdatedAt)).ToList());
    }

    public Task<CodeProjectDto?> GetCodeProjectAsync(Guid id)
    {
        if (FailWith is not null) throw FailWith;
        return Task.FromResult(_projects.GetValueOrDefault(id));
    }

    public Task<CodeProjectDto> SaveCodeProjectAsync(CodeProjectDto project)
    {
        if (FailWith is not null) throw FailWith;
        _projects[project.Id] = project;
        return Task.FromResult(project);
    }

    public Task DeleteCodeProjectAsync(Guid id)
    {
        if (FailWith is not null) throw FailWith;
        _projects.Remove(id);
        return Task.CompletedTask;
    }
}

public class CodeBridgeTests
{
    private static (CodeBridge Bridge, Func<string?> LastScript, FakeLocalStore LocalStore) NewBridge()
    {
        string? lastScript = null;
        var localStore = new FakeLocalStore();
        var bridge = new CodeBridge(new ApiClient("http://localhost:0"), localStore)
        {
            InvokeScript = script =>
            {
                lastScript = script;
                return Task.CompletedTask;
            },
        };
        return (bridge, () => lastScript, localStore);
    }

    // window.__sekHostMount/__sekHostReceive both take a JSON *string* argument (see
    // CodeBridge's InvokeScript calls) — the script is
    // "window.<fn>(<JSON-encoded-string-literal>)", so unwrap that outer string encoding
    // to get back the actual JSON payload the JS side would JSON.parse.
    private static string ExtractPayload(string script, string functionName)
    {
        var prefix = $"window.{functionName}(";
        var start = script.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        var end = script.LastIndexOf(')');
        var stringLiteral = script[start..end];
        return JsonSerializer.Deserialize<string>(stringLiteral)!;
    }

    // SEK-01: no reachable server, so every request must fail closed with a mapped
    // SekError rather than throwing out of HandleMessageAsync and crashing the WebView
    // message pump — same requirement SekBridgeTests enforces for Notes.
    [Fact]
    public async Task Sek01_HandleMessage_Run_WithNoReachableServer_RespondsWithNetworkError()
    {
        var (bridge, lastScript, _) = NewBridge();
        var payload = JsonSerializer.Serialize(new
        {
            requestId = "run-1",
            method = "run",
            payload = new
            {
                project = new
                {
                    id = Guid.NewGuid(),
                    name = "proj",
                    files = new[] { new { path = "main.py", language = "python", content = "print(1)" } },
                    entryFilePath = "main.py",
                    activeFilePath = "main.py",
                    stdin = (string?)null,
                },
            },
        });

        await bridge.HandleMessageAsync(payload);

        var script = lastScript();
        Assert.NotNull(script);
        var response = ExtractPayload(script, "__sekHostReceive");
        Assert.Contains("run-1", response);
        Assert.Contains("\"ok\":false", response);
        Assert.Contains("network_error", response);
    }

    // Work Item C: 'save' now writes to the local encrypted scratch store, not the
    // backend — an unreachable server no longer matters for this path, so the old
    // no-server-fails-closed premise doesn't apply here anymore (see the
    // LocalStoreUnavailable test below for save's actual failure mode).
    [Fact]
    public async Task Sek01_HandleMessage_Save_PersistsToLocalStore_AndRespondsOk()
    {
        var (bridge, lastScript, localStore) = NewBridge();
        var projectId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            requestId = "save-1",
            method = "save",
            payload = new
            {
                project = new
                {
                    id = projectId,
                    name = "proj",
                    files = new[] { new { path = "main.py", language = "python", content = "print(1)" } },
                    entryFilePath = "main.py",
                    activeFilePath = "main.py",
                    stdin = (string?)null,
                },
            },
        });

        await bridge.HandleMessageAsync(payload);

        var script = lastScript();
        Assert.NotNull(script);
        var response = ExtractPayload(script, "__sekHostReceive");
        Assert.Contains("save-1", response);
        Assert.Contains("\"ok\":true", response);
        var saved = await localStore.GetCodeProjectAsync(projectId);
        Assert.NotNull(saved);
        Assert.Equal("proj", saved!.Name);
    }

    [Fact]
    public async Task Sek01_HandleMessage_Save_WithLocalStoreUnavailable_RespondsWithNetworkError()
    {
        var (bridge, lastScript, localStore) = NewBridge();
        localStore.FailWith = new LocalStoreUnavailableException("Local scratch storage is unavailable.");
        var payload = JsonSerializer.Serialize(new
        {
            requestId = "save-1",
            method = "save",
            payload = new
            {
                project = new
                {
                    id = Guid.NewGuid(),
                    name = "proj",
                    files = new[] { new { path = "main.py", language = "python", content = "print(1)" } },
                    entryFilePath = "main.py",
                    activeFilePath = "main.py",
                    stdin = (string?)null,
                },
            },
        });

        await bridge.HandleMessageAsync(payload);

        var script = lastScript();
        Assert.NotNull(script);
        var response = ExtractPayload(script, "__sekHostReceive");
        Assert.Contains("save-1", response);
        Assert.Contains("\"ok\":false", response);
        Assert.Contains("network_error", response);
    }

    [Fact]
    public async Task Sek01_HandleMessage_TerminalStart_WithNoReachableServer_RespondsWithNetworkError()
    {
        var (bridge, lastScript, _) = NewBridge();
        var payload = JsonSerializer.Serialize(new
        {
            requestId = "term-start-1",
            method = "terminalStart",
            payload = new
            {
                files = new[] { new { path = "main.py", language = "python", content = "print(1)" } },
            },
        });

        await bridge.HandleMessageAsync(payload);

        var script = lastScript();
        Assert.NotNull(script);
        var response = ExtractPayload(script, "__sekHostReceive");
        Assert.Contains("term-start-1", response);
        Assert.Contains("\"ok\":false", response);
        Assert.Contains("network_error", response);
    }

    [Fact]
    public async Task Sek01_HandleMessage_TerminalExec_WithNoReachableServer_RespondsWithNetworkError()
    {
        var (bridge, lastScript, _) = NewBridge();
        var payload = JsonSerializer.Serialize(new
        {
            requestId = "term-exec-1",
            method = "terminalExec",
            payload = new { sessionId = Guid.NewGuid(), command = "ls" },
        });

        await bridge.HandleMessageAsync(payload);

        var script = lastScript();
        Assert.NotNull(script);
        var response = ExtractPayload(script, "__sekHostReceive");
        Assert.Contains("term-exec-1", response);
        Assert.Contains("\"ok\":false", response);
        Assert.Contains("network_error", response);
    }

    // Distinct from the run/save no-server tests: 'close' is fire-and-forget cleanup
    // (called from TerminalPanel's unmount/restart, not user-awaited), so it must
    // respond ok even when the server is unreachable rather than surfacing an error
    // nobody's listening for.
    [Fact]
    public async Task Sek01_HandleMessage_TerminalClose_WithNoReachableServer_StillRespondsOk()
    {
        var (bridge, lastScript, _) = NewBridge();
        var payload = JsonSerializer.Serialize(new
        {
            requestId = "term-close-1",
            method = "terminalClose",
            payload = new { sessionId = Guid.NewGuid() },
        });

        await bridge.HandleMessageAsync(payload);

        var script = lastScript();
        Assert.NotNull(script);
        var response = ExtractPayload(script, "__sekHostReceive");
        Assert.Contains("term-close-1", response);
        Assert.Contains("\"ok\":true", response);
    }

    [Fact]
    public async Task Sek01_HandleMessage_UnknownMethod_RespondsWithValidationError()
    {
        var (bridge, lastScript, _) = NewBridge();
        var payload = JsonSerializer.Serialize(new { requestId = "req-1", method = "bogus", payload = new { } });

        await bridge.HandleMessageAsync(payload);

        var script = lastScript();
        Assert.NotNull(script);
        Assert.Contains("req-1", script);
        Assert.Contains("validation_error", script);
    }

    [Fact]
    public async Task Sek01_Mount_WithNoInvokeScriptWired_DoesNotThrow()
    {
        var bridge = new CodeBridge(new ApiClient("http://localhost:0"), new FakeLocalStore());

        var exception = await Record.ExceptionAsync(
            () => bridge.MountAsync(Guid.NewGuid(), currentProject: null, canRun: true, canEdit: true));

        Assert.Null(exception);
    }

    [Fact]
    public async Task Sek01_Mount_InvokesHostMountWithUserAndProjectContext()
    {
        var (bridge, lastScript, _) = NewBridge();
        var userId = Guid.NewGuid();

        await bridge.MountAsync(userId, currentProject: null, canRun: true, canEdit: true);

        var script = lastScript();
        Assert.NotNull(script);
        Assert.Contains("__sekHostMount", script);
        var payload = ExtractPayload(script, "__sekHostMount");
        Assert.Contains(userId.ToString(), payload);
    }

    // Regression test for the bug this fix addresses: __sekHostMount(json: string) does
    // JSON.parse(json), so the argument MUST be a quoted/escaped JS string literal, not a
    // bare object literal. A bare object literal here would make JSON.parse coerce it to
    // "[object Object]" and throw inside the WebView — silently, since a page-side
    // script exception never surfaces as a .NET exception — leaving a permanently blank
    // editor pane with no error anywhere. ExtractPayload's JsonSerializer.Deserialize<string>
    // call would itself throw if the argument weren't a valid JSON string, so a passing
    // Sek01_Mount_InvokesHostMountWithUserAndProjectContext already proves this, but this
    // test asserts it explicitly so the intent survives even if that test's shape changes.
    [Fact]
    public async Task Sek01_Mount_ArgumentIsAQuotedJsonStringNotABareObjectLiteral()
    {
        var (bridge, lastScript, _) = NewBridge();

        await bridge.MountAsync(Guid.NewGuid(), currentProject: null, canRun: true, canEdit: true);

        var script = lastScript();
        Assert.NotNull(script);
        var prefix = "window.__sekHostMount(";
        var start = script.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        Assert.StartsWith("\"", script[start..]);
    }
}
