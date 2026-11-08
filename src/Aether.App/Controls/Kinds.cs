using Aether.Services.Automation;

namespace Aether.App.Controls;

/// <summary>Enum lists for the automation editor's dropdowns (static so x:Bind can reach them from templates).</summary>
public static class Kinds
{
    public static IReadOnlyList<TriggerKind> Triggers { get; } = Enum.GetValues<TriggerKind>().Where(k => k != TriggerKind.Manual).ToList();
    public static IReadOnlyList<ConditionKind> Conditions { get; } = Enum.GetValues<ConditionKind>();
    public static IReadOnlyList<ActionKind> Actions { get; } = Enum.GetValues<ActionKind>();
}
