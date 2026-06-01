// -----------------------------------------------------------------------
// AntiCheatTests.cs - 反作弊测试（防护验证 + 漏洞暴露/金丝雀测试）
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Xunit;
using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Game;
using LolitaPoker.Core.Models;
using LolitaPoker.Core.AI;

namespace LolitaPoker.Tests;

public class AntiCheatTests
{
    // ========== 辅助方法 ==========

    /// <summary>
    /// 创建 GameManager，开始快速模式，返回地主座位。
    /// </summary>
    private static (GameManager gm, int landlord) StartQuickGame()
    {
        var gm = new GameManager();
        gm.StartNewGameQuick();
        return (gm, gm.LandlordIndex!.Value);
    }

    /// <summary>
    /// 让当前回合玩家出指定的牌列表，然后推进到下一个指定座位。
    /// </summary>
    private static void PlayCards(GameManager gm, List<Card> cards)
    {
        gm.SubmitPlay(gm.CurrentPlayerIndex, cards);
    }

    // ========== A. 现有防护验证（应通过） ==========

    [Fact]
    [Trait("Category", "AntiCheat")]
    public void SubmitPlay_RejectsOutOfTurn()
    {
        var (gm, landlord) = StartQuickGame();
        Assert.Equal(GamePhase.Playing, gm.Phase);

        int wrongPlayer = (gm.CurrentPlayerIndex + 1) % 3;
        var hand = gm.GetPlayerHand(wrongPlayer).ToList();
        if (hand.Count == 0) return;

        int cardsBefore = hand.Count;
        gm.SubmitPlay(wrongPlayer, new List<Card> { hand[^1] });

        // 牌数不应改变——出牌被忽略
        Assert.Equal(cardsBefore, gm.GetPlayerHand(wrongPlayer).Count);
    }

