# Implementation status

Legend: ✅ implemented and tested · 🟡 implemented, Windows-only behavior verified only by CI smoke test · 🗓️ planned

## Foundation
- ✅ Modular solution, DI, SQLite storage with migrations, file logging + in-app log viewer, settings
- ✅ Design system (8 palettes + custom accent, live restyling), command palette (Ctrl+Space), toasts, notification center
- 🟡 WinUI 3 shell: custom title bar, Mica, collapsible/reorderable/searchable sidebar, keyboard navigation (Ctrl+1…9)

## Mod engine
- ✅ Manifests, schema-generated settings, permissions + consent, registry policy (security areas blocked)
- ✅ ECDSA-signed packages, SHA-256 integrity, trusted publishers, re-verification on startup
- ✅ Write-ahead baselines, transactional apply with rollback, drift repair, version history and restore
- ✅ Declarative, component and PowerShell (hash-bound consent) implementations; config import/export; logs
- ✅ 20 built-in mods (see `src/Aether.Services/Mods/Builtin/Manifests`)

## Pages
- 🟡 Overview, Personalization (accent, modes, wallpapers per monitor, Bing daily images, cursors), Taskbar (native settings,
  9 profiles, AETHER dock), Start Menu (native settings, 6 launcher layouts, pins), File Explorer (settings, mods, custom
  folder icons), Windows Mods, Desktop (layouts, rules, workspaces, always-on-top, opacity), Widgets (19 types, layouts),
  Themes (gallery, editor, import/export, schedule, rollback), Marketplace (local + HTTPS sources, favorites, publishers,
  updates), Applications (Start menu + Store apps, startup manager, launch profiles, groups), System (50+ documented settings,
  power plans, system info), Automation (visual rule editor, history), Backups (verified backups, restore points, change
  history), Settings (15 sections), onboarding

## Known limitations / planned
- 🗓️ Colored folder icon packs (custom icons work today)
- 🗓️ Live/video wallpapers (deliberately not offered: no rendering engine yet)
- 🗓️ Plugin-provided mod components and widgets (plugins can add commands and react to events today)
- 🗓️ MSIX/installer packaging and an AETHER self-updater
- Windows offers no supported API for: restyling the native Start menu, Do Not Disturb, the lock screen image for desktop
  apps, or a vertical Windows 11 taskbar — AETHER provides independent alternatives or links to Windows Settings instead.
- The taskbar transparency mod uses `SetWindowCompositionAttribute` (undocumented, no injection) and is labelled as such.
