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
    public const string OverlayHint = "Type here — translation replaces the focused field";

    // Badge
    public const string BadgeTooltip = "Translate — click to type a translation into this field";

    // Settings
    public const string SettingsTitle = "AI Translator — Settings";
    public const string SettingsApiKey = "OpenAI API key";
    public const string SettingsPrimaryLang = "Primary language (BCP-47, e.g. fa)";
    public const string SettingsSecondaryLang = "Secondary language (BCP-47, e.g. en)";
    public const string SettingsModel = "Model";
    public const string SettingsHotkey = "Hotkey (e.g. Ctrl+Alt+T)";
    public const string SettingsAutoDirection = "Auto-detect direction";
    public const string SettingsRunAtStartup = "Run at Windows startup";
    public const string SettingsSave = "Save";
    public const string SettingsHotkeyTaken = "That hotkey is in use — pick another.";
    public const string SettingsSaved = "Saved.";
}
