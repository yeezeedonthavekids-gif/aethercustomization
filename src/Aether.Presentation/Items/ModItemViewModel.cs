using Aether.ModEngine.Lifecycle;
using Aether.ModEngine.Manifest;
using Aether.ModEngine.Security;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Presentation.Items;

public sealed partial class ModItemViewModel : ObservableObject
{
    public ModItemViewModel(InstalledMod mod) => Update(mod);

    public InstalledMod Mod { get; private set; } = null!;

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isWorking;

    public string Id => Mod.Id;
    public string Name => Mod.Manifest.Name;
    public string Description => Mod.Manifest.Description;
    public string Author => Mod.Manifest.Author;
    public string Version => Mod.Manifest.Version.ToString();
    public string Glyph => Mod.Manifest.Icon ?? "";
    public ModCategory Category => Mod.Manifest.Category;
    public string CategoryLabel => CategoryName(Mod.Manifest.Category);
    public bool IsBuiltIn => Mod.IsBuiltIn;
    public bool IsCompatible => Mod.Compatibility.IsCompatible;
    public string? LastError => Mod.Status == ModStatus.Error ? Mod.LastError : null;
    public bool HasError => LastError is not null;
    public string? RuntimeStatus => Mod.RuntimeStatus;
    public bool HasRuntimeStatus => !string.IsNullOrEmpty(Mod.RuntimeStatus);
    public string RiskLabel => Mod.Manifest.Risk switch { RiskLevel.High => "High impact", RiskLevel.Medium => "Moderate impact", _ => "Low impact" };
    public bool IsRisky => Mod.Manifest.Risk != RiskLevel.Low;
    public bool NeedsConsent => Mod.MissingPermissions.Count > 0;
    public bool HasActions => Mod.Manifest.Actions.Count > 0;
    public bool HasSettings => Mod.Manifest.Settings.Count > 0;
    public string CompatibilitySummary => Mod.Compatibility.IsCompatible ? "Compatible" : string.Join(" ", Mod.Compatibility.Problems);

    public string StatusText => Mod.Status switch
    {
        ModStatus.Enabled => "Active",
        ModStatus.Error => "Needs attention",
        ModStatus.Incompatible => "Incompatible",
        _ => NeedsConsent ? "Needs permission" : "Off",
    };

    public string VerificationText => Mod.Verification switch
    {
        VerificationLevel.BuiltIn => "Built into AETHER",
        VerificationLevel.Trusted => "Signed by a trusted publisher",
        VerificationLevel.SignedUntrusted => "Signed, publisher not trusted",
        VerificationLevel.Unsigned => "Unsigned",
        _ => "Integrity check failed",
    };

    public void Update(InstalledMod mod)
    {
        Mod = mod;
        IsEnabled = mod.IsEnabled;
        OnPropertyChanged(string.Empty);
    }

    public static string CategoryName(ModCategory c) => c switch
    {
        ModCategory.StartMenu => "Start menu",
        ModCategory.FileExplorer => "File Explorer",
        ModCategory.WindowsUi => "Windows UI",
        ModCategory.WindowManagement => "Window management",
        ModCategory.ContextMenus => "Context menus",
        ModCategory.SystemBehavior => "System behavior",
        ModCategory.VisualEffects => "Visual effects",
        _ => c.ToString(),
    };
}
