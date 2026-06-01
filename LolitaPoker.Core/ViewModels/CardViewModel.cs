// -----------------------------------------------------------------------
// CardViewModel.cs - 单张扑克牌视图模型
// -----------------------------------------------------------------------

using System.Windows.Media.Imaging;
using LolitaPoker.Core.Assets;
using LolitaPoker.Core.Models;

namespace LolitaPoker.Core.ViewModels;

/// <summary>
/// 卡牌动画状态
/// </summary>
public enum CardAnimation
{
    Idle,       // 无动画
    Gathering,  // 聚拢
    Revealing,  // 翻牌
    Revealed    // 铺开
}

/// <summary>
/// 单张扑克牌的视图模型
/// </summary>
public class CardViewModel : ViewModelBase
{
    private bool _isSelected;
    private bool _isFaceUp;
    private bool _isPlayable = true;
    private CardAnimation _animationState = CardAnimation.Idle;
    private double _gatherOffset;

    /// <summary>
    /// 选中状态变化事件（静态，供外部监听）
    /// </summary>
    public static event Action<CardViewModel, bool>? SelectionStateChanged;

    public CardViewModel(Card card, bool faceUp)
    {
        Model = card;
        _isFaceUp = faceUp;
        DisplayName = card.DisplayName;
    }

    /// <summary>底层牌模型</summary>
    public Card Model { get; }

    /// <summary>显示名称</summary>
    public string DisplayName { get; }

    /// <summary>图片源（正面牌的图片）</summary>
    public BitmapImage? ImageSource => CardImageProvider.GetCardImage(Model);

    /// <summary>是否被选中（用于出牌选择）</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                // 触发选中状态变化事件
                SelectionStateChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>是否显示正面</summary>
    public bool IsFaceUp
    {
        get => _isFaceUp;
        set => SetProperty(ref _isFaceUp, value);
    }

    /// <summary>是否可以操作</summary>
    public bool IsPlayable
    {
        get => _isPlayable;
        set => SetProperty(ref _isPlayable, value);
    }

    /// <summary>当前动画状态（触发 DataTrigger 动画）</summary>
    public CardAnimation AnimationState
    {
        get => _animationState;
        set => SetProperty(ref _animationState, value);
    }

    /// <summary>聚拢偏移量（像素，负值向左）</summary>
    public double GatherOffset
    {
        get => _gatherOffset;
        set => SetProperty(ref _gatherOffset, value);
    }
}
