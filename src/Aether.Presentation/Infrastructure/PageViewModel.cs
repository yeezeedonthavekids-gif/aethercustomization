using Aether.Core.Abstractions;
using Aether.Core.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Presentation.Infrastructure;

/// <summary>
/// Base for page view models: lazy loading on first navigation, busy/error state and helpers that turn every
/// operation result into a toast, so no action ever fails silently.
/// </summary>
public abstract partial class PageViewModel(INotificationSink notifications) : ObservableObject
{
    private bool _loaded;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasError))] private string? _errorMessage;

    protected INotificationSink Notifications { get; } = notifications;

    public bool HasError => ErrorMessage is not null;

    /// <summary>Called by the shell each time the page is shown.</summary>
    public async Task ActivateAsync(object? parameter)
    {
        if (!_loaded)
        {
            _loaded = true;
            IsLoading = true;
            try { await LoadAsync().ConfigureAwait(true); ErrorMessage = null; }
            catch (Exception ex) { ErrorMessage = ex.Message; }
            finally { IsLoading = false; }
        }
        else
        {
            try { await RefreshAsync().ConfigureAwait(true); }
            catch (Exception ex) { ErrorMessage = ex.Message; }
        }
        OnActivated(parameter);
    }

    protected virtual Task LoadAsync() => Task.CompletedTask;
    protected virtual Task RefreshAsync() => Task.CompletedTask;
    protected virtual void OnActivated(object? parameter) { }

    public async Task RetryAsync()
    {
        _loaded = false;
        await ActivateAsync(null).ConfigureAwait(true);
    }

    protected void Report(OperationResult result, string? title = null)
    {
        Notifications.Notify(new AppNotification(title ?? (result.Success ? "Done" : "Something went wrong"), result.Describe(),
            result.Success ? (result.Errors.Count > 0 ? NotificationSeverity.Warning : NotificationSeverity.Success) : NotificationSeverity.Error));
    }

    protected void Toast(string title, string message, NotificationSeverity severity = NotificationSeverity.Success) =>
        Notifications.Notify(new AppNotification(title, message, severity));

    /// <summary>Runs an operation with the busy flag set; exceptions become error toasts.</summary>
    protected async Task RunAsync(Func<Task> action, string failureTitle = "Something went wrong")
    {
        IsBusy = true;
        try { await action().ConfigureAwait(true); }
        catch (Exception ex) { Toast(failureTitle, ex.Message, NotificationSeverity.Error); }
        finally { IsBusy = false; }
    }
}
