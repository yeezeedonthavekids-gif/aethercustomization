using System.Text.Json.Nodes;
using Aether.Core.Abstractions;
using Aether.ModEngine.Manifest;

namespace Aether.Services.Tweaks;

/// <summary>The documented Windows settings AETHER exposes. Each entry is data — no special-case code.</summary>
public static class TweakCatalog
{
    private const string Adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string Cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    private static RegistrySetOperation Dw(string path, string name, int value, string hive = "HKCU") =>
        new() { Hive = hive, Path = path, Name = name, Kind = RegistryDataKind.DWord, Value = JsonValue.Create(value) };

    private static RegistrySetOperation Sz(string path, string name, string value) =>
        new() { Hive = "HKCU", Path = path, Name = name, Kind = RegistryDataKind.String, Value = JsonValue.Create(value) };

    private static BroadcastOperation Bc(string area) => new() { Area = area };

    /// <summary>On/off tweak backed by one DWORD (plus an optional broadcast).</summary>
    private static TweakDefinition Toggle(string id, TweakArea area, string title, string path, string name, int on, int off, string what, string why,
        string? broadcast = "TraySettings", int def = -1, string sideEffects = "None known.", RestartRequirement restart = RestartRequirement.None,
        int? minBuild = null, int? maxBuild = null, string? group = null, params string[] keywords)
    {
        ModOperation[] Ops(int v) => broadcast is null ? [Dw(path, name, v)] : [Dw(path, name, v), Bc(broadcast)];
        return new TweakDefinition
        {
            Id = id, Area = area, Title = title, WhatItChanges = what, Why = why, SideEffects = sideEffects, Restart = restart,
            MinBuild = minBuild, MaxBuild = maxBuild, Group = group, Keywords = keywords,
            Options = [new("on", "On", Ops(on)), new("off", "Off", Ops(off))],
            Probe = Dw(path, name, on),
            DefaultOption = def == on ? "on" : def == off ? "off" : null,
        };
    }

    private static TweakDefinition Choice(string id, TweakArea area, string title, string path, string name, (int Value, string Label)[] options, string what, string why,
        string? broadcast = "TraySettings", int? def = null, string sideEffects = "None known.", RestartRequirement restart = RestartRequirement.None,
        int? minBuild = null, int? maxBuild = null, string? group = null, params string[] keywords) => new()
    {
        Id = id, Area = area, Title = title, WhatItChanges = what, Why = why, SideEffects = sideEffects, Restart = restart,
        MinBuild = minBuild, MaxBuild = maxBuild, Group = group, Keywords = keywords,
        Options = options.Select(o => new TweakOption(o.Value.ToString(), o.Label,
            broadcast is null ? [Dw(path, name, o.Value)] : [Dw(path, name, o.Value), Bc(broadcast)])).ToList(),
        Probe = Dw(path, name, options[0].Value),
        DefaultOption = def?.ToString(),
    };

    public static IReadOnlyList<TweakDefinition> All { get; } = Build();

