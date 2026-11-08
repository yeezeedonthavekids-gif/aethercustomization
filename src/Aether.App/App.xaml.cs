using Aether.App.Services;
using Aether.App.Views;
using Aether.Core.Abstractions;
using Aether.Core.Configuration;
using Aether.Presentation;
using Aether.Presentation.Infrastructure;
using Aether.Presentation.ViewModels;
using Aether.Services;
using Aether.Services.Logging;
using Aether.Services.Platform;
using Aether.Services.Profiles;
using Aether.Services.Settings;
using Aether.Services.Windows;
using Aether.ThemeEngine;
using Aether.WindowsIntegration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Aether.App;

public partial class App : Application
{
    private static Mutex? _instanceMutex;
    private MainWindow? _window;
    private MessageWindow? _messages;
    private ILogger<App>? _logger;

    public static ServiceProvider Services { get; private set; } = null!;
    public static T Get<T>() where T : notnull => Services.GetRequiredService<T>();
    public static string? SmokeReport { get; private set; }
    public static bool StartInBackground { get; private set; }
    public static bool IsExiting { get; set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            _logger?.LogCritical(e.Exception, "Unhandled UI exception");
            e.Handled = SmokeReport is null; // keep running; the error is logged and shown
            Services?.GetService<INotificationSink>()?.Notify(new AppNotification("Unexpected error", e.Exception.Message, NotificationSeverity.Error));
        };
        TaskScheduler.UnobservedTaskException += (_, e) => { _logger?.LogError(e.Exception, "Unobserved task exception"); e.SetObserved(); };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var argv = Environment.GetCommandLineArgs();
        var smokeIndex = Array.IndexOf(argv, "--smoke-test");
        if (smokeIndex >= 0) SmokeReport = smokeIndex + 1 < argv.Length ? argv[smokeIndex + 1] : Path.Combine(Path.GetTempPath(), "aether-smoke.txt");
        StartInBackground = argv.Contains("--background");

        // Single instance: a second launch just signals the first to show itself.
        if (SmokeReport is null)
        {
            _instanceMutex = new Mutex(true, "AETHER.SingleInstance", out var first);
            if (!first)
            {
                using var signal = EventWaitHandle.OpenExisting("AETHER.Activate");
                signal.Set();
                Exit();
                return;
            }
        }

        var dataRoot = Environment.GetEnvironmentVariable("AETHER_DATA") is { Length: > 0 } d ? d : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether");
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        var dialogs = new DialogService();
        _messages = new MessageWindow();

        var services = new ServiceCollection();
        services.AddWindowsPlatform();
        services.AddSingleton<IUiDispatcher>(new UiDispatcher(dispatcher));
        services.AddSingleton<IDialogService>(dialogs);
        services.AddSingleton(dialogs);
        services.AddSingleton<IHotkeyService>(_messages);
        services.AddSingleton<WidgetHost>();
        services.AddSingleton<IWidgetHost>(sp => sp.GetRequiredService<WidgetHost>());
        services.AddSingleton<DockHost>();
        services.AddSingleton<IDockHost>(sp => sp.GetRequiredService<DockHost>());
        services.AddSingleton<LauncherHost>();
        services.AddSingleton<ILauncherHost>(sp => sp.GetRequiredService<LauncherHost>());
        services.AddAetherPresentation();
        services.AddAether(new AetherHostOptions
        {
            Paths = new AppPaths(dataRoot),
            Version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
            BundledMarketplaceFolder = Path.Combine(AppContext.BaseDirectory, "Marketplace"),
        });
        Services = services.BuildServiceProvider();
        _logger = Get<ILogger<App>>();
        _logger.LogInformation("AETHER starting (data: {Root})", dataRoot);

        var settings = Get<ISettingsService>();
        var themes = Get<ThemeManager>();
        ThemeResources.Apply(themes.ActiveTheme.Palette, settings.Current.AccentOverride);
        themes.ThemeApplied += t => dispatcher.TryEnqueue(() => ThemeResources.Apply(t.Palette, settings.Current.AccentOverride));
        settings.Changed += (b, a) => { if (b.AccentOverride != a.AccentOverride) dispatcher.TryEnqueue(() => ThemeResources.Apply(themes.ActiveTheme.Palette, a.AccentOverride)); };

        if (SmokeReport is not null) settings.Update(s => s with { OnboardingCompleted = true });

        _window = new MainWindow();
        dialogs.Owner = _window;
        if (!StartInBackground || !settings.Current.OnboardingCompleted) _window.Activate();

        RegisterHotkeys(settings);
        settings.Changed += (b, a) => { if (b.CommandPaletteHotkey != a.CommandPaletteHotkey || b.LauncherHotkey != a.LauncherHotkey) dispatcher.TryEnqueue(() => RegisterHotkeys(settings)); };
        _messages.TrayOpenRequested += () => _window.ShowAndFocus();
        _messages.TrayPaletteRequested += () => { _window.ShowAndFocus(); Get<CommandPaletteViewModel>().Open(); };
        _messages.TrayExitRequested += () => _ = ExitAppAsync();
        if (SmokeReport is null) _messages.ShowTrayIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "aether.ico"));
        ListenForActivation(dispatcher);

        if (SmokeReport is not null) { await SmokeTest.RunAsync(_window, SmokeReport); return; }

        try { await Get<AetherRuntime>().StartAsync(); }
        catch (Exception ex) { _logger.LogError(ex, "Runtime start failed"); }
    }

    private void RegisterHotkeys(ISettingsService settings)
    {
        var problems = new List<string>();
        if (Hotkey.TryParse(settings.Current.CommandPaletteHotkey, out var palette) &&
            !_messages!.Register("palette", palette, () => { _window?.ShowAndFocus(); Get<CommandPaletteViewModel>().Open(); }))
            problems.Add($"{palette} (command palette)");
        if (Hotkey.TryParse(settings.Current.LauncherHotkey, out var launcher) && !_messages!.Register("launcher", launcher, () => Get<LauncherHost>().Toggle()))
            problems.Add($"{launcher} (launcher)");
        var layouts = Get<WindowLayoutService>();
        for (var i = 1; i <= 9; i++)
        {
            var zone = i - 1;
            _messages!.Register($"snap{i}", new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt, i.ToString()), () => layouts.SnapForeground(zone));
        }
        _messages!.Register("topmost", new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt, "T"), () => layouts.ToggleTopmostForeground());
        if (problems.Count > 0)
            Get<INotificationSink>().Notify(new AppNotification("Shortcut unavailable", $"Another app already uses {string.Join(", ", problems)}. Choose a different shortcut in Settings ▸ Navigation.", NotificationSeverity.Warning));
    }

    private void ListenForActivation(DispatcherQueue dispatcher)
    {
        if (SmokeReport is not null) return;
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, "AETHER.Activate");
        new Thread(() =>
        {
            while (!IsExiting) if (signal.WaitOne(1000)) dispatcher.TryEnqueue(() => _window?.ShowAndFocus());
        }) { IsBackground = true }.Start();
    }

    public static async Task ExitAppAsync(int exitCode = 0)
    {
        IsExiting = true;
        try
        {
            await Get<WidgetHost>().StopAsync();
            await Get<DockHost>().HideAsync();
            await Get<AetherRuntime>().StopAsync();
            Get<FileLoggerProvider>().Dispose();
        }
        catch (Exception) { /* exiting anyway */ }
        ((App)Current)._messages?.Dispose();
        Environment.Exit(exitCode);
    }
}
