using System.Collections.Concurrent;

namespace MTGTournamentDashboard.Sync;

public enum SyncLogLevel { Info, Warn, Error, Success }

public sealed record SyncLogEntry(DateTime TimestampUtc, SyncLogLevel Level, string Message);

public sealed class SyncProgress
{
    private const int MaxBufferedLines = 500;

    private readonly ConcurrentQueue<SyncLogEntry> _buffer = new();
    private readonly object _stateLock = new();

    public event Action<SyncLogEntry>? OnEntry;
    public event Action? OnStateChanged;

    public bool IsRunning { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? FinishedAtUtc { get; private set; }
    public string? CurrentStep { get; private set; }
    public string? LastError { get; private set; }

    public IReadOnlyCollection<SyncLogEntry> Snapshot() => _buffer.ToArray();

    public void BeginRun(string step)
    {
        lock (_stateLock)
        {
            _buffer.Clear();
            IsRunning = true;
            StartedAtUtc = DateTime.UtcNow;
            FinishedAtUtc = null;
            CurrentStep = step;
            LastError = null;
        }
        OnStateChanged?.Invoke();
        Info(step);
    }

    public void SetStep(string step)
    {
        lock (_stateLock) { CurrentStep = step; }
        Info(step);
        OnStateChanged?.Invoke();
    }

    public void EndRun(bool success, string? error = null)
    {
        lock (_stateLock)
        {
            IsRunning = false;
            FinishedAtUtc = DateTime.UtcNow;
            CurrentStep = success ? "Completado" : "Error";
            LastError = error;
        }
        if (success) Success("Sync completado");
        else Error(error ?? "Sync abortado");
        OnStateChanged?.Invoke();
    }

    public void Info(string message) => Append(SyncLogLevel.Info, message);
    public void Warn(string message) => Append(SyncLogLevel.Warn, message);
    public void Error(string message) => Append(SyncLogLevel.Error, message);
    public void Success(string message) => Append(SyncLogLevel.Success, message);

    private void Append(SyncLogLevel level, string message)
    {
        var entry = new SyncLogEntry(DateTime.UtcNow, level, message);
        _buffer.Enqueue(entry);
        while (_buffer.Count > MaxBufferedLines && _buffer.TryDequeue(out _)) { }
        OnEntry?.Invoke(entry);
    }
}
