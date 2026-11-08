using Aether.Presentation.Items;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Aether.App.Controls.Ui;

namespace Aether.App.Controls;

/// <summary>Generates controls from a <see cref="SettingsFormViewModel"/>: switches, dropdowns, sliders, colors, text, hotkeys, paths.</summary>
public sealed class SettingsFormView : StackPanel
{
    public SettingsFormView() { Spacing = 14; }

    public static readonly DependencyProperty FormProperty =
        DependencyProperty.Register(nameof(Form), typeof(SettingsFormViewModel), typeof(SettingsFormView), new PropertyMetadata(null, (d, _) => ((SettingsFormView)d).Build()));

    public SettingsFormViewModel? Form { get => (SettingsFormViewModel?)GetValue(FormProperty); set => SetValue(FormProperty, value); }

    /// <summary>Set by the host to open file/folder pickers for path settings.</summary>
    public Func<bool, Task<string?>>? PickPath { get; set; }

    private void Build()
    {
        Children.Clear();
        if (Form is null) return;
        if (Form.IsEmpty) { Children.Add(Text("This mod has no settings.", "AeCaption")); return; }
        string? group = null;
        foreach (var f in Form.Fields)
        {
            if (f.Group is { } g && g != group)
            {
                group = g;
                Children.Add(new TextBlock { Text = g.ToUpperInvariant(), FontSize = 11.5, CharacterSpacing = 80, Foreground = Brush("AeTextMuted"), Margin = new Thickness(0, 6, 0, -4) });
            }
            var row = BuildField(f);
            void Vis() => row.Visibility = f.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            f.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SettingFieldViewModel.IsVisible)) Vis(); };
            Vis();
            Children.Add(row);
        }
        if (Form.HasAdvanced)
        {
            var adv = new ToggleSwitch { Header = "Show advanced settings", IsOn = Form.ShowAdvanced };
            adv.Toggled += (_, _) => Form.ShowAdvanced = adv.IsOn;
            Children.Add(adv);
        }
    }

    private FrameworkElement BuildField(SettingFieldViewModel f)
    {
        var label = Stack(2, Title(f.Label, 13.5));
        if (f.HasDescription) label.Children.Add(Text(f.Description!, "AeCaption"));
        var error = Text("", "AeCaption");
        error.Foreground = Brush("AeDanger");
        f.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SettingFieldViewModel.Error)) { error.Text = f.Error ?? ""; error.Visibility = f.Error is null ? Visibility.Collapsed : Visibility.Visible; } };
        error.Visibility = Visibility.Collapsed;

        FrameworkElement input;
        if (f.IsBoolean)
        {
            var t = new ToggleSwitch { IsOn = f.BoolValue, OnContent = "", OffContent = "", MinWidth = 0 };
            t.Toggled += (_, _) => f.BoolValue = t.IsOn;
            return Row(label, t);
        }
        if (f.IsChoice)
        {
            var c = new ComboBox { ItemsSource = f.Options, DisplayMemberPath = "Label", SelectedItem = f.SelectedOption, MinWidth = 220, CornerRadius = new CornerRadius(10) };
            c.SelectionChanged += (_, _) => { if (c.SelectedItem is OptionItem o) f.SelectedOption = o; };
            return Row(label, c);
        }
        if (f.IsSlider)
        {
            var value = Text(f.DisplayValue, "AeCaption");
            var s = new Slider { Minimum = f.Minimum, Maximum = f.Maximum, StepFrequency = f.Step, Value = f.DoubleValue, Width = 240 };
            s.ValueChanged += (_, e) => { f.DoubleValue = e.NewValue; value.Text = f.DisplayValue; };
            return Row(label, HStack(12, s, value));
        }
        if (f.IsNumber)
        {
            var n = new NumberBox { Value = f.DoubleValue, Minimum = f.Minimum, Maximum = f.Maximum, SmallChange = f.Step, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 160 };
            n.ValueChanged += (_, e) => { if (!double.IsNaN(e.NewValue)) f.DoubleValue = e.NewValue; };
            return Row(label, n);
        }
        if (f.IsColor)
        {
            var swatch = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(8), BorderBrush = Brush("AeBorder"), BorderThickness = new Thickness(1), Background = B.Brush(f.ColorHex) };
            var picker = new ColorPicker { IsAlphaEnabled = true, IsMoreButtonVisible = false, Color = Services.ThemeResources.ToColor(f.ColorHex) };
            picker.ColorChanged += (_, e) => { var c = e.NewColor; f.ColorHex = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}"; swatch.Background = B.Brush(f.ColorHex); };
            var btn = new Button { Content = HStack(8, swatch, new TextBlock { Text = f.ColorHex, VerticalAlignment = VerticalAlignment.Center }), Style = Style("AeButton"), Flyout = new Flyout { Content = picker } };
            return Row(label, btn);
        }
        if (f.IsPath)
        {
            var box = new TextBox { Text = f.TextValue, Width = 260, Style = Style("AeTextBox"), PlaceholderText = "Not set" };
            box.LostFocus += (_, _) => f.TextValue = box.Text;
            var browse = Ui.Button("Browse…", async () =>
            {
                if (PickPath is null) return;
                var p = await PickPath(f.Kind == Core.Configuration.SettingKind.FolderPath);
                if (p is not null) { box.Text = p; f.SetPath(p); }
            });
            input = HStack(8, box, browse);
            return Stack(6, Row(label, input), error);
        }
        var text = new TextBox { Text = f.TextValue, Width = 260, Style = Style("AeTextBox"), PlaceholderText = f.Definition.Placeholder ?? "",
            AcceptsReturn = f.Key is "apps" or "text", TextWrapping = TextWrapping.Wrap, MaxHeight = 140 };
        text.TextChanged += (_, _) => f.TextValue = text.Text;
        return Stack(6, Row(label, text), error);
    }
}
