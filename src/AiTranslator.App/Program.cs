using System;
using Velopack;

namespace AiTranslator.App;

/// <summary>
/// Explicit entry point. Velopack's install/update/uninstall hooks must run before any UI (they are
/// invoked with special arguments by the installer/updater and exit immediately), so they go first;
/// then the normal WPF application starts.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
