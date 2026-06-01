using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LolitaPoker.Core.Assets;
using LolitaPoker.Core.ViewModels;

namespace LolitaPoker.Core.Views;

public partial class GameTableControl : UserControl
{
    private static BitmapImage? _cardBackImage;

    // 拖拽选中状态
    internal static bool IsDragging;
    private StackPanel? _handStackPanel;
    private CardViewModel? _dragStartCard;
    private CardControl? _lastDragCard;

    // 负边距值，与 XAML 中的 Margin="-35,0,0,0" 一致
    private const double CardNegativeMargin = -35;
    private const double CardWidth = 70;

    public GameTableControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is GameViewModel vm)
        {
            vm.ShuffleRequested += OnShuffleRequested;
            vm.HandLayoutRequested += OnHandLayoutRequested;
        }

        if (_cardBackImage == null && CardImageProvider.IsInitialized)
            _cardBackImage = CardImageProvider.GetCardBack();

        // 绑定手牌区域的拖拽选中事件
        AttachDragHandlers();
    }

    private void OnShuffleRequested()
    {
        ShuffleCanvas.Children.Clear();
        ShuffleCanvas.Visibility = Visibility.Visible;

        // 强制布局刷新，让 Collapsed→Visible 后 ActualWidth/Height 生效
        ShuffleCanvas.Measure(new Size(ActualWidth, ActualHeight));
        ShuffleCanvas.Arrange(new Rect(ShuffleCanvas.DesiredSize));
        UpdateLayout();

        if (_cardBackImage == null)
        {
            // 无牌背图片时直接跳过动画
            if (DataContext is GameViewModel vm)
                vm.OnShuffleComplete();
            return;
        }

        double canvasW = ShuffleCanvas.ActualWidth;
        double canvasH = ShuffleCanvas.ActualHeight;
        if (canvasW < 1) canvasW = 600;
        if (canvasH < 1) canvasH = 400;

        double cardW = 85;
        double cardH = 120;
        double centerX = (canvasW - cardW) / 2;
        double centerY = (canvasH - cardH) / 2;

        // 创建两叠牌（左叠、右叠），共 12 张
        var leftStack = new List<Image>();
        var rightStack = new List<Image>();

        for (int i = 0; i < 6; i++)
        {
            var leftCard = CreateDeckCard(cardW, cardH);
            Canvas.SetLeft(leftCard, centerX - 100 + i * 2.0);
            Canvas.SetTop(leftCard, centerY - i * 1.0);
            Canvas.SetZIndex(leftCard, i);
            ShuffleCanvas.Children.Add(leftCard);
            leftStack.Add(leftCard);

            var rightCard = CreateDeckCard(cardW, cardH);
            Canvas.SetLeft(rightCard, centerX + 100 + i * 2.0);
            Canvas.SetTop(rightCard, centerY - i * 1.0);
            Canvas.SetZIndex(rightCard, i + 6);
            ShuffleCanvas.Children.Add(rightCard);
            rightStack.Add(rightCard);
        }

        // 中间牌堆（最终合并位置）
        var centerStack = new List<Image>();
        for (int i = 0; i < 6; i++)
        {
            var card = CreateDeckCard(cardW, cardH);
            card.Opacity = 0;
            Canvas.SetLeft(card, centerX + i * 1.5);
            Canvas.SetTop(card, centerY - i * 0.6);
            Canvas.SetZIndex(card, i + 12);
            ShuffleCanvas.Children.Add(card);
            centerStack.Add(card);
        }

        // 播放洗牌动画
        PlayShuffleAnimation(leftStack, rightStack, centerStack, centerX, centerY);
    }

    /// <summary>
    /// 强制完成手牌区域布局，确保所有 CardControl 实例已创建。
    /// 必须在发牌动画前调用，否则 CardControl 的 PropertyChanged 订阅不存在，动画无法触发。
    /// </summary>
    private void OnHandLayoutRequested()
    {
        HandItemsControl.UpdateLayout();
    }

    private Image CreateDeckCard(double w, double h)
    {
        return new Image
        {
            Source = _cardBackImage,
            Width = w,
            Height = h,
        };
    }

    private void PlayShuffleAnimation(List<Image> leftStack, List<Image> rightStack,
        List<Image> centerStack, double centerX, double centerY)
    {
        var storyboard = new Storyboard();
        double leftOrigin = Canvas.GetLeft(leftStack[0]);
        double rightOrigin = Canvas.GetLeft(rightStack[0]);

        // 阶段1: 左叠和右叠交错向中间移动（洗牌效果）
        // 左叠的牌向右移动
        for (int i = 0; i < leftStack.Count; i++)
        {
            var card = leftStack[i];
            double delay = i * 0.1;

            // X 方向：从左侧移到中间偏右
            var animX = new DoubleAnimation
            {
                From = Canvas.GetLeft(card),
                To = centerX + 40,
                BeginTime = TimeSpan.FromSeconds(delay),
                Duration = TimeSpan.FromSeconds(0.2),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                AutoReverse = true,
            };
            Storyboard.SetTarget(animX, card);
            Storyboard.SetTargetProperty(animX, new PropertyPath("(Canvas.Left)"));
            storyboard.Children.Add(animX);

            // Y 方向：轻微下移再回来
            var animY = new DoubleAnimation
            {
                From = Canvas.GetTop(card),
                To = centerY + 15,
                BeginTime = TimeSpan.FromSeconds(delay),
                Duration = TimeSpan.FromSeconds(0.2),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                AutoReverse = true,
            };
            Storyboard.SetTarget(animY, card);
            Storyboard.SetTargetProperty(animY, new PropertyPath("(Canvas.Top)"));
            storyboard.Children.Add(animY);
        }

        // 右叠的牌向左移动
        for (int i = 0; i < rightStack.Count; i++)
        {
            var card = rightStack[i];
            double delay = i * 0.1 + 0.05; // 错开半拍

            var animX = new DoubleAnimation
            {
                From = Canvas.GetLeft(card),
                To = centerX - 40,
                BeginTime = TimeSpan.FromSeconds(delay),
                Duration = TimeSpan.FromSeconds(0.2),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                AutoReverse = true,
            };
            Storyboard.SetTarget(animX, card);
            Storyboard.SetTargetProperty(animX, new PropertyPath("(Canvas.Left)"));
            storyboard.Children.Add(animX);

            var animY = new DoubleAnimation
            {
                From = Canvas.GetTop(card),
                To = centerY + 15,
                BeginTime = TimeSpan.FromSeconds(delay),
                Duration = TimeSpan.FromSeconds(0.2),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                AutoReverse = true,
            };
            Storyboard.SetTarget(animY, card);
            Storyboard.SetTargetProperty(animY, new PropertyPath("(Canvas.Top)"));
            storyboard.Children.Add(animY);
        }

        // 阶段2: 两叠牌淡出，中间牌堆淡入（合并效果）
        double mergeDelay = 0.8; // 洗牌动作结束后开始合并

        foreach (var card in leftStack)
        {
            var fadeOut = new DoubleAnimation
            {
                From = 1, To = 0,
                BeginTime = TimeSpan.FromSeconds(mergeDelay),
                Duration = TimeSpan.FromSeconds(0.25),
            };
            Storyboard.SetTarget(fadeOut, card);
            Storyboard.SetTargetProperty(fadeOut, new PropertyPath(Image.OpacityProperty));
            storyboard.Children.Add(fadeOut);
        }

        foreach (var card in rightStack)
        {
            var fadeOut = new DoubleAnimation
            {
                From = 1, To = 0,
                BeginTime = TimeSpan.FromSeconds(mergeDelay),
                Duration = TimeSpan.FromSeconds(0.25),
            };
            Storyboard.SetTarget(fadeOut, card);
            Storyboard.SetTargetProperty(fadeOut, new PropertyPath(Image.OpacityProperty));
            storyboard.Children.Add(fadeOut);
        }

        foreach (var card in centerStack)
        {
            var fadeIn = new DoubleAnimation
            {
                From = 0, To = 1,
                BeginTime = TimeSpan.FromSeconds(mergeDelay),
                Duration = TimeSpan.FromSeconds(0.3),
            };
            Storyboard.SetTarget(fadeIn, card);
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath(Image.OpacityProperty));
            storyboard.Children.Add(fadeIn);
        }

        // 阶段3: 中间牌堆整体淡出
        double fadeOutAllDelay = 1.2;
        foreach (var card in centerStack)
        {
            var fadeOut = new DoubleAnimation
            {
                From = 1, To = 0,
                BeginTime = TimeSpan.FromSeconds(fadeOutAllDelay),
                Duration = TimeSpan.FromSeconds(0.3),
            };
            Storyboard.SetTarget(fadeOut, card);
            Storyboard.SetTargetProperty(fadeOut, new PropertyPath(Image.OpacityProperty));
            storyboard.Children.Add(fadeOut);
        }

        // 动画完成后通知 ViewModel
        storyboard.Duration = TimeSpan.FromSeconds(1.6);
        storyboard.Completed += (s, e) =>
        {
            ShuffleCanvas.Visibility = Visibility.Collapsed;
            ShuffleCanvas.Children.Clear();
            if (DataContext is GameViewModel vm)
                vm.OnShuffleComplete();
        };

        storyboard.Begin();
    }

    // ========== 房间号复制 ==========

    private async void OnRoomCodeClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not GameViewModel vm) return;
        if (string.IsNullOrEmpty(vm.RoomCode)) return;

        for (int i = 0; i < 3; i++)
        {
            try
            {
                Clipboard.SetText(vm.RoomCode);
                vm.CopyFeedback = "已复制 ✓";

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (_, _) =>
                {
                    vm.CopyFeedback = "";
                    timer.Stop();
                };
                timer.Start();
                return;
            }
            catch
            {
                await Task.Delay(100);
            }
        }

        vm.CopyFeedback = "复制失败";
    }

    // ========== 房间可见性切换 ==========

    private void OnVisibilityToggle(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is GameViewModel vm)
            vm.ToggleRoomVisibility();
    }

    // ========== 拖拽选中 ==========

    private void AttachDragHandlers()
    {
        _handStackPanel ??= FindChildOfType<StackPanel>(
            this, sp => sp.Orientation == Orientation.Horizontal);

        if (_handStackPanel != null)
        {
            _handStackPanel.PreviewMouseLeftButtonDown += OnHandMouseDown;
            _handStackPanel.PreviewMouseMove += OnHandMouseMove;
            _handStackPanel.PreviewMouseLeftButtonUp += OnHandMouseUp;
        }
    }

    private void OnHandMouseDown(object sender, MouseButtonEventArgs e)
    {
        var stackPanel = (StackPanel)sender;
        var pos = e.GetPosition(stackPanel);

        var card = FindCardAtPoint(stackPanel, pos);
        if (card == null) return;

        _dragStartCard = card.DataContext as CardViewModel;
        _lastDragCard = card;
        IsDragging = false;

        Mouse.Capture(stackPanel, CaptureMode.SubTree);
        e.Handled = true;
    }

    private void OnHandMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStartCard == null) return;

        var stackPanel = (StackPanel)sender;
        var pos = e.GetPosition(stackPanel);

        if (!IsDragging)
        {
            var origin = e.GetPosition(this);
            if (Math.Abs(origin.X) < 3 && Math.Abs(origin.Y) < 3) return;

            IsDragging = true;
        }

        var card = FindCardAtPoint(stackPanel, pos);
        if (card == null || card == _lastDragCard) return;

        _lastDragCard = card;

        if (card.DataContext is CardViewModel vm)
            vm.IsSelected = _dragStartCard.IsSelected;
    }

    private void OnHandMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStartCard != null && !IsDragging)
        {
            // 没有拖拽 → 切换起始牌的选中状态
            _dragStartCard.IsSelected = !_dragStartCard.IsSelected;
        }

        Mouse.Capture(null);
        _dragStartCard = null;
        _lastDragCard = null;
        IsDragging = false;
    }

    /// <summary>
    /// 根据坐标找到对应的 CardControl（牌重叠时取最上层）
    /// </summary>
    private CardControl? FindCardAtPoint(StackPanel panel, Point pos)
    {
        var cards = panel.Children;
        int count = cards.Count;
        if (count == 0) return null;

        for (int i = count - 1; i >= 0; i--)
        {
            if (cards[i] is not ContentPresenter cp) continue;

            double left = i * (CardWidth + CardNegativeMargin);

            if (pos.X >= left && pos.X < left + CardWidth &&
                pos.Y >= 0 && pos.Y <= CardWidth)
            {
                return FindChildOfType<CardControl>(cp);
            }
        }

        return null;
    }

    /// <summary>
    /// 递归查找指定类型的子元素
    /// </summary>
    private static T? FindChildOfType<T>(DependencyObject parent,
        Func<T, bool>? predicate = null) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is T typed && (predicate == null || predicate(typed)))
                return typed;

            var result = FindChildOfType(child, predicate);
            if (result != null) return result;
        }

        return null;
    }
}
