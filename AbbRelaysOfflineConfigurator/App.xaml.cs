using System.Windows;
using AbbRelaysOfflineConfigurator.Services;

namespace AbbRelaysOfflineConfigurator;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            RuntimeSecurityGuard.EnsureClientRuntimeSafe();
            base.OnStartup(e);

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
