using Microsoft.Win32;
using AiTranslator.App.Resources;

namespace AiTranslator.App.Shell;

/// <summary>Registers/unregisters run-at-login via the per-user HKCU Run key (no admin needed).</summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Apply(bool runAtStartup)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null)
        {
            return;
        }

        if (runAtStartup)
        {
            key.SetValue(UiStrings.AppName, $"\"{Environment.ProcessPath}\"");
        }
        else
        {
            key.DeleteValue(UiStrings.AppName, throwOnMissingValue: false);
        }
    }
}
