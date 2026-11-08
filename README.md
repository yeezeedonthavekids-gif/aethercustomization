# AETHER — Your Windows. Your Rules.

AETHER is an all-in-one Windows customization platform: a transactional **mod engine**, native Windows settings with full
disclosure, themes, desktop widgets, an independent dock and launcher, window management, automation, a marketplace,
a plugin SDK and verified backups — in one WinUI 3 app.

> Status: early but functional (v0.1). The core engine and every page are implemented and covered by tests; the WinUI
> app is built and smoke-tested on Windows in CI. See [docs/status.md](docs/status.md) for what's done and what's planned.

## Principles

- **Real functionality only.** Every switch changes something real; features Windows doesn't support are labelled as such
  (e.g. the Start menu can't be restyled, so AETHER ships its own launcher).
- **Everything is reversible.** Before any change, the original value is written to a SQLite-backed baseline
  (write-ahead), so disabling a mod or choosing *Restore original* puts Windows back exactly.
- **Transactional.** A failing step rolls back every step already applied in that operation.
- **Explained before applied.** Permissions, what changes, side effects, admin/restart needs and how to revert are shown.
- **Safe by default.** Security-critical registry areas (Defender, UAC, Windows Update, services, LSA, Winlogon, IFEO…)
  are hard-blocked; packages are SHA-256 integrity-checked and ECDSA-signed; PowerShell consent is bound to the script hash.
- **Honest data.** The marketplace never invents ratings or download counts; restore points are only reported when Windows
  actually created one.

## Solution layout

| Project | Target | Purpose |
|---|---|---|
| `Aether.Core` | net10.0 | Platform abstractions, setting schema, SemVer, colors, hotkeys, event bus, solar calculator |
| `Aether.Storage` | net10.0 | SQLite (migrations, documents, mod state/history, snapshots, permission grants, audit log) |
| `Aether.ModEngine` | net10.0 | Manifests, permissions, registry policy, signing, baselines, transactional executor, `ModManager` |
| `Aether.ThemeEngine` | net10.0 | Palettes, theme packs, Windows personalization, rollback, import/export, schedules |
| `Aether.WidgetEngine` | net10.0 | Widget catalog (19 types), layouts/profiles, data provider contracts |
| `Aether.SDK` / `Aether.PluginSystem` | net10.0 | Plugin contracts and the capability-gated plugin host |
| `Aether.Services` | net10.0 | 20 built-in mods, 50+ documented tweaks, backups, automation, palette, marketplace, apps, windows, profiles |
| `Aether.Presentation` | net10.0 | MVVM view models (CommunityToolkit.Mvvm) for every page |
| `Aether.WindowsIntegration` | net10.0-windows | Win32/COM/WinRT implementations (registry, SPI, IDesktopWallpaper, shell links, windows, metrics, media) |
| `Aether.App` | WinUI 3 | Shell, design system, pages, widgets, dock, launcher (unpackaged, self-contained Windows App SDK) |
| `Aether.Tests` | net10.0 | xUnit tests over fakes and real SQLite (run on Linux and Windows) |

Everything except the WinUI app builds and tests on any OS. See [docs/architecture.md](docs/architecture.md).

## Build

Requirements: .NET 10 SDK; Windows 10 1809+ to run the app.

```bash
dotnet test tests/Aether.Tests
```

```bash
dotnet build src/Aether.App/Aether.App.csproj -c Release -p:Platform=x64
```

The app writes its data to `%LocalAppData%\Aether` (override with the `AETHER_DATA` environment variable).
`Aether.exe --smoke-test <report.txt>` visits every page, saves screenshots next to the report and exits.

## Writing mods and plugins

- Mods: [docs/mod-format.md](docs/mod-format.md) — declarative JSON manifests, signed with `tools/Aether.PackTool`.
- Plugins: [docs/plugin-sdk.md](docs/plugin-sdk.md) — see `samples/Aether.Samples.HelloPlugin`.
