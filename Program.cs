using Avalonia;
using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using OmniLauncher.Services.Desktop;
using OmniLauncher.Views;

namespace OmniLauncher;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before Main is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        using var instance = new SingleInstanceService();
        if (!instance.IsPrimary)
        {
            try { instance.ActivateExistingAsync().GetAwaiter().GetResult(); }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Could not activate the running OmniLauncher: {ex}");
            }
            return;
        }

        instance.Listen(() => Dispatcher.UIThread.Post(() =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
                { MainWindow: MainWindow window })
                window.ShowLauncher();
        }));
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
