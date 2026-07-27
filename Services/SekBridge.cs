using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using StudentDesktop.Models;

namespace StudentDesktop.Services;

// SDA-19: bridges SEK-03's NotesEditor (hosted in a NativeWebView — see NotesView) to the
// Backend API. The JS-side host entry (packages/shared-editor-kit/src/host/notes-host-entry.tsx)
// posts { requestId, method, payload } messages via window.chrome.webview.postMessage — an
// API Avalonia's NativeWebView injects uniformly across WebView2/WKWebView/WebKitGTK, so the
// same bundle works on every platform. This side never awaits a JS-computed response of its
// own, so each incoming message gets exactly one reply back, delivered via InvokeScript
// calling window.__sekHostReceive — no request/response correlation bookkeeping needed here.
//
// InvokeScript is a settable delegate, not a NativeWebView reference, so this stays testable
// without a live WebView — same pattern as BrowserViewModel's GetPageTitleAsync/
// GetSelectedTextAsync delegates.
public sealed class SekBridge(ApiClient apiClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private Guid _userId;

    public Func<string, Task>? InvokeScript { get; set; }

    /// Raised after a save or delete completes successfully, so the note list can refresh.
    public event Action? NoteChanged;

    /// Raised when the host bundle's window.__sekHostMount call completes successfully.
    public event Action? MountSucceeded;

    /// Raised when window.__sekHostMount throws — see notes-host-entry.tsx's try/catch
    /// around the mount body. message is the JS error's String(error) rendering.
    public event Action<string>? MountFailed;

    // SDA doesn't track a real college tenant client-side yet (LoginResponse has no
    // CollegeId — that's Track 1/auth territory, out of scope for SDA-19). NotesEditor
    // never reads UserContext.collegeId, so an empty placeholder is honest here rather
    // than fabricating a value nothing actually validates.
    public async Task MountNotesEditorAsync(Guid userId, NoteDto? currentNote, bool canEdit)
    {
        _userId = userId;
        var user = new SekUserContext(userId.ToString(), apiClient.Token ?? "", "student", "");
        var mount = new MountMessage(user, currentNote is null ? null : ToSekNote(currentNote), canEdit);

        if (InvokeScript is null)
        {
            return;
        }
        // __sekHostMount(json: string) does JSON.parse(json) — needs a JSON *string*
        // argument, so the payload must be serialized twice: once to produce the JSON,
        // once more to turn that JSON into a quoted/escaped JS string literal. A single
        // serialize interpolates a bare object literal, which JSON.parse coerces to the
        // literal text "[object Object]" and fails to parse — silently, since an error
        // thrown inside a WebView2 ExecuteScriptAsync call never surfaces as a .NET
        // exception. See CodeBridge.MountAsync for the same fix.
        var mountJson = JsonSerializer.Serialize(mount, JsonOptions);
        await InvokeScript($"window.__sekHostMount({JsonSerializer.Serialize(mountJson)})");
    }

    public async Task HandleMessageAsync(string json)
    {
        // Distinct from the {requestId, method, payload} bridge protocol below — these
        // are the mount-ready/mount-failed signals notes-host-entry.tsx posts from inside
        // window.__sekHostMount's try/catch, not a request awaiting a reply.
        var signal = JsonSerializer.Deserialize<HostSignal>(json, JsonOptions);
        if (signal?.Type == "sekHostMounted")
        {
            MountSucceeded?.Invoke();
            return;
        }
        if (signal?.Type == "sekHostMountFailed")
        {
            MountFailed?.Invoke(signal.Message ?? "Unknown error.");
            return;
        }

        var request = JsonSerializer.Deserialize<BridgeRequest>(json, JsonOptions);
        if (request is null)
        {
            return;
        }

        BridgeResponse response;
        try
        {
            response = request.Method switch
            {
                "save" => await SaveAsync(request.RequestId, request.Payload),
                "delete" => await DeleteAsync(request.RequestId, request.Payload),
                "resolveLink" => await ResolveLinkAsync(request.RequestId, request.Payload),
                "listBacklinks" => await ListBacklinksAsync(request.RequestId, request.Payload),
                "searchImages" => await SearchImagesAsync(request.RequestId, request.Payload),
                "uploadImage" => await UploadImageAsync(request.RequestId, request.Payload),
                _ => new BridgeResponse(request.RequestId, false, null,
                    new SekErrorDto("validation_error", $"Unknown SEK bridge method '{request.Method}'.")),
            };
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
            response = new BridgeResponse(request.RequestId, false, null, MapError(ex));
        }

        if (InvokeScript is null)
        {
            return;
        }
        var responseJson = JsonSerializer.Serialize(response, JsonOptions);
        await InvokeScript($"window.__sekHostReceive({JsonSerializer.Serialize(responseJson)})");
    }

    private async Task<BridgeResponse> SaveAsync(string requestId, JsonElement payload)
    {
        var save = payload.Deserialize<SavePayload>(JsonOptions)
            ?? throw new InvalidOperationException("Malformed 'save' payload.");
        var input = save.Note;

        NoteDto saved;
        try
        {
            saved = await apiClient.UpdateNoteAsync(input.Id, input.Title, input.ContentMarkdown, save.Links);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            // First save of a note SEK just generated an Id for — nothing to update yet.
            saved = await apiClient.CreateNoteAsync(input.Title, input.ContentMarkdown, input.Id, save.Links);
        }

        NoteChanged?.Invoke();
        return new BridgeResponse(requestId, true, ToSekNote(saved), null);
    }

    private async Task<BridgeResponse> DeleteAsync(string requestId, JsonElement payload)
    {
        var delete = payload.Deserialize<NoteIdPayload>(JsonOptions)
            ?? throw new InvalidOperationException("Malformed 'delete' payload.");
        await apiClient.DeleteNoteAsync(delete.NoteId);
        NoteChanged?.Invoke();
        return new BridgeResponse(requestId, true, null, null);
    }

    private async Task<BridgeResponse> ResolveLinkAsync(string requestId, JsonElement payload)
    {
        var resolve = payload.Deserialize<ToNoteIdPayload>(JsonOptions)
            ?? throw new InvalidOperationException("Malformed 'resolveLink' payload.");
        var note = await apiClient.GetNoteAsync(resolve.ToNoteId);
        return new BridgeResponse(requestId, true, ToSekNote(note), null);
    }

    private async Task<BridgeResponse> ListBacklinksAsync(string requestId, JsonElement payload)
    {
        var list = payload.Deserialize<ToNoteIdPayload>(JsonOptions)
            ?? throw new InvalidOperationException("Malformed 'listBacklinks' payload.");
        var backlinks = await apiClient.GetBacklinksAsync(list.ToNoteId);
        return new BridgeResponse(requestId, true, backlinks.Select(ToSekNote).ToList(), null);
    }

    private async Task<BridgeResponse> SearchImagesAsync(string requestId, JsonElement payload)
    {
        var search = payload.Deserialize<SearchImagesPayload>(JsonOptions)
            ?? throw new InvalidOperationException("Malformed 'searchImages' payload.");
        var result = await apiClient.SearchImagesAsync(search.Query);
        return new BridgeResponse(requestId, true, result, null);
    }

    // SEK-04: mirrors campus-teacher-web's uploadImage adapter — the backend's
    // /image-search/save only fetches+stores bytes, this builds the ImageInsert
    // client-side from the search result's own title/width/height/attribution plus the
    // freshly re-hosted embeddedUrl.
    private async Task<BridgeResponse> UploadImageAsync(string requestId, JsonElement payload)
    {
        var upload = payload.Deserialize<UploadImagePayload>(JsonOptions)
            ?? throw new InvalidOperationException("Malformed 'uploadImage' payload.");
        var embeddedUrl = await apiClient.SaveImageAsync(upload.Result.SourceUrl);
        var insert = new SekImageInsertDto(embeddedUrl, upload.Result.Title, upload.Result.Width, upload.Result.Height, upload.Result.Attribution);
        return new BridgeResponse(requestId, true, insert, null);
    }

    // All notes flowing through this bridge belong to the signed-in student (the backend
    // enforces that), so the SEK Note.ownerId field can be synthesized from the session
    // rather than requiring NoteDto to carry it.
    private SekNoteDto ToSekNote(NoteDto n) => new(n.Id, _userId, n.Title, n.ContentMarkdown, n.CreatedAt, n.UpdatedAt);

    private static SekErrorDto MapError(Exception ex) => ex switch
    {
        ApiException { StatusCode: 404 } => new SekErrorDto("note_not_found", "Note not found."),
        ApiException { StatusCode: 403 } => new SekErrorDto("unauthorized", "You don't have access to this note."),
        ApiException { StatusCode: 400 } apiEx => new SekErrorDto("validation_error", apiEx.Message),
        ApiException apiEx => new SekErrorDto("network_error", apiEx.Message),
        _ => new SekErrorDto("network_error", "Could not reach the server. Check your connection and try again."),
    };

    private sealed record HostSignal(string? Type, string? Message);
    private sealed record BridgeRequest(string RequestId, string Method, JsonElement Payload);
    private sealed record BridgeResponse(string RequestId, bool Ok, object? Value, SekErrorDto? Error);
    private sealed record SekErrorDto(string Code, string Message);
    private sealed record SekUserContext(string UserId, string SessionToken, string Role, string CollegeId);
    private sealed record SekNoteDto(Guid Id, Guid OwnerId, string Title, string ContentMarkdown, DateTime CreatedAt, DateTime UpdatedAt);
    private sealed record MountMessage(SekUserContext User, SekNoteDto? CurrentNote, bool CanEdit);
    private sealed record SavePayload(SekNoteDto Note, IReadOnlyList<NoteLinkInput>? Links);
    private sealed record NoteIdPayload(Guid NoteId);
    private sealed record ToNoteIdPayload(Guid ToNoteId);
    private sealed record SearchImagesPayload(string Query);
    private sealed record UploadImagePayload(ImageSearchResultDto Result);
    private sealed record SekImageInsertDto(string EmbeddedUrl, string AltText, int Width, int Height, string Attribution);
}
