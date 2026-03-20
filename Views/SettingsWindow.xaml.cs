using System.Diagnostics;
using System.Windows;
using VRChatLotteryTool.Core;
using VRChatLotteryTool.UI.ViewModels;

namespace VRChatLotteryTool.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void OpenDataFolder(object sender, RoutedEventArgs e)
        => Process.Start("explorer.exe", AppPaths.DataDir);

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
