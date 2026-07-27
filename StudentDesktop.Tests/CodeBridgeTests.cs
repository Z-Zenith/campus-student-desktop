using System.Text.Json;
using StudentDesktop.Services;

namespace StudentDesktop.Tests;

public class CodeBridgeTests
{
    private static (CodeBridge Bridge, Func<string?> LastScript) NewBridge()
    {
        string? lastScript = null;
        var bridge = new CodeBridge(new ApiClient("http://localhost:0"))
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
        var (bridge, lastScript) = NewBridge();
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

    [Fact]
    public async Task Sek01_HandleMessage_Save_WithNoReachableServer_RespondsWithNetworkError()
    {
        var (bridge, lastScript) = NewBridge();
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
    public async Task Sek01_HandleMessage_UnknownMethod_RespondsWithValidationError()
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
    public async Task Sek01_Mount_WithNoInvokeScriptWired_DoesNotThrow()
    {
        var bridge = new CodeBridge(new ApiClient("http://localhost:0"));

        var exception = await Record.ExceptionAsync(
            () => bridge.MountAsync(Guid.NewGuid(), currentProject: null, canRun: true, canEdit: true));

        Assert.Null(exception);
    }

    [Fact]
    public async Task Sek01_Mount_InvokesHostMountWithUserAndProjectContext()
    {
        var (bridge, lastScript) = NewBridge();
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
        var (bridge, lastScript) = NewBridge();

        await bridge.MountAsync(Guid.NewGuid(), currentProject: null, canRun: true, canEdit: true);

        var script = lastScript();
        Assert.NotNull(script);
        var prefix = "window.__sekHostMount(";
        var start = script.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        Assert.StartsWith("\"", script[start..]);
    }
}
