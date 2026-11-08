using System.Text.Json.Nodes;
using Aether.Core.Abstractions;
using Aether.ModEngine.Lifecycle;
using Aether.Services;
using Aether.Services.Applications;
using Aether.Services.Automation;
using Aether.Services.Backup;
using Aether.Services.Commands;
using Aether.Services.Marketplace;
using Aether.Services.Profiles;
using Aether.Services.Tweaks;
using Aether.Services.Wallpapers;
using Aether.Services.Windows;
using Aether.Storage;
using Aether.Tests.Fakes;
using Aether.ThemeEngine;
using Aether.WidgetEngine;

namespace Aether.Tests.Services;

public class ServiceTests
{
    [Fact]
    public void Every_service_resolves_from_the_container()
    {
        using var h = new ServiceHarness();
        Assert.NotNull(h.Get<AetherRuntime>());
        Assert.NotNull(h.Get<CommandPaletteService>());
        Assert.NotNull(h.Get<MarketplaceService>());
        Assert.NotNull(h.Get<BackupService>());
        Assert.NotNull(h.Get<ShellProfileService>());
        Assert.Equal(20, h.Get<ModManager>().GetMods().Count);
    }

    [Fact]
    public async Task Tweaks_apply_detect_and_restore()
    {
        using var h = new ServiceHarness();
        var tweaks = h.Get<TweakService>();
        var adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        h.Registry.SetValue(RegistryRoot.CurrentUser, adv, "TaskbarAl", RegistryData.Dword(1));

        Assert.Equal("1", tweaks.GetState(TweakCatalog.All.Single(t => t.Id == "taskbar.alignment")).CurrentOption);
        var r = await tweaks.SetAsync("taskbar.alignment", "0");
        Assert.True(r.Success, r.Describe());
        var state = tweaks.GetState(TweakCatalog.All.Single(t => t.Id == "taskbar.alignment"));
        Assert.Equal("0", state.CurrentOption);
        Assert.True(state.IsModified);
        Assert.Contains("TraySettings", h.SystemSettings.Broadcasts);

        Assert.True((await tweaks.RestoreOriginalAsync("taskbar.alignment")).Success);
        Assert.Equal(1, (int)h.Registry.GetValue(RegistryRoot.CurrentUser, adv, "TaskbarAl")!.Data);
        Assert.False(tweaks.GetState(TweakCatalog.All.Single(t => t.Id == "taskbar.alignment")).IsModified);
    }

    [Fact]
    public async Task Tweaks_respect_build_compatibility()
    {
        using var h = new ServiceHarness();
        h.Environment.Version = h.Environment.Version with { Build = 19045 };
        var r = await h.Get<TweakService>().SetAsync("taskbar.alignment", "0");
        Assert.False(r.Success);
        Assert.Null(h.Registry.GetValue(RegistryRoot.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAl"));
    }

    [Fact]
    public void Every_tweak_passes_registry_policy_and_has_documentation()
    {
        Assert.True(TweakCatalog.All.Count >= 45);
        Assert.Equal(TweakCatalog.All.Count, TweakCatalog.All.Select(t => t.Id).Distinct().Count());
        foreach (var t in TweakCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Why), t.Id);
            Assert.False(string.IsNullOrWhiteSpace(t.WhatItChanges), t.Id);
            foreach (var op in t.Options.SelectMany(o => o.Operations).OfType<Aether.ModEngine.Manifest.RegistryOperation>())
                Assert.True(Aether.ModEngine.Security.RegistryPolicy.Evaluate(op.Root, op.Path, ["registry.user", "registry.machine"]).Allowed, $"{t.Id}: {op.Path}");
        }
    }

