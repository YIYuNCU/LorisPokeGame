using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using LolitaPoker.Core.Assets;
using LolitaPoker.Core.ViewModels;

namespace LolitaPoker.Core.Views;

public partial class CardControl : UserControl
{
    private static BitmapImage? _sharedCardBack;

    // 缓动函数
    private static readonly CubicEase GatherEasing = new() { EasingMode = EasingMode.EaseOut };
    private static readonly ElasticEase SpreadEasing = new()
    {
        EasingMode = EasingMode.EaseOut,
        Oscillations = 1,
        Springiness = 3
    };

    public CardControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        if (_sharedCardBack == null && CardImageProvider.IsInitialized)
            _sharedCardBack = CardImageProvider.GetCardBack();

        if (_sharedCardBack != null)
            CardBackImage.Source = _sharedCardBack;

        // 响应动态卡牌尺寸变化
        GameTableControl.CardSizeChanged += OnCardSizeChanged;
        UpdateScaleCenter();
        Unloaded += (_, _) => GameTableControl.CardSizeChanged -= OnCardSizeChanged;
    }

    private void OnCardSizeChanged()
    {
        UpdateScaleCenter();
    }

    private void UpdateScaleCenter()
    {
        CardScale.CenterX = GameTableControl.CardWidth / 2;
        CardScale.CenterY = GameTableControl.CardHeight / 2;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is CardViewModel oldVm)
            oldVm.PropertyChanged -= OnCardPropertyChanged;
        if (e.NewValue is CardViewModel newVm)
        {
            newVm.PropertyChanged += OnCardPropertyChanged;
            UpdateVisualState(newVm);
        }
    }

    private void OnCardPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not CardViewModel vm) return;

        switch (e.PropertyName)
        {
            case nameof(CardViewModel.IsSelected):
                UpdateSelectionState(vm);
                break;
            case nameof(CardViewModel.IsFaceUp):
                UpdateFaceState(vm);
                break;
            case nameof(CardViewModel.AnimationState):
                OnAnimationStateChanged(vm);
                break;
        }
    }

    private void UpdateVisualState(CardViewModel vm)
    {
        UpdateSelectionState(vm);
        UpdateFaceState(vm);
    }

    private void UpdateSelectionState(CardViewModel vm)
    {
        if (vm.IsSelected)
        {
            CardTransform.Y = -GameTableControl.SelectionLift;
            SelectionBorder.Visibility = Visibility.Visible;
            CardBorder.BorderBrush = System.Windows.Media.Brushes.Gold;
        }
        else
        {
            CardTransform.Y = 0;
            SelectionBorder.Visibility = Visibility.Collapsed;
            CardBorder.BorderBrush = System.Windows.Media.Brushes.Gray;
        }
    }

    private void UpdateFaceState(CardViewModel vm)
    {
        // 翻牌动画期间不直接切换，由动画系统控制
        if (vm.AnimationState == CardAnimation.Revealing)
            return;

        if (vm.IsFaceUp)
        {
            FaceImage.Visibility = Visibility.Visible;
            FaceImage.Opacity = 1;
            CardBackImage.Visibility = Visibility.Collapsed;
            CardBackImage.Opacity = 0;
        }
        else
        {
            FaceImage.Visibility = Visibility.Collapsed;
            FaceImage.Opacity = 0;
            CardBackImage.Visibility = Visibility.Visible;
            CardBackImage.Opacity = 1;
        }
    }

    private void OnCardClicked(object sender, MouseButtonEventArgs e)
    {
        // 拖拽选中由 GameTableControl 统一处理，此处跳过
        if (GameTableControl.IsDragging) return;

        if (DataContext is CardViewModel cardVm && cardVm.IsPlayable)
            cardVm.IsSelected = !cardVm.IsSelected;
    }

    // ========== 动画系统 ==========

    private void OnAnimationStateChanged(CardViewModel vm)
    {
        switch (vm.AnimationState)
        {
            case CardAnimation.Gathering:
                PlayGatherAnimation(vm);
                break;
            case CardAnimation.Revealing:
                PlayRevealAnimation(vm);
                break;
            case CardAnimation.Revealed:
                PlaySpreadAnimation(vm);
                break;
            case CardAnimation.Idle:
                break;
        }
    }

    /// <summary>
    /// 聚拢：TranslateX 从0移到 GatherOffset
    /// </summary>
    private void PlayGatherAnimation(CardViewModel vm)
    {
        var anim = new DoubleAnimation
        {
            From = 0,
            To = vm.GatherOffset,
            Duration = TimeSpan.FromSeconds(0.3),
            EasingFunction = GatherEasing,
            FillBehavior = FillBehavior.HoldEnd
        };
        CardTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    /// <summary>
    /// 翻牌：ScaleX 1→0→1 + 背面→正面淡变
    /// </summary>
    private void PlayRevealAnimation(CardViewModel vm)
    {
        var storyboard = new Storyboard();

        // ScaleX: 1 → 0 → 1（翻牌压缩再展开）
        var scaleXAnim = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(0.2),
            FillBehavior = FillBehavior.HoldEnd
        };
        scaleXAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0)));
        scaleXAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0.5)));
        scaleXAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(1)));
        Storyboard.SetTarget(scaleXAnim, CardScale);
        Storyboard.SetTargetProperty(scaleXAnim, new PropertyPath(ScaleTransform.ScaleXProperty));
        storyboard.Children.Add(scaleXAnim);

        // 背面淡出（中点开始）
        var backOpacityAnim = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(0.2),
            FillBehavior = FillBehavior.HoldEnd
        };
        backOpacityAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0)));
        backOpacityAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.49)));
        backOpacityAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0.51)));
        Storyboard.SetTarget(backOpacityAnim, CardBackImage);
        Storyboard.SetTargetProperty(backOpacityAnim, new PropertyPath(Image.OpacityProperty));
        storyboard.Children.Add(backOpacityAnim);

        // 正面淡入（中点开始）
        FaceImage.Visibility = Visibility.Visible;
        var faceOpacityAnim = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(0.2),
            FillBehavior = FillBehavior.HoldEnd
        };
        faceOpacityAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        faceOpacityAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0.49)));
        faceOpacityAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.51)));
        Storyboard.SetTarget(faceOpacityAnim, FaceImage);
        Storyboard.SetTargetProperty(faceOpacityAnim, new PropertyPath(Image.OpacityProperty));
        storyboard.Children.Add(faceOpacityAnim);

        storyboard.Completed += (s, e) =>
        {
            // 清除动画的 HoldEnd，恢复属性为本地值控制
            CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            CardBackImage.BeginAnimation(Image.OpacityProperty, null);
            FaceImage.BeginAnimation(Image.OpacityProperty, null);

            // 翻牌完成：确保最终状态正确
            FaceImage.Visibility = Visibility.Visible;
            FaceImage.Opacity = 1;
            CardBackImage.Visibility = Visibility.Collapsed;
            CardBackImage.Opacity = 0;

            // 触发铺开动画
            vm.AnimationState = CardAnimation.Revealed;
        };

        storyboard.Begin();
    }

    /// <summary>
    /// 铺开：TranslateX 弹回0（弹性缓动），完成后重置动画状态
    /// </summary>
    private void PlaySpreadAnimation(CardViewModel vm)
    {
        var anim = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromSeconds(0.3),
            EasingFunction = SpreadEasing,
            FillBehavior = FillBehavior.HoldEnd
        };
        anim.Completed += (s, e) =>
        {
            // 清除动画的 HoldEnd，恢复为本地值
            CardTransform.BeginAnimation(TranslateTransform.XProperty, null);
            // 重置为 Idle，确保后续 UpdateFaceState 不被 guard 拦截
            vm.AnimationState = CardAnimation.Idle;
        };
        CardTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }
}
