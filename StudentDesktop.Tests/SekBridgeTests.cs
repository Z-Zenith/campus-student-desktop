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

    // #9: a 'save' payload missing the required 'note' field throws InvalidOperationException
    // from inside SaveAsync — before the fix, that exception fell outside HandleMessageAsync's
    // catch filter, propagated out of this fire-and-forget call as an unobserved task
    // exception, and InvokeScript (the only reply channel) was never invoked, hanging the
    // JS-side request promise forever. Must fail closed with a mapped error instead, same as
    // every other path through this method.
    [Fact]
    public async Task Sda19_HandleMessage_Save_WithMissingNoteField_RespondsWithValidationErrorInsteadOfHanging()
    {
        var (bridge, lastScript) = NewBridge();
        var payload = JsonSerializer.Serialize(new { requestId = "save-2", method = "save", payload = new { } });

        var exception = await Record.ExceptionAsync(() => bridge.HandleMessageAsync(payload));

        Assert.Null(exception);
        var script = lastScript();
        Assert.NotNull(script);
        var response = ExtractPayload(script, "__sekHostReceive");
        Assert.Contains("save-2", response);
        Assert.Contains("\"ok\":false", response);
        Assert.Contains("validation_error", response);
    }

    // #9: a non-object 'payload' value fails payload.Deserialize<T>() with a JsonException
    // rather than InvalidOperationException — a distinct failure mode that must be caught too.
    [Fact]
    public async Task Sda19_HandleMessage_Save_WithNonObjectPayload_RespondsWithValidationErrorInsteadOfHanging()
    {
        var (bridge, lastScript) = NewBridge();
        var payload = JsonSerializer.Serialize(new { requestId = "save-3", method = "save", payload = "not-an-object" });

        var exception = await Record.ExceptionAsync(() => bridge.HandleMessageAsync(payload));

        Assert.Null(exception);
        var script = lastScript();
        Assert.NotNull(script);
        var response = ExtractPayload(script, "__sekHostReceive");
        Assert.Contains("save-3", response);
        Assert.Contains("\"ok\":false", response);
        Assert.Contains("validation_error", response);
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

    // notes-host-entry.tsx posts { type: 'navigateToNote', noteId } as a one-way signal
    // (not the {requestId,...} request/response shape) when a resolved wikilink is
    // clicked in the editor.
    [Fact]
    public async Task Sda19_HandleMessage_NavigateToNote_RaisesEventWithParsedGuid()
    {
        var (bridge, _) = NewBridge();
        var noteId = Guid.NewGuid();
        Guid? raised = null;
        bridge.NavigateToNoteRequested += id => raised = id;

        await bridge.HandleMessageAsync(JsonSerializer.Serialize(new { type = "navigateToNote", noteId }));

        Assert.Equal(noteId, raised);
    }

    // Wikilink targets are free-text Markdown, not validated GUIDs — a malformed
    // [[not-a-guid]] link must not crash the bridge's message pump.
    [Fact]
    public async Task Sda19_HandleMessage_NavigateToNote_WithMalformedNoteId_DoesNotThrowOrRaise()
    {
        var (bridge, _) = NewBridge();
        var raised = false;
        bridge.NavigateToNoteRequested += _ => raised = true;

        var exception = await Record.ExceptionAsync(() =>
            bridge.HandleMessageAsync(JsonSerializer.Serialize(new { type = "navigateToNote", noteId = "not-a-guid" })));

        Assert.Null(exception);
        Assert.False(raised);
    }
}
