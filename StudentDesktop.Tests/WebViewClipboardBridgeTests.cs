using StudentDesktop.Services;

namespace StudentDesktop.Tests;

// #11 (SDA-21): WebViewClipboardBridge.TryHandleAsync is the .NET-side half of the
// WebView clipboard isolation fix — the JS-side half (InjectScript) can't be exercised
// without a live WebView, but the message handling itself is plain, testable C#.
public class WebViewClipboardBridgeTests
{
    private sealed class FakeClipboard : IAppClipboardService
    {
        private string? _text;
        public bool HasText => !string.IsNullOrEmpty(_text);
        public void SetText(string? text) => _text = text;
        public string? GetText() => _text;
        public void Clear() => _text = null;
    }

    [Fact]
    public async Task TryHandle_Copy_StoresTextInAppClipboard_NotForwardedAnywhereElse()
    {
        var clipboard = new FakeClipboard();
        var body = """{"type":"sdaClipboard","action":"copy","text":"hello world"}""";

        var handled = await WebViewClipboardBridge.TryHandleAsync(body, clipboard, invokeScript: null);

        Assert.True(handled);
        Assert.Equal("hello world", clipboard.GetText());
    }

    [Fact]
    public async Task TryHandle_Cut_StoresTextInAppClipboard()
    {
        var clipboard = new FakeClipboard();
        var body = """{"type":"sdaClipboard","action":"cut","text":"cut me"}""";

        var handled = await WebViewClipboardBridge.TryHandleAsync(body, clipboard, invokeScript: null);

        Assert.True(handled);
        Assert.Equal("cut me", clipboard.GetText());
    }

    [Fact]
    public async Task TryHandle_Paste_InvokesScriptWithStoredAppClipboardText_NeverTouchingOsClipboard()
    {
        var clipboard = new FakeClipboard();
        clipboard.SetText("stored text");
        string? invoked = null;

        var handled = await WebViewClipboardBridge.TryHandleAsync(
            """{"type":"sdaClipboard","action":"paste"}""",
            clipboard,
            script =>
            {
                invoked = script;
                return Task.CompletedTask;
            });

        Assert.True(handled);
        Assert.NotNull(invoked);
        Assert.Contains("__sdaClipboardPaste", invoked);
        Assert.Contains("stored text", invoked);
    }

    [Fact]
    public async Task TryHandle_Paste_WithEmptyAppClipboard_InvokesScriptWithEmptyString()
    {
        var clipboard = new FakeClipboard();
        string? invoked = null;

        await WebViewClipboardBridge.TryHandleAsync(
            """{"type":"sdaClipboard","action":"paste"}""",
            clipboard,
            script =>
            {
                invoked = script;
                return Task.CompletedTask;
            });

        Assert.NotNull(invoked);
        Assert.Contains("__sdaClipboardPaste(\"\")", invoked);
    }

    // Unrelated WebView messages (e.g. a SekBridge/DmsBridge {requestId, method, payload}
    // request) must be left alone so the caller forwards them to the real bridge.
    [Fact]
    public async Task TryHandle_UnrelatedMessage_ReturnsFalse_AndDoesNotTouchClipboard()
    {
        var clipboard = new FakeClipboard();
        var body = """{"requestId":"save-1","method":"save","payload":{}}""";

        var handled = await WebViewClipboardBridge.TryHandleAsync(body, clipboard, invokeScript: null);

        Assert.False(handled);
        Assert.False(clipboard.HasText);
    }

    [Fact]
    public async Task TryHandle_MalformedJson_ReturnsFalse_DoesNotThrow()
    {
        var clipboard = new FakeClipboard();

        var handled = await WebViewClipboardBridge.TryHandleAsync("not json at all but mentions sdaClipboard", clipboard, invokeScript: null);

        Assert.False(handled);
    }

    [Fact]
    public async Task TryHandle_NullOrEmptyBody_ReturnsFalse()
    {
        var clipboard = new FakeClipboard();

        Assert.False(await WebViewClipboardBridge.TryHandleAsync(null, clipboard, invokeScript: null));
        Assert.False(await WebViewClipboardBridge.TryHandleAsync("", clipboard, invokeScript: null));
    }
}
