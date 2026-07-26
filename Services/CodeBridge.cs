using System;
using System.Text.Json;
using System.Threading.Tasks;
using StudentDesktop.Models;

namespace StudentDesktop.Services;

// SEK-01: bridges the Coding app's CodeEditor (hosted in a NativeWebView — see
// CodeEditorView) to the Backend API, mirroring SekBridge's protocol exactly (see that
// class for the full postMessage/InvokeScript rationale). One bridged method for this
// first cut: 'run' — no persistence yet, this is a scratch code-run surface.
public sealed class CodeBridge(ApiClient apiClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public Func<string, Task>? InvokeScript { get; set; }

    public async Task MountAsync(Guid userId, bool canRun, bool canEdit)
    {
        var user = new SekUserContext(userId.ToString(), apiClient.Token ?? "", "student", "");
        var mount = new MountMessage(user, canRun, canEdit);

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
                _ => new BridgeResponse(request.RequestId, false, null,
                    new SekErrorDto("validation_error", $"Unknown code bridge method '{request.Method}'.")),
            };
        }
        catch (Exception ex) when (ex is ApiException or System.Net.Http.HttpRequestException or TaskCanceledException)
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
        var source = run.Source;

        var result = await apiClient.RunCodeAsync(source.Language, source.Content, source.Stdin);
        return new BridgeResponse(requestId, true, ToSekResult(result), null);
    }

    private static SekCodeRunResultDto ToSekResult(CodeRunResultDto r) =>
        new(r.Stdout, r.Stderr, r.ExitCode, r.DurationMs, r.TimedOut);

    private static SekErrorDto MapError(Exception ex) => ex switch
    {
        // The backend distinguishes unsupported_language/validation_error in its {error,
        // message} body, but ApiException only surfaces the human `message` (see
        // ApiClient.ExtractErrorMessage) — the message text itself already names the
        // rejected language clearly, so a single validation_error code here is enough for
        // SEK-01's "clear error, not a silent failure" requirement.
        ApiException { StatusCode: 400 } apiEx => new SekErrorDto("validation_error", apiEx.Message),
        ApiException apiEx => new SekErrorDto("network_error", apiEx.Message),
        _ => new SekErrorDto("network_error", "Could not reach the server. Check your connection and try again."),
    };

    private sealed record BridgeRequest(string RequestId, string Method, JsonElement Payload);
    private sealed record BridgeResponse(string RequestId, bool Ok, object? Value, SekErrorDto? Error);
    private sealed record SekErrorDto(string Code, string Message);
    private sealed record SekUserContext(string UserId, string SessionToken, string Role, string CollegeId);
    private sealed record MountMessage(SekUserContext User, bool CanRun, bool CanEdit);
    private sealed record SekCodeSourceDto(string Language, string Content, string? Stdin, string? Filename);
    private sealed record RunPayload(SekCodeSourceDto Source);
    private sealed record SekCodeRunResultDto(string Stdout, string Stderr, int ExitCode, long DurationMs, bool TimedOut);
}
