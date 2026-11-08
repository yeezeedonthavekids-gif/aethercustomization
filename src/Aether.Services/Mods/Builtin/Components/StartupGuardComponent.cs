using Aether.Core.Abstractions;
using Aether.Core.Configuration;
using Aether.ModEngine.Execution;
using Aether.ModEngine.Manifest;
using Aether.Services.Applications;

namespace Aether.Services.Mods.Builtin.Components;

/// <summary>Mod 11 — detects new startup entries; optionally disables new per-user ones until approved.</summary>
public sealed class StartupGuardComponent(StartupManager startup) : LoopingComponent
{
    private HashSet<string> _known = [];
    private int _blocked;
    public override string? Status => $"Watching {_known.Count} startup entries{(_blocked > 0 ? $" · {_blocked} blocked" : "")}";

    protected override TimeSpan Interval(ModComponentContext context) => TimeSpan.FromSeconds(Math.Max(10, context.Settings.GetDouble("intervalSeconds", 30)));

    public override Task StartAsync(ModComponentContext context, CancellationToken ct)
    {
        context.Require(ModPermissions.StartupManage);
        _known = startup.GetEntries().Select(e => e.Id).ToHashSet();
        StartLoop(context);
        return Task.CompletedTask;
    }

    protected override Task TickAsync(ModComponentContext context, CancellationToken ct)
    {
        var entries = startup.GetEntries();
        foreach (var entry in entries.Where(e => !_known.Contains(e.Id)))
        {
            var blocked = false;
            if (context.Settings.GetBool("blockNew") && entry.Enabled && !entry.RequiresAdmin)
            {
                blocked = startup.SetEnabled(entry, false, context.Registry).Success;
                if (blocked) _blocked++;
            }
            if (context.Settings.GetBool("notifyNew", true) && context.Has(ModPermissions.Notifications))
                context.GetService<INotificationSink>(ModPermissions.Notifications).Notify(new AppNotification(
                    blocked ? "New startup program blocked" : "New startup program",
                    $"{entry.Name} added itself to startup ({entry.SourceLabel}).{(blocked ? " It was disabled until you approve it in Applications ▸ Startup." : "")}",
                    blocked ? NotificationSeverity.Warning : NotificationSeverity.Info));
        }
        _known = entries.Select(e => e.Id).ToHashSet();
        return Task.CompletedTask;
    }
}
