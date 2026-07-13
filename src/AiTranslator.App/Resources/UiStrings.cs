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
    public const string TrayTooltip = "AI Translator. Press your hotkey to translate.";
    public const string UpdateReady = "An update is ready. It will install when you quit AI Translator.";

    // Overlay
    public const string OverlayHint = "Type your text, then press Translate (or Ctrl+Enter).";
    public const string OverlayPlaceholder = "Type your text…";
    public const string OverlayPlaceholderRtl = "متن خود را بنویسید…";   // shown when the source is an RTL language
    public const string OverlayTranslate = "Translate";
    public const string OverlayStyleLabel = "Style";

    // Dictation (speech to text)
    public const string DictationStart = "Dictate (speak your text)";
    public const string DictationStop = "Stop dictating";
    public const string DictationListening = "Listening…";
    public const string DictationConnecting = "Starting the microphone…";
    public const string DictationNoMicrophone = "No microphone is available. Check your microphone and Windows privacy settings.";
    public const string DictationFailed = "Dictation stopped:";

    // Auto-correct
    public const string Correcting = "Correcting…";
    public const string OverlayTranslating = "Translating…";
    public const string OverlayNoApiKey = "Add your OpenAI key in Settings first.";
    public const string OverlayError = "Couldn't translate:";

    // Badge
    public const string BadgeTooltip = "Click to translate into this field";
    public const string BadgeIgnoreFormat = "Don't show the badge in {0}";
    public const string BadgeIgnoreGeneric = "Don't show the badge in this app";
    public const string BadgeSettings = "Settings…";
    public const string BadgeQuit = "Quit AI Translator";

    // Overlay header
    public const string OverlayClose = "Close";

    // Selection translator (read mode)
    public const string SelectionTooltip = "Translate the selected text";
    public const string SelectionCopy = "Copy";
    public const string SelectionCopied = "Copied";
    public const string SelectionCopyResult = "Copy the translation";
    public const string SelectionTargetLang = "Translate to";
    public const string SelectionTranslating = "Translating…";
    public const string SelectionEmpty = "No translation was returned.";
    public const string SettingsSelectionTranslator = "Translate selected text";
    public const string SettingsSelectionTranslatorHint = "Show a translate icon when you select text anywhere, and translate it in a pop-up.";

    // Settings: window + tabs
    public const string SettingsTitle = "AI Translator Settings";
    public const string SettingsHeading = "Settings";
    public const string SettingsTabGeneral = "General";
    public const string SettingsTabAccount = "Account";
    public const string SettingsTabBlockList = "Block List";

    // Settings: General tab
    public const string SettingsLanguages = "Languages";
    public const string SettingsLanguagesHint = "Translate between these two languages. Auto-detect picks the direction from what you type.";
    public const string SettingsPrimaryLang = "Primary";
    public const string SettingsSecondaryLang = "Secondary";
    public const string SettingsAutoDirection = "Auto-detect direction";
    public const string SettingsAutoDirectionHint = "Detect the source language from what you type.";
    public const string SettingsBehavior = "Behavior";
    public const string SettingsModel = "OpenAI model";
    public const string SettingsHotkey = "Global hotkey";
    public const string SettingsHotkeyHint = "Opens the translation box anywhere, even when the badge is hidden.";
    public const string SettingsAutoAppearBadge = "Show the badge automatically";
    public const string SettingsAutoAppearBadgeHint = "Show the badge next to any editable text field, except in blocked apps.";
    public const string SettingsRunAtStartup = "Run at Windows startup";
    public const string SettingsRunAtStartupHint = "Start AI Translator when you sign in to Windows.";
    public const string SettingsHumanize = "Natural, human-sounding output";
    public const string SettingsHumanizeHint = "Make translations read like a person wrote them, avoiding stiff, machine-like phrasing.";
    public const string SettingsDictation = "Dictation (speech to text)";
    public const string SettingsDictationHint = "Show a microphone in the box so you can speak instead of typing. Audio is sent to OpenAI only while you are dictating.";
    public const string SettingsAutoCorrect = "Auto-correct";
    public const string SettingsAutoCorrectHint = "Proof-read the box before translating: fix typos, repair words dictation misheard, and write English terms in Latin script. It corrects spelling, never your style.";

    // Settings: Account tab
    public const string SettingsApiKey = "OpenAI API key";
    public const string SettingsApiKeyHint = "Kept in Windows Credential Manager, never saved to a file.";
    public const string SettingsApiKeyShow = "Show";
    public const string SettingsApiKeyPlaceholder = "sk-…";

    // Settings: Block List tab
    public const string SettingsBlockListHint = "The badge shows in every editable field. Add an app here to hide it there (matched on the process name, e.g. \"chrome\" or \"^Code$\").";
    public const string SettingsBlockListAddPlaceholder = "App name or pattern (e.g. notepad)";
    public const string SettingsBlockListAdd = "Add";
    public const string SettingsBlockListRemove = "Remove";
    public const string SettingsBlockListEmpty = "No blocked apps yet. The badge can show anywhere.";

    // Settings: footer
    public const string SettingsSave = "Save changes";
    public const string SettingsHotkeyTaken = "That hotkey is already in use. Pick another.";
    public const string SettingsHotkeyInvalid = "Not a valid hotkey. Try Ctrl+Alt+T.";
    public const string SettingsSaved = "Saved.";
    public const string SettingsSaveError = "Couldn't save:";
}
