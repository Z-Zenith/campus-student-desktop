using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace StudentDesktop.Services;

// #11 (SDA-21): extends the app's clipboard isolation (AppClipboardService/
// IAppClipboardService — "app clipboard content must never reach or come from the OS
// clipboard") to WebView-hosted content (Notes/Messages/Browser). MainWindow's existing
// SDA-21 interception only covers Avalonia TextBox routed events
// (CopyingToClipboardEvent/CuttingToClipboardEvent/PastingFromClipboardEvent) — it cannot
// reach the WebView control's own native copy/cut/paste handling (Ctrl+C/V or the built-in
// context menu, handled entirely inside WebView2/WKWebView/WPE before Avalonia's input
// pipeline, or even the WebView control's own .NET event surface, ever sees it).
//
// Instead, this intercepts copy/cut/paste at the DOM level, inside the hosted page itself:
// InjectScript is (re-)run after every navigation and adds capturing document-level
// listeners for the 'copy'/'cut'/'paste' ClipboardEvents. Calling preventDefault() on each
// suppresses the browser engine's own OS-clipboard read/write for that action — true
// whether the action was triggered by a keyboard shortcut or by the native right-click
// context menu, since both dispatch through the same DOM event before the engine acts on
// it. The selected text is then routed over the same postMessage/InvokeScript channel
// SekBridge/DmsBridge already use (window.chrome.webview.postMessage / InvokeScript),
// landing in the exact same AppClipboardService.Instance the TextBox path uses — one
// shared, process-scoped, OS-isolated clipboard for the whole app.
public static class WebViewClipboardBridge
{
    private const string TypeMarker = "sdaClipboard";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // Idempotent (guards on window.__sdaClipboardInstalled) since NavigationCompleted can
    // fire more than once for the same document, and re-adding listeners would double-handle
    // every copy/cut/paste. Deliberately reads the *active field's* value/selection range
    // (rather than only window.getSelection()) so this also works for <input>/<textarea>
    // elements, whose text isn't part of the document selection API the same way.
    public const string InjectScript = """
        (function () {
            if (window.__sdaClipboardInstalled) { return; }
            window.__sdaClipboardInstalled = true;

            function activeField() {
                var el = document.activeElement;
                if (el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA') && typeof el.selectionStart === 'number') {
                    return el;
                }
                return null;
            }

            function selectedText() {
                var field = activeField();
                if (field) {
                    return field.value.substring(field.selectionStart, field.selectionEnd);
                }
                var sel = window.getSelection();
                return sel ? sel.toString() : '';
            }

            function deleteSelection() {
                var field = activeField();
                if (field) {
                    var start = field.selectionStart, end = field.selectionEnd;
                    field.value = field.value.slice(0, start) + field.value.slice(end);
                    field.selectionStart = field.selectionEnd = start;
                    field.dispatchEvent(new Event('input', { bubbles: true }));
                } else {
                    document.execCommand('delete');
                }
            }

            function insertText(text) {
                var field = activeField();
                if (field) {
                    var start = field.selectionStart, end = field.selectionEnd;
                    field.value = field.value.slice(0, start) + text + field.value.slice(end);
                    var caret = start + text.length;
                    field.selectionStart = field.selectionEnd = caret;
                    field.dispatchEvent(new Event('input', { bubbles: true }));
                } else {
                    document.execCommand('insertText', false, text);
                }
            }

            document.addEventListener('copy', function (e) {
                e.preventDefault();
                window.chrome.webview.postMessage(JSON.stringify({ type: 'sdaClipboard', action: 'copy', text: selectedText() }));
            }, true);

            document.addEventListener('cut', function (e) {
                e.preventDefault();
                var text = selectedText();
                deleteSelection();
                window.chrome.webview.postMessage(JSON.stringify({ type: 'sdaClipboard', action: 'cut', text: text }));
            }, true);

            document.addEventListener('paste', function (e) {
                e.preventDefault();
                window.chrome.webview.postMessage(JSON.stringify({ type: 'sdaClipboard', action: 'paste' }));
            }, true);

            window.__sdaClipboardPaste = function (text) {
                insertText(text || '');
            };
        })();
        """;

    /// Call from a WebView's WebMessageReceived handler, before forwarding the message to any
    /// application-level bridge (SekBridge/DmsBridge, etc.). Returns true when the message was
    /// one of this bridge's own copy/cut/paste signals (fully handled — the caller should
    /// stop), or false when it's an unrelated message the caller should keep processing as
    /// normal (e.g. hand off to SekBridge.HandleMessageAsync).
    public static async Task<bool> TryHandleAsync(string? body, IAppClipboardService clipboard, Func<string, Task>? invokeScript)
    {
        if (string.IsNullOrEmpty(body) || !body.Contains(TypeMarker, StringComparison.Ordinal))
        {
            return false;
        }

        ClipboardMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<ClipboardMessage>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (message?.Type != TypeMarker)
        {
            return false;
        }

        switch (message.Action)
        {
            case "copy":
            case "cut":
                if (!string.IsNullOrEmpty(message.Text))
                {
                    clipboard.SetText(message.Text);
                }
                break;
            case "paste":
                if (invokeScript is not null)
                {
                    var text = clipboard.GetText() ?? "";
                    await invokeScript($"window.__sdaClipboardPaste({JsonSerializer.Serialize(text)})");
                }
                break;
        }

        return true;
    }

    private sealed record ClipboardMessage(string? Type, string? Action, string? Text);
}