    private static List<TweakDefinition> Build() =>
    [
        // ───────── Taskbar ─────────
        Choice("taskbar.alignment", TweakArea.Taskbar, "Taskbar alignment", Adv, "TaskbarAl", [(1, "Center"), (0, "Left")],
            "HKCU\\…\\Explorer\\Advanced\\TaskbarAl", "Left alignment puts the Start button back in the corner, like earlier Windows versions.",
            def: 1, minBuild: 22000, group: "Layout", keywords: ["center", "left", "start button"]),
        Choice("taskbar.search", TweakArea.Taskbar, "Search on the taskbar", @"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode",
            [(0, "Hidden"), (1, "Icon only"), (2, "Search box")], "HKCU\\…\\Search\\SearchboxTaskbarMode",
            "Hiding search frees taskbar space; Windows+S still opens search.", def: 2, group: "Layout", keywords: ["search box", "cortana"]),
        Toggle("taskbar.taskview", TweakArea.Taskbar, "Task View button", Adv, "ShowTaskViewButton", 1, 0,
            "HKCU\\…\\Explorer\\Advanced\\ShowTaskViewButton", "Hide it if you use Windows+Tab or don't use virtual desktops.", def: 1, group: "Layout"),
        Choice("taskbar.combine", TweakArea.Taskbar, "Combine taskbar buttons", Adv, "TaskbarGlomLevel", [(0, "Always, hide labels"), (1, "When taskbar is full"), (2, "Never")],
            "HKCU\\…\\Explorer\\Advanced\\TaskbarGlomLevel", "Showing labels makes it easier to tell windows of the same app apart.",
            def: 0, sideEffects: "Labels take more space.", restart: RestartRequirement.Explorer, group: "Behavior", keywords: ["group", "labels"]),
        Toggle("taskbar.smallicons", TweakArea.Taskbar, "Small taskbar buttons", Adv, "TaskbarSmallIcons", 1, 0,
            "HKCU\\…\\Explorer\\Advanced\\TaskbarSmallIcons", "A thinner taskbar leaves more room for apps.", def: 0, maxBuild: 21999, group: "Layout", keywords: ["height", "size"]),
        Toggle("taskbar.badges", TweakArea.Taskbar, "Badges on taskbar apps", Adv, "TaskbarBadges", 1, 0,
            "HKCU\\…\\Explorer\\Advanced\\TaskbarBadges", "Turn off unread-count badges for a calmer taskbar.", def: 1, group: "Behavior"),
        Toggle("taskbar.flashing", TweakArea.Taskbar, "Flashing taskbar buttons", Adv, "TaskbarFlashing", 1, 0,
            "HKCU\\…\\Explorer\\Advanced\\TaskbarFlashing", "Stop apps from flashing to get your attention.", def: 1, minBuild: 22621, group: "Behavior"),
        Toggle("taskbar.endtask", TweakArea.Taskbar, "'End task' in the taskbar right-click menu", Adv + "\\TaskbarDeveloperSettings", "TaskbarEndTask", 1, 0,
            "HKCU\\…\\Advanced\\TaskbarDeveloperSettings\\TaskbarEndTask", "Force-close a frozen app without opening Task Manager.",
            def: 0, minBuild: 22631, group: "Behavior", keywords: ["kill", "force close"]),
        Toggle("taskbar.showdesktop", TweakArea.Taskbar, "Show desktop when clicking the far corner", Adv, "TaskbarSd", 1, 0,
            "HKCU\\…\\Explorer\\Advanced\\TaskbarSd", "Disable it if you click the corner by accident.", def: 1, minBuild: 22621, group: "Behavior"),
        Toggle("taskbar.lastactive", TweakArea.Taskbar, "Clicking a grouped app opens its last active window", Adv, "LastActiveClick", 1, 0,
            "HKCU\\…\\Explorer\\Advanced\\LastActiveClick", "Switch back to the window you used last with one click instead of picking from thumbnails.",
            def: 0, restart: RestartRequirement.Explorer, group: "Behavior"),
        Toggle("taskbar.multimonitor", TweakArea.Taskbar, "Show taskbar on all displays", Adv, "MMTaskbarEnabled", 1, 0,
            "HKCU\\…\\Explorer\\Advanced\\MMTaskbarEnabled", "Keep secondary monitors clean, or give each one its own taskbar.", def: 1, group: "Multiple displays"),
        Choice("taskbar.multimode", TweakArea.Taskbar, "On other displays, show taskbar apps on", Adv, "MMTaskbarMode",
            [(0, "All taskbars"), (1, "Main taskbar and where the window is open"), (2, "Taskbar where the window is open")],
            "HKCU\\…\\Explorer\\Advanced\\MMTaskbarMode", "Keep each monitor's taskbar relevant to the windows on it.", def: 0, group: "Multiple displays"),

        // ───────── Start menu (native) ─────────
        Choice("start.layout", TweakArea.StartMenu, "Start layout", Adv, "Start_Layout", [(0, "Default"), (1, "More pins"), (2, "More recommendations")],
            "HKCU\\…\\Explorer\\Advanced\\Start_Layout", "Fit more pinned apps, or more recent files, in the native Start menu.", def: 0, minBuild: 22621, keywords: ["pins"]),
        Toggle("start.recentdocs", TweakArea.StartMenu, "Show recently opened items in Start, Jump Lists and File Explorer", Adv, "Start_TrackDocs", 1, 0,
            "HKCU\\…\\Explorer\\Advanced\\Start_TrackDocs", "Turn off for privacy on shared screens.", def: 1, sideEffects: "Jump Lists stop showing recent files.", keywords: ["recent", "jump list"]),
        Toggle("start.recentapps", TweakArea.StartMenu, "Show most used apps", Adv, "Start_TrackProgs", 1, 0,
            "HKCU\\…\\Explorer\\Advanced\\Start_TrackProgs", "Hide app usage tracking in Start.", def: 1, keywords: ["most used"]),
        Toggle("start.tips", TweakArea.StartMenu, "Show recommendations for tips, shortcuts and new apps", Adv, "Start_IrisRecommendations", 1, 0,
            "HKCU\\…\\Explorer\\Advanced\\Start_IrisRecommendations", "Removes promotional recommendations from Start.", def: 1, minBuild: 22621, keywords: ["ads", "suggestions"]),

        // ───────── File Explorer ─────────
        Choice("explorer.launchto", TweakArea.FileExplorer, "Open File Explorer to", Adv, "LaunchTo", [(1, "This PC"), (2, "Home / Quick access"), (3, "Downloads")],
            "HKCU\\…\\Explorer\\Advanced\\LaunchTo", "Start where you actually work.", broadcast: null, def: 2, group: "Navigation", keywords: ["default folder", "this pc"]),
        Toggle("explorer.compact", TweakArea.FileExplorer, "Compact view", Adv, "UseCompactMode", 1, 0,
            "HKCU\\…\\Explorer\\Advanced\\UseCompactMode", "Tighter spacing shows more files at once.", broadcast: "ShellState", def: 0, group: "Appearance", keywords: ["density", "spacing"]),
        Toggle("explorer.expandnav", TweakArea.FileExplorer, "Expand navigation pane to the open folder", Adv, "NavPaneExpandToCurrentFolder", 1, 0,
            "HKCU\\…\\Explorer\\Advanced\\NavPaneExpandToCurrentFolder", "Always see where you are in the folder tree.", broadcast: "ShellState", def: 0, group: "Navigation pane"),
        Toggle("explorer.allfolders", TweakArea.FileExplorer, "Show all folders in the navigation pane", Adv, "NavPaneShowAllFolders", 1, 0,
            "HKCU\\…\\Explorer\\Advanced\\NavPaneShowAllFolders", "Shows the desktop hierarchy, Control Panel and Recycle Bin in the tree.", broadcast: "ShellState", def: 0, group: "Navigation pane"),
        Toggle("explorer.checkboxes", TweakArea.FileExplorer, "Item check boxes", Adv, "AutoCheckSelect", 1, 0,
            "HKCU\\…\\Explorer\\Advanced\\AutoCheckSelect", "Select multiple files without holding Ctrl — great for touch and pens.", broadcast: "ShellState", def: 0, group: "Appearance"),
        Toggle("explorer.fullpath", TweakArea.FileExplorer, "Full path in the title bar", @"Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState", "FullPath", 1, 0,
            "HKCU\\…\\Explorer\\CabinetState\\FullPath", "Always know exactly which folder a window shows.", broadcast: "ShellState", def: 0, group: "Appearance"),
        Toggle("explorer.recentquick", TweakArea.FileExplorer, "Show recent files in Home / Quick access", @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowRecent", 1, 0,
            "HKCU\\…\\Explorer\\ShowRecent", "Hide recent files for privacy.", broadcast: null, def: 1, group: "Quick access"),
        Toggle("explorer.frequentquick", TweakArea.FileExplorer, "Show frequent folders in Home / Quick access", @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowFrequent", 1, 0,
            "HKCU\\…\\Explorer\\ShowFrequent", "Keep Quick access to the folders you pin yourself.", broadcast: null, def: 1, group: "Quick access"),
        Toggle("explorer.syncads", TweakArea.FileExplorer, "Show sync provider notifications", Adv, "ShowSyncProviderNotifications", 1, 0,
            "HKCU\\…\\Explorer\\Advanced\\ShowSyncProviderNotifications", "Turn off cloud-storage promotions inside File Explorer.", broadcast: null, def: 1, group: "Quick access", keywords: ["onedrive", "ads"]),
        Toggle("explorer.separate", TweakArea.FileExplorer, "Open folder windows in a separate process", Adv, "SeparateProcess", 1, 0,
            "HKCU\\…\\Explorer\\Advanced\\SeparateProcess", "A crashing folder window won't take down the taskbar.", broadcast: null, def: 0,
            sideEffects: "Uses a little more memory.", restart: RestartRequirement.SignOut, group: "Reliability"),

        // ───────── Performance ─────────
        Toggle("perf.startupdelay", TweakArea.Performance, "Delay startup apps after sign-in", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 1, 0,
            "HKCU\\…\\Explorer\\Serialize\\StartupDelayInMSec (0 removes Windows' artificial delay)", "Turning the delay off starts your startup apps sooner after sign-in.",
            broadcast: null, def: 1, sideEffects: "More programs compete for the disk right after sign-in, so the desktop may feel busy briefly.", restart: RestartRequirement.SignOut, keywords: ["boot", "sign in"]),
        Choice("perf.menudelay", TweakArea.Performance, "Menu show delay", @"Control Panel\Desktop", "MenuShowDelay", [(400, "400 ms (Windows default)"), (200, "200 ms"), (100, "100 ms"), (0, "Instant")],
            "HKCU\\Control Panel\\Desktop\\MenuShowDelay", "Cascading menus open faster. This changes perceived responsiveness only.", broadcast: null, def: 400,
            restart: RestartRequirement.SignOut, keywords: ["menus", "snappy"]) with { Options = MenuDelayOptions(), Probe = Sz(@"Control Panel\Desktop", "MenuShowDelay", "400") },
        Toggle("perf.backgroundapps", TweakArea.Performance, "Let apps run in the background", @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 0, 1,
            "HKCU\\…\\BackgroundAccessApplications\\GlobalUserDisabled", "Store apps stop running background tasks, which can save battery.", broadcast: null, def: 0,
            sideEffects: "Some apps stop delivering notifications or updating live content.", maxBuild: 21999),

        // ───────── Privacy ─────────
        Toggle("privacy.adid", TweakArea.Privacy, "Advertising ID", @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 1, 0,
            "HKCU\\…\\AdvertisingInfo\\Enabled", "Apps can't use your advertising ID to personalize ads.", broadcast: null, def: 1, keywords: ["ads", "tracking"]),
        Toggle("privacy.tailored", TweakArea.Privacy, "Tailored experiences", @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 1, 0,
            "HKCU\\…\\Privacy\\TailoredExperiencesWithDiagnosticDataEnabled", "Microsoft won't use diagnostic data to personalize tips and offers.", broadcast: null, def: 1),
        Toggle("privacy.settingsads", TweakArea.Privacy, "Suggested content in Settings", Cdm, "SubscribedContent-338393Enabled", 1, 0,
            "HKCU\\…\\ContentDeliveryManager\\SubscribedContent-338393Enabled", "Removes promotional suggestions from the Settings app.", broadcast: null, def: 1, keywords: ["ads"]),
        Toggle("privacy.lockscreentips", TweakArea.Privacy, "Fun facts and tips on the lock screen", Cdm, "RotatingLockScreenOverlayEnabled", 1, 0,
            "HKCU\\…\\ContentDeliveryManager\\RotatingLockScreenOverlayEnabled", "A cleaner lock screen without overlays.", broadcast: null, def: 1, keywords: ["lock screen"]),
        Toggle("privacy.websearch", TweakArea.Privacy, "Web results in Windows search", @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 0, 1,
            "HKCU\\Software\\Policies\\Microsoft\\Windows\\Explorer\\DisableSearchBoxSuggestions", "Search only your PC — faster and more private.", broadcast: null, def: 0,
            sideEffects: "Bing results and web suggestions disappear from Start search.", restart: RestartRequirement.Explorer, keywords: ["bing"]),

        // ───────── Notifications ─────────
        Toggle("notif.toasts", TweakArea.Notifications, "Notifications", @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", 1, 0,
            "HKCU\\…\\PushNotifications\\ToastEnabled", "A master switch for notification banners from apps.", broadcast: null, def: 1, sideEffects: "You may miss messages and reminders."),
        Toggle("notif.lock", TweakArea.Notifications, "Show notifications on the lock screen", @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings", "NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK", 1, 0,
            "HKCU\\…\\Notifications\\Settings\\NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK", "Keep message previews private when you step away.", broadcast: null, def: 1),
        Toggle("notif.sounds", TweakArea.Notifications, "Notification sounds", @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings", "NOC_GLOBAL_SETTING_ALLOW_NOTIFICATION_SOUND", 1, 0,
            "HKCU\\…\\Notifications\\Settings\\NOC_GLOBAL_SETTING_ALLOW_NOTIFICATION_SOUND", "Silent banners — handy in meetings.", broadcast: null, def: 1),
        Toggle("notif.welcome", TweakArea.Notifications, "Windows welcome experience after updates", Cdm, "SubscribedContent-310093Enabled", 1, 0,
            "HKCU\\…\\ContentDeliveryManager\\SubscribedContent-310093Enabled", "Skip the 'what's new' screens after feature updates.", broadcast: null, def: 1),
        Toggle("notif.tips", TweakArea.Notifications, "Tips and suggestions when using Windows", Cdm, "SubscribedContent-338389Enabled", 1, 0,
            "HKCU\\…\\ContentDeliveryManager\\SubscribedContent-338389Enabled", "Fewer nudges and promotional notifications.", broadcast: null, def: 1),

        // ───────── Accessibility ─────────
        Choice("a11y.textscale", TweakArea.Accessibility, "Text size", @"Software\Microsoft\Accessibility", "TextScaleFactor", [(100, "100%"), (115, "115%"), (125, "125%"), (150, "150%"), (175, "175%"), (200, "200%")],
            "HKCU\\Software\\Microsoft\\Accessibility\\TextScaleFactor", "Make text larger without changing display scaling.", broadcast: null, def: 100, restart: RestartRequirement.SignOut, keywords: ["font size", "bigger text"]),
        Choice("a11y.stickykeys", TweakArea.Accessibility, "Sticky Keys shortcut (press Shift five times)", @"Control Panel\Accessibility\StickyKeys", "Flags", [(510, "Enabled"), (506, "Disabled")],
            "HKCU\\Control Panel\\Accessibility\\StickyKeys\\Flags", "Stop the Sticky Keys prompt from interrupting games.", broadcast: null, def: 510, restart: RestartRequirement.SignOut, keywords: ["shift", "games"])
            with { Options = [new("510", "Enabled", [Sz(@"Control Panel\Accessibility\StickyKeys", "Flags", "510")]), new("506", "Disabled", [Sz(@"Control Panel\Accessibility\StickyKeys", "Flags", "506")])],
                   Probe = Sz(@"Control Panel\Accessibility\StickyKeys", "Flags", "510") },

        // ───────── Windows behavior ─────────
        Toggle("behavior.snapflyout", TweakArea.WindowsBehavior, "Snap layouts when hovering the maximize button", Adv, "EnableSnapAssistFlyout", 1, 0,
            "HKCU\\…\\Explorer\\Advanced\\EnableSnapAssistFlyout", "Turn off if the flyout appears when you don't want it.", def: 1, minBuild: 22000, keywords: ["snap"]),
        Toggle("behavior.snapassist", TweakArea.WindowsBehavior, "Suggest what to snap next to a window", Adv, "SnapAssist", 1, 0,
            "HKCU\\…\\Explorer\\Advanced\\SnapAssist", "Disable if you prefer arranging windows yourself.", def: 1, keywords: ["snap"]),
        Toggle("behavior.shake", TweakArea.WindowsBehavior, "Title bar window shake (minimize others)", Adv, "DisallowShaking", 0, 1,
            "HKCU\\…\\Explorer\\Advanced\\DisallowShaking", "Prevents accidentally minimizing every window by shaking one.", def: 1, minBuild: 22000, keywords: ["aero shake"]),
        Choice("behavior.numlock", TweakArea.WindowsBehavior, "Num Lock at sign-in", @"Control Panel\Keyboard", "InitialKeyboardIndicators", [(2, "On"), (0, "Off")],
            "HKCU\\Control Panel\\Keyboard\\InitialKeyboardIndicators", "Type your PIN on the number pad straight away.", broadcast: null, def: 0, restart: RestartRequirement.SignOut)
            with { Options = [new("2", "On", [Sz(@"Control Panel\Keyboard", "InitialKeyboardIndicators", "2")]), new("0", "Off", [Sz(@"Control Panel\Keyboard", "InitialKeyboardIndicators", "0")])],
                   Probe = Sz(@"Control Panel\Keyboard", "InitialKeyboardIndicators", "2") },

        // ───────── Gaming ─────────
        Toggle("gaming.gamemode", TweakArea.Gaming, "Game Mode", @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1, 0,
            "HKCU\\Software\\Microsoft\\GameBar\\AutoGameModeEnabled", "Windows prioritizes the game and pauses Windows Update driver installs and restart notifications while you play.",
            broadcast: null, def: 1, sideEffects: "Effects vary by game and PC; there is no guaranteed frame-rate change."),
        Toggle("gaming.dvr", TweakArea.Gaming, "Background game recording (Game DVR)", @"System\GameConfigStore", "GameDVR_Enabled", 1, 0,
            "HKCU\\System\\GameConfigStore\\GameDVR_Enabled", "Stop Windows from continuously recording gameplay in the background.", broadcast: null, def: 1,
            sideEffects: "'Record what happened' in Game Bar stops working."),
        Toggle("gaming.nexus", TweakArea.Gaming, "Open Game Bar with the controller Xbox button", @"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 1, 0,
            "HKCU\\Software\\Microsoft\\GameBar\\UseNexusForGameBarEnabled", "Prevent the Xbox button from popping up the Game Bar.", broadcast: null, def: 1),

        // ───────── Storage ─────────
        Toggle("storage.sense", TweakArea.Storage, "Storage Sense", @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01", 1, 0,
            "HKCU\\…\\StorageSense\\Parameters\\StoragePolicy\\01", "Windows automatically frees space by cleaning temporary files and the Recycle Bin.", broadcast: null, def: 0,
            sideEffects: "Files in the Recycle Bin and Downloads may be removed according to the Storage Sense schedule in Settings.", keywords: ["cleanup", "disk space"]),
    ];

    private static List<TweakOption> MenuDelayOptions() =>
        new[] { ("400", "400 ms (Windows default)"), ("200", "200 ms"), ("100", "100 ms"), ("0", "Instant") }
            .Select(o => new TweakOption(o.Item1, o.Item2, [Sz(@"Control Panel\Desktop", "MenuShowDelay", o.Item1)])).ToList();
}
