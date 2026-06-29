namespace AiTranslator.Core.Abstractions;

/// <summary>Registers a system-wide hotkey and raises an event when it is pressed.</summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>Registers the hotkey (e.g. "Ctrl+Alt+T"). Returns false if the combo is already taken.</summary>
    bool Register(string hotkey);

    void Unregister();

    event EventHandler? HotkeyPressed;
}
