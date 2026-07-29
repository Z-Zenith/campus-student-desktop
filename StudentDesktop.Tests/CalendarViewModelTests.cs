using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using StudentDesktop.Services;
using StudentDesktop.ViewModels;

namespace StudentDesktop.Tests;

public class CalendarViewModelTests
{
    // Routes by request path/method rather than returning one fixed body (unlike
    // ApiClientTests' single-response FakeHandler), since CalendarViewModel hits several
    // endpoints (calendar/mine, todos/mine, todos POST/PATCH) in a single test.
    private sealed class RoutingFakeHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Body)> Requests { get; } = [];
        public string TodosMineResponse { get; set; } = "[]";
        public string CalendarMineResponse { get; set; } = "{\"items\":[]}";
        public string CreateOrUpdateTodoResponse { get; set; } =
            "{\"id\":\"33333333-3333-3333-3333-333333333333\",\"title\":\"x\",\"dueDate\":null,\"completed\":false,\"priority\":0,\"createdAt\":\"2026-07-01T00:00:00Z\"}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add((request.Method, path, body));

            var response = path switch
            {
                "/api/v1/todos/mine" => TodosMineResponse,
                "/api/v1/calendar/mine" => CalendarMineResponse,
                "/api/v1/todos" => CreateOrUpdateTodoResponse,
                _ when path.StartsWith("/api/v1/todos/") => CreateOrUpdateTodoResponse,
                _ => "{}",
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) };
        }
    }

    private static (ApiClient Client, RoutingFakeHandler Handler) NewFakeClient()
    {
        var handler = new RoutingFakeHandler();
        var client = new ApiClient();
        var httpClient = new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:8080") };
        typeof(ApiClient).GetField("_http", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(client, httpClient);
        return (client, handler);
    }

    // Regression test for the fixed bug: the standalone Todos list must come from
    // GetMyTodosAsync (todos/mine), which includes undated todos — not from calendar/mine,
    // which omits them by design (#159). This is the client-side half of the fix; the
    // backend half is covered by CalendarControllerTests.MyTodos_ReturnsUndatedAndDatedTodos.
    [Fact]
    public async Task LoadTodosAsync_PopulatesUndatedAndDatedTodos()
    {
        var (client, handler) = NewFakeClient();
        handler.TodosMineResponse =
            "[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"title\":\"No due date\",\"dueDate\":null,\"completed\":false,\"priority\":0,\"createdAt\":\"2026-07-01T00:00:00Z\"}," +
            "{\"id\":\"22222222-2222-2222-2222-222222222222\",\"title\":\"Has a due date\",\"dueDate\":\"2026-08-01T00:00:00Z\",\"completed\":false,\"priority\":2,\"createdAt\":\"2026-07-01T00:00:00Z\"}]";
        var viewModel = new CalendarViewModel(client);

        await viewModel.LoadTodosCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Todos.Count);
        Assert.Contains(viewModel.Todos, t => t.DueDate is null);
        Assert.Contains(viewModel.Todos, t => t.DueDate is not null && t.Priority == 2);
    }

    [Fact]
    public async Task AddTodoAsync_SendsThePickedDueDateAndPriority_NotHardcodedNull()
    {
        var (client, handler) = NewFakeClient();
        var viewModel = new CalendarViewModel(client)
        {
            NewTodoTitle = "Finish lab report",
            NewTodoDueDate = new System.DateTime(2026, 8, 15),
            NewTodoPriority = 3,
        };

        await viewModel.AddTodoCommand.ExecuteAsync(null);

        var createRequest = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == "/api/v1/todos");
        using var json = JsonDocument.Parse(createRequest.Body!);
        Assert.Equal("Finish lab report", json.RootElement.GetProperty("title").GetString());
        Assert.False(json.RootElement.GetProperty("dueDate").ValueKind == JsonValueKind.Null);
        Assert.Equal(3, json.RootElement.GetProperty("priority").GetInt32());
    }

    [Fact]
    public void WeekNavigation_PreviousAndNext_MoveByExactlySevenDays()
    {
        var (client, _) = NewFakeClient();
        var viewModel = new CalendarViewModel(client);
        var initialMonday = viewModel.ViewedMonday;
        Assert.True(viewModel.IsCurrentWeek);

        viewModel.PreviousWeekCommand.Execute(null);
        Assert.Equal(initialMonday.AddDays(-7), viewModel.ViewedMonday);
        Assert.False(viewModel.IsCurrentWeek);

        viewModel.NextWeekCommand.Execute(null);
        viewModel.NextWeekCommand.Execute(null);
        Assert.Equal(initialMonday.AddDays(7), viewModel.ViewedMonday);
        Assert.False(viewModel.IsCurrentWeek);

        viewModel.GoToTodayCommand.Execute(null);
        Assert.Equal(initialMonday, viewModel.ViewedMonday);
        Assert.True(viewModel.IsCurrentWeek);
    }

    [Fact]
    public void WeekNavigation_RebuildsGridHeaderDatesForTheViewedWeek()
    {
        var (client, _) = NewFakeClient();
        var viewModel = new CalendarViewModel(client);
        var mondayHeaderBefore = viewModel.GridCells.Single(c => c.Row == 0 && c.Column == 1).SubText;

        viewModel.NextWeekCommand.Execute(null);

        var mondayHeaderAfter = viewModel.GridCells.Single(c => c.Row == 0 && c.Column == 1).SubText;
        Assert.NotEqual(mondayHeaderBefore, mondayHeaderAfter);
    }
}
