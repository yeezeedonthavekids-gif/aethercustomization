using System.IO.Compression;
using Aether.PluginSystem;
using Aether.Services.Settings;
using Aether.Tests.Fakes;

namespace Aether.Tests.Plugins;

public class PluginHostTests
{
    private static string SampleOutput()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var dir = Path.Combine(root, "samples", "Aether.Samples.HelloPlugin", "bin", "Debug", "net10.0");
        return Directory.Exists(dir) ? dir : Path.Combine(root, "samples", "Aether.Samples.HelloPlugin", "bin", "Release", "net10.0");
    }

    private static string Package(string dir, string target)
    {
        using var zip = ZipFile.Open(target, ZipArchiveMode.Create);
        zip.CreateEntryFromFile(Path.Combine(dir, "aether-plugin.json"), "aether-plugin.json");
        zip.CreateEntryFromFile(Path.Combine(dir, "Aether.Samples.HelloPlugin.dll"), "Aether.Samples.HelloPlugin.dll");
        return target;
    }

    [Fact]
    public async Task Unsigned_plugins_need_developer_mode_and_load_with_granted_capabilities()
    {
        using var h = new ServiceHarness();
        var host = h.Get<PluginHost>();
        var pkg = Package(SampleOutput(), Path.Combine(h.TempPath, "hello.aetherplugin"));

        Assert.False(host.Install(pkg).Success); // not trusted, developer mode off
        h.Get<ISettingsService>().Update(s => s with { DeveloperMode = true });
        Assert.True(host.Install(pkg).Success);

        var info = Assert.Single(host.GetPlugins());
        Assert.False((await host.EnableAsync(info.Manifest.Id)).Success); // capabilities not granted yet
        host.GrantCapabilities(info.Manifest.Id, info.MissingCapabilities);
        var r = await host.EnableAsync(info.Manifest.Id);
        Assert.True(r.Success, r.Describe());

        var command = Assert.Single(host.Commands);
        await command.Command.Execute(CancellationToken.None);
        await command.Command.Execute(CancellationToken.None);
        host.Publish(Aether.SDK.PluginEvents.ThemeApplied, new Dictionary<string, string> { ["name"] = "Nord" });

        Assert.True((await host.DisableAsync(info.Manifest.Id)).Success);
        Assert.Empty(host.Commands);
        Assert.True(host.Uninstall(info.Manifest.Id).Success);
        Assert.Empty(host.GetPlugins());
    }
}
