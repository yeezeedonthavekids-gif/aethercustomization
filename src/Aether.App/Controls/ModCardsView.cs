using Aether.Presentation.Items;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Aether.App.Controls.Ui;

namespace Aether.App.Controls;

/// <summary>Mod cards with live toggles, status and quick actions. Used on feature pages and the mods page.</summary>
public sealed class ModCardsView : ItemsRepeater
{
    public ModCardsView()
    {
        Layout = new UniformGridLayout { MinItemWidth = 320, MinItemHeight = 150, MinColumnSpacing = 12, MinRowSpacing = 12, ItemsStretch = UniformGridLayoutItemsStretch.Fill };
        ItemTemplate = new CardFactory(this);
    }

    public static readonly DependencyProperty ModsProperty =
        DependencyProperty.Register(nameof(Mods), typeof(System.Collections.IEnumerable), typeof(ModCardsView), new PropertyMetadata(null, (d, e) => ((ModCardsView)d).ItemsSource = e.NewValue));

    public System.Collections.IEnumerable? Mods { get => (System.Collections.IEnumerable?)GetValue(ModsProperty); set => SetValue(ModsProperty, value); }

    /// <summary>Toggle handler: (item, enable).</summary>
    public Func<ModItemViewModel, bool, Task>? Toggle { get; set; }
    public Action<ModItemViewModel>? Open { get; set; }
    public Func<ModItemViewModel, Task>? RunAction { get; set; }

    private sealed class CardFactory(ModCardsView owner) : IElementFactory
    {
        public UIElement GetElement(ElementFactoryGetArgs args) => Build((ModItemViewModel)args.Data);
        public void RecycleElement(ElementFactoryRecycleArgs args) { }

        private UIElement Build(ModItemViewModel m)
        {
            var toggle = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0, IsOn = m.IsEnabled };
            var status = Chip(m.StatusText);
            var runtime = Text(m.RuntimeStatus ?? "", "AeMuted");
            var error = Text(m.LastError ?? "", "AeCaption");
            error.Foreground = Brush("AeDanger");
            var ring = new ProgressRing { Width = 18, Height = 18, IsActive = false };
            toggle.Toggled += async (_, _) =>
            {
                if (toggle.IsOn == m.IsEnabled || owner.Toggle is null) return;
                await owner.Toggle(m, toggle.IsOn);
                toggle.IsOn = m.IsEnabled;
            };
            void Sync()
            {
                toggle.IsOn = m.IsEnabled;
                toggle.IsEnabled = !m.IsWorking && m.IsCompatible;
                ring.IsActive = m.IsWorking;
                ((TextBlock)status.Child).Text = m.StatusText;
                status.Background = Brush(m.IsEnabled ? "AeAccentSoft" : "AeSurfaceHover");
                runtime.Text = m.RuntimeStatus ?? ""; runtime.Visibility = m.HasRuntimeStatus ? Visibility.Visible : Visibility.Collapsed;
                error.Text = m.LastError ?? ""; error.Visibility = m.HasError ? Visibility.Visible : Visibility.Collapsed;
            }
            m.PropertyChanged += (_, _) => Sync();
            Sync();

            var header = new Grid { ColumnSpacing = 12 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titles = Stack(2, Title(m.Name, 14.5), Text($"{m.CategoryLabel} · {m.Author} · v{m.Version}", "AeMuted"));
            Grid.SetColumn(titles, 1);
            var right = HStack(6, ring, toggle);
            Grid.SetColumn(right, 2);
            header.Children.Add(IconTile(m.Glyph, 38)); header.Children.Add(titles); header.Children.Add(right);

            var desc = Text(m.Description, "AeCaption");
            desc.MaxLines = 2;
            desc.TextTrimming = TextTrimming.CharacterEllipsis;

            var footer = HStack(8, status);
            if (m.IsRisky) footer.Children.Add(Chip(m.RiskLabel, "AeSurfaceHover", "AeWarning"));
            if (!m.IsCompatible) footer.Children.Add(Chip("Incompatible", "AeSurfaceHover", "AeDanger"));
            var configure = new HyperlinkButton { Content = m.HasSettings ? "Configure" : "Details", FontSize = 12.5 };
            configure.Click += (_, _) => owner.Open?.Invoke(m);
            footer.Children.Add(configure);
            if (m.HasActions)
            {
                var act = new HyperlinkButton { Content = m.Mod.Manifest.Actions[0].Label, FontSize = 12.5 };
                act.Click += async (_, _) => { if (owner.RunAction is not null) await owner.RunAction(m); };
                footer.Children.Add(act);
            }
            if (!m.IsCompatible) ToolTipService.SetToolTip(footer, m.CompatibilitySummary);

            var card = Card(Stack(10, header, desc, runtime, error, footer));
            card.Padding = new Thickness(16);
            return card;
        }
    }
}
