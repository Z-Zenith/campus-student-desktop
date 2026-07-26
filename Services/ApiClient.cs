using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using StudentDesktop.Models;

namespace StudentDesktop.Services;

public class ApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

// Session-scoped: the desktop app is locked-down and re-authenticates each launch (SDA-02),
// so the JWT only needs to live in memory, not persisted to disk.
public class ApiClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public string? Token { get; private set; }

    public ApiClient(string baseAddress = "http://localhost:8080")
    {
        _http = new HttpClient { BaseAddress = new Uri(baseAddress) };
    }

    public async Task<LoginResponse> LoginAsync(string identifier, string password, string totpCode)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/auth/login",
            new LoginRequest(identifier, password, totpCode, Environment.MachineName));
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions)
            ?? throw new ApiException(500, "Empty login response");
        Token = login.Token;
        return login;
    }

    public async Task<MyCalendarResponse> GetMyCalendarAsync()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/calendar/mine");
        return await response.Content.ReadFromJsonAsync<MyCalendarResponse>(JsonOptions)
            ?? new MyCalendarResponse([]);
    }

    // SDA-14: personal to-do / custom-entry CRUD backing Calendar's interactive lists.
    public async Task<TodoDto> CreateTodoAsync(string title, DateTime? dueDate)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/todos", new CreateTodoRequest(title, dueDate));
        return await response.Content.ReadFromJsonAsync<TodoDto>(JsonOptions)
            ?? throw new ApiException(500, "Empty to-do response");
    }

    public async Task<TodoDto> SetTodoCompleteAsync(Guid todoId, bool completed)
    {
        var response = await SendAsync(HttpMethod.Patch, $"/api/v1/todos/{todoId}/complete", new SetTodoCompleteRequest(completed));
        return await response.Content.ReadFromJsonAsync<TodoDto>(JsonOptions)
            ?? throw new ApiException(500, "Empty to-do response");
    }

    public Task DeleteTodoAsync(Guid todoId) => SendAsync(HttpMethod.Delete, $"/api/v1/todos/{todoId}");

    public async Task<CustomCalendarEntryDto> CreateCustomCalendarEntryAsync(string title, DateOnly entryDate)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/calendar/custom-entries", new CreateCustomCalendarEntryRequest(title, entryDate));
        return await response.Content.ReadFromJsonAsync<CustomCalendarEntryDto>(JsonOptions)
            ?? throw new ApiException(500, "Empty custom entry response");
    }

    public Task DeleteCustomCalendarEntryAsync(Guid entryId) => SendAsync(HttpMethod.Delete, $"/api/v1/calendar/custom-entries/{entryId}");

    // SEK-01: the Coding app's "Run" action.
    public async Task<CodeRunResultDto> RunCodeAsync(string language, string content, string? stdin)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/code/run", new RunCodeRequest(language, content, stdin, null));
        return await response.Content.ReadFromJsonAsync<CodeRunResultDto>(JsonOptions)
            ?? throw new ApiException(500, "Empty code-run response");
    }

    public async Task<List<EventDto>> ListEventsAsync()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/events");
        return await response.Content.ReadFromJsonAsync<List<EventDto>>(JsonOptions) ?? [];
    }

    public async Task RegisterForEventAsync(Guid eventId)
    {
        await SendAsync(HttpMethod.Post, $"/api/v1/events/{eventId}/register");
    }

    // SDA-23: self-service password change requires a fresh, successful TOTP challenge —
    // the backend rejects the request outright if the code is missing or invalid.
    public async Task ChangePasswordAsync(string currentPassword, string newPassword, string totpCode)
    {
        await SendAsync(HttpMethod.Post, "/api/v1/auth/change-password",
            new ChangePasswordRequest(currentPassword, newPassword, totpCode));
    }

    public async Task<MyMarksResponse> GetMyMarksAsync()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/marks/mine");
        return await response.Content.ReadFromJsonAsync<MyMarksResponse>(JsonOptions)
            ?? new MyMarksResponse([], []);
    }

    // SDA-10: manual submission. The backend flags late submissions and rejects a
    // format mismatch (e.g. a quiz submitted as a file upload) — this just forwards.
    // SDA-10: the Assignments tile grid's data source.
    public async Task<List<AssignmentSummaryDto>> GetMyAssignmentsAsync()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/assignments/mine");
        return await response.Content.ReadFromJsonAsync<List<AssignmentSummaryDto>>(JsonOptions) ?? [];
    }

    public async Task<SubmissionDto> SubmitAssignmentAsync(Guid assignmentId, string contentUrl, string submissionFormat)
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/v1/assignments/{assignmentId}/submissions",
            new SubmitAssignmentRequest(contentUrl, submissionFormat));
        return await response.Content.ReadFromJsonAsync<SubmissionDto>(JsonOptions)
            ?? throw new ApiException(500, "Empty submission response");
    }

    // SDA-17
    public async Task<List<MyTeacherDto>> GetMyTeachersAsync()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/teacher-feedback/my-teachers");
        return await response.Content.ReadFromJsonAsync<List<MyTeacherDto>>(JsonOptions) ?? [];
    }

    public async Task<TeacherFeedbackDto> SubmitTeacherFeedbackAsync(Guid teacherId, int rating, string? comments)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/teacher-feedback",
            new SubmitTeacherFeedbackRequest(teacherId, rating, comments));
        return await response.Content.ReadFromJsonAsync<TeacherFeedbackDto>(JsonOptions)
            ?? throw new ApiException(500, "Empty feedback response");
    }

    // SDA-18
    public async Task<List<MySubjectDto>> GetMySubjectsAsync()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/subjects/mine");
        return await response.Content.ReadFromJsonAsync<List<MySubjectDto>>(JsonOptions) ?? [];
    }

    // SDA-11: called by AssignmentAutoSubmitService when the app detects exit or
    // focus-loss during an active assignment window.
    public async Task<SubmissionDto> AutoSubmitAssignmentAsync(Guid assignmentId, string contentUrl, string submissionFormat)
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/v1/assignments/{assignmentId}/submissions/auto-submit",
            new SubmitAssignmentRequest(contentUrl, submissionFormat));
        return await response.Content.ReadFromJsonAsync<SubmissionDto>(JsonOptions)
            ?? throw new ApiException(500, "Empty auto-submit response");
    }

    // SDA-16
    public async Task<List<GroupDto>> GetMyGroupsAsync()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/groups/mine");
        var body = await response.Content.ReadFromJsonAsync<MyGroupsResponse>(JsonOptions);
        return body?.Groups ?? [];
    }

    public async Task<List<GroupPostDto>> GetGroupPostsAsync(Guid groupId)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/groups/{groupId}/posts");
        return await response.Content.ReadFromJsonAsync<List<GroupPostDto>>(JsonOptions) ?? [];
    }

    public async Task<GroupPostDto> CreateGroupPostAsync(Guid groupId, string content)
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/posts", new CreatePostRequest(content));
        return await response.Content.ReadFromJsonAsync<GroupPostDto>(JsonOptions)
            ?? throw new ApiException(500, "Empty post response");
    }

    public async Task<List<MaterialDto>> GetGroupMaterialsAsync(Guid groupId)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/groups/{groupId}/materials");
        return await response.Content.ReadFromJsonAsync<List<MaterialDto>>(JsonOptions) ?? [];
    }

    // SDA-03/SDA-04
    public async Task<WhitelistResponse> GetWhitelistAsync()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/whitelist");
        return await response.Content.ReadFromJsonAsync<WhitelistResponse>(JsonOptions)
            ?? new WhitelistResponse([]);
    }

    // SDA-04: asks a teacher/admin to approve a site not yet on the whitelist.
    public async Task<WhitelistRequestDto> RequestWhitelistAsync(string url)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/whitelist/requests", new CreateWhitelistRequestRequest(url));
        return await response.Content.ReadFromJsonAsync<WhitelistRequestDto>(JsonOptions)
            ?? throw new ApiException(500, "Empty whitelist request response");
    }

    // SEK-04
    public async Task<ImageSearchResponseDto> SearchImagesAsync(string query)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/image-search", new ImageSearchRequestBody(query));
        return await response.Content.ReadFromJsonAsync<ImageSearchResponseDto>(JsonOptions)
            ?? new ImageSearchResponseDto(query, [], true);
    }

    public async Task<string> SaveImageAsync(string sourceUrl)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/image-search/save", new SaveImageRequestBody(sourceUrl));
        var dto = await response.Content.ReadFromJsonAsync<SaveImageResponseDto>(JsonOptions)
            ?? throw new ApiException(500, "Empty save-image response");
        return dto.Url;
    }

    // SDA-08
    public async Task<List<NoteSummaryDto>> GetMyNotesAsync()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/notes/mine");
        return await response.Content.ReadFromJsonAsync<List<NoteSummaryDto>>(JsonOptions) ?? [];
    }

    public async Task<NoteDto> GetNoteAsync(Guid noteId)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/notes/{noteId}");
        return await response.Content.ReadFromJsonAsync<NoteDto>(JsonOptions)
            ?? throw new ApiException(500, "Empty note response");
    }

    public async Task<NoteDto> CreateNoteAsync(string title, string contentMarkdown, Guid? id = null, IReadOnlyList<NoteLinkInput>? links = null)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/notes", new CreateNoteRequest(title, contentMarkdown, id, links));
        return await response.Content.ReadFromJsonAsync<NoteDto>(JsonOptions)
            ?? throw new ApiException(500, "Empty note response");
    }

    public async Task<NoteDto> UpdateNoteAsync(Guid noteId, string title, string contentMarkdown, IReadOnlyList<NoteLinkInput>? links = null)
    {
        var response = await SendAsync(HttpMethod.Patch, $"/api/v1/notes/{noteId}", new UpdateNoteRequest(title, contentMarkdown, links));
        return await response.Content.ReadFromJsonAsync<NoteDto>(JsonOptions)
            ?? throw new ApiException(500, "Empty note response");
    }

    // SDA-19
    public async Task DeleteNoteAsync(Guid noteId)
    {
        await SendAsync(HttpMethod.Delete, $"/api/v1/notes/{noteId}");
    }

    // SDA-19/SEK-03: onListBacklinks
    public async Task<List<NoteDto>> GetBacklinksAsync(Guid noteId)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/notes/{noteId}/backlinks");
        return await response.Content.ReadFromJsonAsync<List<NoteDto>>(JsonOptions) ?? [];
    }

    // SDA-24, DMS-01
    public async Task<List<DmsThreadSummaryDto>> GetMessageThreadsAsync()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/messages/threads");
        return await response.Content.ReadFromJsonAsync<List<DmsThreadSummaryDto>>(JsonOptions) ?? [];
    }

    public async Task<List<DmsMessageDto>> GetThreadMessagesAsync(Guid threadId)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/messages/threads/{threadId}/messages");
        return await response.Content.ReadFromJsonAsync<List<DmsMessageDto>>(JsonOptions) ?? [];
    }

    public async Task<DmsMessageDto> SendMessageAsync(Guid threadId, string content)
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/v1/messages/threads/{threadId}/messages", new SendMessageRequest(content));
        return await response.Content.ReadFromJsonAsync<DmsMessageDto>(JsonOptions)
            ?? throw new ApiException(500, "Empty message response");
    }

    // SDA-25: batch of usage-pattern telemetry events, each already tagged by the caller
    // with the active class session and/or assignment it was gathered during.
    public async Task SubmitTelemetryAsync(IReadOnlyList<TelemetryEventRequest> events)
    {
        if (events.Count == 0)
        {
            return;
        }
        await SendAsync(HttpMethod.Post, "/api/v1/telemetry/usage", new SubmitTelemetryRequest(events));
    }

    public async Task LogoutAsync()
    {
        if (Token is null)
        {
            return;
        }
        try
        {
            await SendAsync(HttpMethod.Post, "/api/v1/auth/logout");
        }
        finally
        {
            Token = null;
        }
    }

    // SDA-12: fired whenever the app loses effective focus or is closing. Whether that
    // actually matters (i.e. whether the student is in a scheduled class right now) is
    // decided entirely server-side, so this always fires and is a best-effort, fire-and-
    // forget style call — a failed ping must never block the student from closing the app
    // or interrupt whatever they were doing when focus moved elsewhere.
    public async Task ExitPingAsync()
    {
        if (Token is null)
        {
            return;
        }
        try
        {
            await SendAsync(HttpMethod.Post, "/api/v1/class-sessions/exit-ping");
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
            // Best-effort — there is no user-facing feedback for this event either way.
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        if (Token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        }

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body2 = await response.Content.ReadAsStringAsync();
            throw new ApiException((int)response.StatusCode, ExtractErrorMessage(body2, response.ReasonPhrase));
        }
        return response;
    }

    // #158 — every backend controller returns {"error": "...", "message": "human text"} on
    // failure; surface that human message instead of the raw JSON blob, falling back to the
    // raw text/reason phrase if the body isn't the shape expected (or isn't JSON at all).
    private static string ExtractErrorMessage(string body, string? reasonPhrase)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return reasonPhrase ?? "Request failed";
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var messageProp) && messageProp.ValueKind == JsonValueKind.String)
            {
                var message = messageProp.GetString();
                if (!string.IsNullOrEmpty(message))
                {
                    return message;
                }
            }
        }
        catch (JsonException)
        {
            // not JSON - fall through to the raw text below
        }

        return body;
    }
}
