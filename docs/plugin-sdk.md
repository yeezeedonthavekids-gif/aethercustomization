# Plugin SDK

Plugins are .NET 10 class libraries referencing `Aether.SDK` (not copied into the plugin output). Implement
`IAetherPlugin` and describe the plugin in `aether-plugin.json` (see `samples/Aether.Samples.HelloPlugin`).

| Capability | Grants |
|---|---|
| `plugin.commands` | `RegisterCommand` — add command palette entries |
| `plugin.events` | `Subscribe` to `theme.applied`, `mod.changed`, `automation.ruleRan` |
| `notifications.show` | `Notify` |
| `network.access` | `GetHttpClient` |

`Storage` (private key/value) and `Log` are always available. Calling a member without its capability throws
`UnauthorizedAccessException`. Users grant capabilities when enabling the plugin.

Each plugin loads in its own collectible `AssemblyLoadContext` (dependency isolation, unload on disable). This is **not a
security sandbox** — plugins are .NET code — so AETHER only loads plugins signed by a trusted publisher unless developer mode
is enabled. Package a plugin as a zip (`.aetherplugin`) containing the manifest, your DLLs and optionally `signature.json`
(same signing scheme as mods).
