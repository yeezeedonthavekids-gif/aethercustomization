using Aether.ModEngine.Manifest;

namespace Aether.Tests.ModEngine;

public static class TestManifests
{
    public const string ExtensionsJson = """
    {
      "id": "test.file-extensions",
      "name": "File extensions",
      "description": "Shows file extensions",
      "author": "Tests",
      "version": "1.0.0",
      "category": "fileExplorer",
      "permissions": ["registry.user"],
      "settings": [
        { "key": "show", "label": "Show", "type": "boolean", "default": true },
        { "key": "superHidden", "label": "Protected files", "type": "boolean", "default": false }
      ],
      "operations": [
        { "op": "registry.set", "hive": "HKCU", "path": "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced",
          "name": "HideFileExt", "kind": "dWord", "value": "{{show}}", "transform": "invertBool" },
        { "op": "registry.set", "hive": "HKCU", "path": "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced",
          "name": "ShowSuperHidden", "kind": "dWord", "value": 1, "when": { "setting": "superHidden", "equals": true } },
        { "op": "shell.refresh" },
        { "op": "broadcast", "area": "ShellState" }
      ]
    }
    """;

    public static ModManifest Extensions() => ManifestSerializer.Parse(ExtensionsJson);

    public static ModManifest Animations() => ManifestSerializer.Parse("""
    {
      "id": "test.animations", "name": "Animations", "description": "d", "author": "a", "version": "1.0.0",
      "category": "visualEffects", "permissions": ["system.parameters", "registry.user"],
      "settings": [ { "key": "enabled", "label": "Enabled", "type": "boolean", "default": false },
                    { "key": "format", "label": "Format", "type": "text", "default": "HH:mm" } ],
      "operations": [
        { "op": "system.flag", "flag": "clientAreaAnimation", "value": "{{enabled}}" },
        { "op": "system.flag", "flag": "menuAnimation", "value": "{{enabled}}" },
        { "op": "registry.set", "hive": "HKCU", "path": "Control Panel\\International", "name": "sShortTime", "kind": "string", "value": "{{format}}" },
        { "op": "registry.set", "hive": "HKCU", "path": "Control Panel\\International", "name": "sTimeFormat", "kind": "string", "value": "{{format}}:ss" }
      ]
    }
    """);
}
