using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using LolitaPoker.Core.Audio;
using LolitaPoker.Core.ViewModels;

namespace LolitaPoker.Core.Views;

public partial class DoudizhuMainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public DoudizhuMainWindow(ITtsService? ttsService = null, IBgmService? bgmService = null)
    {
        ViewModel = new MainViewModel(ttsService, bgmService);
        DataContext = ViewModel;
        InitializeComponent();
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        // 启动时短暂置顶，确保窗口显示在最上层
        Topmost = true;

        // 使用定时器延迟取消置顶
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        timer.Tick += (s, args) =>
        {
            Topmost = false;
            timer.Stop();
        };
        timer.Start();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        // 清理资源
        ViewModel.Cleanup();

        // 确保应用程序正常退出
        Application.Current.Shutdown();
    }
}
