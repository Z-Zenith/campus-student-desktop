using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using StudentDesktop.Services;
using Xunit;

namespace StudentDesktop.Tests;

public class ApiClientTests
{
    private class FakeHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
    }

    private static ApiClient NewClientWithFakeResponse(HttpStatusCode statusCode, string body)
    {
        var client = new ApiClient();
        var httpClient = new HttpClient(new FakeHandler(statusCode, body)) { BaseAddress = new System.Uri("http://localhost:8080") };
        typeof(ApiClient).GetField("_http", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(client, httpClient);
        return client;
    }

    // #158 — the backend returns {"error": "...", "message": "human text"} on failure;
    // ApiException.Message must surface that human text, not the raw JSON blob.
    [Fact]
    public async Task LoginAsync_SurfacesBackendMessage_NotRawJsonBody()
    {
        var client = NewClientWithFakeResponse(
            HttpStatusCode.Unauthorized,
            "{\"error\":\"invalid_password\",\"message\":\"Incorrect password.\"}");

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.LoginAsync("101", "wrong", "000000"));

        Assert.Equal("Incorrect password.", ex.Message);
        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task LoginAsync_FallsBackToRawBody_WhenResponseIsNotJson()
    {
        var client = NewClientWithFakeResponse(HttpStatusCode.InternalServerError, "Internal Server Error");

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.LoginAsync("101", "wrong", "000000"));

        Assert.Equal("Internal Server Error", ex.Message);
    }

    // GetMyTodosAsync (not GetMyCalendarAsync) is the read path for undated todos — this is
    // the client-side half of the fix for the "quick-add todos vanish" bug, so it must
    // deserialize an item with a null DueDate without throwing.
    [Fact]
    public async Task GetMyTodosAsync_DeserializesUndatedAndDatedTodos()
    {
        var client = NewClientWithFakeResponse(HttpStatusCode.OK,
            "[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"title\":\"No due date\",\"dueDate\":null,\"completed\":false,\"priority\":2,\"createdAt\":\"2026-07-01T00:00:00Z\"}," +
            "{\"id\":\"22222222-2222-2222-2222-222222222222\",\"title\":\"Has a due date\",\"dueDate\":\"2026-08-01T00:00:00Z\",\"completed\":false,\"priority\":0,\"createdAt\":\"2026-07-01T00:00:00Z\"}]");

        var todos = await client.GetMyTodosAsync();

        Assert.Equal(2, todos.Count);
        Assert.Contains(todos, t => t.DueDate is null && t.Priority == 2);
        Assert.Contains(todos, t => t.DueDate is not null);
    }

    [Fact]
    public async Task UpdateTodoAsync_DeserializesResponse()
    {
        var client = NewClientWithFakeResponse(HttpStatusCode.OK,
            "{\"id\":\"11111111-1111-1111-1111-111111111111\",\"title\":\"Renamed\",\"dueDate\":null,\"completed\":false,\"priority\":3,\"createdAt\":\"2026-07-01T00:00:00Z\"}");

        var updated = await client.UpdateTodoAsync(
            System.Guid.Parse("11111111-1111-1111-1111-111111111111"), "Renamed", null, 3);

        Assert.Equal("Renamed", updated.Title);
        Assert.Equal(3, updated.Priority);
    }
}
