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

    // Settings — window + tabs
    public const string SettingsTitle = "AI Translator — Settings";
    public const string SettingsHeading = "Settings";
    public const string SettingsTabGeneral = "General";
    public const string SettingsTabAccount = "Account";
    public const string SettingsTabBlockList = "Block List";

    // Settings — General tab
    public const string SettingsLanguages = "Languages";
    public const string SettingsLanguagesHint = "Translate between these two. With auto-detect on, direction follows what you type.";
    public const string SettingsPrimaryLang = "Primary";
    public const string SettingsSecondaryLang = "Secondary";
    public const string SettingsAutoDirection = "Auto-detect direction";
    public const string SettingsAutoDirectionHint = "Pick the source language automatically from the script you type.";
    public const string SettingsBehavior = "Behavior";
    public const string SettingsModel = "OpenAI model";
    public const string SettingsHotkey = "Global hotkey";
    public const string SettingsHotkeyHint = "Opens the translation box anywhere, even if the badge is hidden.";
    public const string SettingsAutoAppearBadge = "Show the badge automatically";
    public const string SettingsAutoAppearBadgeHint = "Display the badge beside any editable text field, except blocked apps.";
    public const string SettingsRunAtStartup = "Run at Windows startup";
    public const string SettingsRunAtStartupHint = "Start AI Translator automatically when you sign in.";

    // Settings — Account tab
    public const string SettingsApiKey = "OpenAI API key";
    public const string SettingsApiKeyHint = "Stored securely in Windows Credential Manager — never written to any file in this project.";
    public const string SettingsApiKeyShow = "Show";
    public const string SettingsApiKeyPlaceholder = "sk-…";

    // Settings — Block List tab
    public const string SettingsBlockListHint = "The badge appears in every editable field. Add an app here to suppress it (matched against the process name, e.g. \"chrome\" or \"^Code$\").";
    public const string SettingsBlockListAddPlaceholder = "App name or pattern (e.g. notepad)";
    public const string SettingsBlockListAdd = "Add";
    public const string SettingsBlockListRemove = "Remove";
    public const string SettingsBlockListEmpty = "No blocked apps — the badge can appear everywhere.";

    // Settings — footer
    public const string SettingsSave = "Save changes";
    public const string SettingsHotkeyTaken = "That hotkey is in use — pick another.";
    public const string SettingsHotkeyInvalid = "Not a valid hotkey (e.g. Ctrl+Alt+T).";
    public const string SettingsSaved = "Saved.";
    public const string SettingsSaveError = "Couldn't fully save:";
}
