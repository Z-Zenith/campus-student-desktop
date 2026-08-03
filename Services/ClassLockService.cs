using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Timers;
using StudentDesktop.Models;

namespace StudentDesktop.Services;

// SDA-01: while a scheduled class session is active (per the student's timetable) the
// app must go full-screen and block app-switching; outside class hours there must be
// zero restriction. This service polls the same /calendar/mine endpoint the calendar
// view already uses, re-evaluates on a short timer, and raises LockStateChanged only
// when the locked/unlocked state actually flips so the UI layer (MainWindow) can react.
public sealed class ClassLockService : IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly TimeSpan _refreshInterval;
    private readonly Timer _timer;
    private readonly object _gate = new();

    private List<CalendarItemDto> _classSessions = [];
    private DateTime _lastRefreshUtc = DateTime.MinValue;
    private bool _isLocked;
    private bool _disposed;

    public event EventHandler<bool>? LockStateChanged;

    public bool IsLocked
    {
        get { lock (_gate) return _isLocked; }
    }

    // checkInterval: how often we re-evaluate the cached schedule against the clock.
    // refreshInterval: how often we re-fetch the schedule from the server (schedules
    // rarely change mid-session, so this doesn't need to be as frequent).
    public ClassLockService(ApiClient apiClient, TimeSpan? checkInterval = null, TimeSpan? refreshInterval = null)
    {
        _apiClient = apiClient;
        _refreshInterval = refreshInterval ?? TimeSpan.FromMinutes(5);
        _timer = new Timer((checkInterval ?? TimeSpan.FromSeconds(15)).TotalMilliseconds)
        {
            AutoReset = true,
        };
        _timer.Elapsed += OnTimerElapsed;
    }

    public void Start()
    {
        _timer.Start();
        _ = TickAsync();
    }

    public void Stop()
    {
        _timer.Stop();
        SetLocked(false);
    }

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e) => _ = TickAsync();

    private async Task TickAsync()
    {
        try
        {
            if (DateTime.UtcNow - _lastRefreshUtc >= _refreshInterval)
            {
                await RefreshAsync();
            }
            Evaluate();
        }
        catch
        {
            // A background poller must never crash the app — worst case we keep
            // using the last-known schedule until the next tick.
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var response = await _apiClient.GetMyCalendarAsync();
            lock (_gate)
            {
                _classSessions = response.Items.Where(i => i.Kind == "class_session").ToList();
            }
            _lastRefreshUtc = DateTime.UtcNow;
        }
        catch (ApiException)
        {
            // Best-effort — keep the previously-known schedule.
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
        }
    }

    private void Evaluate()
    {
        List<CalendarItemDto> snapshot;
        lock (_gate) snapshot = _classSessions;
        // #7: CalendarItemDto.Start/.End are server-sourced UTC timestamps (see
        // Models/ApiModels.cs), so the "now" compared against them must be UTC too —
        // DateTime.Now (local wall-clock) would engage/disengage the SDA-01 lock offset by
        // the machine's local UTC delta. Matches every other comparison against
        // server-sourced times in this codebase (RefreshAsync's _lastRefreshUtc above,
        // AssignmentAutoSubmitService, UsageTelemetryService).
        SetLocked(ClassScheduleEvaluator.IsClassInSession(snapshot, DateTime.UtcNow));
    }

    private void SetLocked(bool locked)
    {
        lock (_gate)
        {
            if (_isLocked == locked)
            {
                return;
            }
            _isLocked = locked;
        }
        LockStateChanged?.Invoke(this, locked);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _timer.Elapsed -= OnTimerElapsed;
        _timer.Dispose();
    }
}
