using Aether.ModEngine.Manifest;
using Aether.Presentation.Infrastructure;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace Aether.App.Services;

public sealed class UiDispatcher(DispatcherQueue queue) : IUiDispatcher
{
    public void Post(Action action)
    {
        if (queue.HasThreadAccess) action();
        else queue.TryEnqueue(() => action());
    }
}

/// <summary>ContentDialog and picker implementations of <see cref="IDialogService"/>.</summary>
public sealed class DialogService : IDialogService
{
    private readonly SemaphoreSlim _one = new(1, 1); // WinUI allows a single open ContentDialog
    public Window? Owner { get; set; }

    private XamlRoot? Root => Owner?.Content?.XamlRoot;

    private async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        if (Root is null) return ContentDialogResult.None;
        await _one.WaitAsync();
        try
        {
            dialog.XamlRoot = Root;
            dialog.RequestedTheme = ThemeResources.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;
            dialog.CornerRadius = new CornerRadius(16);
            return await dialog.ShowAsync();
        }
        finally { _one.Release(); }
    }

    private static TextBlock Text(string s, string style = "AeBody") => new() { Text = s, Style = (Style)Application.Current.Resources[style], TextWrapping = TextWrapping.Wrap };

    public async Task<bool> ConfirmAsync(string title, string message, string primary = "Continue", bool destructive = false)
    {
        var dialog = new ContentDialog
        {
            Title = title, PrimaryButtonText = primary, CloseButtonText = "Cancel", DefaultButton = destructive ? ContentDialogButton.Close : ContentDialogButton.Primary,
            Content = new ScrollViewer { Content = Text(message), MaxHeight = 460 },
        };
        if (destructive) dialog.PrimaryButtonStyle = (Style)Application.Current.Resources["AeAccentButton"];
        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    public async Task<bool> RequestPermissionsAsync(string subjectName, IReadOnlyList<PermissionInfo> permissions, RiskLevel risk, string? scriptPreview = null)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Text($"{subjectName} needs the following permissions. AETHER snapshots everything it changes so you can undo it by turning the mod off.", "AeCaption"));
        foreach (var p in permissions)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0),
                Background = ThemeResources.Brush(p.Risk switch { RiskLevel.High => "AeDanger", RiskLevel.Medium => "AeWarning", _ => "AeSuccess" }) };
            var texts = new StackPanel { Spacing = 2 };
            texts.Children.Add(new TextBlock { Text = p.Title + (p.RequiresAdmin ? " (administrator)" : ""), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = ThemeResources.Brush("AeText") });
            texts.Children.Add(Text(p.Description, "AeCaption"));
            Grid.SetColumn(texts, 1);
            row.Children.Add(dot); row.Children.Add(texts);
            panel.Children.Add(row);
        }
        if (scriptPreview is not null)
        {
            panel.Children.Add(Text("Script to be executed:", "AeSectionTitle"));
            panel.Children.Add(new Border
            {
                Background = ThemeResources.Brush("AeSurfaceSolid"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10),
                Child = new TextBlock { Text = scriptPreview, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Foreground = ThemeResources.Brush("AeTextSecondary") },
            });
        }
        var dialog = new ContentDialog
        {
            Title = risk == RiskLevel.High ? $"Allow {subjectName}? (high impact)" : $"Allow {subjectName}?",
            PrimaryButtonText = "Allow and continue", CloseButtonText = "Not now", DefaultButton = ContentDialogButton.Close,
            Content = new ScrollViewer { Content = panel, MaxHeight = 480 },
        };
        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    public async Task<string?> PromptAsync(string title, string label, string? initial = null)
    {
        var box = new TextBox { Header = label, Text = initial ?? "", Style = (Style)Application.Current.Resources["AeTextBox"], MinWidth = 360 };
        var dialog = new ContentDialog { Title = title, Content = box, PrimaryButtonText = "OK", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        box.Loaded += (_, _) => { box.Focus(FocusState.Programmatic); box.SelectAll(); };
        return await ShowAsync(dialog) == ContentDialogResult.Primary ? box.Text : null;
    }

    public async Task ShowTextAsync(string title, string text)
    {
        var dialog = new ContentDialog
        {
            Title = title, CloseButtonText = "Close",
            Content = new ScrollViewer { MaxHeight = 520, Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = 13, Foreground = ThemeResources.Brush("AeTextSecondary") } },
        };
        await ShowAsync(dialog);
    }

    private nint Hwnd => Owner is null ? 0 : WinRT.Interop.WindowNative.GetWindowHandle(Owner);

    public async Task<string?> PickFileAsync(params FileFilter[] filters)
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.Thumbnail };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
        foreach (var ext in filters.SelectMany(f => f.Extensions).Distinct()) picker.FileTypeFilter.Add(ext);
        if (picker.FileTypeFilter.Count == 0) picker.FileTypeFilter.Add("*");
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public async Task<string?> SaveFileAsync(string suggestedName, FileFilter filter)
    {
        var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName) };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
        picker.FileTypeChoices.Add(filter.Name, filter.Extensions.ToList());
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }
}
