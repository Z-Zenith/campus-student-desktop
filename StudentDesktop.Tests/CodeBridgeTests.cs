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
        Assert.Contains("run-1", script);
        Assert.Contains("\"ok\":false", script);
        Assert.Contains("network_error", script);
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
        Assert.Contains("save-1", script);
        Assert.Contains("\"ok\":false", script);
        Assert.Contains("network_error", script);
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
        Assert.Contains(userId.ToString(), script);
    }
}
