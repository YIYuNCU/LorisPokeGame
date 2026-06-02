using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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

    // 动态卡牌尺寸（由 SizeChanged 更新）
    internal static double CardWidth { get; private set; } = 89;
    internal static double CardHeight { get; private set; } = 126;
    internal static double CardNegativeMargin { get; private set; } = -44.5;
    // 出牌区/底牌尺寸（与手牌等比缩放）
    internal static double PlayedWidth { get; private set; } = 61;
    internal static double PlayedHeight { get; private set; } = 86;
    internal static double PlayedMargin { get; private set; } = -26;
    internal static double AiPlayedWidth { get; private set; } = 58;
    internal static double AiPlayedHeight { get; private set; } = 82;
    internal static double AiPlayedMargin { get; private set; } = -21;
    internal static double LandlordWidth { get; private set; } = 42;
    internal static double LandlordHeight { get; private set; } = 59;

    // 卡牌宽高比
    private const double CardAspect = 105.0 / 74.0; // ≈1.419

    // 设计基准窗口宽度
    private const double DesignWidth = 1000;
    // 基础缩放阻尼系数
    private const double BaseDampen = 0.5;
    // 牌多时额外阻尼（每多1张增加 0.1，仅考虑17~20张）
    private const double ExtraDampenPerCard = 0.1;
    // 阻尼上限（0.5 + 3×0.1 = 0.8，对应20张牌）
    private const double MaxDampen = 0.8;
    // 当前手牌数量（用于动态阻尼）
    private int _handCount = 17;

    // 选牌上移量
    internal static double SelectionLift { get; private set; } = 16;
    // 身份区动态尺寸（基准值 ×0.8）
    internal static double IdentityFontSize { get; private set; } = 12.8;
    internal static double IdentityPaddingH { get; private set; } = 16;
    internal static double IdentityPaddingV { get; private set; } = 4.8;
    internal static double IdentityCornerRadius { get; private set; } = 11.2;
    // 操作按钮动态尺寸（基准 ×0.8，减小缩放比例）
    internal static double ButtonWidth { get; private set; } = 96;
    internal static double ButtonHeight { get; private set; } = 35;
    internal static double ButtonFontSize { get; private set; } = 14.4;
    internal static double ButtonPaddingH { get; private set; } = 16;
    internal static double ButtonPaddingV { get; private set; } = 6.4;
    internal static double ButtonMargin { get; private set; } = 8;

    // 尺寸变化事件，通知 CardControl 更新 ScaleTransform 中心
    internal static event Action? CardSizeChanged;

    public GameTableControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        // 监听自身尺寸变化（窗口缩小时也会触发）
        SizeChanged += OnControlSizeChanged;
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

        // 手牌数量变化时刷新卡牌尺寸（防止新建的 CardControl 使用 XAML 默认值）
        HandItemsControl.ItemContainerGenerator.StatusChanged += OnHandContainerStatusChanged;

        // 初始计算卡牌尺寸
        RecalcCardSize(ActualWidth);
    }

    private void OnHandContainerStatusChanged(object? sender, EventArgs e)
    {
        if (HandItemsControl.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
        {
            int newCount = HandItemsControl.Items.Count;
            bool countChanged = newCount != _handCount && newCount > 0;
            _handCount = newCount > 0 ? newCount : _handCount;

            if (countChanged)
                RecalcCardSize(ActualWidth);
            else
                Dispatcher.BeginInvoke(() => RefreshHandCardSizes(), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void OnControlSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RecalcCardSize(e.NewSize.Width);
    }

    /// <summary>
    /// 根据窗口宽度动态计算卡牌尺寸，阻尼缩放，保持宽高比。
    /// </summary>
    private void RecalcCardSize(double controlWidth)
    {
        if (controlWidth < 1) return;

        // 基于宽度的比例缩放 + 动态阻尼（牌越多缩得越狠）
        double baseW = 89.0;
        double extraDampen = Math.Max(0, _handCount - 17) * ExtraDampenPerCard;
        double dampen = Math.Min(BaseDampen + extraDampen, MaxDampen);
        double ratio = controlWidth / DesignWidth;
        double dampedRatio = 1.0 + (ratio - 1.0) * dampen;
        double cardW = baseW * dampedRatio;

        // 限制范围
        cardW = Math.Max(40, Math.Min(cardW, 110));
        double cardH = Math.Round(cardW * CardAspect, 1);
        cardW = Math.Round(cardW, 1);

        if (Math.Abs(cardW - CardWidth) < 0.5) return;

        CardWidth = cardW;
        CardHeight = cardH;
        CardNegativeMargin = -Math.Round(cardW * 0.5, 1);
        SelectionLift = Math.Round(cardW * 0.22, 1);

        // 出牌区/底牌等比缩放（AI出牌放大20%）
        PlayedWidth = Math.Round(cardW * 0.685, 1);
        PlayedHeight = Math.Round(cardH * 0.685, 1);
        PlayedMargin = -Math.Round(PlayedWidth * 0.43, 1);
        AiPlayedWidth = Math.Round(cardW * 0.626, 1);
        AiPlayedHeight = Math.Round(cardH * 0.626, 1);
        AiPlayedMargin = -Math.Round(AiPlayedWidth * 0.36, 1);
        LandlordWidth = Math.Round(cardW * 0.472, 1);
        LandlordHeight = Math.Round(cardH * 0.472, 1);

        // 身份区跟随缩放
        double scale = cardW / 89.0;
        IdentityFontSize = Math.Round(Math.Min(12.8 * scale, 16), 1);
        IdentityPaddingH = Math.Round(Math.Min(16 * scale, 20), 1);
        IdentityPaddingV = Math.Round(Math.Min(4.8 * scale, 6), 1);
        IdentityCornerRadius = Math.Round(Math.Min(11.2 * scale, 14), 1);

        // 操作按钮跟随缩放（缩放比例减半，上限限制）
        double btnScale = 1.0 + (scale - 1.0) * 0.5;
        ButtonWidth = Math.Round(Math.Min(96 * btnScale, 110), 1);
        ButtonHeight = Math.Round(Math.Min(35 * btnScale, 42), 1);
        ButtonFontSize = Math.Round(Math.Min(14.4 * btnScale, 17), 1);
        ButtonPaddingH = Math.Round(Math.Min(16 * btnScale, 20), 1);
        ButtonPaddingV = Math.Round(Math.Min(6.4 * btnScale, 8), 1);
        ButtonMargin = Math.Round(Math.Min(8 * btnScale, 10), 1);

        // 刷新已有手牌控件尺寸
        RefreshHandCardSizes();
        RefreshAllCardSizes();
        RefreshIdentityControls();

        CardSizeChanged?.Invoke();
    }

    /// <summary>
    /// 更新身份区（昵称/角色标签）和操作按钮的尺寸。
    /// 遍历 Row 4 的 Grid 查找 Border 和 Button 并更新。
    /// </summary>
    private void RefreshIdentityControls()
    {
        // 找到操作按钮行的 Grid（Row 4 内的第二个子元素）
        var row4Stack = GameGrid.Children
            .OfType<StackPanel>()
            .FirstOrDefault(sp => Grid.GetRow(sp) == 4);
        if (row4Stack == null) return;

        var identityGrid = row4Stack.Children.OfType<Grid>().FirstOrDefault();
        if (identityGrid == null) return;

        // 更新身份区 Border
        var identityStack = identityGrid.Children
            .OfType<StackPanel>()
            .FirstOrDefault(sp => Grid.GetColumn(sp) == 0);
        if (identityStack != null)
        {
            foreach (var border in identityStack.Children.OfType<Border>())
            {
                border.CornerRadius = new CornerRadius(IdentityCornerRadius);
                border.Padding = new Thickness(IdentityPaddingH, IdentityPaddingV, IdentityPaddingH, IdentityPaddingV);
                var tb = border.Child as TextBlock;
                if (tb != null)
                    tb.FontSize = IdentityFontSize;
            }
        }

        // 更新操作按钮
        var buttonStack = identityGrid.Children
            .OfType<StackPanel>()
            .FirstOrDefault(sp => Grid.GetColumn(sp) == 1);
        if (buttonStack != null)
        {
            foreach (var btn in buttonStack.Children.OfType<Button>())
            {
                btn.Width = ButtonWidth;
                btn.Height = ButtonHeight;
                btn.FontSize = ButtonFontSize;
                btn.Margin = new Thickness(ButtonMargin);
                // 更新模板内的 Border Padding
                if (btn.Template.FindName("border", btn) is Border bd)
                    bd.Padding = new Thickness(ButtonPaddingH, ButtonPaddingV, ButtonPaddingH, ButtonPaddingV);
            }
        }
    }

    /// <summary>
    /// 更新所有手牌 CardControl 的 Width/Height 和容器 Margin。
    /// </summary>
    private void RefreshHandCardSizes()
    {
        if (HandItemsControl == null) return;

        // 更新 ItemsControl 自身的左间距（为选牌上移留空间）
        double topPad = SelectionLift + 10;
        HandItemsControl.Margin = new Thickness(CardWidth * 0.5, topPad, 10, 0);

        foreach (var item in HandItemsControl.Items)
        {
            var container = HandItemsControl.ItemContainerGenerator.ContainerFromItem(item) as ContentPresenter;
            if (container == null) continue;

            // 更新容器负边距
            container.Margin = new Thickness(CardNegativeMargin, 0, 0, 0);

            // 更新卡牌尺寸
            var card = FindChildOfType<CardControl>(container);
            if (card != null)
            {
                card.Width = CardWidth;
                card.Height = CardHeight;
            }
        }
    }

    /// <summary>
    /// 更新所有出牌区/底牌 CardControl 的 Width/Height（遍历可视化树）。
    /// </summary>
    private void RefreshAllCardSizes()
    {
        RefreshCardSizesRecursive(this);
    }

    private void RefreshCardSizesRecursive(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is CardControl card)
            {
                // 根据当前尺寸判断属于哪个区域并更新
                // 手牌由 RefreshHandCardSizes 单独处理，这里处理其余
                if (Math.Abs(card.Width - PlayedWidth) < 1 || Math.Abs(card.Width - 61) < 1)
                {
                    card.Width = PlayedWidth;
                    card.Height = PlayedHeight;
                }
                else if (Math.Abs(card.Width - AiPlayedWidth) < 1 || Math.Abs(card.Width - 58) < 1)
                {
                    card.Width = AiPlayedWidth;
                    card.Height = AiPlayedHeight;
                }
                else if (Math.Abs(card.Width - LandlordWidth) < 1 || Math.Abs(card.Width - 42) < 1)
                {
                    card.Width = LandlordWidth;
                    card.Height = LandlordHeight;
                }
            }
            RefreshCardSizesRecursive(child);
        }
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

        // 洗牌动画卡牌尺寸跟随当前动态尺寸（略大于手牌）
        double cardW = CardWidth * 1.2;
        double cardH = CardHeight * 1.2;
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
