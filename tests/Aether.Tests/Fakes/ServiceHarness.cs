using Aether.Core.Abstractions;
using Aether.Services;
using Aether.Services.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace Aether.Tests.Fakes;

/// <summary>The full AETHER container over fakes — the same wiring the app uses.</summary>
public sealed class ServiceHarness : IDisposable
{
    private readonly TempDirectory _temp = new();

    public ServiceHarness()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRegistryAccess>(Registry);
        services.AddSingleton<ISystemEnvironment>(Environment);
        services.AddSingleton<ISystemSettings>(SystemSettings);
        services.AddSingleton<IShellController>(new FakeShell());
        services.AddSingleton<IProcessRunner>(Processes);
        services.AddSingleton<IPowerShellRunner>(new FakePowerShell());
        services.AddSingleton<IDesktopController>(Desktop);
        services.AddSingleton<IShortcutService>(new FakeShortcuts(_temp.Path));
        services.AddSingleton<IWindowManager>(Windows);
        services.AddSingleton<IShellLauncher>(Launcher);
        services.AddSingleton<Aether.Services.Platform.ISystemInfoProvider>(new FakeInfo());
        services.AddSingleton<Aether.Presentation.Infrastructure.IUiDispatcher, Aether.Presentation.Infrastructure.ImmediateDispatcher>();
        services.AddSingleton<Aether.Presentation.Infrastructure.IDialogService>(Dialogs);
        Aether.Presentation.PresentationServices.AddAetherPresentation(services);
        services.AddAether(new AetherHostOptions { Paths = new AppPaths(Path.Combine(_temp.Path, "data")), BundledMarketplaceFolder = Path.Combine(_temp.Path, "bundled") });
        Provider = services.BuildServiceProvider();
    }

    public FakeDialogs Dialogs { get; } = new();
    public InMemoryRegistry Registry { get; } = new();
    public FakeEnvironment Environment { get; } = new();
    public FakeSystemSettings SystemSettings { get; } = new();
    public FakeProcessRunner Processes { get; } = new();
    public FakeDesktop Desktop { get; } = new();
    public FakeWindowManager Windows { get; } = new();
    public FakeLauncher Launcher { get; } = new();
    public ServiceProvider Provider { get; }
    public string TempPath => _temp.Path;

    public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

    public void Dispose()
    {
        Provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _temp.Dispose();
    }
}
