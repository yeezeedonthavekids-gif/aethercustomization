using Aether.Core.Abstractions;
using Aether.ModEngine.Execution;
using Aether.ModEngine.Lifecycle;
using Aether.ModEngine.Manifest;
using Aether.Services.Backup;
using Aether.Services.Settings;

namespace Aether.Presentation.Infrastructure;

/// <summary>
/// The single path every page uses to enable/disable mods: explains permissions and shows scripts before consent,
/// confirms risky changes, creates an AETHER backup first when configured, and reports every outcome.
/// </summary>
public sealed class ModInteraction(ModManager mods, IDialogService dialogs, INotificationSink notifications, ISettingsService settings, BackupService backups)
{
    public async Task<bool> SetEnabledAsync(string id, bool enable)
    {
        var mod = mods.GetMod(id);
        if (mod is null) return false;
        ModActionResult result;
        if (!enable)
        {
            result = await mods.DisableAsync(id);
            Notify(mod.Manifest.Name, result);
            return result.Succeeded;
        }

        if (mod.Manifest.Risk == RiskLevel.High || (mod.Manifest.Risk == RiskLevel.Medium && mod.Manifest.Restart != RestartRequirement.None))
        {
            if (settings.Current.ConfirmRiskyChanges)
            {
                var changes = mods.PreviewChanges(id);
                var ok = await dialogs.ConfirmAsync($"Enable {mod.Manifest.Name}?",
                    $"{mod.Manifest.Description}\n\nWhat will change:\n• {string.Join("\n• ", changes)}\n\n{mod.Manifest.Documentation.Uninstall ?? "Disabling the mod reverts these changes."}",
                    "Enable");
                if (!ok) return false;
            }
            if (settings.Current.CreateBackupBeforeRiskyChanges)
            {
                var b = await backups.CreateAsync(BackupKind.Full, $"Before enabling {mod.Manifest.Name}", automatic: true);
                if (!b.Success) notifications.Notify(new AppNotification("Backup skipped", b.ToResult().Describe(), NotificationSeverity.Warning));
            }
        }

        result = await mods.EnableAsync(id);
        if (result.Status == ModActionStatus.NeedsConsent)
        {
            var scripts = mods.GetScripts(id);
            var preview = scripts is { } s ? $"SHA-256 {s.Hash}\n\n# apply\n{s.Apply}\n\n# revert\n{s.Revert}" : null;
            var infos = result.MissingPermissions.Select(ModPermissions.Describe).DistinctBy(p => p.Id).ToList();
            if (!await dialogs.RequestPermissionsAsync(mod.Manifest.Name, infos, mod.Manifest.Risk, preview)) return false;
            mods.GrantPermissions(id, result.MissingPermissions);
            result = await mods.EnableAsync(id);
        }
        Notify(mod.Manifest.Name, result);
        return result.Succeeded;
    }

    public async Task InvokeActionAsync(string id, ModAction action)
    {
        if (action.RequiresConfirmation && !await dialogs.ConfirmAsync(action.Label, action.Description ?? "Continue?", action.Label)) return;
        var missing = mods.GetMissingPermissions(id);
        if (missing.Count > 0)
        {
            var mod = mods.GetMod(id)!;
            if (!await dialogs.RequestPermissionsAsync(mod.Manifest.Name, missing.Select(ModPermissions.Describe).ToList(), mod.Manifest.Risk)) return;
            mods.GrantPermissions(id, missing);
        }
        var r = await mods.InvokeActionAsync(id, action.Id);
        notifications.Notify(new AppNotification(action.Label, r.Describe(), r.Success ? NotificationSeverity.Success : NotificationSeverity.Error));
    }

    private void Notify(string name, ModActionResult r)
    {
        var text = r.Details.Count > 0 && !r.Succeeded ? $"{r.Message}\n• {string.Join("\n• ", r.Details)}" : r.Message;
        notifications.Notify(new AppNotification(name, text, r.Succeeded ? NotificationSeverity.Success : r.Status == ModActionStatus.Blocked ? NotificationSeverity.Warning : NotificationSeverity.Error));
    }
}
