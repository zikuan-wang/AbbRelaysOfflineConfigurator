using System.Windows;
using AbbRelaysOfflineConfigurator.Services;

namespace AbbRelaysOfflineConfigurator;

// WPF 客户端启动入口。安全检查必须先于窗口和业务 ViewModel 创建，
// 这样错误发布包中若混入签名私钥，应用会在加载业务数据前直接拒绝运行。
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            // 先建立客户端运行目录的安全边界，再进入标准 WPF 生命周期。
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
