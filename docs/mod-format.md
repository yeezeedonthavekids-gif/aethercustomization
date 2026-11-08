# AETHER mod format

A mod is a folder or `.aethermod` zip with `aether-mod.json` at its root.

```json
{
  "id": "contoso.show-extensions",
  "name": "Show extensions",
  "description": "Always show file extensions.",
  "author": "Contoso",
  "version": "1.0.0",
  "category": "fileExplorer",
  "permissions": ["registry.user"],
  "compatibility": { "minBuild": 10240 },
  "settings": [ { "key": "show", "label": "Show extensions", "type": "boolean", "default": true } ],
  "operations": [
    { "op": "registry.set", "hive": "HKCU", "path": "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced",
      "name": "HideFileExt", "kind": "dWord", "value": "{{show}}", "transform": "invertBool" },
    { "op": "shell.refresh" }
  ]
}
```

## Operations
`registry.set`, `registry.delete`, `system.flag`, `broadcast`, `shell.refresh`, `cursors.reload`, `explorer.restart`.
Each can have `"when": { "setting": "key", "equals": value }` (or `notEquals`). Values support `{{setting}}` templates,
`valueMap` and transforms (`colorToAbgr`, `colorToRgbTriplet`, `invertBool`, `boolToInt`).

## Settings types
`boolean`, `choice`, `slider`, `number`, `text`, `color`, `hotkey`, `time`, `filePath`, `folderPath` — with `min`, `max`,
`step`, `options`, `advanced`, `group`, `visibleWhen`, `pattern`. The UI form is generated from this schema.

## Permissions
`registry.user`, `registry.machine` (admin), `registry.shellClasses`, `startup.manage`, `system.parameters`,
`explorer.restart`, `powershell.execute`, `process.launch`, `filesystem.user`, `wallpaper.change`, `window.manage`,
`network.access`, `power.manage`, `shell.appearance`, `notifications.show`. Protected security areas are never writable.

## PowerShell mods
`"implementation": { "kind": "powerShell", "applyScript": "apply.ps1", "revertScript": "revert.ps1" }`. Users see both
scripts and their SHA-256 before consenting; a changed script requires new consent. Settings arrive as `$AetherSettings`.

## Signing
```bash
dotnet run --project tools/Aether.PackTool -- keygen contoso contoso.key
```
```bash
dotnet run --project tools/Aether.PackTool -- pack ./my-mod my-mod.aethermod --key contoso.key --publisher contoso --name "Contoso"
```
Users add your public key (`pubkey`) in Settings ▸ Security ▸ Trusted publishers.
