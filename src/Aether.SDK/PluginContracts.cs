using System.Text.Json.Nodes;
using Aether.Core.Configuration;

namespace Aether.SDK;

/// <summary>Entry point every AETHER plugin implements. The type must have a public parameterless constructor.</summary>
public interface IAetherPlugin
{
    /// <summary>Called once after the plugin is loaded. Register commands and subscriptions here.</summary>
    Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken);

    /// <summary>Called before the plugin is unloaded. Release every resource.</summary>
    Task ShutdownAsync(CancellationToken cancellationToken);

    /// <summary>Called when the user changes the plugin's settings.</summary>
    Task OnSettingsChangedAsync(JsonObject settings, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>The plugin's view of AETHER. Every member is gated by a declared and user-granted capability.</summary>
public interface IPluginContext
{
    string PluginId { get; }
    string HostVersion { get; }
    JsonObject Settings { get; }
    IPluginLogger Log { get; }
    /// <summary>Plugin-private persistent key/value storage. Always available.</summary>
    IPluginStorage Storage { get; }
    bool HasCapability(string capability);
    /// <summary>Requires <see cref="PluginCapabilities.Commands"/>.</summary>
    IDisposable RegisterCommand(PluginCommand command);
    /// <summary>Requires <see cref="PluginCapabilities.Events"/>.</summary>
    IDisposable Subscribe(string eventName, Action<PluginEvent> handler);
    /// <summary>Requires <see cref="PluginCapabilities.Notifications"/>.</summary>
    void Notify(string title, string message);
    /// <summary>Requires <see cref="PluginCapabilities.Network"/>. The client is owned by the host.</summary>
    HttpClient GetHttpClient();
}

public interface IPluginLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}

public interface IPluginStorage
{
    string? Get(string key);
    void Set(string key, string? value);
}

public sealed class PluginCommand
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string Glyph { get; init; } = "";
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public required Func<CancellationToken, Task> Execute { get; init; }
}

public sealed record PluginEvent(string Name, IReadOnlyDictionary<string, string> Data);

/// <summary>Event names plugins can subscribe to.</summary>
public static class PluginEvents
{
    public const string ThemeApplied = "theme.applied";
    public const string ModChanged = "mod.changed";
    public const string AutomationRuleRan = "automation.ruleRan";
}

/// <summary>Capabilities a plugin can declare in its manifest.</summary>
public static class PluginCapabilities
{
    public const string Commands = "plugin.commands";
    public const string Events = "plugin.events";
    public const string Notifications = "notifications.show";
    public const string Network = "network.access";

    public static IReadOnlyDictionary<string, string> Descriptions { get; } = new Dictionary<string, string>
    {
        [Commands] = "Add commands to the command palette.",
        [Events] = "Be notified when themes, mods or automation rules change.",
        [Notifications] = "Show AETHER notifications.",
        [Network] = "Access the internet.",
    };
}

/// <summary>aether-plugin.json</summary>
public sealed class PluginManifest
{
    public const string FileName = "aether-plugin.json";
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Author { get; init; }
    public string Description { get; init; } = "";
    /// <summary>Assembly file name inside the package, e.g. "Contoso.Plugin.dll".</summary>
    public required string EntryAssembly { get; init; }
    /// <summary>Fully qualified type implementing <see cref="IAetherPlugin"/>.</summary>
    public required string EntryType { get; init; }
    public string MinHostVersion { get; init; } = "0.1.0";
    public List<string> Capabilities { get; init; } = [];
    public List<PluginDependency> Dependencies { get; init; } = [];
    public List<SettingDefinition> Settings { get; init; } = [];
    public string? Homepage { get; init; }
}

public sealed class PluginDependency
{
    public required string Id { get; init; }
    public string VersionRange { get; init; } = "*";
}
