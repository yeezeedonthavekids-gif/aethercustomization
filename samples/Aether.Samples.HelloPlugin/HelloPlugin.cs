using System.Text.Json.Nodes;
using Aether.SDK;

namespace Aether.Samples.HelloPlugin;

/// <summary>A minimal plugin: one palette command, one event subscription, persistent storage.</summary>
public sealed class HelloPlugin : IAetherPlugin
{
    private readonly List<IDisposable> _registrations = [];
    private IPluginContext? _context;

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _context = context;
        _registrations.Add(context.RegisterCommand(new PluginCommand
        {
            Id = "say-hello",
            Title = "Say hello",
            Subtitle = "Hello AETHER sample plugin",
            Keywords = ["greeting", "sample"],
            Execute = _ =>
            {
                var count = int.TryParse(context.Storage.Get("count"), out var c) ? c + 1 : 1;
                context.Storage.Set("count", count.ToString());
                context.Notify("Hello!", $"Hi {Name}, this is greeting number {count}.");
                return Task.CompletedTask;
            },
        }));
        _registrations.Add(context.Subscribe(PluginEvents.ThemeApplied, e =>
            context.Notify("Nice theme", $"{Name}, you switched to {e.Data.GetValueOrDefault("name", "a new theme")}.")));
        context.Log.Info("Hello plugin initialized.");
        return Task.CompletedTask;
    }

    private string Name => _context?.Settings["name"]?.GetValue<string>() ?? "friend";

    public Task OnSettingsChangedAsync(JsonObject settings, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ShutdownAsync(CancellationToken cancellationToken)
    {
        foreach (var r in _registrations) r.Dispose();
        _registrations.Clear();
        return Task.CompletedTask;
    }
}
