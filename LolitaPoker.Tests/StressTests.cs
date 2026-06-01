// -----------------------------------------------------------------------
// StressTests.cs - 压力测试（高负载、大量迭代、边界极端场景）
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Game;
using LolitaPoker.Core.Models;
using LolitaPoker.Core.AI;
using LolitaPoker.Core.Network;

namespace LolitaPoker.Tests;

public class StressTests
{
    // ========== 辅助方法 ==========

    /// <summary>
    /// 构造 20 张最大组合潜力的「地主手牌」：炸弹(3×4)、对子(4/5/6)、长顺(3-A)、
    /// 连对(33-44-55-66)、火箭(大小王)。
    /// </summary>
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

    /// <summary>
    /// 帮助函数：用 AI 打一局完整对局，返回总回合数。
    /// </summary>
    private static int PlayOneFullGame()
    {
        var gm = new GameManager();
        gm.StartNewGameQuick();

        var ai = new SimpleAIPlayer[3];
        for (int i = 0; i < 3; i++) ai[i] = new SimpleAIPlayer();

        int turns = 0;
        const int maxTurns = 500;
        while (gm.Phase != GamePhase.GameOver && turns < maxTurns)
        {
            int cp = gm.CurrentPlayerIndex;
            var hand = gm.GetPlayerHand(cp).ToList();
            if (hand.Count == 0) break;

            bool isLandlord = cp == gm.LandlordIndex;
            bool isFirstPlay = gm.LastPlayedCombo == null || !gm.LastPlayedCombo.IsValid;

            var (cards, pass) = ai[cp].DecidePlay(
                hand,
                isFirstPlay ? null : gm.LastPlayedCombo,
                isFirstPlay,
                isLandlord,
                gm.LastPlayedByIndex,
                cp,
                gm.LandlordIndex);

            if (pass || cards == null)
                gm.SubmitPass(cp);
            else
                gm.SubmitPlay(cp, cards);

            turns++;
        }
        return turns;
    }

    // ========== CardComboFinder 压力测试 ==========

    [Fact]
    [Trait("Category", "Stress")]
    public void FindAllPlayableCombos_20CardHand_FreeLead_10000Iterations()
    {
        var hand = CreateWorstCaseHand();
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < 10_000; i++)
        {
            var combos = CardComboFinder.FindAllPlayableCombos(hand, null);
            Assert.NotEmpty(combos);
        }

