using System.Text.Json;
using StudentDesktop.Services;

namespace StudentDesktop.Tests;

public class DmsBridgeTests
{
    private static (DmsBridge Bridge, Func<string?> LastScript) NewBridge()
    {
        string? lastScript = null;
        var bridge = new DmsBridge(new ApiClient("http://localhost:0"))
        {
            InvokeScript = script =>
            {
                lastScript = script;
                return Task.CompletedTask;
            },
        };
        return (bridge, () => lastScript);
    }

    // window.__dmsHostMount/__dmsHostReceive both take a JSON *string* argument (see
    // DmsBridge's InvokeScript calls) — the script is
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

    // SDA-24: no reachable server, so every request must fail closed with a mapped
    // DmsError rather than throwing out of HandleMessageAsync and crashing the WebView
    // message pump.
    [Fact]
    public async Task Sda24_HandleMessage_ListThreads_WithNoReachableServer_RespondsWithNetworkError()
    {
        var (bridge, lastScript) = NewBridge();
        var payload = JsonSerializer.Serialize(new { requestId = "list-1", method = "listThreads", payload = new { } });

        await bridge.HandleMessageAsync(payload);

        var script = lastScript();
        Assert.NotNull(script);
        var response = ExtractPayload(script, "__dmsHostReceive");
        Assert.Contains("list-1", response);
        Assert.Contains("\"ok\":false", response);
        Assert.Contains("network_error", response);
    }

    [Fact]
    public async Task Sda24_HandleMessage_ListMessages_WithNoReachableServer_RespondsWithNetworkError()
    {
        var (bridge, lastScript) = NewBridge();
        var payload = JsonSerializer.Serialize(new { requestId = "msgs-1", method = "listMessages", payload = new { threadId = Guid.NewGuid() } });

        await bridge.HandleMessageAsync(payload);

        var script = lastScript();
        Assert.NotNull(script);
        var response = ExtractPayload(script, "__dmsHostReceive");
        Assert.Contains("msgs-1", response);
        Assert.Contains("\"ok\":false", response);
    }

    [Fact]
    public async Task Sda24_HandleMessage_SendMessage_WithNoReachableServer_RespondsWithNetworkError()
    {
        var (bridge, lastScript) = NewBridge();
        var payload = JsonSerializer.Serialize(new
        {
            requestId = "send-1",
            method = "sendMessage",
            payload = new { threadId = Guid.NewGuid(), content = "hi" },
        });

        await bridge.HandleMessageAsync(payload);

        var script = lastScript();
        Assert.NotNull(script);
        var response = ExtractPayload(script, "__dmsHostReceive");
        Assert.Contains("send-1", response);
        Assert.Contains("\"ok\":false", response);
    }

    // #9: a non-object 'payload' value for 'sendMessage' fails payload.Deserialize<T>() with a
    // JsonException — before the fix, this fell outside HandleMessageAsync's catch filter,
    // propagated out of this fire-and-forget call as an unobserved task exception, and
    // InvokeScript (the only reply channel) was never invoked, hanging the JS-side request
    // promise forever. Must fail closed with a mapped error instead.
    [Fact]
    public async Task Sda24_HandleMessage_SendMessage_WithNonObjectPayload_RespondsWithValidationErrorInsteadOfHanging()
    {
        var (bridge, lastScript) = NewBridge();
        var payload = JsonSerializer.Serialize(new { requestId = "send-2", method = "sendMessage", payload = "not-an-object" });

        var exception = await Record.ExceptionAsync(() => bridge.HandleMessageAsync(payload));

        Assert.Null(exception);
        var script = lastScript();
        Assert.NotNull(script);
        var response = ExtractPayload(script, "__dmsHostReceive");
        Assert.Contains("send-2", response);
        Assert.Contains("\"ok\":false", response);
        Assert.Contains("validation_error", response);
    }

    // #9: a non-object 'payload' value fails payload.Deserialize<T>() with a JsonException
    // rather than InvalidOperationException — a distinct failure mode that must be caught too.
    [Fact]
    public async Task Sda24_HandleMessage_ListMessages_WithNonObjectPayload_RespondsWithValidationErrorInsteadOfHanging()
    {
        var (bridge, lastScript) = NewBridge();
        var payload = JsonSerializer.Serialize(new { requestId = "msgs-2", method = "listMessages", payload = "not-an-object" });

        var exception = await Record.ExceptionAsync(() => bridge.HandleMessageAsync(payload));

        Assert.Null(exception);
        var script = lastScript();
        Assert.NotNull(script);
        var response = ExtractPayload(script, "__dmsHostReceive");
        Assert.Contains("msgs-2", response);
        Assert.Contains("\"ok\":false", response);
        Assert.Contains("validation_error", response);
    }

    [Fact]
    public async Task Sda24_HandleMessage_UnknownMethod_RespondsWithValidationError()
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
    public async Task Sda24_MountInbox_WithNoInvokeScriptWired_DoesNotThrow()
    {
        var bridge = new DmsBridge(new ApiClient("http://localhost:0"));

        var exception = await Record.ExceptionAsync(() => bridge.MountInboxAsync(Guid.NewGuid()));

        Assert.Null(exception);
    }

    [Fact]
    public async Task Sda24_MountInbox_InvokesHostMountWithUserContext()
    {
        var (bridge, lastScript) = NewBridge();
        var userId = Guid.NewGuid();

        await bridge.MountInboxAsync(userId);

        var script = lastScript();
        Assert.NotNull(script);
        Assert.Contains("__dmsHostMount", script);
        var payload = ExtractPayload(script, "__dmsHostMount");
        Assert.Contains(userId.ToString(), payload);
        Assert.Contains("\"role\":\"student\"", payload);
    }
}
