using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Aether.Core.Abstractions;
using Aether.Core.Diagnostics;
using Aether.ModEngine.Execution;
using Aether.ModEngine.Lifecycle;
using Aether.ModEngine.Manifest;
using Aether.ModEngine.Packaging;
using Aether.ModEngine.Security;
using Aether.Tests.Fakes;

namespace Aether.Tests.ModEngine;

public class ModManagerTests
{
    private const string Adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    [Fact]
    public async Task Enable_requires_consent_then_applies_and_disable_restores_originals()
    {
        using var h = new ModEngineHarness(TestManifests.Extensions());
        h.Registry.SetValue(RegistryRoot.CurrentUser, Adv, "HideFileExt", RegistryData.Dword(1));

        var first = await h.Manager.EnableAsync("test.file-extensions");
        Assert.Equal(ModActionStatus.NeedsConsent, first.Status);
        Assert.Contains("registry.user", first.MissingPermissions);
        Assert.Equal(1, (int)h.Registry.GetValue(RegistryRoot.CurrentUser, Adv, "HideFileExt")!.Data);

        h.Manager.GrantPermissions("test.file-extensions", first.MissingPermissions);
        var enabled = await h.Manager.EnableAsync("test.file-extensions");
        Assert.True(enabled.Succeeded, enabled.Message);
        Assert.Equal(0, (int)h.Registry.GetValue(RegistryRoot.CurrentUser, Adv, "HideFileExt")!.Data);
        Assert.Null(h.Registry.GetValue(RegistryRoot.CurrentUser, Adv, "ShowSuperHidden"));
        Assert.Contains("ShellState", h.SystemSettings.Broadcasts);
        Assert.True(h.Manager.GetMod("test.file-extensions")!.IsEnabled);

        var disabled = await h.Manager.DisableAsync("test.file-extensions");
        Assert.True(disabled.Succeeded, disabled.Message);
        Assert.Equal(1, (int)h.Registry.GetValue(RegistryRoot.CurrentUser, Adv, "HideFileExt")!.Data);
        Assert.Empty(h.Snapshots.List("test.file-extensions"));
    }

    [Fact]
    public async Task Values_created_by_a_mod_are_deleted_on_disable()
    {
        using var h = new ModEngineHarness(TestManifests.Extensions());
        h.Manager.GrantPermissions("test.file-extensions", ["registry.user"]);
        await h.Manager.UpdateSettingsAsync("test.file-extensions", new JsonObject { ["superHidden"] = true });
        Assert.True((await h.Manager.EnableAsync("test.file-extensions")).Succeeded);
        Assert.Equal(1, (int)h.Registry.GetValue(RegistryRoot.CurrentUser, Adv, "ShowSuperHidden")!.Data);

        await h.Manager.DisableAsync("test.file-extensions");
        Assert.Null(h.Registry.GetValue(RegistryRoot.CurrentUser, Adv, "ShowSuperHidden"));
        Assert.Null(h.Registry.GetValue(RegistryRoot.CurrentUser, Adv, "HideFileExt"));
        Assert.False(h.Registry.KeyExists(RegistryRoot.CurrentUser, Adv));
    }

    [Fact]
    public async Task Changing_settings_while_enabled_reverts_targets_that_drop_out_of_the_plan()
    {
        using var h = new ModEngineHarness(TestManifests.Extensions());
        h.Registry.SetValue(RegistryRoot.CurrentUser, Adv, "ShowSuperHidden", RegistryData.Dword(0));
        h.Manager.GrantPermissions("test.file-extensions", ["registry.user"]);
        await h.Manager.UpdateSettingsAsync("test.file-extensions", new JsonObject { ["superHidden"] = true });
        await h.Manager.EnableAsync("test.file-extensions");
        Assert.Equal(1, (int)h.Registry.GetValue(RegistryRoot.CurrentUser, Adv, "ShowSuperHidden")!.Data);

        var r = await h.Manager.UpdateSettingsAsync("test.file-extensions", new JsonObject { ["superHidden"] = false, ["show"] = false });
        Assert.True(r.Succeeded, r.Message);
        Assert.Equal(0, (int)h.Registry.GetValue(RegistryRoot.CurrentUser, Adv, "ShowSuperHidden")!.Data);
        Assert.Equal(1, (int)h.Registry.GetValue(RegistryRoot.CurrentUser, Adv, "HideFileExt")!.Data);
    }

