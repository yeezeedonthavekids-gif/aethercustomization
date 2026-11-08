# Architecture

```
Aether.App (WinUI 3)  ──▶ Aether.Presentation (view models) ──▶ Aether.Services ──▶ ModEngine / ThemeEngine / WidgetEngine / PluginSystem ──▶ Storage ──▶ Core
        └──▶ Aether.WindowsIntegration (implements Core + Services platform contracts)
```

## Why this structure

- **Testability without Windows.** All logic sits behind small platform interfaces (`IRegistryAccess`, `ISystemSettings`,
  `IWallpaperService`, `IWindowManager`, …). Tests run the real engine against in-memory fakes and real SQLite.
- **One write path.** Every registry change made by mods *and* native tweaks goes through `OperationExecutor`, which
  persists the original value to the owner's baseline before writing and rolls back on failure. Components get a
  `GuardedRegistry` that enforces `RegistryPolicy` and baselines writes automatically.
- **The design system isn't a separate WinUI library.** Resource lookup across unpackaged WinUI class libraries adds PRI
  merging complexity with no benefit yet, so `Aether.App/Themes` holds it; `Aether.Presentation` was added instead so
  view models are testable on any OS.

## Mod lifecycle

1. **Install** — package read with zip-slip/size limits → manifest validated → SHA-256 digest + ECDSA signature checked
   against trusted publishers (tampered = refused, unsigned = refused unless allowed) → compatibility and dependencies
   checked → extracted to `mods/<id>/<version>` (last 3 versions kept).
2. **Enable** — compatibility, admin and dependency checks → missing permissions returned for consent → declarative plan
   built from settings → transactional apply → component started / PowerShell apply script run.
3. **Configure** — new plan applied; targets that drop out of the plan are restored from the baseline; on failure the
   previous configuration is re-applied.
4. **Disable** — component stopped → revert script → baseline restored (failures stay in the baseline for retry).
5. **Startup** — installed packages are re-verified; enabled declarative mods re-applied (repairs drift).

## Background work

`AetherRuntime` starts each component independently (mods, plugins, automation, window rules, theme schedule, scheduled
backups). Nothing polls without a reason: automation only ticks while enabled rules exist and only snapshots processes
when a rule needs them; metrics are sampled on demand by visible widgets.

## Windows API dependencies

| Feature | API | Minimum |
|---|---|---|
| Registry tweaks / mods | `Microsoft.Win32.Registry` | Windows 10 |
| Animations, shadows, pointer precision | `SystemParametersInfo` | Windows 10 |
| Setting refresh | `WM_SETTINGCHANGE`, `SHChangeNotify` | Windows 10 |
| Wallpapers per monitor | `IDesktopWallpaper` (COM) | Windows 8 |
| Shortcuts | `IShellLinkW` | Windows 10 |
| Taskbar effects | `SetWindowCompositionAttribute` (undocumented, no injection; mod is labelled) | 1809 |
| Auto-hide taskbar | `SHAppBarMessage` | Windows 10 |
| Window management | `EnumWindows`, `SetWindowPos`, `SetWinEventHook`, layered windows | Windows 10 |
| Media widgets | `GlobalSystemMediaTransportControlsSessionManager` (WinRT) | 1809 |
| GPU widget | PDH `GPU Engine` counters | 1709 |
| Store app discovery | `Get-StartApps` | Windows 10 |
| Restore points | `Checkpoint-Computer` (admin) | Windows 10 |
| Power plans | `powercfg.exe` | Windows 10 |
| Windows 11 taskbar/Start options | per-tweak `minBuild` (22000/22621/22631) | Windows 11 |
