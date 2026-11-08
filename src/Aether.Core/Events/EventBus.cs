using System.Collections.Concurrent;

namespace Aether.Core.Events;

public interface IEventBus
{
    IDisposable Subscribe<TEvent>(Action<TEvent> handler);
    void Publish<TEvent>(TEvent @event);
}

/// <summary>
/// In-process publish/subscribe bus. Handlers run synchronously on the publisher's thread; a failing
/// handler is isolated and reported through <see cref="HandlerFailed"/> instead of breaking publishers.
/// </summary>
public sealed class EventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, ImmutableHandlers> _handlers = new();

    public event Action<Exception>? HandlerFailed;

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Delegate d = handler;
        _handlers.AddOrUpdate(typeof(TEvent), _ => new ImmutableHandlers([d]), (_, h) => h.Add(d));
        return new Subscription(() => _handlers.AddOrUpdate(typeof(TEvent), _ => new ImmutableHandlers([]), (_, h) => h.Remove(d)));
    }

    public void Publish<TEvent>(TEvent @event)
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var handlers)) return;
        foreach (var h in handlers.Items)
        {
            try { ((Action<TEvent>)h)(@event); }
            catch (Exception ex) { HandlerFailed?.Invoke(ex); }
        }
    }

    private sealed record ImmutableHandlers(Delegate[] Items)
    {
        public ImmutableHandlers Add(Delegate d) => new([.. Items, d]);
        public ImmutableHandlers Remove(Delegate d) => new(Items.Where(x => x != d).ToArray());
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