        sw.Stop();
        Assert.True(sw.Elapsed.TotalSeconds < 30,
            $"10,000 次 20 张手牌搜索耗时 {sw.Elapsed.TotalSeconds:F1}s，超过 30s 阈值");
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void FindAllPlayableCombos_BeatingTarget_10000Iterations()
    {
        var hand = CreateWorstCaseHand();
        // 构造一个 5 张顺子目标：3-4-5-6-7
        var target = new CardCombo(CardComboType.Straight, Rank.Seven, 5,
            new List<Card>
            {
                new Card(Suit.Clubs, Rank.Three),
                new Card(Suit.Clubs, Rank.Four),
                new Card(Suit.Clubs, Rank.Five),
                new Card(Suit.Clubs, Rank.Six),
                new Card(Suit.Clubs, Rank.Seven),
            });

        for (int i = 0; i < 10_000; i++)
        {
            var combos = CardComboFinder.FindAllPlayableCombos(hand, target);
            // 只要不抛异常就算通过；结果可能为空（无法压过）
        }
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void FindAllPlayableCombos_EmptyHand_10000Iterations()
    {
        var hand = new List<Card>();

        for (int i = 0; i < 10_000; i++)
        {
            var combos = CardComboFinder.FindAllPlayableCombos(hand, null);
            Assert.Empty(combos);
        }
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void FindAllPlayableCombos_SingleCardHand_RepeatedCalls()
    {
        var hand = new List<Card> { new Card(Suit.Diamonds, Rank.Ace) };

        // 自由出牌
        for (int i = 0; i < 5_000; i++)
        {
            var combos = CardComboFinder.FindAllPlayableCombos(hand, null);
            Assert.NotEmpty(combos);
        }

        // 压一个对子目标（无法压过）
        var pairTarget = new CardCombo(CardComboType.Pair, Rank.Two, 1,
            new List<Card>
            {
                new Card(Suit.Diamonds, Rank.Two),
                new Card(Suit.Hearts, Rank.Two),
            });

        for (int i = 0; i < 5_000; i++)
        {
            var combos = CardComboFinder.FindAllPlayableCombos(hand, pairTarget);
            Assert.Empty(combos);
        }
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void FindAllPlayableCombos_FullDeckAsHand()
    {
        // 54 张全牌作为手牌（不可能在真实游戏中出现，但测试算法边界）
        var hand = CardHelper.CreateFullDeck();
        var combos = CardComboFinder.FindAllPlayableCombos(hand, null);
        Assert.NotEmpty(combos);
    }

    // ========== RulesEngine 压力测试 ==========

    [Fact]
    [Trait("Category", "Stress")]
    public void ClassifyPlay_AllComboTypes_100000Iterations()
    {
        // 预构建 14 种合法牌型
        var combos = new List<List<Card>>
        {
            new() { new Card(Suit.Diamonds, Rank.Three) },                                                        // Single
            new() { new Card(Suit.Diamonds, Rank.Five), new Card(Suit.Hearts, Rank.Five) },                        // Pair
            new() { new Card(Suit.Diamonds, Rank.Seven), new Card(Suit.Hearts, Rank.Seven), new Card(Suit.Spades, Rank.Seven) }, // Triple
            new() { new Card(Suit.Diamonds, Rank.Nine), new Card(Suit.Hearts, Rank.Nine), new Card(Suit.Spades, Rank.Nine), new Card(Suit.Diamonds, Rank.Three) }, // TriplePlusOne
            new() { new Card(Suit.Diamonds, Rank.Ten), new Card(Suit.Hearts, Rank.Ten), new Card(Suit.Spades, Rank.Ten), new Card(Suit.Diamonds, Rank.Four), new Card(Suit.Hearts, Rank.Four) }, // TriplePlusPair
            new() { new Card(Suit.Diamonds, Rank.Three), new Card(Suit.Hearts, Rank.Four), new Card(Suit.Spades, Rank.Five), new Card(Suit.Diamonds, Rank.Six), new Card(Suit.Hearts, Rank.Seven) }, // Straight
            new() { new Card(Suit.Diamonds, Rank.Three), new Card(Suit.Hearts, Rank.Three), new Card(Suit.Diamonds, Rank.Four), new Card(Suit.Hearts, Rank.Four), new Card(Suit.Diamonds, Rank.Five), new Card(Suit.Hearts, Rank.Five) }, // ConsecutivePairs
            new() { new Card(Suit.Diamonds, Rank.Three), new Card(Suit.Hearts, Rank.Three), new Card(Suit.Spades, Rank.Three), new Card(Suit.Diamonds, Rank.Four), new Card(Suit.Hearts, Rank.Four), new Card(Suit.Spades, Rank.Four) }, // Airplane
            // AirplaneWithSingles: 333-444 + 5,6
            new() { new Card(Suit.Diamonds, Rank.Three), new Card(Suit.Hearts, Rank.Three), new Card(Suit.Spades, Rank.Three), new Card(Suit.Diamonds, Rank.Four), new Card(Suit.Hearts, Rank.Four), new Card(Suit.Spades, Rank.Four), new Card(Suit.Diamonds, Rank.Five), new Card(Suit.Hearts, Rank.Six) },
            // AirplaneWithPairs: 333-444 + 55-66
            new() { new Card(Suit.Diamonds, Rank.Three), new Card(Suit.Hearts, Rank.Three), new Card(Suit.Spades, Rank.Three), new Card(Suit.Diamonds, Rank.Four), new Card(Suit.Hearts, Rank.Four), new Card(Suit.Spades, Rank.Four), new Card(Suit.Diamonds, Rank.Five), new Card(Suit.Hearts, Rank.Five), new Card(Suit.Diamonds, Rank.Six), new Card(Suit.Hearts, Rank.Six) },
            // FourPlusTwo: 4444 + 3,5
            new() { new Card(Suit.Diamonds, Rank.Four), new Card(Suit.Hearts, Rank.Four), new Card(Suit.Spades, Rank.Four), new Card(Suit.Clubs, Rank.Four), new Card(Suit.Diamonds, Rank.Three), new Card(Suit.Diamonds, Rank.Five) },
            // FourPlusTwoPairs: 4444 + 33,55
            new() { new Card(Suit.Diamonds, Rank.Four), new Card(Suit.Hearts, Rank.Four), new Card(Suit.Spades, Rank.Four), new Card(Suit.Clubs, Rank.Four), new Card(Suit.Diamonds, Rank.Three), new Card(Suit.Hearts, Rank.Three), new Card(Suit.Diamonds, Rank.Five), new Card(Suit.Hearts, Rank.Five) },
            // Bomb
            new() { new Card(Suit.Diamonds, Rank.Jack), new Card(Suit.Hearts, Rank.Jack), new Card(Suit.Spades, Rank.Jack), new Card(Suit.Clubs, Rank.Jack) },
            // Rocket
            new() { new Card(Suit.None, Rank.SmallJoker), new Card(Suit.None, Rank.BigJoker) },
        };

        var expectedTypes = new[]
        {
            CardComboType.Single, CardComboType.Pair, CardComboType.Triple,
            CardComboType.TriplePlusOne, CardComboType.TriplePlusPair,
            CardComboType.Straight, CardComboType.ConsecutivePairs,
            CardComboType.Airplane, CardComboType.AirplaneWithSingles,
            CardComboType.AirplaneWithPairs, CardComboType.FourPlusTwo,
            CardComboType.FourPlusTwoPairs, CardComboType.Bomb, CardComboType.Rocket,
        };

        for (int i = 0; i < 100_000; i++)
        {
            int idx = i % combos.Count;
            var result = RulesEngine.ClassifyPlay(combos[idx]);
            Assert.True(result.IsValid, $"第 {i} 次: 牌型 {expectedTypes[idx]} 应为合法");
            Assert.Equal(expectedTypes[idx], result.Type);
        }
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void ClassifyPlay_InvalidInputs_50000Iterations()
    {
        var invalidInputs = new List<List<Card>>
        {
            new(),  // 空
            new() { new Card(Suit.Diamonds, Rank.Three), new Card(Suit.Hearts, Rank.Four) }, // 两张不匹配
            new() { new Card(Suit.Diamonds, Rank.Three), new Card(Suit.Hearts, Rank.Four), new Card(Suit.Spades, Rank.Six) }, // 三张不连续
        };

        for (int i = 0; i < 50_000; i++)
        {
            int idx = i % invalidInputs.Count;
            var result = RulesEngine.ClassifyPlay(invalidInputs[idx]);
            Assert.False(result.IsValid, $"第 {i} 次: 应为非法牌型");
        }
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void ClassifyPlay_BombDetectionUnderPressure()
    {
        // 构造所有可构成炸弹的牌（Rank.Three 到 Rank.Two 共 13 种）
        var bombHands = new List<List<Card>>();
        for (int r = (int)Rank.Three; r <= (int)Rank.Two; r++)
        {
            bombHands.Add(new List<Card>
            {
                new Card(Suit.Diamonds, (Rank)r),
                new Card(Suit.Hearts, (Rank)r),
                new Card(Suit.Spades, (Rank)r),
                new Card(Suit.Clubs, (Rank)r),
            });
        }

        for (int i = 0; i < 100_000; i++)
        {
            int idx = i % bombHands.Count;
            var result = RulesEngine.ClassifyPlay(bombHands[idx]);
            Assert.Equal(CardComboType.Bomb, result.Type);
        }
    }

    // ========== GameManager 压力测试 ==========

    [Fact]
    [Trait("Category", "Stress")]
    public void GameManager_1000_FullGames_AIVsAIVsAI()
    {
        var wins = new int[3];

        for (int game = 0; game < 1000; game++)
        {
            var gm = new GameManager();
            gm.StartNewGameQuick();

            int? winner = null;
            gm.GameEnded += (w, m) => winner = w;

            var ai = new SimpleAIPlayer[3];
            for (int i = 0; i < 3; i++) ai[i] = new SimpleAIPlayer();

            int turns = 0;
            while (gm.Phase != GamePhase.GameOver && turns < 500)
            {
                int cp = gm.CurrentPlayerIndex;
                var hand = gm.GetPlayerHand(cp).ToList();
                if (hand.Count == 0) break;

                bool isFirstPlay = gm.LastPlayedCombo == null || !gm.LastPlayedCombo.IsValid;
                var (cards, pass) = ai[cp].DecidePlay(
                    hand,
                    isFirstPlay ? null : gm.LastPlayedCombo,
                    isFirstPlay,
                    cp == gm.LandlordIndex,
                    gm.LastPlayedByIndex,
                    cp,
                    gm.LandlordIndex);

                if (pass || cards == null)
                    gm.SubmitPass(cp);
                else
                    gm.SubmitPlay(cp, cards);

                turns++;
            }

            Assert.Equal(GamePhase.GameOver, gm.Phase);
            Assert.NotNull(winner);
            wins[winner.Value]++;
        }

        // 所有胜者座位之和应等于总局数
        Assert.Equal(1000, wins[0] + wins[1] + wins[2]);
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void GameManager_RapidStartStop_5000Cycles()
    {
        for (int i = 0; i < 5000; i++)
        {
            var gm = new GameManager();
            gm.StartNewGameQuick();
            // 立即丢弃，测试无状态泄漏
        }
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void GameManager_SubmitPlay_RapidFire_50000Calls()
    {
        var gm = new GameManager();
        gm.StartNewGameQuick();

        int cp = gm.CurrentPlayerIndex;
        var hand = gm.GetPlayerHand(cp).ToList();
        if (hand.Count == 0) return; // 不应发生

        var singleCard = new List<Card> { hand[0] };

        // 第一次出牌成功，后续的尝试不应该崩溃
        for (int i = 0; i < 50_000; i++)
        {
            gm.SubmitPlay(cp, singleCard);
        }
    }

    // ========== DeckPool 线程安全压力测试 ==========

    [Fact]
    [Trait("Category", "Stress")]
    public async Task DeckPool_ConcurrentGetAndReturn_100Threads()
    {
        const int threadCount = 100;
        var barrier = new Barrier(threadCount);
        var exceptions = new List<Exception>();
        var tasks = new Task[threadCount];

        for (int t = 0; t < threadCount; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                try
                {
                    barrier.SignalAndWait(TimeSpan.FromSeconds(10));
                    var deck = DeckPool.GetShuffledDeck();
                    Assert.Equal(54, deck.Count);
                    Thread.SpinWait(100);
                    DeckPool.Return(deck);
                }
                catch (Exception ex)
                {
                    lock (exceptions) exceptions.Add(ex);
                }
            });
        }

        await Task.WhenAll(tasks);
        Assert.Empty(exceptions);
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void DeckPool_PoolOverflow_ReturnsBeyond4()
    {
        // 获取 20 个牌组，全部归还——池上限为 4，多余的应被安全丢弃
        var decks = new List<List<Card>>();
        for (int i = 0; i < 20; i++)
            decks.Add(DeckPool.GetShuffledDeck());

        foreach (var d in decks)
            DeckPool.Return(d);

        // 再获取 5 个，应全部正常返回 54 张
        for (int i = 0; i < 5; i++)
        {
            var deck = DeckPool.GetShuffledDeck();
            Assert.Equal(54, deck.Count);
            DeckPool.Return(deck);
        }
    }

    // ========== SimpleAIPlayer 完整对局压力测试 ==========

    [Fact]
    [Trait("Category", "Stress")]
    public void SimpleAIPlayer_500_FullGames_NoExceptions()
    {
        for (int game = 0; game < 500; game++)
        {
            int turns = PlayOneFullGame();
            Assert.True(turns < 500, $"游戏 {game} 超过 500 回合上限，可能死循环");
        }
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void SimpleAIPlayer_DecideBid_10000_Iterations()
    {
        var ai = new SimpleAIPlayer();
        var hand = CardHelper.CreateFullDeck().Take(17).ToList();

        for (int i = 0; i < 10_000; i++)
        {
            int bid = ai.DecideBid(hand, 0);
            Assert.InRange(bid, 0, 3);
        }
    }

    // ========== NetworkGameManager 压力测试 ==========

    private class StressStubNetworkAdapter : INetworkAdapter
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

    [Fact]
    [Trait("Category", "Stress")]
    public void NetworkGameManager_10000_Messages_NoCrash()
    {
        var stub = new StressStubNetworkAdapter();
        var gm = new GameManager();
        var ngm = new NetworkGameManager(gm, stub);

        for (int i = 0; i < 10_000; i++)
        {
            stub.SimulateMessage(new NetworkMessage
            {
                Type = "PlayerJoin",
                SenderId = $"player_{i}",
                Payload = "{}"
            });
        }
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void NetworkGameManager_RapidCreateDestroy_1000Instances()
    {
        for (int i = 0; i < 1000; i++)
        {
            var stub = new StressStubNetworkAdapter();
            var gm = new GameManager();
            var ngm = new NetworkGameManager(gm, stub);
            // 立即丢弃所有引用
        }
    }
}
