using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using LolitaPoker.Core.Audio;
using LolitaPoker.Core.ViewModels;

namespace LolitaPoker.Core.Views;

public partial class DoudizhuMainWindow : Window
{
    public MainViewModel ViewModel { get; }

    // 宽高比限制（防止牌过大或过小）
    private const double MinAspect = 1.3;  // 最窄（偏竖）
    private const double MaxAspect = 1.8;  // 最宽（偏横）

    public DoudizhuMainWindow(ITtsService? ttsService = null, IBgmService? bgmService = null)
    {
        ViewModel = new MainViewModel(ttsService, bgmService);
        DataContext = ViewModel;
        InitializeComponent();
        SizeChanged += OnWindowSizeChanged;
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        double w = e.NewSize.Width;
        double h = e.NewSize.Height;
        if (w < 1 || h < 1) return;

        double aspect = w / h;
        if (aspect < MinAspect)
            Width = h * MinAspect;
        else if (aspect > MaxAspect)
            Width = h * MaxAspect;
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
