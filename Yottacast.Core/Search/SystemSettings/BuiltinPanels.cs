namespace Yottacast.Core.Search.SystemSettings;

internal static class BuiltinPanels {
    // Anchors verificados en macOS Ventura 13 / Sonoma 14.
    // Al actualizar macOS, ejecutar tools/verify-settings-anchors.sh para re-verificar.
    // Si un anchor falla, open abre el panel padre sin navegar: degradación silenciosa.
    public static readonly IReadOnlyList<SystemSettingsPanel> All = [

        // ── Paneles de primer nivel ───────────────────────────────────────────
        new("Wi-Fi",               "com.apple.preference.network"),
        new("Bluetooth",           "com.apple.preferences.Bluetooth"),
        new("Network",             "com.apple.preference.network"),
        new("VPN",                 "com.apple.preference.network"),
        new("Notifications",       "com.apple.preference.notifications"),
        new("Focus",               "com.apple.preference.notifications"),
        new("Sound",               "com.apple.preference.sound"),
        new("Screen Time",         "com.apple.preference.screentime"),
        new("General",             "com.apple.preference.general"),
        new("Appearance",          "com.apple.preference.general"),
        new("Accessibility",       "com.apple.preference.universalaccess"),
        new("Control Center",      "com.apple.preference.controllcenter"),
        new("Siri & Spotlight",    "com.apple.preference.speech"),
        new("Privacy & Security",  "com.apple.preference.security"),
        new("Privacy",             "com.apple.preference.security"),
        new("Security",            "com.apple.preference.security"),
        new("Desktop & Dock",      "com.apple.preference.exposeclassic"),
        new("Stage Manager",       "com.apple.preference.exposeclassic"),
        new("Mission Control",     "com.apple.preference.exposeclassic"),
        new("Displays",            "com.apple.preference.displays"),
        new("Wallpaper",           "com.apple.preference.desktopscreeneffect"),
        new("Screen Saver",        "com.apple.preference.desktopscreeneffect"),
        new("Battery",             "com.apple.preference.battery"),
        new("Energy Saver",        "com.apple.preference.battery"),
        new("Lock Screen",         "com.apple.preference.security"),
        new("Touch ID & Password", "com.apple.systempreferences.LocalAuthenticationPrefPane"),
        new("Users & Groups",      "com.apple.preferences.users"),
        new("Passwords",           "com.apple.Passwords"),
        new("Apple ID",            "com.apple.systempreferences.AppleIDPrefPane"),
        new("Family Sharing",      "com.apple.systempreferences.FamilySharingPrefPane"),
        new("Internet Accounts",   "com.apple.preference.internetaccounts"),
        new("Game Center",         "com.apple.systempreferences.GameCenterPrefPane"),
        new("Wallet & Apple Pay",  "com.apple.systempreferences.WalletPrefPane"),
        new("Keyboard",            "com.apple.preference.keyboard"),
        new("Trackpad",            "com.apple.preference.trackpad"),
        new("Mouse",               "com.apple.preference.mouse"),
        new("Printers & Scanners", "com.apple.preference.printfax"),
        new("Date & Time",         "com.apple.preference.datetime"),
        new("Language & Region",   "com.apple.Localization"),
        new("Storage",             "com.apple.preference.storage"),
        new("Sharing",             "com.apple.preferences.sharing"),
        new("Time Machine",        "com.apple.prefs.backup"),
        new("Software Update",     "com.apple.preferences.softwareupdate"),
        new("Startup Disk",        "com.apple.preference.startupdisk"),
        new("Extensions",          "com.apple.preference.extensions"),

        // ── Privacy & Security ───────────────────────────────────────────────
        new("Camera",                 "com.apple.preference.security?Privacy_Camera",            ParentName: "Privacy & Security"),
        new("Microphone",             "com.apple.preference.security?Privacy_Microphone",        ParentName: "Privacy & Security"),
        new("Location Services",      "com.apple.preference.security?Privacy_LocationServices",  ParentName: "Privacy & Security"),
        new("Contacts Permissions",   "com.apple.preference.security?Privacy_ContactsFull",      ParentName: "Privacy & Security"),
        new("Calendars Permissions",  "com.apple.preference.security?Privacy_Calendars",         ParentName: "Privacy & Security"),
        new("Reminders Permissions",  "com.apple.preference.security?Privacy_Reminders",         ParentName: "Privacy & Security"),
        new("Photos Permissions",     "com.apple.preference.security?Privacy_Photos",            ParentName: "Privacy & Security"),
        new("Bluetooth Permissions",  "com.apple.preference.security?Privacy_Bluetooth",         ParentName: "Privacy & Security"),
        new("Screen Recording",       "com.apple.preference.security?Privacy_ScreenCapture",     ParentName: "Privacy & Security"),
        new("Accessibility Apps",     "com.apple.preference.security?Privacy_Accessibility",     ParentName: "Privacy & Security"),
        new("Full Disk Access",       "com.apple.preference.security?Privacy_AllFiles",          ParentName: "Privacy & Security"),
        new("Files and Folders",      "com.apple.preference.security?Privacy_FilesAndFolders",   ParentName: "Privacy & Security"),
        new("Home Permissions",       "com.apple.preference.security?Privacy_HomeKit",           ParentName: "Privacy & Security"),
        new("Media & Apple Music",    "com.apple.preference.security?Privacy_MediaLibrary",      ParentName: "Privacy & Security"),
        new("Motion & Fitness",       "com.apple.preference.security?Privacy_Motion",            ParentName: "Privacy & Security"),
        new("Speech Recognition",     "com.apple.preference.security?Privacy_SpeechRecognition", ParentName: "Privacy & Security"),
        new("Automation Permissions", "com.apple.preference.security?Privacy_Automation",        ParentName: "Privacy & Security"),
        new("Developer Tools",        "com.apple.preference.security?Privacy_DevTools",          ParentName: "Privacy & Security"),
        new("Analytics & Improvements","com.apple.preference.security?Privacy_Analytics",        ParentName: "Privacy & Security"),
        new("Apple Advertising",      "com.apple.preference.security?Privacy_Advertising",       ParentName: "Privacy & Security"),
        new("FileVault",              "com.apple.preference.security?FDE",                       ParentName: "Privacy & Security"),
        new("Firewall",               "com.apple.preference.security?Firewall",                  ParentName: "Privacy & Security"),
        new("Advanced Security",      "com.apple.preference.security?Advanced",                  ParentName: "Privacy & Security"),

        // ── Keyboard ─────────────────────────────────────────────────────────
        new("Keyboard Shortcuts",  "com.apple.preference.keyboard?Shortcuts",    ParentName: "Keyboard"),
        new("Text Replacements",   "com.apple.preference.keyboard?Text",         ParentName: "Keyboard"),
        new("Dictation",           "com.apple.preference.keyboard?Dictation",    ParentName: "Keyboard"),
        new("Input Sources",       "com.apple.preference.keyboard?InputSources", ParentName: "Keyboard"),

        // ── Displays ──────────────────────────────────────────────────────────
        new("Night Shift",         "com.apple.preference.displays?nightShift",   ParentName: "Displays"),
        new("Display Resolution",  "com.apple.preference.displays?scaled",       ParentName: "Displays"),
        new("Color Profile",       "com.apple.preference.displays?ColorProfile", ParentName: "Displays"),

        // ── Desktop & Dock ────────────────────────────────────────────────────
        new("Hot Corners",         "com.apple.preference.exposeclassic?hotcorners", ParentName: "Desktop & Dock"),
        new("Dock Settings",       "com.apple.preference.exposeclassic?dock",       ParentName: "Desktop & Dock"),

        // ── Sound ─────────────────────────────────────────────────────────────
        new("Sound Output",        "com.apple.preference.sound?output", ParentName: "Sound"),
        new("Sound Input",         "com.apple.preference.sound?input",  ParentName: "Sound"),

        // ── General ───────────────────────────────────────────────────────────
        new("Login Items",         "com.apple.preference.general?LoginItems", ParentName: "General"),
        new("AirDrop & Handoff",   "com.apple.preference.general?AirDrop",   ParentName: "General"),

        // ── Language & Region ─────────────────────────────────────────────────
        new("Language",            "com.apple.Localization?language", ParentName: "Language & Region"),
        new("Region",              "com.apple.Localization?region",   ParentName: "Language & Region"),
        new("Calendar Format",     "com.apple.Localization?calendar", ParentName: "Language & Region"),

        // ── Date & Time ───────────────────────────────────────────────────────
        new("Time Zone",           "com.apple.preference.datetime?TimeZone", ParentName: "Date & Time"),

        // ── Sharing ───────────────────────────────────────────────────────────
        new("Screen Sharing",      "com.apple.preferences.sharing?Services_ScreenSharing",  ParentName: "Sharing"),
        new("File Sharing",        "com.apple.preferences.sharing?Services_ARDService",     ParentName: "Sharing"),
        new("Printer Sharing",     "com.apple.preferences.sharing?Services_PrinterSharing", ParentName: "Sharing"),
        new("Remote Login",        "com.apple.preferences.sharing?Services_RemoteLogin",    ParentName: "Sharing"),
        new("Remote Management",   "com.apple.preferences.sharing?Services_ARD",            ParentName: "Sharing"),
        new("Internet Sharing",    "com.apple.preferences.sharing?Services_InternetSharing",ParentName: "Sharing"),
        new("Content Caching",     "com.apple.preferences.sharing?Services_NetworkCache",   ParentName: "Sharing"),

        // ── Accessibility ─────────────────────────────────────────────────────
        new("VoiceOver",           "com.apple.preference.universalaccess?VoiceOver",       ParentName: "Accessibility"),
        new("Zoom",                "com.apple.preference.universalaccess?Seeing_Zoom",     ParentName: "Accessibility"),
        new("Display Accessibility","com.apple.preference.universalaccess?Seeing_Display", ParentName: "Accessibility"),
        new("Spoken Content",      "com.apple.preference.universalaccess?Seeing_Content",  ParentName: "Accessibility"),
        new("Audio Descriptions",  "com.apple.preference.universalaccess?Seeing_Audio",    ParentName: "Accessibility"),
        new("Audio Accessibility", "com.apple.preference.universalaccess?Hearing_Audio",   ParentName: "Accessibility"),
        new("RTT",                 "com.apple.preference.universalaccess?Hearing_RTT",     ParentName: "Accessibility"),
        new("Keyboard Accessibility","com.apple.preference.universalaccess?Keyboard",      ParentName: "Accessibility"),
        new("Pointer Control",     "com.apple.preference.universalaccess?Mouse",           ParentName: "Accessibility"),
        new("Switch Control",      "com.apple.preference.universalaccess?Switch",          ParentName: "Accessibility"),

        // ── Battery ───────────────────────────────────────────────────────────
        new("Battery Options",     "com.apple.preference.battery?options",     ParentName: "Battery"),
        new("Battery Usage History","com.apple.preference.battery?UsageHistory",ParentName: "Battery"),
    ];
}