    [Fact]
    public async Task A_failing_step_rolls_back_everything_applied_in_the_transaction()
    {
        using var h = new ModEngineHarness(TestManifests.Animations());
        h.Registry.SetValue(RegistryRoot.CurrentUser, @"Control Panel\International", "sShortTime", RegistryData.Str("h:mm tt"));
        h.Registry.FailOnName = "sTimeFormat";
        h.Manager.GrantPermissions("test.animations", ["system.parameters", "registry.user"]);

        var r = await h.Manager.EnableAsync("test.animations");

        Assert.Equal(ModActionStatus.Failed, r.Status);
        Assert.Contains("rolled back", r.Message);
        Assert.Equal("h:mm tt", h.Registry.GetValue(RegistryRoot.CurrentUser, @"Control Panel\International", "sShortTime")!.Data);
        Assert.True(h.SystemSettings.Flags[SystemFlag.ClientAreaAnimation]);
        Assert.False(h.Manager.GetMod("test.animations")!.IsEnabled);
    }

    [Fact]
    public async Task Invalid_settings_are_rejected_without_touching_the_system()
    {
        using var h = new ModEngineHarness(TestManifests.Extensions());
        var r = await h.Manager.UpdateSettingsAsync("test.file-extensions", new JsonObject { ["show"] = "yes please" });
        Assert.Equal(ModActionStatus.Blocked, r.Status);
        Assert.Equal(0, h.Registry.Writes);
    }

    [Fact]
    public async Task Incompatible_mods_are_blocked()
    {
        var json = TestManifests.ExtensionsJson.Replace("\"permissions\"", "\"compatibility\": { \"minBuild\": 99999 }, \"permissions\"");
        using var h = new ModEngineHarness(ManifestSerializer.Parse(json));
        h.Manager.GrantPermissions("test.file-extensions", ["registry.user"]);
        var r = await h.Manager.EnableAsync("test.file-extensions");
        Assert.Equal(ModActionStatus.Blocked, r.Status);
        Assert.Equal(ModStatus.Incompatible, h.Manager.GetMod("test.file-extensions")!.Status);
    }

    [Fact]
    public async Task Restart_reapplies_enabled_mods_to_repair_drift()
    {
        using var h = new ModEngineHarness(TestManifests.Extensions());
        h.Manager.GrantPermissions("test.file-extensions", ["registry.user"]);
        await h.Manager.EnableAsync("test.file-extensions");
        h.Registry.SetValue(RegistryRoot.CurrentUser, Adv, "HideFileExt", RegistryData.Dword(1)); // Windows reset it

        var manager = h.Rebuild();
        var results = await manager.StartAsync();

        Assert.All(results, r => Assert.True(r.Result.Succeeded));
        Assert.Equal(0, (int)h.Registry.GetValue(RegistryRoot.CurrentUser, Adv, "HideFileExt")!.Data);
    }

    [Fact]
    public async Task Signed_package_installs_updates_and_uninstalls_cleanly()
    {
        using var h = new ModEngineHarness();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        h.Publishers.Add(new TrustedPublisher { Id = "contoso", Name = "Contoso", PublicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) });

        var v1 = new ModPackageBuilder().WithManifestJson(TestManifests.ExtensionsJson.Replace("test.file-extensions", "contoso.extensions"))
            .WithText("README.md", "hello").SignWith(key, "contoso", "Contoso").Build();
        var inspection = h.Manager.Inspect(v1);
        Assert.Equal(VerificationLevel.Trusted, inspection.Verification.Level);
        Assert.True(inspection.CanInstall, inspection.Summary);

