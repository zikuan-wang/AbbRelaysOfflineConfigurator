using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace AbbRelaysOfflineConfigurator.Services;

public static class ClipboardService
{
    public static bool TrySetText(string? text, string caption, bool isEnglish = false, bool showError = true)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        Exception? lastError = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (ExternalException ex)
            {
                lastError = ex;
            }
            catch (InvalidOperationException ex)
            {
                lastError = ex;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            Thread.Sleep(40);
        }

        if (showError)
        {
            var message = isEnglish
                ? $"Copy failed because the Windows clipboard is temporarily unavailable. Please try again.\n\n{lastError?.Message}"
                : $"复制失败：Windows 剪贴板暂时不可用，请稍后重试。\n\n{lastError?.Message}";
            MessageBox.Show(
                Application.Current?.MainWindow,
                message,
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        return false;
    }
}
