using Aether.ModEngine.Lifecycle;
using Aether.PluginSystem;
using Aether.Services.Applications;
using Aether.Services.Automation;
using Aether.Core.Abstractions;
using Aether.ThemeEngine;
using Aether.WidgetEngine;

namespace Aether.Services.Commands;

public sealed class ModCommandSource(ModManager mods, INotificationSink notify) : ICommandSource
{
    public IEnumerable<CommandItem> GetCommands()
    {
        foreach (var m in mods.GetMods())
        {
            var verb = m.IsEnabled ? "Disable" : "Enable";
            yield return new CommandItem($"mod:toggle:{m.Id}", $"{verb} {m.Manifest.Name}", m.Manifest.Description, "Mods", m.Manifest.Icon ?? "", async () =>
            {
                var r = m.IsEnabled ? await mods.DisableAsync(m.Id) : await mods.EnableAsync(m.Id);
                notify.Notify(new AppNotification(m.Manifest.Name,
                    r.Status == ModActionStatus.NeedsConsent ? "Open Windows Mods to review and grant its permissions." : r.Message,
                    r.Succeeded ? NotificationSeverity.Success : NotificationSeverity.Warning));
            }) { Keywords = [.. m.Manifest.Tags, m.Manifest.Category.ToString()] };
            foreach (var action in m.Manifest.Actions)
                yield return new CommandItem($"mod:action:{m.Id}:{action.Id}", action.Label, m.Manifest.Name, "Mods", action.Glyph ?? "", async () =>
                {
                    var r = await mods.InvokeActionAsync(m.Id, action.Id);
                    notify.Notify(new AppNotification(action.Label, r.Describe(), r.Success ? NotificationSeverity.Success : NotificationSeverity.Error));
                }) { Keywords = m.Manifest.Tags };
        }
    }
}

public sealed class ThemeCommandSource(ThemeManager themes, INotificationSink notify) : ICommandSource
{
    public IEnumerable<CommandItem> GetCommands() => themes.GetThemes().Select(t =>
        new CommandItem($"theme:{t.Id}", $"Apply theme: {t.Name}", t.Description, "Themes", "", async () =>
        {
            var r = await themes.ApplyAsync(t.Id);
            notify.Notify(new AppNotification("Theme", r.Describe(), r.Success ? NotificationSeverity.Success : NotificationSeverity.Error));
        }) { Keywords = ["theme", "appearance", t.Palette.IsDark ? "dark" : "light"] });
}

public sealed class AppCommandSource(AppCatalog apps, INotificationSink notify) : ICommandSource
{
    public IEnumerable<CommandItem> GetCommands()
    {
        foreach (var a in apps.Cached)
            yield return new CommandItem($"app:{a.Id}", a.Name, $"{a.Category} · {a.SourceLabel}", "Applications", "", () =>
            {
                var r = apps.Launch(a);
                if (!r.Success) notify.Notify(new AppNotification(a.Name, r.Describe(), NotificationSeverity.Error));
                return Task.CompletedTask;
            }) { Keywords = ["launch", "open", "run"] };
        foreach (var p in apps.GetProfiles())
            yield return new CommandItem($"profile:{p.Id}", $"Launch profile: {p.Name}", p.Target, "Applications", "", () => { apps.Launch(p); return Task.CompletedTask; });
        foreach (var g in apps.GetGroups())
            yield return new CommandItem($"group:{g.Id}", $"Launch group: {g.Name}", $"{g.Items.Count} items", "Applications", g.Glyph, async () => await apps.LaunchGroupAsync(g));
    }
}

public sealed class WidgetCommandSource(WidgetManager widgets, INotificationSink notify) : ICommandSource
{
    public IEnumerable<CommandItem> GetCommands() => WidgetCatalog.All.Select(d =>
        new CommandItem($"widget:add:{d.TypeId}", $"Add widget: {d.Name}", d.Description, "Widgets", d.Glyph, () =>
        {
            widgets.AddWidget(d.TypeId);
            notify.Notify(new AppNotification("Widget added", $"{d.Name} was added to '{widgets.ActiveLayout.Name}'.", NotificationSeverity.Success));
            return Task.CompletedTask;
        }) { Keywords = ["widget", d.Category] })
        .Concat(widgets.GetLayouts().Select(l => new CommandItem($"widget:layout:{l.Id}", $"Switch widget layout: {l.Name}", $"{l.Widgets.Count} widgets", "Widgets", "",
            () => { widgets.ActivateLayout(l.Id); return Task.CompletedTask; })));
}

public sealed class AutomationCommandSource(AutomationEngine engine, INotificationSink notify) : ICommandSource
{
    public IEnumerable<CommandItem> GetCommands() => engine.GetRules().Select(r =>
        new CommandItem($"automation:{r.Id}", $"Run automation: {r.Name}", r.Description, "Automation", "", async () =>
        {
            var run = await engine.RunAsync(r, "Command palette", ignoreCooldown: true);
            notify.Notify(new AppNotification(r.Name, string.Join("\n", run.Log), run.Success ? NotificationSeverity.Success : NotificationSeverity.Error));
        }) { Keywords = ["rule", "profile", "run"] });
}

public sealed class PluginCommandSource(PluginHost plugins) : ICommandSource
{
    public IEnumerable<CommandItem> GetCommands() => plugins.Commands.Select(c =>
        new CommandItem($"plugin:{c.PluginId}:{c.Command.Id}", c.Command.Title, c.Command.Subtitle ?? c.PluginId, "Plugins", c.Command.Glyph,
            () => c.Command.Execute(CancellationToken.None)) { Keywords = [.. c.Command.Keywords] });
}