        Assert.True((await h.Manager.InstallAsync(v1)).Succeeded);
        h.Manager.GrantPermissions("contoso.extensions", ["registry.user"]);
        Assert.True((await h.Manager.EnableAsync("contoso.extensions")).Succeeded);

        var v2 = new ModPackageBuilder().WithManifestJson(TestManifests.ExtensionsJson.Replace("test.file-extensions", "contoso.extensions").Replace("1.0.0", "1.1.0"))
            .SignWith(key, "contoso", "Contoso").Build();
        var update = await h.Manager.InstallAsync(v2);
        Assert.True(update.Succeeded, update.Message);
        var mod = h.Manager.GetMod("contoso.extensions")!;
        Assert.Equal("1.1.0", mod.Manifest.Version.ToString());
        Assert.True(mod.IsEnabled);
        Assert.Equal(2, h.Manager.GetStoredVersions("contoso.extensions").Count);

        // Reload from disk re-verifies the installed files.
        Assert.Equal(VerificationLevel.Trusted, h.Rebuild().GetMod("contoso.extensions")!.Verification);

        var removed = await h.Manager.UninstallAsync("contoso.extensions");
        Assert.True(removed.Succeeded, removed.Message);
        Assert.Null(h.Manager.GetMod("contoso.extensions"));
        Assert.Null(h.Registry.GetValue(RegistryRoot.CurrentUser, Adv, "HideFileExt"));
    }

    [Fact]
    public void Tampered_and_unsigned_packages_are_refused()
    {
        using var h = new ModEngineHarness();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = new ModPackageBuilder().WithManifestJson(TestManifests.ExtensionsJson).SignWith(key, "x", "X").Build();
        var files = signed.Files.ToDictionary(f => f.Key, f => f.Value);
        files["extra.ps1"] = "Remove-Item C:\\ -Recurse"u8.ToArray();
        var tampered = ModPackage.FromFiles(files);
        Assert.Equal(VerificationLevel.Invalid, h.Manager.Inspect(tampered).Verification.Level);
        Assert.False(h.Manager.Inspect(tampered).CanInstall);

        var unsigned = new ModPackageBuilder().WithManifestJson(TestManifests.ExtensionsJson).Build();
        Assert.False(h.Manager.Inspect(unsigned).CanInstall);
        h.Options.AllowUnsignedPackages = () => true;
        Assert.True(h.Manager.Inspect(unsigned).CanInstall);
        Assert.Equal(VerificationLevel.SignedUntrusted, h.Manager.Inspect(signed).Verification.Level);
    }

    [Fact]
    public void Packages_writing_protected_areas_fail_validation()
    {
        var json = TestManifests.ExtensionsJson.Replace(@"Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced", @"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System");
        var errors = ManifestValidator.Validate(ManifestSerializer.Parse(json));
        Assert.Contains(errors, e => e.Contains("protected"));
    }

    [Fact]
    public async Task PowerShell_consent_is_bound_to_the_script_hash()
    {
        using var h = new ModEngineHarness();
        h.Options.AllowUnsignedPackages = () => true;
        var manifest = """
        { "id": "test.ps", "name": "PS", "description": "d", "author": "a", "version": "1.0.0", "category": "systemBehavior",
          "permissions": ["powershell.execute"], "implementation": { "kind": "powerShell", "applyScript": "apply.ps1", "revertScript": "revert.ps1" } }
        """;
        var pkg = new ModPackageBuilder().WithManifestJson(manifest).WithText("apply.ps1", "Write-Output on").WithText("revert.ps1", "Write-Output off").Build();
        Assert.True((await h.Manager.InstallAsync(pkg)).Succeeded);

        var r = await h.Manager.EnableAsync("test.ps");
        Assert.Equal(ModActionStatus.NeedsConsent, r.Status);
        Assert.Contains(r.MissingPermissions, p => p.StartsWith("powershell.execute:"));
        h.Manager.GrantPermissions("test.ps", r.MissingPermissions);
        Assert.True((await h.Manager.EnableAsync("test.ps")).Succeeded);
        Assert.Contains(h.PowerShell.Scripts, s => s.Contains("Write-Output on"));

        Assert.True((await h.Manager.DisableAsync("test.ps")).Succeeded);
        Assert.Contains(h.PowerShell.Scripts, s => s.Contains("Write-Output off"));

        // A changed script requires new consent.
        var pkg2 = new ModPackageBuilder().WithManifestJson(manifest.Replace("1.0.0", "1.0.1")).WithText("apply.ps1", "Write-Output changed").WithText("revert.ps1", "Write-Output off").Build();
        await h.Manager.InstallAsync(pkg2);
        Assert.Equal(ModActionStatus.NeedsConsent, (await h.Manager.EnableAsync("test.ps")).Status);
    }

    [Fact]
    public async Task Components_run_with_guarded_registry_and_are_reverted()
    {
        var manifest = ManifestSerializer.Parse("""
        { "id": "test.component", "name": "C", "description": "d", "author": "a", "version": "1.0.0", "category": "desktop",
          "permissions": ["registry.user"], "implementation": { "kind": "component", "component": "probe" },
          "actions": [ { "id": "ping", "label": "Ping" } ] }
        """);
        using var h = new ModEngineHarness(manifest);
        var probe = new ProbeComponent();
        h.Components.Register("probe", _ => probe);
        h.Manager.GrantPermissions("test.component", ["registry.user"]);

        Assert.True((await h.Manager.EnableAsync("test.component")).Succeeded);
        Assert.Equal("1", h.Registry.GetValue(RegistryRoot.CurrentUser, @"Software\Probe", "Started")!.Data);
        Assert.Throws<UnauthorizedAccessException>(() => probe.Context!.Registry.SetValue(RegistryRoot.CurrentUser, @"SOFTWARE\Microsoft\Windows Defender", "x", RegistryData.Dword(0)));
        Assert.Throws<UnauthorizedAccessException>(() => probe.Context!.Registry.SetValue(RegistryRoot.LocalMachine, @"SOFTWARE\Probe", "x", RegistryData.Dword(0)));
        Assert.True((await h.Manager.InvokeActionAsync("test.component", "ping")).Success);

        Assert.True((await h.Manager.DisableAsync("test.component")).Succeeded);
        Assert.True(probe.Stopped);
        Assert.Null(h.Registry.GetValue(RegistryRoot.CurrentUser, @"Software\Probe", "Started"));
    }

    [Fact]
    public async Task Configuration_export_round_trips()
    {
        using var h = new ModEngineHarness(TestManifests.Extensions());
        h.Manager.GrantPermissions("test.file-extensions", ["registry.user"]);
        await h.Manager.UpdateSettingsAsync("test.file-extensions", new JsonObject { ["show"] = false });
        await h.Manager.EnableAsync("test.file-extensions");
        var json = h.Manager.ExportConfiguration();

        await h.Manager.DisableAsync("test.file-extensions");
        await h.Manager.ResetSettingsAsync("test.file-extensions");
        var results = await h.Manager.ImportConfigurationAsync(json);

        Assert.All(results, r => Assert.True(r.Result.Succeeded, r.Result.Message));
        var mod = h.Manager.GetMod("test.file-extensions")!;
        Assert.True(mod.IsEnabled);
        Assert.False(mod.Settings["show"]!.GetValue<bool>());
        Assert.NotEmpty(h.Manager.GetHistory("test.file-extensions"));
    }

    private sealed class ProbeComponent : ModComponentBase
    {
        public ModComponentContext? Context;
        public bool Stopped;
        public override Task StartAsync(ModComponentContext context, CancellationToken ct)
        {
            Context = context;
            context.Registry.SetValue(RegistryRoot.CurrentUser, @"Software\Probe", "Started", RegistryData.Str("1"));
            return Task.CompletedTask;
        }
        public override Task StopAsync(ModComponentContext context, CancellationToken ct) { Stopped = true; return Task.CompletedTask; }
        public override Task<OperationResult> InvokeActionAsync(ModComponentContext context, string actionId, CancellationToken ct) =>
            Task.FromResult(OperationResult.Ok("pong"));
    }
}