    [Fact]
    public async Task Backups_round_trip_and_detect_corruption()
    {
        using var h = new ServiceHarness();
        var themes = h.Get<ThemeManager>();
        var backups = h.Get<BackupService>();
        var copy = themes.Duplicate("midnight", "Night Shift");

        var created = await backups.CreateAsync(BackupKind.Full, "test");
        Assert.True(created.Success, created.ToResult().Describe());
        Assert.True(backups.Validate(created.Value!.Path).IsValid);

        themes.Delete(copy.Id);
        Assert.Null(themes.GetTheme(copy.Id));
        var restored = await backups.RestoreAsync(created.Value.Path);
        Assert.True(restored.Success, restored.Describe());
        Assert.Equal(new[] { copy.Id }, h.Get<IDocumentStore>().GetIds("themes"));
        Assert.NotNull(themes.GetTheme(copy.Id));
        Assert.Contains(backups.List(), b => b.Manifest.Automatic); // safety backup before restore

        // Corrupt a byte inside the archive's documents entry.
        var corrupt = Path.Combine(h.TempPath, "corrupt.aetherbackup");
        using (var src = System.IO.Compression.ZipFile.OpenRead(created.Value.Path))
        using (var dst = System.IO.Compression.ZipFile.Open(corrupt, System.IO.Compression.ZipArchiveMode.Create))
            foreach (var e in src.Entries)
            {
                using var r = e.Open(); using var ms = new MemoryStream(); r.CopyTo(ms);
                var bytes = ms.ToArray();
                if (e.FullName == "documents.json") bytes[^2] ^= 0x20;
                using var w = dst.CreateEntry(e.FullName).Open(); w.Write(bytes);
            }
        var v = backups.Validate(corrupt);
        Assert.False(v.IsValid);
        Assert.Contains(v.Problems, p => p.Contains("documents.json"));
    }

    [Fact]
    public async Task Theme_apply_writes_personalization_and_rollback_restores_it()
    {
        using var h = new ServiceHarness();
        var themes = h.Get<ThemeManager>();
        var p = WindowsPersonalizationApplier.PersonalizePath;
        h.Registry.SetValue(RegistryRoot.CurrentUser, p, "AppsUseLightTheme", RegistryData.Dword(1));

        var r = await themes.ApplyAsync("dracula");
        Assert.True(r.Success, r.Describe());
        Assert.Equal(0, (int)h.Registry.GetValue(RegistryRoot.CurrentUser, p, "AppsUseLightTheme")!.Data);
        Assert.Equal("dracula", themes.ActiveThemeId);
        Assert.Equal(32, ((byte[])h.Registry.GetValue(RegistryRoot.CurrentUser, WindowsPersonalizationApplier.AccentPath, "AccentPalette")!.Data).Length);

        Assert.True(themes.Rollback().Success);
        Assert.Equal(1, (int)h.Registry.GetValue(RegistryRoot.CurrentUser, p, "AppsUseLightTheme")!.Data);
        Assert.Equal("midnight", themes.ActiveThemeId);
    }

    [Fact]
    public void Themes_export_and_import()
    {
        using var h = new ServiceHarness();
        var themes = h.Get<ThemeManager>();
        using var ms = new MemoryStream();
        themes.Export("nord", ms);
        ms.Position = 0;
        var imported = themes.Import(ms, Path.Combine(h.TempPath, "assets"));
        Assert.True(imported.Success, imported.Message);
        Assert.False(imported.Value!.BuiltIn);
        Assert.Equal("#FF88C0D0", imported.Value.Palette.Accent);
    }

    [Fact]
    public void Theme_schedule_picks_the_right_theme()
    {
        var s = new ThemeSchedule { Enabled = true, LightThemeId = "L", DarkThemeId = "D", LightAt = "07:00", DarkAt = "19:00" };
        var tz = TimeSpan.FromHours(1);
        Assert.Equal("D", ThemeScheduleCalculator.Evaluate(s, new DateTimeOffset(2026, 3, 1, 6, 0, 0, tz)).ThemeId);
        var (theme, next) = ThemeScheduleCalculator.Evaluate(s, new DateTimeOffset(2026, 3, 1, 12, 0, 0, tz));
        Assert.Equal("L", theme);
        Assert.Equal(19, next.Hour);
        var night = ThemeScheduleCalculator.Evaluate(s, new DateTimeOffset(2026, 3, 1, 22, 0, 0, tz));
        Assert.Equal(("D", 2), (night.ThemeId, night.NextChange.Day));
    }

    [Fact]
    public void Solar_times_are_plausible()
    {
        var (rise, set) = Aether.Core.Configuration.SolarCalculator.Calculate(new DateOnly(2026, 6, 21), 51.5, -0.12, TimeSpan.FromHours(1));
        Assert.InRange(rise!.Value.Hour, 4, 5);
        Assert.InRange(set!.Value.Hour, 21, 21);
        var polar = Aether.Core.Configuration.SolarCalculator.Calculate(new DateOnly(2026, 6, 21), 80, 0, TimeSpan.Zero);
        Assert.Null(polar.Sunrise);
    }

