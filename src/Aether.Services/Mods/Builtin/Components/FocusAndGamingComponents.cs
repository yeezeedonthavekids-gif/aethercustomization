using Aether.Core.Abstractions;
using Aether.Core.Configuration;
using Aether.ModEngine.Execution;
using Aether.ModEngine.Manifest;
using Aether.Services.Applications;
using Aether.Services.Platform;
using Aether.Storage;
using Microsoft.Extensions.Logging;

namespace Aether.Services.Mods.Builtin.Components;

/// <summary>Mod 14 — a timed distraction-free session that restores everything afterwards.</summary>
public sealed class FocusModeComponent(TimeProvider time) : ModComponentBase
{
    private IDesktopController? _desktop;
    private bool? _iconsBefore;
    private bool? _autoHideBefore;
    private IReadOnlyList<nint> _minimized = [];
    private CancellationTokenSource? _timer;
    private DateTimeOffset? _endsAt;

    public override string? Status => _endsAt is { } end ? $"Focus until {end.ToLocalTime():HH:mm}" : "Focus on";

    public override Task StartAsync(ModComponentContext context, CancellationToken ct)
    {
        _desktop = context.GetService<IDesktopController>(ModPermissions.WindowManage);
        var s = context.Settings;
        if (s.GetBool("hideIcons", true)) { _iconsBefore = _desktop.AreDesktopIconsVisible(); _desktop.SetDesktopIconsVisible(false); }
        if (s.GetBool("autoHideTaskbar", true)) { _autoHideBefore = _desktop.IsTaskbarAutoHide(); _desktop.SetTaskbarAutoHide(true); }
        if (s.GetBool("minimizeOthers")) _minimized = _desktop.MinimizeOtherWindows();

        var minutes = s.GetDouble("minutes", 25);
        if (minutes > 0)
        {
            _endsAt = time.GetUtcNow().AddMinutes(minutes);
            _timer = new CancellationTokenSource();
            var token = _timer.Token;
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(TimeSpan.FromMinutes(minutes), time, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                if (context.Has(ModPermissions.Notifications))
                    context.GetService<INotificationSink>(ModPermissions.Notifications)
                        .Notify(new AppNotification("Focus session complete", $"Nice work — {minutes:0} minutes of focus.", NotificationSeverity.Success));
                if (context.RequestDisable is { } disable) await disable().ConfigureAwait(false);
            }, token);
        }
        return Task.CompletedTask;
    }

    public override Task StopAsync(ModComponentContext context, CancellationToken ct)
    {
        _timer?.Cancel();
        _timer = null;
        _endsAt = null;
        if (_desktop is null) return Task.CompletedTask;
        if (_iconsBefore is { } icons) _desktop.SetDesktopIconsVisible(icons);
        if (_autoHideBefore is { } autoHide) _desktop.SetTaskbarAutoHide(autoHide);
        if (_minimized.Count > 0) _desktop.RestoreWindows(_minimized);
        _iconsBefore = null; _autoHideBefore = null; _minimized = [];
        return Task.CompletedTask;
    }
}

/// <summary>Mod 15 — Game Mode, power plan and reduced animations, all restored on disable.</summary>
public sealed class GamingProfileComponent(PowerPlanService power, IDocumentStore store) : ModComponentBase
{
    private const string GameBar = @"Software\Microsoft\GameBar";
    private sealed record SavedPlan(Guid Id);
    private IDesktopController? _desktop;
    private bool? _iconsBefore;
    private string? _status;
    public override string? Status => _status;

    public override async Task StartAsync(ModComponentContext context, CancellationToken ct) => await ApplyAsync(context, ct).ConfigureAwait(false);
    public override async Task OnSettingsChangedAsync(ModComponentContext context, CancellationToken ct) => await ApplyAsync(context, ct).ConfigureAwait(false);

    private async Task ApplyAsync(ModComponentContext context, CancellationToken ct)
    {
        var s = context.Settings;
        var notes = new List<string>();
        if (s.GetBool("gameMode", true))
        {
            context.Registry.SetValue(RegistryRoot.CurrentUser, GameBar, "AutoGameModeEnabled", RegistryData.Dword(1));
            context.Registry.SetValue(RegistryRoot.CurrentUser, GameBar, "AllowAutoGameMode", RegistryData.Dword(1));
            notes.Add("Game Mode on");
        }
        if (s.GetBool("reduceAnimations", true))
        {
            context.SetFlag(SystemFlag.MinimizeMaximizeAnimation, false);
            context.SetFlag(SystemFlag.ClientAreaAnimation, false);
            context.SetFlag(SystemFlag.MenuAnimation, false);
            notes.Add("animations reduced");
        }

        var plan = s.GetString("powerPlan", "high");
        if (plan != "keep")
        {
            context.Require(ModPermissions.Power);
            var plans = await power.GetPlansAsync(ct).ConfigureAwait(false);
            var active = plans.FirstOrDefault(p => p.IsActive);
            var target = plan == "ultimate" && plans.Any(p => p.Id == PowerPlanService.Ultimate) ? PowerPlanService.Ultimate : PowerPlanService.HighPerformance;
            if (plans.All(p => p.Id != target))
                notes.Add("High performance plan is not available on this PC");
            else
            {
                if (active is not null && store.Get<SavedPlan>("gaming-profile", "previous-plan") is null)
                    store.Put("gaming-profile", "previous-plan", new SavedPlan(active.Id));
                if (active?.Id != target) await power.SetActiveAsync(target, ct).ConfigureAwait(false);
                notes.Add(plans.First(p => p.Id == target).Name);
            }
        }

        if (s.GetBool("hideIcons"))
        {
            _desktop ??= context.GetService<IDesktopController>(ModPermissions.RegistryUser);
            _iconsBefore ??= _desktop.AreDesktopIconsVisible();
            _desktop.SetDesktopIconsVisible(false);
        }
        else if (_iconsBefore is { } before && _desktop is not null) { _desktop.SetDesktopIconsVisible(before); _iconsBefore = null; }
        _status = string.Join(" · ", notes);
    }

    public override async Task StopAsync(ModComponentContext context, CancellationToken ct)
    {
        if (store.Get<SavedPlan>("gaming-profile", "previous-plan") is { } saved)
        {
            try { await power.SetActiveAsync(saved.Id, ct).ConfigureAwait(false); }
            catch (Exception ex) { context.Logger.LogWarning(ex, "Could not restore power plan"); throw; }
            store.Delete("gaming-profile", "previous-plan");
        }
        if (_iconsBefore is { } before && _desktop is not null) _desktop.SetDesktopIconsVisible(before);
        _iconsBefore = null;
    }
}
