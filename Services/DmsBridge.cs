using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using StudentDesktop.Models;

namespace StudentDesktop.Services;

// SDA-24: bridges DMS-01's MessageInbox/MessageThreadView (hosted in a NativeWebView — see
// MessagesView) to the Backend API. Same postMessage/InvokeScript bridge protocol as
// SekBridge (SDA-19) — see that file for the shared rationale on why no request/response
// correlation bookkeeping is needed on this side, and why InvokeScript is a settable
// delegate rather than a NativeWebView reference (keeps this testable without a live WebView).
public sealed class DmsBridge(ApiClient apiClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public Func<string, Task>? InvokeScript { get; set; }

    // DMS's UserContext has no collegeId field (unlike SEK's) — nothing else to fake here.
    public async Task MountInboxAsync(Guid userId)
    {
        var user = new DmsUserContext(userId.ToString(), apiClient.Token ?? "", "student");
        if (InvokeScript is null)
        {
            return;
        }
        // __dmsHostMount(json: string) does JSON.parse(json) — needs a JSON *string*
        // argument, so the payload must be serialized twice: once to produce the JSON,
        // once more to turn that JSON into a quoted/escaped JS string literal. See
        // CodeBridge.MountAsync for the full rationale (same bug, same fix).
        var mountJson = JsonSerializer.Serialize(new MountMessage(user), JsonOptions);
        await InvokeScript($"window.__dmsHostMount({JsonSerializer.Serialize(mountJson)})");
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
                "listThreads" => await ListThreadsAsync(request.RequestId),
                "listMessages" => await ListMessagesAsync(request.RequestId, request.Payload),
                "sendMessage" => await SendMessageAsync(request.RequestId, request.Payload),
                _ => new BridgeResponse(request.RequestId, false, null,
                    new DmsErrorDto("validation_error", $"Unknown DMS bridge method '{request.Method}'.")),
            };
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException
            or JsonException or InvalidOperationException)
        {
            // #9: see SekBridge.HandleMessageAsync's identical fix — a malformed payload throws
            // JsonException/InvalidOperationException from inside the switch above, which used
            // to fall outside this filter and propagate past this fire-and-forget call
            // (MessagesView.OnWebMessageReceived) as an unobserved task exception, so
            // InvokeScript (the only reply channel) was never called and the JS-side request
            // promise hung forever instead of failing closed.
            response = new BridgeResponse(request.RequestId, false, null, MapError(ex));
        }

        if (InvokeScript is null)
        {
            return;
        }
        var responseJson = JsonSerializer.Serialize(response, JsonOptions);
        await InvokeScript($"window.__dmsHostReceive({JsonSerializer.Serialize(responseJson)})");
    }

    private async Task<BridgeResponse> ListThreadsAsync(string requestId)
    {
        var threads = await apiClient.GetMessageThreadsAsync();
        return new BridgeResponse(requestId, true, threads, null);
    }

    private async Task<BridgeResponse> ListMessagesAsync(string requestId, JsonElement payload)
    {
        var list = payload.Deserialize<ThreadIdPayload>(JsonOptions)
            ?? throw new InvalidOperationException("Malformed 'listMessages' payload.");
        var messages = await apiClient.GetThreadMessagesAsync(list.ThreadId);
        return new BridgeResponse(requestId, true, messages, null);
    }

    private async Task<BridgeResponse> SendMessageAsync(string requestId, JsonElement payload)
    {
        var send = payload.Deserialize<SendPayload>(JsonOptions)
            ?? throw new InvalidOperationException("Malformed 'sendMessage' payload.");
        var sent = await apiClient.SendMessageAsync(send.ThreadId, send.Content);
        return new BridgeResponse(requestId, true, sent, null);
    }

    // Matches DMS's DmsErrorCode union (packages/direct-messaging/src/types.ts) exactly,
    // same mapping shape as SekBridge.MapError.
    private static DmsErrorDto MapError(Exception ex) => ex switch
    {
        ApiException { StatusCode: 404 } => new DmsErrorDto("thread_not_found", "Conversation not found."),
        ApiException { StatusCode: 403 } => new DmsErrorDto("not_a_participant", "You're not a participant in this conversation."),
        ApiException { StatusCode: 400 } apiEx => new DmsErrorDto("validation_error", apiEx.Message),
        ApiException apiEx => new DmsErrorDto("network_error", apiEx.Message),
        // #9: deserialization/shape failures are a client-payload problem, not a network one —
        // map them to validation_error like the ApiException 400 case above.
        JsonException or InvalidOperationException => new DmsErrorDto("validation_error", "Malformed request."),
        _ => new DmsErrorDto("network_error", "Could not reach the server. Check your connection and try again."),
    };

    private sealed record BridgeRequest(string RequestId, string Method, JsonElement Payload);
    private sealed record BridgeResponse(string RequestId, bool Ok, object? Value, DmsErrorDto? Error);
    private sealed record DmsErrorDto(string Code, string Message);
    private sealed record DmsUserContext(string UserId, string SessionToken, string Role);
    private sealed record MountMessage(DmsUserContext User);
    private sealed record ThreadIdPayload(Guid ThreadId);
    private sealed record SendPayload(Guid ThreadId, string Content);
}
