using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace AbbRelaysOfflineConfigurator.Views;

public partial class Rio600FunctionCatalogWindow : Window
{
    private readonly IReadOnlyList<Rio600FunctionCatalogItem> _items =
    [
        new("通信", "LECM", "以太网通信模块", "Ethernet communication module", "LECM", "支持 RIO600 与保护装置或系统之间的远程 I/O 通信连接。"),
        new("电源", "PSM", "电源模块", "Power supply module", "PSM", "支持高压或低压电源输入，并可按配置扩展第二电源。"),
        new("I/O", "DIM", "开关量输入扩展", "Binary input extension", "DIM8H / DIM8L", "用于采集远方开关量状态、告警、联锁或过程信号。"),
        new("I/O", "DOM", "开关量输出扩展", "Binary output extension", "DOM4 / DOM8", "用于输出跳闸、合闸、告警、联锁或控制命令。"),
        new("I/O", "RTD", "RTD 测量扩展", "RTD measurement extension", "RTD4", "用于接入温度传感器或其他 RTD 测量信号。"),
        new("I/O", "AIM", "mA 模拟量输入扩展", "mA analog input extension", "AIM4", "用于接入 0/4-20 mA 等模拟量测量信号。"),
        new("工程", "CHANNEL", "通道与点数校验", "Channel and point validation", "全部模块", "根据通信模块、电源配置和 I/O 模块组合实时校验通道数与点数限制。"),
        new("工程", "ORDER", "模块订货号", "Module ordering number", "全部模块", "在槽位分配中显示每个已配置模块的订货号，并支持复制。"),
        new("图纸", "TERMINAL", "接线图", "Terminal diagram", "I/O 模块", "已配置 I/O 模块可打开对应接线图，并支持放大、缩小和适应窗口。"),
        new("图纸", "DIMENSION", "尺寸图", "Dimension drawing", "电源/通信/I/O 模块", "已配置模块可打开对应尺寸图，并支持放大、缩小和适应窗口。")
    ];

    public Rio600FunctionCatalogWindow()
    {
        InitializeComponent();
        FunctionDataGrid.ItemsSource = new ObservableCollection<Rio600FunctionCatalogItem>(_items);
    }

    private void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        var token = SearchTextBox.Text.Trim();
        FunctionDataGrid.ItemsSource = new ObservableCollection<Rio600FunctionCatalogItem>(
            string.IsNullOrWhiteSpace(token)
                ? _items
                : _items.Where(item =>
                    item.Category.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                    item.Code.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                    item.ChineseName.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                    item.EnglishName.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                    item.Modules.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                    item.Description.Contains(token, StringComparison.OrdinalIgnoreCase)));
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();
}

public sealed record Rio600FunctionCatalogItem(
    string Category,
    string Code,
    string ChineseName,
    string EnglishName,
    string Modules,
    string Description);
