// -----------------------------------------------------------------------
// GameHostWindow.xaml.cs - 游戏窗口
// 承载 GameTableControl，管理游戏生命周期
// -----------------------------------------------------------------------

using System.ComponentModel;
using System.Windows;
using LolitaPoker.Core.ViewModels;

namespace LolitaPoker.Plugin;

public partial class GameHostWindow : Window
{
    private readonly GameViewModel _gameViewModel;
    private readonly Action? _onWindowClosed;

    public GameHostWindow(GameViewModel gameViewModel, Action? onWindowClosed = null)
    {
        InitializeComponent();

        _gameViewModel = gameViewModel;
        _onWindowClosed = onWindowClosed;

        DataContext = _gameViewModel;

        // 当用户从游戏内点击返回菜单时，关闭窗口
        _gameViewModel.RequestNavigateToMenu = () =>
        {
            Dispatcher.Invoke(Close);
        };
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _gameViewModel.Cleanup();
        _onWindowClosed?.Invoke();
    }
}
