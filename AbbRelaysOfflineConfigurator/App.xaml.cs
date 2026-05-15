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
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "安全检查失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        base.OnStartup(e);
    }
}
