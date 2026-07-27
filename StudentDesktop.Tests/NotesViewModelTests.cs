using System.Text.Json;
using StudentDesktop.Services;
using StudentDesktop.ViewModels;

namespace StudentDesktop.Tests;

public class NotesViewModelTests
{
    // window.__sekHostMount takes a JSON *string* argument (see SekBridge's InvokeScript
    // call) — the script is "window.<fn>(<JSON-encoded-string-literal>)", so unwrap that
    // outer string encoding to get back the actual JSON payload the JS side would
    // JSON.parse.
    private static string ExtractPayload(string script, string functionName)
    {
        var prefix = $"window.{functionName}(";
        var start = script.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        var end = script.LastIndexOf(')');
        var stringLiteral = script[start..end];
        return JsonSerializer.Deserialize<string>(stringLiteral)!;
    }

    // SDA-19: no reachable server, so the initial note-list load must fail closed with an
    // ErrorMessage rather than throwing out of the constructor's fire-and-forget load.
    [Fact]
    public void Sda19_Construction_WithNoReachableServer_DoesNotThrow()
    {
        var exception = Record.Exception(() => new NotesViewModel(new ApiClient("http://localhost:0"), Guid.NewGuid()));

        Assert.Null(exception);
    }

    [Fact]
    public void Sda19_Construction_StartsWithNoSelectedNote()
    {
        var viewModel = new NotesViewModel(new ApiClient("http://localhost:0"), Guid.NewGuid());

        Assert.Null(viewModel.SelectedNote);
        Assert.Empty(viewModel.Notes);
    }

    [Fact]
    public async Task Sda19_NewNote_ClearsSelectionAndMountsABlankEditor()
    {
        var viewModel = new NotesViewModel(new ApiClient("http://localhost:0"), Guid.NewGuid());
        string? lastScript = null;
        viewModel.Bridge.InvokeScript = script =>
        {
            lastScript = script;
            return Task.CompletedTask;
        };
        viewModel.SelectedNote = new Models.NoteSummaryDto(Guid.NewGuid(), "Some note", DateTime.UtcNow);

        await viewModel.NewNoteCommand.ExecuteAsync(null);

        Assert.Null(viewModel.SelectedNote);
        Assert.NotNull(lastScript);
        var payload = ExtractPayload(lastScript, "__sekHostMount");
        Assert.Contains("\"currentNote\":null", payload);
    }
}
