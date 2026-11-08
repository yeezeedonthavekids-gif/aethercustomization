namespace Aether.Core.Abstractions;

public interface IAppPaths
{
    string Root { get; }
    string DatabaseFile { get; }
    string Mods { get; }
    string Plugins { get; }
    string Themes { get; }
    string Backups { get; }
    string Logs { get; }
    string Cache { get; }
    string Marketplace { get; }
    string Wallpapers { get; }
}

public sealed class AppPaths : IAppPaths
{
    public AppPaths(string root)
    {
        Root = root;
        Directory.CreateDirectory(root);
        foreach (var dir in new[] { Mods, Plugins, Themes, Backups, Logs, Cache, Marketplace, Wallpapers })
            Directory.CreateDirectory(dir);
    }

    public static AppPaths ForCurrentUser() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether"));

    public string Root { get; }
    public string DatabaseFile => Path.Combine(Root, "aether.db");
    public string Mods => Path.Combine(Root, "mods");
    public string Plugins => Path.Combine(Root, "plugins");
    public string Themes => Path.Combine(Root, "themes");
    public string Backups => Path.Combine(Root, "backups");
    public string Logs => Path.Combine(Root, "logs");
    public string Cache => Path.Combine(Root, "cache");
    public string Marketplace => Path.Combine(Root, "marketplace");
    public string Wallpapers => Path.Combine(Root, "wallpapers");
}
