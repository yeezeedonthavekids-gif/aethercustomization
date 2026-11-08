using System.Text.Json.Nodes;
using Aether.Core.Configuration;
using Aether.Core.Events;
using Aether.Core.Versioning;

namespace Aether.Tests.Core;

public class CoreTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.4", -1)]
    [InlineData("1.0.0-alpha", "1.0.0", -1)]
    [InlineData("1.0.0-alpha.2", "1.0.0-alpha.10", -1)]
    [InlineData("2.0.0", "1.9.9", 1)]
    [InlineData("v1.0", "1.0.0", 0)]
    public void SemVersion_orders_correctly(string a, string b, int expected) =>
        Assert.Equal(expected, Math.Sign(SemVersion.Parse(a).CompareTo(SemVersion.Parse(b))));

    [Theory]
    [InlineData("^1.2.0", "1.9.0", true)]
    [InlineData("^1.2.0", "2.0.0", false)]
    [InlineData("~1.2.0", "1.2.9", true)]
    [InlineData("~1.2.0", "1.3.0", false)]
    [InlineData(">=1.0.0 <1.5.0", "1.4.9", true)]
    [InlineData("*", "0.0.1", true)]
    [InlineData("1.0.0", "1.0.1", false)]
    public void VersionRange_matches(string range, string version, bool expected) =>
        Assert.Equal(expected, VersionRange.Parse(range).IsSatisfiedBy(SemVersion.Parse(version)));

    [Fact]
    public void Colors_parse_and_convert_to_abgr()
    {
        var c = ColorValue.Parse("#FF3366CC");
        Assert.Equal(unchecked((int)0xFFCC6633), c.ToAbgr());
        Assert.Equal(c, ColorValue.FromAbgr(c.ToAbgr()));
        Assert.Equal("#FFAABBCC", ColorValue.Parse("#abc").ToHex());
        Assert.False(ColorValue.TryParse("#12", out _));
    }

    [Theory]
    [InlineData("Ctrl+Space", true)]
    [InlineData("Ctrl+Alt+F12", true)]
    [InlineData("Win+Shift+K", true)]
    [InlineData("Space", false)]
    [InlineData("Ctrl+Nope", false)]
    public void Hotkeys_parse(string text, bool ok) => Assert.Equal(ok, Hotkey.TryParse(text, out _));

    [Fact]
    public void Settings_resolve_with_defaults_and_reject_invalid_values()
    {
        var defs = new List<SettingDefinition>
        {
            new() { Key = "size", Label = "Size", Type = SettingKind.Slider, Min = 0, Max = 10, Default = 5 },
            new() { Key = "mode", Label = "Mode", Type = SettingKind.Choice, Default = "a", Options = [new() { Value = "a", Label = "A" }, new() { Value = "b", Label = "B" }] },
            new() { Key = "color", Label = "Color", Type = SettingKind.Color, Default = "#FF000000" },
        };
        var (values, errors) = SettingValues.Resolve(defs, new JsonObject { ["size"] = 42, ["mode"] = "b", ["color"] = "nope", ["unknown"] = 1 });
        Assert.Equal(5, values.GetDouble("size"));
        Assert.Equal("b", values.GetString("mode"));
        Assert.Equal("#FF000000", values.GetString("color"));
        Assert.False(values.ContainsKey("unknown"));
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void EventBus_isolates_failing_handlers()
    {
        var bus = new EventBus();
        var failures = 0;
        var received = 0;
        bus.HandlerFailed += _ => failures++;
        bus.Subscribe<string>(_ => throw new InvalidOperationException());
        using (bus.Subscribe<string>(_ => received++)) bus.Publish("x");
        bus.Publish("y");
        Assert.Equal(1, received);
        Assert.Equal(2, failures);
    }
}
