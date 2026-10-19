using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OmniLauncher.Services.Desktop;
using OmniLauncher.ViewModels;
using OmniLauncher.Views;

namespace OmniLauncher;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
            desktop.MainWindow = window;
            DesktopIntegrationService? integration = null;
            window.Opened += (_, _) => integration ??= new DesktopIntegrationService(window, (MainWindowViewModel)window.DataContext!);
            window.Closed += (_, _) => integration?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
