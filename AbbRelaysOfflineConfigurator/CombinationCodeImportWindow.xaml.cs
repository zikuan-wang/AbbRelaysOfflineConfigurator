using System.Windows;
using MaterialDesignThemes.Wpf;

namespace AbbRelaysOfflineConfigurator;

public partial class CombinationCodeImportWindow : Window
{
    public CombinationCodeImportWindow()
        : this("导入组合代码", "主代码必须在开头，后续选项代码可乱序，用 + 分隔。", "导入", "REX615A10GN+APP1+...")
    {
    }

    public CombinationCodeImportWindow(string title, string description, string actionText)
        : this(title, description, actionText, "REX615A10GN+APP1+...")
    {
    }

    public CombinationCodeImportWindow(string title, string description, string actionText, string hint)
    {
        InitializeComponent();
        Title = title;
        TitleTextBlock.Text = title;
        DescriptionTextBlock.Text = description;
        ImportButton.Content = actionText;
        HintAssist.SetHint(CodeTextBox, hint);
        CodeTextBox.Focus();
    }

    public string CombinationCode { get; private set; } = "";

    private void ImportButton_OnClick(object sender, RoutedEventArgs e)
    {
        CombinationCode = CodeTextBox.Text;
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
