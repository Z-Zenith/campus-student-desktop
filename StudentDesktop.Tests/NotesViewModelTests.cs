using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using StudentDesktop.Services;
using StudentDesktop.ViewModels;

namespace StudentDesktop.Tests;

public class NotesViewModelTests
{
    // GetMyNotesAsync's actual endpoint/shape isn't the point of these tests (that's
    // ApiClientTests' job) — this just needs GET /api/v1/notes/mine to return a fixed list
    // so LoadNotesAsync populates Notes/FilteredNotes deterministically.
    private sealed class FixedNotesHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }

    private static ApiClient NewClientWithNotes(params (Guid Id, string Title)[] notes)
    {
        var items = string.Join(",", notes.Select(n =>
            $"{{\"id\":\"{n.Id}\",\"title\":\"{n.Title}\",\"updatedAt\":\"2026-07-01T00:00:00Z\"}}"));
        var client = new ApiClient();
        var httpClient = new HttpClient(new FixedNotesHandler($"[{items}]")) { BaseAddress = new Uri("http://localhost:8080") };
        typeof(ApiClient).GetField("_http", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(client, httpClient);
        return client;
    }

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

    [Fact]
    public async Task Sda19_SearchText_FiltersFilteredNotesByTitle_CaseInsensitive()
    {
        var client = NewClientWithNotes((Guid.NewGuid(), "Physics revision"), (Guid.NewGuid(), "Chemistry lab notes"));
        var viewModel = new NotesViewModel(client, Guid.NewGuid());
        await viewModel.LoadNotesCommand.ExecuteAsync(null);
        Assert.Equal(2, viewModel.FilteredNotes.Count);

        viewModel.SearchText = "chem";

        Assert.Single(viewModel.FilteredNotes);
        Assert.Equal("Chemistry lab notes", viewModel.FilteredNotes[0].Title);
    }

    // SekBridgeTests covers that HandleMessageAsync raises NavigateToNoteRequested with the
    // right parsed GUID; this covers what NotesViewModel's subscriber does with it —
    // find-by-id in the already-loaded Notes list, clear any active search filter (so the
    // target is actually visible), and select it (which mounts it via SelectedNote's
    // existing OnSelectedNoteChanged path).
    [Fact]
    public async Task Sda19_NavigateToNoteRequested_SelectsTargetAndClearsSearch()
    {
        var targetId = Guid.NewGuid();
        var client = NewClientWithNotes((Guid.NewGuid(), "Physics revision"), (targetId, "Chemistry lab notes"));
        var viewModel = new NotesViewModel(client, Guid.NewGuid());
        viewModel.Bridge.InvokeScript = _ => Task.CompletedTask;
        await viewModel.LoadNotesCommand.ExecuteAsync(null);
        viewModel.SearchText = "physics"; // hides the navigate target from FilteredNotes

        await viewModel.Bridge.HandleMessageAsync(
            JsonSerializer.Serialize(new { type = "navigateToNote", noteId = targetId }));

        Assert.Equal("", viewModel.SearchText);
        Assert.NotNull(viewModel.SelectedNote);
        Assert.Equal(targetId, viewModel.SelectedNote!.Id);
    }
}
