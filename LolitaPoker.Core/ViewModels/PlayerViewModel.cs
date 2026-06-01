// -----------------------------------------------------------------------
// PlayerViewModel.cs - 玩家视图模型
// -----------------------------------------------------------------------

using System.Collections.ObjectModel;
using LolitaPoker.Core.Enums;

namespace LolitaPoker.Core.ViewModels;

/// <summary>
/// 玩家视图模型，管理手牌显示和状态
/// </summary>
public class PlayerViewModel : ViewModelBase
{
    private string _name = "";
    private bool _isCurrentTurn;
    private bool _isHuman;
    private PlayerRole _role = PlayerRole.Farmer;
    private string _lastAction = "";
    private bool _isThinking;
    private int _cardCount;

    /// <summary>座位索引 (0=下方玩家, 1=右边AI, 2=左边AI)</summary>
    public int SeatIndex { get; set; }

    /// <summary>玩家名称</summary>
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>手牌集合</summary>
    public ObservableCollection<CardViewModel> Hand { get; } = new();

    /// <summary>当前回合出的牌</summary>
    public ObservableCollection<CardViewModel> PlayedCards { get; } = new();

    /// <summary>是否是当前回合</summary>
    public bool IsCurrentTurn
    {
        get => _isCurrentTurn;
        set => SetProperty(ref _isCurrentTurn, value);
    }

    /// <summary>是否是人类玩家</summary>
    public bool IsHuman
    {
        get => _isHuman;
        set => SetProperty(ref _isHuman, value);
    }

    /// <summary>玩家角色 (地主/农民)</summary>
    public PlayerRole Role
    {
        get => _role;
        set
        {
            if (SetProperty(ref _role, value))
                OnPropertyChanged(nameof(RoleDisplay));
        }
    }

    /// <summary>角色显示文本</summary>
    public string RoleDisplay => _role == PlayerRole.Landlord ? "地主" : "农民";

    /// <summary>剩余牌数（AI玩家显示用）</summary>
    public int CardCount
    {
        get => _cardCount;
        set => SetProperty(ref _cardCount, value);
    }

    /// <summary>上次动作描述 ("不出" 或出牌类型)</summary>
    public string LastAction
    {
        get => _lastAction;
        set => SetProperty(ref _lastAction, value);
    }

    /// <summary>是否正在思考中 (AI用)</summary>
    public bool IsThinking
    {
        get => _isThinking;
        set => SetProperty(ref _isThinking, value);
    }
}