    [Fact]
    public async Task Automation_fires_time_and_process_triggers_with_conditions()
    {
        using var h = new ServiceHarness();
        var executor = new RecordingExecutor();
        var signals = new ScriptedSignals();
        var engine = new AutomationEngine(h.Get<IDocumentStore>(), executor, signals, () => (51.5, 0), Microsoft.Extensions.Logging.Abstractions.NullLogger<AutomationEngine>.Instance);
        engine.Save(new AutomationRule
        {
            Id = "r1", Name = "Evening", CooldownSeconds = 0,
            Triggers = [new AutomationTrigger { Kind = TriggerKind.TimeOfDay, Time = "19:00" }],
            Actions = [new AutomationAction { Kind = ActionKind.ApplyTheme, Target = "midnight" }],
        });
        engine.Save(new AutomationRule
        {
            Id = "r2", Name = "Game", CooldownSeconds = 0, Priority = 5,
            Triggers = [new AutomationTrigger { Kind = TriggerKind.ProcessStarted, ProcessName = "Game.exe" }],
            Conditions = [new AutomationCondition { Kind = ConditionKind.OnAcPower }],
            Actions = [new AutomationAction { Kind = ActionKind.EnableMod, Target = "aether.gaming-profile" }],
        });

        var day = new DateTimeOffset(2026, 5, 4, 18, 59, 50, TimeSpan.Zero);
        signals.Next = new AutomationSignals(day, new HashSet<string>(), false, 80, "x");
        await engine.TickAsync();
        signals.Next = new AutomationSignals(day.AddSeconds(15), new HashSet<string> { "game" }, false, 80, "x");
        await engine.TickAsync();

        Assert.Equal([ActionKind.EnableMod, ActionKind.ApplyTheme], executor.Ran.Order().ToArray().OrderBy(k => k).ToArray().Length == 2 ? executor.Ran.ToArray() : []);
        Assert.Equal(2, engine.GetHistory().Count);

        // Condition blocks the process rule on battery.
        executor.Ran.Clear();
        signals.Next = new AutomationSignals(day.AddSeconds(30), new HashSet<string>(), true, 80, "x");
        await engine.TickAsync();
        signals.Next = new AutomationSignals(day.AddSeconds(45), new HashSet<string> { "game" }, true, 80, "x");
        await engine.TickAsync();
        Assert.Empty(executor.Ran);
    }

    [Fact]
    public void Fuzzy_matching_ranks_sensible_results()
    {
        CommandItem C(string title, params string[] kw) => new(title, title, "", "Cat", "", () => Task.CompletedTask) { Keywords = kw };
        var items = new[] { C("Apply theme: Midnight"), C("Restart Explorer now"), C("Enable Show Seconds in Clock", "clock"), C("Taskbar alignment") };
        Assert.Equal("Restart Explorer now", FuzzyMatcher.Rank("rexp", items)[0].Item.Title);
        Assert.Equal("Taskbar alignment", FuzzyMatcher.Rank("tba", items)[0].Item.Title);
        Assert.Equal("Enable Show Seconds in Clock", FuzzyMatcher.Rank("clock", items)[0].Item.Title);
        Assert.Empty(FuzzyMatcher.Rank("zzzz", items));
    }