    [Fact]
    [Trait("Category", "AntiCheat")]
    public void SubmitPlay_RejectsInvalidCombo()
    {
        var (gm, landlord) = StartQuickGame();
        int cp = gm.CurrentPlayerIndex;
        var hand = gm.GetPlayerHand(cp).ToList();
        if (hand.Count < 3) return;

        // 尝试出 3 张不构成合法牌型的牌（不同花色、不连续的 3 张）
        var invalidCards = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Seven),
            new Card(Suit.Spades, Rank.Ace),
        };

        // 确保玩家手中有这些牌（或至少验证非法牌型被拒绝）
        var combo = RulesEngine.ClassifyPlay(invalidCards);
        if (combo.IsValid) return; // 如果恰好是合法牌型则跳过

        int cardsBefore = gm.GetPlayerHand(cp).Count;
        gm.SubmitPlay(cp, invalidCards);
        Assert.Equal(cardsBefore, gm.GetPlayerHand(cp).Count);
    }

    [Fact]
    [Trait("Category", "AntiCheat")]
    public void SubmitPlay_RejectsWhenCannotBeat()
    {
        var (gm, landlord) = StartQuickGame();

        // 第一个玩家出一张牌
        int first = gm.CurrentPlayerIndex;
        var firstHand = gm.GetPlayerHand(first).ToList();
        if (firstHand.Count == 0) return;
        var firstCard = new List<Card> { firstHand[0] };
        gm.SubmitPlay(first, firstCard);

        // 下一个玩家尝试出一张更小的牌
        int second = gm.CurrentPlayerIndex;
        if (second == first) return; // 不应发生
        var secondHand = gm.GetPlayerHand(second).ToList();
        if (secondHand.Count == 0) return;

        // 找一张比 firstCard 小的牌
        var smallerCards = secondHand.Where(c => c.Strength < firstHand[0].Strength).ToList();
        if (smallerCards.Count == 0) return; // 没有更小的牌

        int cardsBefore = gm.GetPlayerHand(second).Count;
        gm.SubmitPlay(second, new List<Card> { smallerCards[0] });
        // 应被拒绝——无法压过
        Assert.Equal(cardsBefore, gm.GetPlayerHand(second).Count);
    }

    [Fact]
    [Trait("Category", "AntiCheat")]
    public void SubmitPass_RejectedWhenLeading()
    {
        var (gm, landlord) = StartQuickGame();
        int cp = gm.CurrentPlayerIndex;

        // 首次出牌阶段，不能跳过
        int cardsBefore = gm.GetPlayerHand(cp).Count;
        gm.SubmitPass(cp);

        // 阶段不应改变，仍然是当前玩家
        Assert.Equal(GamePhase.Playing, gm.Phase);
        Assert.Equal(cp, gm.CurrentPlayerIndex);
    }

    [Fact]
    [Trait("Category", "AntiCheat")]
    public void SubmitPass_RejectedOutOfTurn()
    {
        var (gm, landlord) = StartQuickGame();
        int wrongPlayer = (gm.CurrentPlayerIndex + 1) % 3;

        gm.SubmitPass(wrongPlayer);

        // 当前回合不应改变
        Assert.NotEqual(wrongPlayer, gm.CurrentPlayerIndex);
    }

    [Fact]
    [Trait("Category", "AntiCheat")]
    public void SubmitBid_RejectedOutOfTurn()
    {
        var gm = new GameManager();
        gm.StartNewGame();
        Assert.Equal(GamePhase.Bidding, gm.Phase);

        int wrongPlayer = (gm.CurrentPlayerIndex + 1) % 3;
        int bidBefore = gm.CurrentBid;
        gm.SubmitBid(wrongPlayer, 3);

        // 叫分不应改变
        Assert.Equal(bidBefore, gm.CurrentBid);
    }

    [Fact]
    [Trait("Category", "AntiCheat")]
    public void SubmitBid_RejectedInWrongPhase()
    {
        var (gm, landlord) = StartQuickGame();
        Assert.Equal(GamePhase.Playing, gm.Phase);

        int bidBefore = gm.CurrentBid;
        gm.SubmitBid(gm.CurrentPlayerIndex, 3);

        // 在出牌阶段叫分应被忽略
        Assert.Equal(bidBefore, gm.CurrentBid);
    }

    // ========== B. 漏洞验证/金丝雀测试 ==========

    [Fact]
    [Trait("Category", "AntiCheat")]
    public void SubmitPlay_DuplicateCardsBombExploit_Rejected()
    {
        // 修复验证：重复牌伪装炸弹现在应被拒绝
        var (gm, landlord) = StartQuickGame();
        int cp = gm.CurrentPlayerIndex;
        var hand = gm.GetPlayerHand(cp).ToList();
        if (hand.Count == 0) return;

        var realCard = hand[^1]; // 取最小的一张
        int cardsBefore = hand.Count;

        // 提交同一张牌 4 次——伪装成炸弹，应被拒绝
        gm.SubmitPlay(cp, new List<Card> { realCard, realCard, realCard, realCard });

        // 修复后：出牌被拒绝，手牌数量不变
        Assert.Equal(cardsBefore, gm.GetPlayerHand(cp).Count);
        Assert.Equal(cp, gm.CurrentPlayerIndex); // 回合不变
    }

    [Fact]
    [Trait("Category", "AntiCheat")]
    public void SubmitPlay_CardsNotInHand_Rejected()
    {
        // 修复验证：提交手中没有的牌应被拒绝
        var (gm, landlord) = StartQuickGame();
        int cp = gm.CurrentPlayerIndex;
        var hand = gm.GetPlayerHand(cp).ToList();
        if (hand.Count == 0) return;

        // 构造一张玩家手中肯定没有的牌
        var allCards = CardHelper.CreateFullDeck();
        var handSet = new HashSet<(Suit, Rank)>(hand.Select(c => (c.Suit, c.Rank)));
        var notInHand = allCards.First(c => !handSet.Contains((c.Suit, c.Rank)));

        int cardsBefore = hand.Count;
        int playerBefore = gm.CurrentPlayerIndex;

        // 提交不在手中的牌——应被拒绝
        gm.SubmitPlay(cp, new List<Card> { notInHand });

        // 修复后：出牌被拒绝，手牌数量和回合不变
        Assert.Equal(cardsBefore, gm.GetPlayerHand(cp).Count);
        Assert.Equal(playerBefore, gm.CurrentPlayerIndex);
    }

    [Fact]
    [Trait("Category", "AntiCheat")]
    public void SubmitBid_HugeValue_Rejected()
    {
        // 修复验证：叫分超过 3 应被拒绝
        var gm = new GameManager();
        gm.StartNewGame();
        Assert.Equal(GamePhase.Bidding, gm.Phase);

        int cp = gm.CurrentPlayerIndex;
        gm.SubmitBid(cp, 999999);

        // 修复后：叫分被拒绝，仍在叫分阶段
        Assert.Equal(GamePhase.Bidding, gm.Phase);
        Assert.Null(gm.LandlordIndex);
        Assert.Equal(1, gm.Multiplier); // 乘数未被污染
    }

    [Fact]
    [Trait("Category", "AntiCheat")]
    public void SubmitBid_ZeroAmount_CountsAsPass()
    {
        var gm = new GameManager();
        gm.StartNewGame();
        Assert.Equal(GamePhase.Bidding, gm.Phase);

        int cp = gm.CurrentPlayerIndex;
        gm.SubmitBid(cp, 0);

        // 叫 0 分应视为不叫，轮到下一个玩家
        Assert.NotEqual(cp, gm.CurrentPlayerIndex);
    }

    [Fact]
    [Trait("Category", "AntiCheat")]
    public void SubmitBid_NegativeAmount_Rejected()
    {
        // 修复验证：负数叫分应被拒绝
        var gm = new GameManager();
        gm.StartNewGame();
        Assert.Equal(GamePhase.Bidding, gm.Phase);

        int cp = gm.CurrentPlayerIndex;
        gm.SubmitBid(cp, -5);

        // 修复后：叫分被拒绝，仍在叫分阶段
        Assert.Equal(GamePhase.Bidding, gm.Phase);
        Assert.Equal(0, gm.CurrentBid);
    }

    [Fact]
    [Trait("Category", "AntiCheat")]
    public void SubmitBid_ValidAmount3_AssignsLandlord()
    {
        var gm = new GameManager();
        gm.StartNewGame();
        Assert.Equal(GamePhase.Bidding, gm.Phase);

        int cp = gm.CurrentPlayerIndex;
        gm.SubmitBid(cp, 3);

        // 叫 3 分应立即分配地主
        Assert.Equal(GamePhase.Playing, gm.Phase);
        Assert.NotNull(gm.LandlordIndex);
        Assert.Equal(3, gm.Multiplier);
    }

    // ========== C. 边界/鲁棒性测试 ==========

    [Fact]
    [Trait("Category", "AntiCheat")]
    public void SubmitPlay_EmptyCardList_StaysInPlayingPhase()
    {
        var (gm, landlord) = StartQuickGame();
        int cp = gm.CurrentPlayerIndex;

        gm.SubmitPlay(cp, new List<Card>());

        // 空牌列表应被 ClassifyPlay 返回 Invalid，出牌被忽略
        Assert.Equal(GamePhase.Playing, gm.Phase);
        Assert.Equal(cp, gm.CurrentPlayerIndex);
    }

    [Fact]
    [Trait("Category", "AntiCheat")]
    public void SubmitPlay_SameCardTwice_Rejected()
    {
        // 修复验证：同一张牌提交两次应被拒绝（重复牌检测）
        var (gm, landlord) = StartQuickGame();
        int cp = gm.CurrentPlayerIndex;
        var hand = gm.GetPlayerHand(cp).ToList();
        if (hand.Count == 0) return;

        var card = hand[^1];
        int cardsBefore = hand.Count;

        // 提交两张相同的牌——应被拒绝
        gm.SubmitPlay(cp, new List<Card> { card, card });

        // 修复后：手牌数量不变
        Assert.Equal(cardsBefore, gm.GetPlayerHand(cp).Count);
    }

    [Fact]
    [Trait("Category", "AntiCheat")]
    public void CanBeat_Rocket_BeatsAnything()
    {
        // 火箭（大小王）压一切
        var rocket = new CardCombo(CardComboType.Rocket, Rank.SmallJoker, 1,
            new List<Card>
            {
                new Card(Suit.None, Rank.SmallJoker),
                new Card(Suit.None, Rank.BigJoker),
            });

        var bomb = RulesEngine.ClassifyPlay(new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Two),
            new Card(Suit.Hearts, Rank.Two),
            new Card(Suit.Spades, Rank.Two),
            new Card(Suit.Clubs, Rank.Two),
        });

        Assert.True(RulesEngine.CanBeat(rocket, bomb), "火箭应能压过炸弹");
        Assert.False(RulesEngine.CanBeat(bomb, rocket), "炸弹不应能压过火箭");
    }

    [Fact]
    [Trait("Category", "AntiCheat")]
    public void CanBeat_HigherBomb_BeatsLowerBomb()
    {
        var bomb5 = RulesEngine.ClassifyPlay(new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Five),
            new Card(Suit.Hearts, Rank.Five),
            new Card(Suit.Spades, Rank.Five),
            new Card(Suit.Clubs, Rank.Five),
        });

        var bomb6 = RulesEngine.ClassifyPlay(new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Six),
            new Card(Suit.Hearts, Rank.Six),
            new Card(Suit.Spades, Rank.Six),
            new Card(Suit.Clubs, Rank.Six),
        });

        Assert.True(RulesEngine.CanBeat(bomb6, bomb5), "6 炸弹应能压过 5 炸弹");
        Assert.False(RulesEngine.CanBeat(bomb5, bomb6), "5 炸弹不应能压过 6 炸弹");
    }

    [Fact]
    [Trait("Category", "AntiCheat")]
    public void GameManager_ConsecutivePasses_ClearTable()
    {
        var (gm, landlord) = StartQuickGame();

        // 第一个玩家出一张牌
        int first = gm.CurrentPlayerIndex;
        var hand = gm.GetPlayerHand(first).ToList();
        if (hand.Count == 0) return;
        gm.SubmitPlay(first, new List<Card> { hand[0] });

        // 后续两个玩家都跳过（需要两次连续跳过才能清桌）
        int second = gm.CurrentPlayerIndex;
        gm.SubmitPass(second);

        int third = gm.CurrentPlayerIndex;
        gm.SubmitPass(third);

        // 两次跳过后，回合应清空（_lastPlayedCombo = null），回到出牌的玩家
        int freePlayer = gm.CurrentPlayerIndex;
        var freeHand = gm.GetPlayerHand(freePlayer).ToList();
        if (freeHand.Count == 0) return;

        // 自由出牌——出最小的一张
        var smallest = freeHand[^1];
        int cardsBefore = freeHand.Count;
        gm.SubmitPlay(freePlayer, new List<Card> { smallest });

        // 应该成功出牌（自由出牌阶段）
        Assert.True(gm.GetPlayerHand(freePlayer).Count < cardsBefore,
            "连续两次跳过后应清空牌桌，当前玩家可自由出牌");
    }

    [Fact]
    [Trait("Category", "AntiCheat")]
    public void GameManager_BombDoublesMultiplier()
    {
        var (gm, landlord) = StartQuickGame();
        Assert.Equal(1, gm.Multiplier);

        int cp = gm.CurrentPlayerIndex;
        var hand = gm.GetPlayerHand(cp).ToList();

        // 找一个炸弹（4 张相同点数）
        var bombGroup = hand.GroupBy(c => c.Rank)
            .FirstOrDefault(g => g.Count() >= 4);
        if (bombGroup == null) return; // 手中没有炸弹

        var bombCards = bombGroup.Take(4).ToList();
        gm.SubmitPlay(cp, bombCards);

        Assert.Equal(2, gm.Multiplier);
    }

    [Fact]
    [Trait("Category", "AntiCheat")]
    public void GameManager_WrongPhase_SubmitPlay_Ignored()
    {
        var gm = new GameManager();
        // 未开局，Phase = Idle
        Assert.Equal(GamePhase.Idle, gm.Phase);

        // 在 Idle 阶段出牌应被忽略
        gm.SubmitPlay(0, new List<Card> { new Card(Suit.Diamonds, Rank.Three) });
        Assert.Equal(GamePhase.Idle, gm.Phase);
    }

    [Fact]
    [Trait("Category", "AntiCheat")]
    public void ClassifyPlay_Empty_ReturnsInvalid()
    {
        var result = RulesEngine.ClassifyPlay(new List<Card>());
        Assert.False(result.IsValid);
    }

    [Fact]
    [Trait("Category", "AntiCheat")]
    public void ClassifyPlay_SingleCard_ReturnsSingle()
    {
        var result = RulesEngine.ClassifyPlay(new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Ace)
        });
        Assert.True(result.IsValid);
        Assert.Equal(CardComboType.Single, result.Type);
    }
}
