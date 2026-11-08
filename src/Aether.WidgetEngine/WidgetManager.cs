using System.Text.Json.Nodes;
using Aether.Storage;

namespace Aether.WidgetEngine;

/// <summary>Stores widget layouts (profiles) and edits the active one. The UI layer renders the windows.</summary>
public sealed class WidgetManager(IDocumentStore store)
{
    private const string Collection = "widget-layouts";
    private const string StateCollection = "widget-state";
    private readonly object _gate = new();

    public event Action? LayoutChanged;

    public IReadOnlyList<WidgetLayout> GetLayouts()
    {
        var layouts = store.GetAll<WidgetLayout>(Collection);
        if (layouts.Count > 0) return layouts;
        var initial = new WidgetLayout { Id = "default", Name = "Default" };
        store.Put(Collection, initial.Id, initial);
        return [initial];
    }

    public string ActiveLayoutId
    {
        get => store.Get<ActiveLayoutRecord>(StateCollection, "active")?.Id ?? GetLayouts()[0].Id;
        private set => store.Put(StateCollection, "active", new ActiveLayoutRecord(value));
    }

    /// <summary>Whether the widget host is running (widgets shown on the desktop).</summary>
    public bool HostEnabled
    {
        get => store.Get<HostRecord>(StateCollection, "host")?.Enabled ?? false;
        set { store.Put(StateCollection, "host", new HostRecord(value)); LayoutChanged?.Invoke(); }
    }

    public WidgetLayout ActiveLayout => GetLayouts().FirstOrDefault(l => l.Id == ActiveLayoutId) ?? GetLayouts()[0];

    public WidgetLayout CreateLayout(string name, bool copyActive)
    {
        var id = "layout-" + Guid.NewGuid().ToString("N")[..8];
        var layout = new WidgetLayout
        {
            Id = id, Name = name,
            Widgets = copyActive ? ActiveLayout.Widgets.Select(w => w with { Id = NewWidgetId() }).ToList() : [],
        };
        store.Put(Collection, id, layout);
        LayoutChanged?.Invoke();
        return layout;
    }

    public void RenameLayout(string id, string name) => Mutate(id, l => l with { Name = name });

    public bool DeleteLayout(string id)
    {
        if (GetLayouts().Count <= 1) return false;
        var ok = store.Delete(Collection, id);
        if (ActiveLayoutId == id) ActiveLayoutId = GetLayouts()[0].Id;
        LayoutChanged?.Invoke();
        return ok;
    }

    public void ActivateLayout(string id)
    {
        if (GetLayouts().All(l => l.Id != id)) throw new KeyNotFoundException($"Layout '{id}' not found.");
        ActiveLayoutId = id;
        LayoutChanged?.Invoke();
    }

    public WidgetInstance AddWidget(string typeId, double? x = null, double? y = null)
    {
        var def = WidgetCatalog.Find(typeId) ?? throw new KeyNotFoundException($"Unknown widget '{typeId}'.");
        var count = ActiveLayout.Widgets.Count;
        var widget = new WidgetInstance
        {
            Id = NewWidgetId(), TypeId = typeId,
            X = x ?? 80 + count % 4 * 40, Y = y ?? 80 + count % 4 * 40,
            Width = def.DefaultWidth, Height = def.DefaultHeight,
            RefreshSeconds = def.DefaultRefreshSeconds,
            Settings = WidgetCatalog.DefaultSettings(def),
        };
        Mutate(ActiveLayoutId, l => l with { Widgets = [.. l.Widgets, widget] });
        return widget;
    }

    public void UpdateWidget(WidgetInstance widget) =>
        Mutate(ActiveLayoutId, l => l with { Widgets = l.Widgets.Select(w => w.Id == widget.Id ? widget : w).ToList() });

    /// <summary>Position/size updates from dragging don't need to notify the host (it caused them).</summary>
    public void UpdateBounds(string widgetId, double x, double y, double width, double height)
    {
        lock (_gate)
        {
            var layout = ActiveLayout;
            var updated = layout with { Widgets = layout.Widgets.Select(w => w.Id == widgetId ? w with { X = x, Y = y, Width = width, Height = height } : w).ToList(), UpdatedAt = DateTimeOffset.UtcNow };
            store.Put(Collection, updated.Id, updated);
        }
    }

    public void UpdateWidgetSettings(string widgetId, JsonObject settings) =>
        Mutate(ActiveLayoutId, l => l with { Widgets = l.Widgets.Select(w => w.Id == widgetId ? w with { Settings = settings } : w).ToList() }, notify: false);

    public void RemoveWidget(string widgetId) =>
        Mutate(ActiveLayoutId, l => l with { Widgets = l.Widgets.Where(w => w.Id != widgetId).ToList() });

    public string ExportLayout(string id) =>
        AetherJson.Serialize(GetLayouts().First(l => l.Id == id), indented: true);

    public WidgetLayout ImportLayout(string json)
    {
        var layout = AetherJson.Deserialize<WidgetLayout>(json) ?? throw new FormatException("The layout file is empty.");
        var valid = layout.Widgets.Where(w => WidgetCatalog.Find(w.TypeId) is not null).Select(w => w with { Id = NewWidgetId() }).ToList();
        var imported = layout with { Id = "layout-" + Guid.NewGuid().ToString("N")[..8], Widgets = valid };
        store.Put(Collection, imported.Id, imported);
        LayoutChanged?.Invoke();
        return imported;
    }

    private void Mutate(string layoutId, Func<WidgetLayout, WidgetLayout> change, bool notify = true)
    {
        lock (_gate)
        {
            var layout = GetLayouts().FirstOrDefault(l => l.Id == layoutId) ?? throw new KeyNotFoundException($"Layout '{layoutId}' not found.");
            var updated = change(layout) with { UpdatedAt = DateTimeOffset.UtcNow };
            store.Put(Collection, updated.Id, updated);
        }
        if (notify) LayoutChanged?.Invoke();
    }

    private static string NewWidgetId() => "w-" + Guid.NewGuid().ToString("N")[..10];

    private sealed record ActiveLayoutRecord(string Id);
    private sealed record HostRecord(bool Enabled);
}
