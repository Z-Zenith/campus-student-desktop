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

    public async Task MountAsync(Guid userId, CodeProjectDto? currentProject, bool canRun, bool canEdit)
    {
        var user = new SekUserContext(userId.ToString(), apiClient.Token ?? "", "student", "");
        var mount = new MountMessage(user, currentProject is null ? null : ToSekProject(currentProject), canRun, canEdit);

        if (InvokeScript is null)
        {
            return;
        }
        await InvokeScript($"window.__sekHostMount({JsonSerializer.Serialize(mount, JsonOptions)})");
    }

    public async Task HandleMessageAsync(string json)
    {
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
                "save" => await SaveAsync(request.RequestId, request.Payload),
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
        await InvokeScript($"window.__sekHostReceive({JsonSerializer.Serialize(response, JsonOptions)})");
    }

    private async Task<BridgeResponse> RunAsync(string requestId, JsonElement payload)
    {
        var run = payload.Deserialize<RunPayload>(JsonOptions)
            ?? throw new InvalidOperationException("Malformed 'run' payload.");
        var project = run.Project;

        var result = await apiClient.RunCodeAsync(project.EntryFilePath, ToApiFiles(project.Files), project.Stdin);
        return new BridgeResponse(requestId, true, ToSekResult(result), null);
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

    private static List<CodeFileDto> ToApiFiles(IReadOnlyList<SekCodeFileDto> files) =>
        files.Select(f => new CodeFileDto(f.Path, f.Language, f.Content)).ToList();

    private static SekCodeFileDto ToSekFile(CodeFileDto f) => new(f.Path, f.Language, f.Content);

    private static SekCodeRunResultDto ToSekResult(CodeRunResultDto r) =>
        new(r.Stdout, r.Stderr, r.ExitCode, r.DurationMs, r.TimedOut, r.Status);

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
}
