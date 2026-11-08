using Aether.Storage;

namespace Aether.Services.Settings;

public interface ISettingsService
{
    AppSettings Current { get; }
    void Update(Func<AppSettings, AppSettings> change);
    void Reset();
    event Action<AppSettings, AppSettings>? Changed;
}

public sealed class SettingsService(IDocumentStore store) : ISettingsService
{
    private const string Collection = "app";
    private const string Key = "settings";
    private readonly object _gate = new();
    private AppSettings? _current;

    public event Action<AppSettings, AppSettings>? Changed;

    public AppSettings Current
    {
        get
        {
            lock (_gate) return _current ??= store.Get<AppSettings>(Collection, Key) ?? new AppSettings();
        }
    }

    public void Update(Func<AppSettings, AppSettings> change)
    {
        AppSettings before, after;
        lock (_gate)
        {
            before = Current;
            after = change(before);
            if (after == before) return;
            store.Put(Collection, Key, after);
            _current = after;
        }
        Changed?.Invoke(before, after);
    }

    public void Reset()
    {
        var keepOnboarding = Current.OnboardingCompleted;
        Update(_ => new AppSettings { OnboardingCompleted = keepOnboarding });
    }
}
