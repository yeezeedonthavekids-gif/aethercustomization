namespace Aether.Core.Abstractions;

public enum NotificationSeverity { Info, Success, Warning, Error }

public sealed record AppNotification(string Title, string Message, NotificationSeverity Severity = NotificationSeverity.Info, string? ActionLabel = null, Action? Action = null);

/// <summary>Services publish user-visible toasts through this sink; the shell renders them.</summary>
public interface INotificationSink
{
    void Notify(AppNotification notification);
}

/// <summary>Collects notifications until the UI attaches. Thread-safe.</summary>
public sealed class NotificationHub : INotificationSink
{
    private readonly object _gate = new();
    private readonly List<AppNotification> _pending = [];
    private Action<AppNotification>? _handler;

    public void Attach(Action<AppNotification> handler)
    {
        List<AppNotification> flush;
        lock (_gate)
        {
            _handler = handler;
            flush = [.. _pending];
            _pending.Clear();
        }
        foreach (var n in flush) handler(n);
    }

    public void Notify(AppNotification notification)
    {
        Action<AppNotification>? handler;
        lock (_gate)
        {
            handler = _handler;
            if (handler is null) { _pending.Add(notification); return; }
        }
        handler(notification);
    }
}
