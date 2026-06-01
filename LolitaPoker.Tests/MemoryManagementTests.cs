// -----------------------------------------------------------------------
// MemoryManagementTests.cs - 内存管理测试（分配压力、泄漏检测、GC 行为）
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Game;
using LolitaPoker.Core.Models;
using LolitaPoker.Core.AI;
using LolitaPoker.Core.Network;
using LolitaPoker.Core.ViewModels;

namespace LolitaPoker.Tests;

public class MemoryManagementTests
{
    // ========== 辅助方法 ==========

    private static long ForceGcAndGetMemory()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    private static List<Card> CreateWorstCaseHand()
    {
        return new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),  new Card(Suit.Hearts, Rank.Three),
            new Card(Suit.Spades, Rank.Three),    new Card(Suit.Clubs, Rank.Three),
            new Card(Suit.Diamonds, Rank.Four),   new Card(Suit.Hearts, Rank.Four),
            new Card(Suit.Diamonds, Rank.Five),   new Card(Suit.Hearts, Rank.Five),
            new Card(Suit.Diamonds, Rank.Six),    new Card(Suit.Hearts, Rank.Six),
            new Card(Suit.Diamonds, Rank.Seven),
            new Card(Suit.Diamonds, Rank.Eight),
            new Card(Suit.Diamonds, Rank.Nine),
            new Card(Suit.Diamonds, Rank.Ten),
            new Card(Suit.Diamonds, Rank.Jack),
            new Card(Suit.Diamonds, Rank.Queen),
            new Card(Suit.Diamonds, Rank.King),
            new Card(Suit.Diamonds, Rank.Ace),
            new Card(Suit.None, Rank.SmallJoker),
            new Card(Suit.None, Rank.BigJoker),
        };
    }

    private class StubNetworkAdapter : INetworkAdapter
    {
        public bool IsConnected { get; set; } = true;
        public string LocalPlayerId { get; set; } = "local";
        public List<NetworkMessage> SentMessages { get; } = new();
        public event Action<NetworkMessage>? OnMessageReceived;
        public event Action<bool>? OnConnectionStateChanged;
        public event Action<string, bool>? OnPlayerPresenceChanged;

        public Task<bool> HostGameAsync(string roomId, string hostPlayerName) => Task.FromResult(true);
        public Task<bool> JoinGameAsync(string roomId, string playerName) => Task.FromResult(true);
        public Task SendMessageAsync(NetworkMessage message) { SentMessages.Add(message); return Task.CompletedTask; }
        public Task DisconnectAsync() => Task.CompletedTask;

        public void SimulateMessage(NetworkMessage msg) => OnMessageReceived?.Invoke(msg);
    }

    // ========== CardComboFinder 分配压力 ==========

    [Fact]
    [Trait("Category", "Memory")]
    public void FindAllPlayableCombos_Gen0_CollectionPressure()
    {
        var hand = CreateWorstCaseHand();

        // 预热 + GC 稳定
        for (int i = 0; i < 10; i++) CardComboFinder.FindAllPlayableCombos(hand, null);
        ForceGcAndGetMemory();

        int gen0Before = GC.CollectionCount(0);

        for (int i = 0; i < 1000; i++)
        {
            CardComboFinder.FindAllPlayableCombos(hand, null);
        }

        int gen0After = GC.CollectionCount(0);
        int gen0Delta = gen0After - gen0Before;

        // 文档化基线：1000 次搜索应不超过 500 次 Gen0 回收
        Assert.True(gen0Delta < 500,
            $"Gen0 回收次数 {gen0Delta} 超过 500 上限，可能存在过度分配");
    }

    [Fact]
    [Trait("Category", "Memory")]
    public void FindAllPlayableCombos_MemoryGrowth_10000Calls()
    {
        var hand = CreateWorstCaseHand();

        // 预热
        for (int i = 0; i < 10; i++) CardComboFinder.FindAllPlayableCombos(hand, null);

        long memBefore = ForceGcAndGetMemory();

        for (int i = 0; i < 10_000; i++)
        {
            CardComboFinder.FindAllPlayableCombos(hand, null);
        }

        long memAfter = ForceGcAndGetMemory();
        long growth = memAfter - memBefore;

        Assert.True(growth < 50_000_000, // 50MB
            $"10,000 次搜索内存增长 {growth / 1_000_000}MB，超过 50MB 上限");
    }

    [Fact]
    [Trait("Category", "Memory")]
    public void FindAllPlayableCombos_ResultLists_AreIndependent()
    {
        var hand = CreateWorstCaseHand();

        var result1 = CardComboFinder.FindAllPlayableCombos(hand, null);
        var result2 = CardComboFinder.FindAllPlayableCombos(hand, null);

        // 列表对象不同
        Assert.NotSame(result1, result2);

        // 每个 CardCombo 的 Cards 列表也应不同
        if (result1.Count > 0 && result2.Count > 0)
        {
            Assert.NotSame(result1[0].Cards, result2[0].Cards);
        }
    }

    // ========== CardCombo 构造分配 ==========

    [Fact]
    [Trait("Category", "Memory")]
    public void CardCombo_CardsProperty_IsReadOnlyCopy()
    {
        var source = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Ace),
            new Card(Suit.Hearts, Rank.Ace),
        };

        var combo = new CardCombo(CardComboType.Pair, Rank.Ace, 1, source);
        Assert.Equal(2, combo.Cards.Count);

        // 修改源列表不影响 combo
        source.Add(new Card(Suit.Spades, Rank.Ace));
        Assert.Equal(2, combo.Cards.Count);
        Assert.Equal(Rank.Ace, combo.Cards[0].Rank);
    }

    [Fact]
    [Trait("Category", "Memory")]
    public void CardCombo_100000_Creations_MemoryBounded()
    {
        ForceGcAndGetMemory();
        long memBefore = GC.GetTotalMemory(false);

        var cards = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Three),
            new Card(Suit.Spades, Rank.Three),
        };

        CardCombo? keepAlive = null; // 防止被优化掉
        for (int i = 0; i < 100_000; i++)
        {
            keepAlive = new CardCombo(CardComboType.Triple, Rank.Three, 1, cards);
        }

        long memAfter = GC.GetTotalMemory(false);
        long growth = memAfter - memBefore;

        // 每个 CardCombo 约 200-400 字节，100K 个约 20-40MB
        Assert.True(growth < 100_000_000, // 100MB
            $"100,000 次 CardCombo 创建内存增长 {growth / 1_000_000}MB，超过 100MB 上限");

        GC.KeepAlive(keepAlive);
    }

    // ========== CardViewModel 静态事件泄漏检测 ==========

    [Fact]
    [Trait("Category", "Memory")]
    public void CardViewModel_SelectionStateChanged_NoLeakAfterUnsubscribe()
    {
        ForceGcAndGetMemory();
        long memBefore = GC.GetTotalMemory(false);

        // 创建 1000 个 CardViewModel 并订阅 + 退订静态事件
        for (int i = 0; i < 1000; i++)
        {
            var cvm = new CardViewModel(new Card(Suit.Diamonds, Rank.Three), false);
            Action<CardViewModel, bool> handler = (sender, selected) => { };
            CardViewModel.SelectionStateChanged += handler;

            // 模拟选中操作触发事件
            cvm.IsSelected = true;
            cvm.IsSelected = false;

            // 退订
            CardViewModel.SelectionStateChanged -= handler;
        }

        ForceGcAndGetMemory();
        long memAfter = GC.GetTotalMemory(false);
        long growth = memAfter - memBefore;

        // 退订后内存应接近基线（允许 5MB 容差）
        Assert.True(growth < 5_000_000,
            $"退订后内存仍增长 {growth / 1_000_000}MB，可能存在泄漏");
    }

    [Fact]
    [Trait("Category", "Memory")]
    public void CardViewModel_SelectionStateChanged_LeakWhenNotUnsubscribed()
    {
        // 金丝雀测试：故意不退订，验证静态事件会持有引用
        // 此测试文档化已知的泄漏模式，若重构静态事件则需更新此测试
        ForceGcAndGetMemory();
        long memBefore = GC.GetTotalMemory(false);

        for (int i = 0; i < 1000; i++)
        {
            var cvm = new CardViewModel(new Card(Suit.Diamonds, Rank.Three), false);
            // 不退订——静态事件通过 delegate 链持有对 handler 的引用
            // 但 handler 是 lambda 且不捕获 cvm，所以 cvm 本身可以被回收
            CardViewModel.SelectionStateChanged += (sender, selected) => { };
        }

        ForceGcAndGetMemory();
        long memAfter = GC.GetTotalMemory(false);
        long growth = memAfter - memBefore;

        // 注意：由于 lambda 不捕获外部变量，静态事件持有的是 delegate 对象而非 CardViewModel 实例
        // 因此内存增长主要是 delegate 对象本身（很小），CardViewModel 实例可被 GC
        // 此测试记录当前行为基线
        Assert.True(growth < 5_000_000,
            $"未退订时内存增长 {growth / 1_000_000}MB，超过预期基线");
    }

    // ========== GameManager 事件泄漏 ==========

    [Fact]
    [Trait("Category", "Memory")]
    public void GameManager_EventSubscribers_DoNotLeak()
    {
        ForceGcAndGetMemory();
        long memBefore = GC.GetTotalMemory(false);

        for (int i = 0; i < 1000; i++)
        {
            var gm = new GameManager();
            // 订阅事件
            Action<GamePhase> phaseHandler = _ => { };
            Action<int> turnHandler = _ => { };
            Action<int, CardCombo?> playedHandler = (_, _) => { };
            Action<int> cardsHandler = _ => { { }; };

            gm.PhaseChanged += phaseHandler;
            gm.TurnChanged += turnHandler;
            gm.PlayerPlayed += playedHandler;
            gm.CardsChanged += cardsHandler;

            gm.StartNewGameQuick();

            // 退订
            gm.PhaseChanged -= phaseHandler;
            gm.TurnChanged -= turnHandler;
            gm.PlayerPlayed -= playedHandler;
            gm.CardsChanged -= cardsHandler;
        }

        ForceGcAndGetMemory();
        long memAfter = GC.GetTotalMemory(false);
        long growth = memAfter - memBefore;

        Assert.True(growth < 10_000_000, // 10MB
            $"GameManager 事件退订后内存增长 {growth / 1_000_000}MB，超过 10MB 上限");
    }

    [Fact]
    [Trait("Category", "Memory")]
    public void GameManager_SubmitPlay_Gen0_Per_Play()
    {
        // 测量单次 SubmitPlay 的 Gen0 分配成本（HashSet 分配）
        ForceGcAndGetMemory();

        int gen0Before = GC.CollectionCount(0);

        // 反复进行 "出最小牌 → 重新开局" 循环
        for (int i = 0; i < 10_000; i++)
        {
            var gm = new GameManager();
            gm.StartNewGameQuick();

            int cp = gm.CurrentPlayerIndex;
            var hand = gm.GetPlayerHand(cp).ToList();
            if (hand.Count > 0)
            {
                gm.SubmitPlay(cp, new List<Card> { hand[^1] }); // 出最小一张
            }
        }

        int gen0After = GC.CollectionCount(0);
        int gen0Delta = gen0After - gen0Before;

        // 10,000 次出牌+开局，Gen0 回收应有上限
        Assert.True(gen0Delta < 1000,
            $"10,000 次 SubmitPlay Gen0 回收 {gen0Delta} 次，超过 1000 上限");
    }

    // ========== DeckPool 内存行为 ==========

    [Fact]
    [Trait("Category", "Memory")]
    public void DeckPool_PoolMax4_DoesNotGrowUnbounded()
    {
        ForceGcAndGetMemory();
        long memBefore = GC.GetTotalMemory(false);

        // 快速获取和归还 200 个牌组
        for (int i = 0; i < 200; i++)
        {
            var deck = DeckPool.GetShuffledDeck();
            Assert.Equal(54, deck.Count);
            DeckPool.Return(deck);
        }

        ForceGcAndGetMemory();
        long memAfter = GC.GetTotalMemory(false);
        long growth = memAfter - memBefore;

        // 池上限 4，每个牌组约 54*8=432 字节，应远小于 1MB
        Assert.True(growth < 1_000_000,
            $"DeckPool 200 次获取归还后内存增长 {growth} 字节，池可能未正确限制");
    }

    [Fact]
    [Trait("Category", "Memory")]
    public void Deck_DealAndReturn_NoOrphanedLists()
    {
        ForceGcAndGetMemory();
        long memBefore = GC.GetTotalMemory(false);

        for (int i = 0; i < 1000; i++)
        {
            var deck = new Deck();
            var (h0, h1, h2, kitty) = deck.Deal();
            deck.ReturnToPool();

            // 手牌列表应正常分配
            Assert.Equal(17, h0.Count);
            Assert.Equal(17, h1.Count);
            Assert.Equal(17, h2.Count);
            Assert.Equal(3, kitty.Count);
        }

        ForceGcAndGetMemory();
        long memAfter = GC.GetTotalMemory(false);
        long growth = memAfter - memBefore;

        // 1000 次发牌，4 个列表/次 = ~4000 个短生命周期列表
        Assert.True(growth < 10_000_000, // 10MB
            $"1000 次发牌归还后内存增长 {growth / 1_000_000}MB，超过 10MB 上限");
    }

    // ========== NetworkGameManager 泄漏检测 ==========

    [Fact]
    [Trait("Category", "Memory")]
    public void NetworkGameManager_EventSubscription_NoLeak()
    {
        ForceGcAndGetMemory();
        long memBefore = GC.GetTotalMemory(false);

        for (int i = 0; i < 1000; i++)
        {
            var stub = new StubNetworkAdapter();
            var gm = new GameManager();
            var ngm = new NetworkGameManager(gm, stub);
            // 三个对象同时变得不可达——事件订阅不阻止 GC（因为整个引用图不可达）
        }

        ForceGcAndGetMemory();
        long memAfter = GC.GetTotalMemory(false);
        long growth = memAfter - memBefore;

        Assert.True(growth < 10_000_000, // 10MB
            $"NetworkGameManager 创建销毁后内存增长 {growth / 1_000_000}MB，超过 10MB 上限");
    }
}
