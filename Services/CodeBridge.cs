using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using StudentDesktop.Models;

namespace StudentDesktop.Services;

// SEK-01: bridges the Coding app's CodeEditor (hosted in a NativeWebView — see
// CodeEditorView) to the Backend API, mirroring SekBridge's protocol exactly (see that
// class for the full postMessage/InvokeScript rationale). Two bridged methods: 'run' and
// 'save' — 'save' tries UpdateCodeProjectAsync first and falls back to
// CreateCodeProjectAsync on a 404, exactly like SekBridge.SaveAsync does for notes (a
// project SEK just generated an Id for has nothing to update yet).
public sealed class CodeBridge(ApiClient apiClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public Func<string, Task>? InvokeScript { get; set; }

    /// Raised after a save completes successfully, so the project list can refresh.
    public event Action? ProjectChanged;

    /// Raised when the host bundle's window.__sekHostMount call completes successfully.
    public event Action? MountSucceeded;

    /// Raised when window.__sekHostMount throws — see code-host-entry.tsx's try/catch
    /// around the mount body. message is the JS error's String(error) rendering.
    public event Action<string>? MountFailed;

    /// B2 live preview (SDA/SEK plan): raised after a successful 'runPreview' bridge call
    /// so CodeEditorViewModel can ask ShellViewModel to open the URL as a new tab in the
    /// built-in browser — this bridge stays UI-agnostic (no Browser/Shell reference of
    /// its own), same reasoning as InvokeScript being a delegate instead of a WebView
    /// reference.
    public event Action<string>? PreviewReady;

    public async Task MountAsync(Guid userId, CodeProjectDto? currentProject, bool canRun, bool canEdit)
    {
        var user = new SekUserContext(userId.ToString(), apiClient.Token ?? "", "student", "");
        var mount = new MountMessage(user, currentProject is null ? null : ToSekProject(currentProject), canRun, canEdit);

        if (InvokeScript is null)
        {
            return;
        }
        // __sekHostMount(json: string) does JSON.parse(json) — it needs a JSON *string*
        // argument, so the payload has to be serialized twice: once to produce the JSON,
        // once more to turn that JSON into a quoted/escaped JS string literal. Serializing
        // only once interpolates a bare object literal, which JSON.parse coerces to the
        // literal text "[object Object]" and fails to parse — silently, since a thrown
        // error inside a WebView2 ExecuteScriptAsync call never surfaces as a .NET exception.
        var mountJson = JsonSerializer.Serialize(mount, JsonOptions);
        await InvokeScript($"window.__sekHostMount({JsonSerializer.Serialize(mountJson)})");
    }

    public async Task HandleMessageAsync(string json)
    {
        // Distinct from the {requestId, method, payload} bridge protocol below — these
        // are the mount-ready/mount-failed signals code-host-entry.tsx posts from inside
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
                "run" => await RunAsync(request.RequestId, request.Payload),
                "runPreview" => await RunPreviewAsync(request.RequestId, request.Payload),
                "save" => await SaveAsync(request.RequestId, request.Payload),
                "terminalStart" => await TerminalStartAsync(request.RequestId, request.Payload),
                "terminalExec" => await TerminalExecAsync(request.RequestId, request.Payload),
                "terminalClose" => await TerminalCloseAsync(request.RequestId, request.Payload),
                _ => new BridgeResponse(request.RequestId, false, null,
                    new SekErrorDto("validation_error", $"Unknown code bridge method '{request.Method}'.")),
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

    private async Task<BridgeResponse> RunAsync(string requestId, JsonElement payload)
    {
        var run = payload.Deserialize<RunPayload>(JsonOptions)
            ?? throw new InvalidOperationException("Malformed 'run' payload.");
        var project = run.Project;

        var result = await apiClient.RunCodeAsync(project.EntryFilePath, ToApiFiles(project.Files), project.Stdin);
        return new BridgeResponse(requestId, true, ToSekResult(result), null);
    }

    private async Task<BridgeResponse> RunPreviewAsync(string requestId, JsonElement payload)
    {
        var run = payload.Deserialize<RunPayload>(JsonOptions)
            ?? throw new InvalidOperationException("Malformed 'runPreview' payload.");
        var project = run.Project;

        var result = await apiClient.RunPreviewAsync(project.EntryFilePath, ToApiFiles(project.Files));
        PreviewReady?.Invoke(result.PreviewUrl);
        return new BridgeResponse(requestId, true, new SekRunPreviewResultDto(result.PreviewUrl, result.Mode, result.IsReady), null);
    }

    private async Task<BridgeResponse> SaveAsync(string requestId, JsonElement payload)
    {
        var save = payload.Deserialize<SavePayload>(JsonOptions)
            ?? throw new InvalidOperationException("Malformed 'save' payload.");
        var input = save.Project;

        CodeProjectDto saved;
        try
        {
            saved = await apiClient.UpdateCodeProjectAsync(
                input.Id, input.Name, ToApiFiles(input.Files), input.EntryFilePath, input.ActiveFilePath, input.Stdin);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            // First save of a project SEK just generated an Id for — nothing to update yet.
            saved = await apiClient.CreateCodeProjectAsync(
                input.Name, ToApiFiles(input.Files), input.EntryFilePath, input.ActiveFilePath, input.Stdin, input.Id);
        }

        ProjectChanged?.Invoke();
        return new BridgeResponse(requestId, true, ToSekProject(saved), null);
    }

    private async Task<BridgeResponse> TerminalStartAsync(string requestId, JsonElement payload)
    {
        var start = payload.Deserialize<TerminalStartPayload>(JsonOptions)
            ?? throw new InvalidOperationException("Malformed 'terminalStart' payload.");

        var sessionId = await apiClient.StartTerminalAsync(ToApiFiles(start.Files));
        return new BridgeResponse(requestId, true, new SekTerminalStartResultDto(sessionId), null);
    }

    private async Task<BridgeResponse> TerminalExecAsync(string requestId, JsonElement payload)
    {
        var exec = payload.Deserialize<TerminalExecPayload>(JsonOptions)
            ?? throw new InvalidOperationException("Malformed 'terminalExec' payload.");

        try
        {
            var result = await apiClient.ExecTerminalCommandAsync(exec.SessionId, exec.Command);
            return new BridgeResponse(requestId, true, ToSekTerminalResult(result), null);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            // A generic "Project not found" (MapError's usual 404 mapping, written for
            // run/save) would be misleading here — the session, not a project, is what
            // expired (the reaper closed it, or it never existed).
            return new BridgeResponse(requestId, false, null,
                new SekErrorDto("network_error", "This terminal session has expired. Click Restart to start a new one."));
        }
    }

    private async Task<BridgeResponse> TerminalCloseAsync(string requestId, JsonElement payload)
    {
        var close = payload.Deserialize<TerminalClosePayload>(JsonOptions)
            ?? throw new InvalidOperationException("Malformed 'terminalClose' payload.");

        // Best-effort: TerminalPanel calls this from an unmount/restart cleanup path with
        // nothing awaiting a meaningful result, so neither an already-expired/reaped
        // session (404) nor a transient network failure is worth surfacing as an error —
        // unlike 'run'/'save'/'terminalExec', which the student is actively waiting on.
        try
        {
            await apiClient.CloseTerminalAsync(close.SessionId);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
        }
        return new BridgeResponse(requestId, true, null, null);
    }

    private static List<CodeFileDto> ToApiFiles(IReadOnlyList<SekCodeFileDto> files) =>
        files.Select(f => new CodeFileDto(f.Path, f.Language, f.Content)).ToList();

    private static SekCodeFileDto ToSekFile(CodeFileDto f) => new(f.Path, f.Language, f.Content);

    private static SekCodeRunResultDto ToSekResult(CodeRunResultDto r) =>
        new(r.Stdout, r.Stderr, r.ExitCode, r.DurationMs, r.TimedOut, r.Status);

    private static SekTerminalExecResultDto ToSekTerminalResult(TerminalExecResultDto r) =>
        new(r.Stdout, r.Stderr, r.ExitCode);

    // All projects flowing through this bridge belong to the signed-in student (the
    // backend enforces that) — SEK's CodeProject.id is optional/absent for a new project,
    // matching CodeProjectDto not existing yet for one SEK just generated a draft Id for.
    private static SekCodeProjectDto ToSekProject(CodeProjectDto p) =>
        new(p.Id, p.Name, p.Files.Select(ToSekFile).ToList(), p.EntryFilePath, p.ActiveFilePath, p.Stdin);

    private static SekErrorDto MapError(Exception ex) => ex switch
    {
        ApiException { StatusCode: 404 } => new SekErrorDto("validation_error", "Project not found."),
        ApiException { StatusCode: 403 } => new SekErrorDto("unauthorized", "You don't have access to this project."),
        ApiException { StatusCode: 400 } apiEx => new SekErrorDto("validation_error", apiEx.Message),
        ApiException apiEx => new SekErrorDto("network_error", apiEx.Message),
        _ => new SekErrorDto("network_error", "Could not reach the server. Check your connection and try again."),
    };

    private sealed record HostSignal(string? Type, string? Message);
    private sealed record BridgeRequest(string RequestId, string Method, JsonElement Payload);
    private sealed record BridgeResponse(string RequestId, bool Ok, object? Value, SekErrorDto? Error);
    private sealed record SekErrorDto(string Code, string Message);
    private sealed record SekUserContext(string UserId, string SessionToken, string Role, string CollegeId);
    private sealed record SekCodeFileDto(string Path, string Language, string Content);
    private sealed record SekCodeProjectDto(
        Guid? Id, string Name, IReadOnlyList<SekCodeFileDto> Files, string EntryFilePath, string ActiveFilePath, string? Stdin);
    private sealed record MountMessage(SekUserContext User, SekCodeProjectDto? CurrentProject, bool CanRun, bool CanEdit);
    private sealed record RunPayload(SekCodeProjectDto Project);
    private sealed record SavePayload(SekCodeProjectRequiredIdDto Project);
    // SEK always assigns a draft Id client-side (crypto.randomUUID) before the first save —
    // unlike CodeProject.id (optional in the public TS contract), the 'save' payload's
    // project always carries one by the time it reaches this bridge.
    private sealed record SekCodeProjectRequiredIdDto(
        Guid Id, string Name, IReadOnlyList<SekCodeFileDto> Files, string EntryFilePath, string ActiveFilePath, string? Stdin);
    private sealed record SekCodeRunResultDto(string Stdout, string Stderr, int ExitCode, long DurationMs, bool TimedOut, string? Status);
    private sealed record SekRunPreviewResultDto(string PreviewUrl, string Mode, bool IsReady);
    private sealed record TerminalStartPayload(IReadOnlyList<SekCodeFileDto> Files);
    private sealed record TerminalExecPayload(Guid SessionId, string Command);
    private sealed record TerminalClosePayload(Guid SessionId);
    private sealed record SekTerminalStartResultDto(Guid SessionId);
    private sealed record SekTerminalExecResultDto(string Stdout, string Stderr, int ExitCode);
}
