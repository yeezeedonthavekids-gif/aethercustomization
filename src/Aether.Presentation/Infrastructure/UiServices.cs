using Aether.ModEngine.Manifest;

namespace Aether.Presentation.Infrastructure;

public sealed record FileFilter(string Name, params string[] Extensions);

/// <summary>Dialogs implemented by the UI shell. View models never touch UI types directly.</summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string primary = "Continue", bool destructive = false);
    Task<bool> RequestPermissionsAsync(string subjectName, IReadOnlyList<PermissionInfo> permissions, RiskLevel risk, string? scriptPreview = null);
    Task<string?> PromptAsync(string title, string label, string? initial = null);
    Task ShowTextAsync(string title, string text);
    Task<string?> PickFileAsync(params FileFilter[] filters);
    Task<string?> PickFolderAsync();
    Task<string?> SaveFileAsync(string suggestedName, FileFilter filter);
}

/// <summary>Marshals work to the UI thread.</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

/// <summary>Synchronous dispatcher used by tests.</summary>
public sealed class ImmediateDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}
