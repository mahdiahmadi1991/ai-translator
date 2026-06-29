namespace AiTranslator.App.Resources;

/// <summary>
/// Single seam for user-facing strings (English source). Keeps hard-coded text out of XAML/code so it
/// can move to resx/satellite-assembly localization later without touching call sites.
/// </summary>
public static class UiStrings
{
    public const string AppName = "AI Translator";

    // Tray
    public const string TraySettings = "Settings…";
    public const string TrayExit = "Exit";
    public const string TrayTooltip = "AI Translator — press the hotkey to translate";

    // Overlay
    public const string OverlayHint = "Type your text, then Translate (Ctrl+Enter) — it replaces the field";
    public const string OverlayTranslate = "Translate";
    public const string OverlayTranslating = "Translating…";
    public const string OverlayNoApiKey = "Set your OpenAI key in Settings first (tray → Settings).";
    public const string OverlayError = "Translation failed:";

    // Badge
    public const string BadgeTooltip = "Translate — click to type a translation into this field";
    public const string BadgeIgnoreFormat = "Don't show the badge in {0}";
    public const string BadgeIgnoreGeneric = "Don't show the badge in this app";
    public const string BadgeSettings = "Settings…";
    public const string BadgeQuit = "Quit AI Translator";

    // Overlay header
    public const string OverlayClose = "Close";

    // Settings
    public const string SettingsTitle = "AI Translator — Settings";
    public const string SettingsApiKey = "OpenAI API key";
    public const string SettingsPrimaryLang = "Primary language (BCP-47, e.g. fa)";
    public const string SettingsSecondaryLang = "Secondary language (BCP-47, e.g. en)";
    public const string SettingsModel = "Model";
    public const string SettingsHotkey = "Hotkey (e.g. Ctrl+Alt+T)";
    public const string SettingsAutoDirection = "Auto-detect direction";
    public const string SettingsAutoAppearBadge = "Show the badge automatically in allowlisted apps";
    public const string SettingsRunAtStartup = "Run at Windows startup";
    public const string SettingsAllowlist = "Allowlist — apps where the badge appears (one exe per line)";
    public const string SettingsBlocklist = "Blocklist — apps to always suppress (one exe per line)";
    public const string SettingsSave = "Save";
    public const string SettingsHotkeyTaken = "That hotkey is in use — pick another.";
    public const string SettingsHotkeyInvalid = "Not a valid hotkey (e.g. Ctrl+Alt+T).";
    public const string SettingsSaved = "Saved.";
    public const string SettingsSaveError = "Couldn't fully save:";
}
