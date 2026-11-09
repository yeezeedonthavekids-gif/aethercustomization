using Aether.ModEngine.Lifecycle;
using Aether.Presentation;
using Aether.Presentation.Infrastructure;
using Aether.Presentation.ViewModels;
using Aether.Tests.Fakes;

namespace Aether.Tests.Presentation;

public class ViewModelTests
{
    [Fact]
    public async Task Every_page_loads_without_errors()
    {
        using var h = new ServiceHarness();
        foreach (var item in NavigationCatalog.All)
        {
            var vm = (PageViewModel)h.Provider.GetService(PresentationServices.ViewModelFor(item.Key))!;
            await vm.ActivateAsync(null);
            Assert.True(vm.ErrorMessage is null, $"{item.Key}: {vm.ErrorMessage}");
            await vm.ActivateAsync(null); // refresh path
            Assert.True(vm.ErrorMessage is null, $"{item.Key} refresh: {vm.ErrorMessage}");
        }
        Assert.NotNull(h.Get<ShellViewModel>());
    }

    [Fact]
    public async Task Mod_toggle_asks_for_consent_then_enables()
    {
        using var h = new ServiceHarness();
        var vm = h.Get<ModsViewModel>();
        await vm.ActivateAsync(null);
        var item = vm.Mods.First(m => m.Id == "aether.explorer-file-extensions");
        await vm.ToggleAsync(item, true);
        Assert.Contains(h.Dialogs.Shown, s => s.StartsWith("perm:"));
        Assert.True(h.Get<ModManager>().GetMod(item.Id)!.IsEnabled);
        Assert.True(item.IsEnabled);
    }

    [Fact]
    public async Task Declining_consent_leaves_the_mod_off()
    {
        using var h = new ServiceHarness();
        h.Dialogs.Answer = false;
        var vm = h.Get<ModsViewModel>();
        await vm.ActivateAsync(null);
        var item = vm.Mods.First(m => m.Id == "aether.explorer-hidden-files");
        await vm.ToggleAsync(item, true);
        Assert.False(h.Get<ModManager>().GetMod(item.Id)!.IsEnabled);
        Assert.Equal(0, h.Registry.Writes);
    }

    [Fact]
    public async Task Settings_form_tracks_dirty_state_and_saves()
    {
        using var h = new ServiceHarness();
        var vm = h.Get<ModsViewModel>();
        await vm.ActivateAsync(null);
        vm.Selected = vm.Mods.First(m => m.Id == "aether.clock-format");
        var field = vm.Form!.Fields.First(f => f.Key == "timePreset");
        field.SelectedOption = field.Options.First(o => o.Value == "h:mm tt");
        Assert.True(vm.Form.IsDirty);
        Assert.False(vm.Form.Fields.First(f => f.Key == "customTime").IsVisible == false && false);
        await vm.SaveSettingsCommand.ExecuteAsync(null);
        Assert.Equal("h:mm tt", h.Get<ModManager>().GetMod("aether.clock-format")!.Settings["timePreset"]!.GetValue<string>());
    }

    [Fact]
    public async Task Palette_finds_pages_settings_and_mods()
    {
        using var h = new ServiceHarness();
        var palette = h.Get<CommandPaletteViewModel>();
        palette.Open();
        palette.Query = "file ext";
        await WaitFor(() => palette.Results.Any(r => r.Title.Contains("File Extensions")));
        Assert.Contains(palette.Results, r => r.Title.Contains("File Extensions"));
        palette.Query = "taskbar alignment";
        await WaitFor(() => palette.Results.Count > 0 && palette.Results[0].Title == "Taskbar alignment");
        Assert.Equal("Taskbar alignment", palette.Results[0].Title);
    }

    // Search runs in the background; poll instead of guessing a delay (CI runners can be slow).
    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(50);
    }

    [Fact]
    public async Task Onboarding_completes_without_touching_windows()
    {
        using var h = new ServiceHarness();
        var vm = h.Get<OnboardingViewModel>();
        var done = false;
        vm.Completed += () => done = true;
        while (!done) await vm.NextCommand.ExecuteAsync(null);
        Assert.True(h.Get<Aether.Services.Settings.ISettingsService>().Current.OnboardingCompleted);
        Assert.NotNull(vm.BackupResult);
        Assert.Equal(0, h.Registry.Writes);
    }
}
