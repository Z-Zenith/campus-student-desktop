using System.Text.Json;
using StudentDesktop.Services;

namespace StudentDesktop.Tests;

public class SekBridgeTests
{
    private static (SekBridge Bridge, Func<string?> LastScript) NewBridge()
    {
        string? lastScript = null;
        var bridge = new SekBridge(new ApiClient("http://localhost:0"))
        {
            InvokeScript = script =>
            {
                lastScript = script;
                return Task.CompletedTask;
            },
        };
        return (bridge, () => lastScript);
    }

    // window.__sekHostMount/__sekHostReceive both take a JSON *string* argument (see
    // SekBridge's InvokeScript calls) — the script is
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

    // SDA-19: no reachable server, so every request must fail closed with a mapped
    // SekError rather than throwing out of HandleMessageAsync and crashing the WebView
    // message pump.
    [Fact]
    public async Task Sda19_HandleMessage_Save_WithNoReachableServer_RespondsWithNetworkError()
    {
        var (bridge, lastScript) = NewBridge();
        var payload = JsonSerializer.Serialize(new
        {
            requestId = "save-1",
            method = "save",
            payload = new
            {
                note = new { id = Guid.NewGuid(), ownerId = Guid.NewGuid(), title = "T", contentMarkdown = "x", createdAt = DateTime.UtcNow, updatedAt = DateTime.UtcNow },
                links = Array.Empty<object>(),
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
    public async Task Sda19_HandleMessage_UnknownMethod_RespondsWithValidationError()
    {
        var (bridge, lastScript) = NewBridge();
        var payload = JsonSerializer.Serialize(new { requestId = "req-1", method = "bogus", payload = new { } });

        await bridge.HandleMessageAsync(payload);

        var script = lastScript();
        Assert.NotNull(script);
        Assert.Contains("req-1", script);
        Assert.Contains("validation_error", script);
    }

    [Fact]
    public async Task Sda19_HandleMessage_Delete_WithNoReachableServer_RespondsWithNetworkError()
    {
        var (bridge, lastScript) = NewBridge();
        var payload = JsonSerializer.Serialize(new { requestId = "del-1", method = "delete", payload = new { noteId = Guid.NewGuid() } });

        await bridge.HandleMessageAsync(payload);

        var script = lastScript();
        Assert.NotNull(script);
        var response = ExtractPayload(script, "__sekHostReceive");
        Assert.Contains("del-1", response);
        Assert.Contains("\"ok\":false", response);
    }

    [Fact]
    public async Task Sda19_HandleMessage_ResolveLink_WithNoReachableServer_RespondsWithNetworkError()
    {
        var (bridge, lastScript) = NewBridge();
        var payload = JsonSerializer.Serialize(new { requestId = "resolve-1", method = "resolveLink", payload = new { toNoteId = Guid.NewGuid() } });

        await bridge.HandleMessageAsync(payload);

        var script = lastScript();
        Assert.NotNull(script);
        var response = ExtractPayload(script, "__sekHostReceive");
        Assert.Contains("resolve-1", response);
        Assert.Contains("\"ok\":false", response);
    }

    [Fact]
    public async Task Sda19_MountNotesEditor_WithNoInvokeScriptWired_DoesNotThrow()
    {
        var bridge = new SekBridge(new ApiClient("http://localhost:0"));

        var exception = await Record.ExceptionAsync(() => bridge.MountNotesEditorAsync(Guid.NewGuid(), currentNote: null, canEdit: true));

        Assert.Null(exception);
    }

    [Fact]
    public async Task Sda19_MountNotesEditor_InvokesHostMountWithUserAndNoteContext()
    {
        var (bridge, lastScript) = NewBridge();
        var userId = Guid.NewGuid();

        await bridge.MountNotesEditorAsync(userId, currentNote: null, canEdit: true);

        var script = lastScript();
        Assert.NotNull(script);
        Assert.Contains("__sekHostMount", script);
        Assert.Contains(userId.ToString(), script);
    }
}