    [Fact]
    public async Task Startup_manager_disables_with_startup_approved_and_reenables()
    {
        using var h = new ServiceHarness();
        h.Registry.SetValue(RegistryRoot.CurrentUser, StartupManager.RunPath, "Chat", RegistryData.Str("chat.exe --tray"));
        var startup = h.Get<StartupManager>();
        var entry = Assert.Single(startup.GetEntries());
        Assert.True(entry.Enabled);
        Assert.True(startup.SetEnabled(entry, false).Success);
        Assert.False(startup.GetEntries().Single().Enabled);
        Assert.Equal(3, ((byte[])h.Registry.GetValue(RegistryRoot.CurrentUser, StartupManager.ApprovedRun, "Chat")!.Data)[0]);
        Assert.True(startup.SetEnabled(startup.GetEntries().Single(), true).Success);
        Assert.True(startup.GetEntries().Single().Enabled);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Gaming_profile_switches_power_plan_and_restores_it()
    {
        using var h = new ServiceHarness();
        var active = PowerPlanService.Balanced;
        h.Processes.Handler = (_, args) =>
        {
            if (args[0] == "/setactive") { active = Guid.Parse(args[1]); return new ProcessResult(0, "", ""); }
            return new ProcessResult(0, $"""
                Power Scheme GUID: {PowerPlanService.Balanced}  (Balanced){(active == PowerPlanService.Balanced ? " *" : "")}
                Power Scheme GUID: {PowerPlanService.HighPerformance}  (High performance){(active == PowerPlanService.HighPerformance ? " *" : "")}
                """, "");
        };
        var mods = h.Get<ModManager>();
        mods.GrantPermissions("aether.gaming-profile", mods.GetMissingPermissions("aether.gaming-profile"));
        var on = await mods.EnableAsync("aether.gaming-profile");
        Assert.True(on.Succeeded, on.Message + string.Join(";", on.Details));
        Assert.Equal(PowerPlanService.HighPerformance, active);
        Assert.False(h.SystemSettings.Flags[SystemFlag.ClientAreaAnimation]);

        var off = await mods.DisableAsync("aether.gaming-profile");
        Assert.True(off.Succeeded, off.Message);
        Assert.Equal(PowerPlanService.Balanced, active);
        Assert.True(h.SystemSettings.Flags[SystemFlag.ClientAreaAnimation]);
        Assert.Null(h.Registry.GetValue(RegistryRoot.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled"));
    }

    [Fact]
    public async Task Power_shortcuts_are_created_and_removed()
    {
        using var h = new ServiceHarness();
        var mods = h.Get<ModManager>();
        mods.GrantPermissions("aether.power-shortcuts", ["filesystem.user"]);
        Assert.True((await mods.EnableAsync("aether.power-shortcuts")).Succeeded);
        var folder = Path.Combine(h.TempPath, "start", "AETHER Power");
        Assert.Equal(5, Directory.GetFiles(folder).Length);
        await mods.UpdateSettingsAsync("aether.power-shortcuts", new JsonObject { ["sleep"] = false });
        Assert.Equal(4, Directory.GetFiles(folder).Length);
        Assert.True((await mods.DisableAsync("aether.power-shortcuts")).Succeeded);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task Focus_mode_restores_desktop_state()
    {
        using var h = new ServiceHarness();
        var mods = h.Get<ModManager>();
        mods.GrantPermissions("aether.focus-mode", mods.GetMissingPermissions("aether.focus-mode"));
        await mods.UpdateSettingsAsync("aether.focus-mode", new JsonObject { ["minutes"] = 0 });
        Assert.True((await mods.EnableAsync("aether.focus-mode")).Succeeded);
        Assert.False(h.Desktop.Icons);
        Assert.True(h.Desktop.AutoHide);
        await mods.DisableAsync("aether.focus-mode");
        Assert.True(h.Desktop.Icons);
        Assert.False(h.Desktop.AutoHide);
    }

    [Fact]
    public async Task Taskbar_profiles_apply_native_settings_and_restore_windows_default()
    {
        using var h = new ServiceHarness();
        var shell = h.Get<ShellProfileService>();
        var r = await shell.ApplyTaskbarProfileAsync("minimal");
        Assert.True(r.Success, r.Describe());
        Assert.True(h.Desktop.AutoHide);
        Assert.Equal(0, (int)h.Registry.GetValue(RegistryRoot.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode")!.Data);
        Assert.Equal("minimal", shell.ActiveTaskbarProfileId);
        Assert.True((await shell.RestoreWindowsTaskbarAsync()).Success);
        Assert.False(h.Desktop.AutoHide);
        Assert.Null(h.Registry.GetValue(RegistryRoot.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode"));
    }

    [Fact]
    public void Window_layouts_compute_zone_rectangles_and_rules_match()
    {
        var area = new Aether.Services.Platform.WorkArea("m", "M", 0, 0, 1920, 1040, true);
        Assert.Equal((4, 4, 952, 1032), WindowLayoutService.ZoneRect(area, new Zone(0, 0, .5, 1), 8));
        var w = new Aether.Services.Platform.WindowInfo(1, "Doc - Code", "Code", 1, 0, 0, 10, 10, false, false, 255);
        Assert.True(WindowLayoutService.Matches(new WindowRule { Id = "r", ProcessName = "code.exe", TitleContains = "doc" }, w));
        Assert.False(WindowLayoutService.Matches(new WindowRule { Id = "r", ProcessName = "notepad" }, w));
    }

    [Fact]
    public void Widget_layouts_add_update_export_import()
    {
        using var h = new ServiceHarness();
        var widgets = h.Get<WidgetManager>();
        var clock = widgets.AddWidget("clock");
        widgets.UpdateBounds(clock.Id, 10, 20, 300, 200);
        Assert.Equal(300, widgets.ActiveLayout.Widgets.Single().Width);
        var json = widgets.ExportLayout(widgets.ActiveLayoutId);
        var imported = widgets.ImportLayout(json);
        Assert.Single(imported.Widgets);
        Assert.NotEqual(clock.Id, imported.Widgets[0].Id);
        Assert.Equal(2, widgets.GetLayouts().Count);
    }

    [Fact]
    public void Parsers_handle_real_payloads()
    {
        var plans = PowerPlanService.Parse("Existing Power Schemes (* Active)\r\n-----------------------------------\r\nPower Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *\r\nPower Scheme GUID: a1841308-3541-4fab-bc81-f71556f20b4a  (Power saver)\r\n");
        Assert.Equal(2, plans.Count);
        Assert.True(plans[0].IsActive);

        var bing = BingDailyWallpaperProvider.Parse("""{"images":[{"urlbase":"/th?id=OHR.Test_EN-US123","title":"Hills","copyright":"© Someone","hsh":"abc"}]}""");
        Assert.Equal("https://www.bing.com/th?id=OHR.Test_EN-US123_UHD.jpg", bing[0].ImageUrl.ToString());

        var weather = OpenMeteoWeatherProvider.ParseForecast("""
            {"current":{"temperature_2m":12.3,"relative_humidity_2m":71,"weather_code":61,"wind_speed_10m":9.4},
             "daily":{"temperature_2m_max":[14.1],"temperature_2m_min":[7.2]}}
            """, "London");
        Assert.Equal("Rain", weather.Condition);
        Assert.Equal(14.1, weather.High);
        var geo = OpenMeteoWeatherProvider.ParseLocation("""{"results":[{"name":"Paris","country":"France","latitude":48.85,"longitude":2.35}]}""");
        Assert.Equal("Paris", geo!.Name);
    }

    private sealed class RecordingExecutor : IAutomationActionExecutor
    {
        public List<ActionKind> Ran { get; } = [];
        public Task<Aether.Core.Diagnostics.OperationResult> ExecuteAsync(AutomationAction action, CancellationToken ct)
        {
            Ran.Add(action.Kind);
            return Task.FromResult(Aether.Core.Diagnostics.OperationResult.Ok("ok"));
        }
    }

    private sealed class ScriptedSignals : IAutomationSignalSource
    {
        public AutomationSignals Next { get; set; } = null!;
        public AutomationSignals Capture(bool includeProcesses) => Next;
    }

    [Fact]
    public async Task Bundled_marketplace_lists_and_installs_signed_samples()
    {
        using var h = new ServiceHarness();
        var bundled = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "marketplace");
        CopyDirectory(Path.GetFullPath(bundled), Path.Combine(h.TempPath, "bundled"));
        var market = h.Get<MarketplaceService>();
        var entries = await market.RefreshAsync();
        Assert.Equal(5, entries.Count);
        Assert.All(entries, e => Assert.Null(e.Listing.Rating)); // never fabricated
        var mod = entries.First(e => e.Listing.Id == "aether-samples.snappy-menus");
        var inspection = await market.InspectModAsync(mod.Listing);
        Assert.Equal(Aether.ModEngine.Security.VerificationLevel.Trusted, inspection.Value!.Verification.Level);
        Assert.True((await market.InstallAsync(mod.Listing)).Success);
        Assert.True((await market.InstallAsync(entries.First(e => e.Listing.Id == "solarflare").Listing)).Success);
        Assert.True((await market.InstallAsync(entries.First(e => e.Listing.Id == "focus-desk").Listing)).Success);
        var after = market.GetEntries();
        Assert.Equal(InstallState.Installed, after.First(e => e.Listing.Id == "aether-samples.snappy-menus").State);
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, f));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(f, target, true);
        }
    }
}
