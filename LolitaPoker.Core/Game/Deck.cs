// -----------------------------------------------------------------------
// Deck.cs - 牌组管理：洗牌、发牌（带缓存）
// -----------------------------------------------------------------------

using LolitaPoker.Core.Models;

namespace LolitaPoker.Core.Game;

/// <summary>
/// 牌组缓存池 - 预生成多副牌组，避免每局重新创建
/// </summary>
public static class DeckPool
{
    private static readonly List<Card> _baseCards = CardHelper.CreateFullDeck();
    private static readonly Queue<List<Card>> _pool = new();
    private static readonly object _lock = new();

    /// <summary>
    /// 从缓存池获取一副已洗好的牌（如果没有则新建）
    /// </summary>
    public static List<Card> GetShuffledDeck()
    {
        List<Card>? deck = null;

        lock (_lock)
        {
            if (_pool.Count > 0)
                deck = _pool.Dequeue();
        }

        if (deck == null)
        {
            deck = new List<Card>(_baseCards);
        }
        else
        {
            // 恢复到原始顺序再洗牌
            for (int i = 0; i < _baseCards.Count; i++)
                deck[i] = _baseCards[i];
        }

        // Fisher-Yates 洗牌
        var rng = Random.Shared;
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }

        return deck;
    }

    /// <summary>
    /// 归还牌组到缓存池
    /// </summary>
    public static void Return(List<Card> deck)
    {
        if (deck.Capacity >= 54)
        {
            lock (_lock)
            {
                if (_pool.Count < 4) // 最多缓存4副
                    _pool.Enqueue(deck);
            }
        }
    }
}

/// <summary>
/// 牌组管理，负责洗牌和发牌
/// </summary>
public class Deck
{
    private readonly List<Card> _cards;

    public Deck()
    {
        _cards = DeckPool.GetShuffledDeck();
    }

    /// <summary>
    /// 发牌：3个玩家各17张，3张底牌
    /// </summary>
    public (List<Card> hand0, List<Card> hand1, List<Card> hand2, List<Card> kitty) Deal()
    {
        var hand0 = new List<Card>(17);
        var hand1 = new List<Card>(17);
        var hand2 = new List<Card>(17);
        var kitty = new List<Card>(3);

        for (int i = 0; i < 51; i++)
        {
            switch (i % 3)
            {
                case 0: hand0.Add(_cards[i]); break;
                case 1: hand1.Add(_cards[i]); break;
                case 2: hand2.Add(_cards[i]); break;
            }
        }

        kitty.Add(_cards[51]);
        kitty.Add(_cards[52]);
        kitty.Add(_cards[53]);

        CardHelper.SortHand(hand0);
        CardHelper.SortHand(hand1);
        CardHelper.SortHand(hand2);
        CardHelper.SortHand(kitty);

        return (hand0, hand1, hand2, kitty);
    }

    /// <summary>使用完毕后归还牌组到缓存池</summary>
    public void ReturnToPool() => DeckPool.Return(_cards);
}
