using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using Aether.Core.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Presentation.Items;

/// <summary>One field of a schema-generated settings form. Exposes typed accessors for each control kind.</summary>
public sealed partial class SettingFieldViewModel : ObservableObject
{
    private readonly SettingsFormViewModel _form;
    private JsonNode? _value;

    public SettingFieldViewModel(SettingsFormViewModel form, SettingDefinition definition, JsonNode? value)
    {
        _form = form;
        Definition = definition;
        _value = value?.DeepClone();
        Options = definition.Options?.Select(o => new OptionItem(o.Value, o.Label)).ToList() ?? [];
    }

    public SettingDefinition Definition { get; }
    public string Key => Definition.Key;
    public string Label => Definition.Label;
    public string? Description => Definition.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Definition.Description);
    public SettingKind Kind => Definition.Type;
    public IReadOnlyList<OptionItem> Options { get; }
    public double Minimum => Definition.Min ?? 0;
    public double Maximum => Definition.Max ?? 100;
    public double Step => Definition.Step ?? (Kind == SettingKind.Slider ? 1 : 0.01);
    public string Unit => Definition.Unit ?? "";
    public bool IsAdvanced => Definition.Advanced;
    public string? Group => Definition.Group;

    public bool IsBoolean => Kind == SettingKind.Boolean;
    public bool IsChoice => Kind == SettingKind.Choice;
    public bool IsSlider => Kind == SettingKind.Slider;
    public bool IsNumber => Kind == SettingKind.Number;
    public bool IsText => Kind is SettingKind.Text or SettingKind.Time or SettingKind.Hotkey;
    public bool IsColor => Kind == SettingKind.Color;
    public bool IsPath => Kind is SettingKind.FilePath or SettingKind.FolderPath;

    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private string? _error;

    public JsonNode? Value => _value;

    private void Set(JsonNode? value)
    {
        _value = value;
        Error = value is null ? null : SettingValues.Validate(Definition, value);
        OnPropertyChanged(nameof(BoolValue)); OnPropertyChanged(nameof(DoubleValue)); OnPropertyChanged(nameof(TextValue));
        OnPropertyChanged(nameof(SelectedOption)); OnPropertyChanged(nameof(ColorHex)); OnPropertyChanged(nameof(DisplayValue));
        _form.OnFieldChanged(this);
    }

    public bool BoolValue
    {
        get => _value is JsonValue v && v.TryGetValue<bool>(out var b) && b;
        set { if (value != BoolValue) Set(JsonValue.Create(value)); }
    }

    public double DoubleValue
    {
        get => _value is JsonValue v && SettingValues.TryGetDouble(v, out var d) ? d : Minimum;
        set { if (Math.Abs(value - DoubleValue) > double.Epsilon) Set(JsonValue.Create(Math.Round(value, 4))); }
    }

    public string TextValue
    {
        get => _value is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        set { if (value != TextValue) Set(JsonValue.Create(value)); }
    }

    public OptionItem? SelectedOption
    {
        get => Options.FirstOrDefault(o => o.Value == TextValue);
        set { if (value is not null && value.Value != TextValue) Set(JsonValue.Create(value.Value)); }
    }

    public string ColorHex
    {
        get => TextValue;
        set { if (ColorValue.TryParse(value, out var c)) Set(JsonValue.Create(c.ToHex())); }
    }

    public string DisplayValue => Kind switch
    {
        SettingKind.Slider or SettingKind.Number => DoubleValue.ToString(Step >= 1 ? "0" : "0.##", CultureInfo.CurrentCulture) + (Unit.Length > 0 ? " " + Unit : ""),
        _ => TextValue,
    };

    public void Reset() => Set(Definition.Default?.DeepClone() ?? SettingValues.ResolveDefaults([Definition])[Key]?.DeepClone());

    public void SetPath(string path) => Set(JsonValue.Create(path));
}

public sealed record OptionItem(string Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A settings form generated from a list of <see cref="SettingDefinition"/>s.</summary>
public sealed partial class SettingsFormViewModel : ObservableObject
{
    public SettingsFormViewModel(IReadOnlyList<SettingDefinition> definitions, JsonObject values)
    {
        Fields = new ObservableCollection<SettingFieldViewModel>(definitions.Select(d => new SettingFieldViewModel(this, d, values[d.Key])));
        UpdateVisibility();
    }

    public ObservableCollection<SettingFieldViewModel> Fields { get; }
    public bool IsEmpty => Fields.Count == 0;
    public bool HasAdvanced => Fields.Any(f => f.IsAdvanced);

    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _showAdvanced;

    public bool HasErrors => Fields.Any(f => f.Error is not null);

    public event Action? Changed;

    internal void OnFieldChanged(SettingFieldViewModel field)
    {
        IsDirty = true;
        UpdateVisibility();
        OnPropertyChanged(nameof(HasErrors));
        Changed?.Invoke();
    }

    partial void OnShowAdvancedChanged(bool value) => UpdateVisibility();

    private void UpdateVisibility()
    {
        foreach (var f in Fields)
        {
            var gate = f.Definition.VisibleWhen is { } key ? Fields.FirstOrDefault(x => x.Key == key)?.BoolValue ?? true : true;
            f.IsVisible = gate && (!f.IsAdvanced || ShowAdvanced);
        }
    }

    public JsonObject ToJson()
    {
        var o = new JsonObject();
        foreach (var f in Fields) o[f.Key] = f.Value?.DeepClone();
        return o;
    }

    public void ResetAll()
    {
        foreach (var f in Fields) f.Reset();
    }

    public void MarkClean() => IsDirty = false;
}
