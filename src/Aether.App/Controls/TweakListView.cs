using Aether.Presentation.Items;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Aether.App.Controls.Ui;

namespace Aether.App.Controls;

/// <summary>Renders native Windows settings with full disclosure (what, why, side effects, restart, revert).</summary>
public sealed class TweakListView : StackPanel
{
    public TweakListView() { Spacing = 8; }

    public static readonly DependencyProperty GroupProperty =
        DependencyProperty.Register(nameof(Group), typeof(TweakGroupViewModel), typeof(TweakListView), new PropertyMetadata(null, (d, _) => ((TweakListView)d).Build()));

    public TweakGroupViewModel? Group { get => (TweakGroupViewModel?)GetValue(GroupProperty); set => SetValue(GroupProperty, value); }

    private void Build()
    {
        Children.Clear();
        if (Group is null) return;
        string? lastGroup = null;
        foreach (var item in Group.Items)
        {
            if (item.Group is { } g && g != lastGroup)
            {
                lastGroup = g;
                Children.Add(new TextBlock { Text = g.ToUpperInvariant(), FontSize = 11.5, CharacterSpacing = 80, Foreground = Brush("AeTextMuted"), Margin = new Thickness(4, 14, 0, 2) });
            }
            Children.Add(BuildRow(item));
        }
    }

    private static UIElement BuildRow(TweakItemViewModel item)
    {
        var title = Title(item.Title, 14);
        var why = Text(item.Why, "AeCaption");
        var chips = HStack(6);
        var note = Text("", "AeCaption");
        note.Foreground = Brush("AeWarning");

        FrameworkElement control;
        if (item.IsToggle)
        {
            var toggle = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0 };
            toggle.Toggled += (_, _) => { if (toggle.IsOn != item.IsOn) item.IsOn = toggle.IsOn; };
            control = toggle;
            void Sync() { toggle.IsOn = item.IsOn; toggle.IsEnabled = item.IsCompatible && !item.IsWorking; }
            item.PropertyChanged += (_, _) => Sync();
            Sync();
        }
        else
        {
            var combo = new ComboBox { ItemsSource = item.Options, DisplayMemberPath = "Label", MinWidth = 200, CornerRadius = new CornerRadius(10) };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is OptionItem o && o != item.SelectedOption) item.SelectedOption = o; };
            control = combo;
            void Sync() { combo.SelectedItem = item.SelectedOption; combo.PlaceholderText = item.IsUnknownValue ? "Custom value" : ""; combo.IsEnabled = item.IsCompatible && !item.IsWorking; }
            item.PropertyChanged += (_, _) => Sync();
            Sync();
        }

        var restore = new HyperlinkButton { Content = "Restore original", Padding = new Thickness(6, 2, 6, 2), FontSize = 12 };
        restore.Click += (_, _) => item.RestoreCommand.Execute(null);
        var info = new Button { Content = Icon("", 14, "AeTextSecondary"), Style = Style("AeGhostButton"), Padding = new Thickness(6) };
        ToolTipService.SetToolTip(info, "Details");
        info.Flyout = new Flyout { Content = new TextBlock { Text = item.Details, TextWrapping = TextWrapping.Wrap, MaxWidth = 380, FontSize = 13 } };

        void Refresh()
        {
            chips.Children.Clear();
            if (item.NeedsRestart) chips.Children.Add(Chip(item.RestartText, "AeSurfaceHover", "AeTextSecondary"));
            if (item.RequiresAdmin) chips.Children.Add(Chip("Admin", "AeSurfaceHover", "AeWarning"));
            if (item.IsModified) chips.Children.Add(Chip("Changed by AETHER"));
            restore.Visibility = item.IsModified ? Visibility.Visible : Visibility.Collapsed;
            note.Text = item.Note ?? "";
            note.Visibility = item.HasNote ? Visibility.Visible : Visibility.Collapsed;
        }
        item.PropertyChanged += (_, _) => Refresh();
        Refresh();

        var left = Stack(4, title, why, HStack(8, chips, restore), note);
        var right = HStack(4, info, control);
        right.VerticalAlignment = VerticalAlignment.Center;
        var card = Card(Row(left, right));
        card.Padding = new Thickness(18, 14, 18, 14);
        return card;
    }
}
