// -----------------------------------------------------------------------
// CardSelectionCache.cs - 选牌缓存管理
// 管理玩家选中的牌，支持跨回合持久化和自动验证
// -----------------------------------------------------------------------

using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Models;

namespace LolitaPoker.Core.ViewModels;

/// <summary>
/// 选牌缓存管理器 - 持久保存玩家选中的牌
/// </summary>
public class CardSelectionCache
{
    private readonly HashSet<(Suit Suit, Rank Rank)> _selectedCards = new();

    /// <summary>
    /// 获取当前选中的牌列表
    /// </summary>
    public IReadOnlyCollection<(Suit Suit, Rank Rank)> SelectedCards => _selectedCards;

    /// <summary>
    /// 选中状态变化事件
    /// </summary>
    public event Action? SelectionChanged;

    /// <summary>
    /// 切换牌的选中状态
    /// </summary>
    public void ToggleSelection(Suit suit, Rank rank)
    {
        var key = (suit, rank);
        if (_selectedCards.Contains(key))
            _selectedCards.Remove(key);
        else
            _selectedCards.Add(key);

        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// 设置牌的选中状态
    /// </summary>
    public void SetSelection(Suit suit, Rank rank, bool isSelected)
    {
        var key = (suit, rank);
        if (isSelected)
            _selectedCards.Add(key);
        else
            _selectedCards.Remove(key);

        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// 检查牌是否被选中
    /// </summary>
    public bool IsSelected(Suit suit, Rank rank)
    {
        return _selectedCards.Contains((suit, rank));
    }

    /// <summary>
    /// 从手牌UI同步选中状态到缓存
    /// </summary>
    public void SyncFromHand(IEnumerable<CardViewModel> hand)
    {
        _selectedCards.Clear();
        foreach (var cardVm in hand)
        {
            if (cardVm.IsSelected)
            {
                _selectedCards.Add((cardVm.Model.Suit, cardVm.Model.Rank));
            }
        }
    }

    /// <summary>
    /// 将缓存的选中状态应用到手牌UI
    /// </summary>
    public void ApplyToHand(IEnumerable<CardViewModel> hand)
    {
        foreach (var cardVm in hand)
        {
            cardVm.IsSelected = _selectedCards.Contains((cardVm.Model.Suit, cardVm.Model.Rank));
        }
    }

    /// <summary>
    /// 获取选中的牌模型列表
    /// </summary>
    public List<Card> GetSelectedCardModels(IEnumerable<CardViewModel> hand)
    {
        return hand
            .Where(c => _selectedCards.Contains((c.Model.Suit, c.Model.Rank)))
            .Select(c => c.Model)
            .ToList();
    }

    /// <summary>
    /// 清除所有选中状态
    /// </summary>
    public void Clear()
    {
        _selectedCards.Clear();
        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// 获取选中牌的数量
    /// </summary>
    public int Count => _selectedCards.Count;
}
