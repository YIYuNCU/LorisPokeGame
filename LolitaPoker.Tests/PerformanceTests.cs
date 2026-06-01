// -----------------------------------------------------------------------
// PerformanceTests.cs - 性能测试（延迟基准、吞吐量测量）
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xunit;
using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Game;
using LolitaPoker.Core.Models;
using LolitaPoker.Core.AI;

namespace LolitaPoker.Tests;

public class PerformanceTests
{
    // ========== 辅助方法 ==========

    /// <summary>
    /// 构造 20 张最大组合潜力的地主手牌。
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
    /// 构造 17 张普通农民手牌。
    /// </summary>
    private static List<Card> CreateTypicalHand()
    {
        return new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Four),
            new Card(Suit.Spades, Rank.Five),
            new Card(Suit.Diamonds, Rank.Six),
            new Card(Suit.Hearts, Rank.Seven),
            new Card(Suit.Spades, Rank.Eight),
            new Card(Suit.Diamonds, Rank.Nine),
            new Card(Suit.Hearts, Rank.Ten),
            new Card(Suit.Spades, Rank.Jack),
            new Card(Suit.Diamonds, Rank.Queen),
            new Card(Suit.Hearts, Rank.King),
            new Card(Suit.Spades, Rank.Ace),
            new Card(Suit.Diamonds, Rank.Two),
            new Card(Suit.Hearts, Rank.Three),
            new Card(Suit.Spades, Rank.Four),
            new Card(Suit.Diamonds, Rank.Five),
            new Card(Suit.Hearts, Rank.Six),
        };
    }

    /// <summary>
    /// 帮助函数：用 AI 打一局完整对局。
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
        return turns;
    }

    // ========== FindAllPlayableCombos 性能 ==========

    [Fact]
    [Trait("Category", "Performance")]
    public void FindAllPlayableCombos_20CardLead_Under20ms()
    {
        var hand = CreateWorstCaseHand();

        // 预热
        CardComboFinder.FindAllPlayableCombos(hand, null);

        var sw = Stopwatch.StartNew();
        const int iterations = 100;
        for (int i = 0; i < iterations; i++)
        {
            CardComboFinder.FindAllPlayableCombos(hand, null);
        }
        sw.Stop();

        double avgMs = sw.Elapsed.TotalMilliseconds / iterations;
        Assert.True(avgMs < 20,
            $"20 张手牌平均搜索时间 {avgMs:F2}ms，超过 20ms 阈值");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void FindAllPlayableCombos_17CardLead_Under10ms()
    {
        var hand = CreateTypicalHand();

        // 预热
        CardComboFinder.FindAllPlayableCombos(hand, null);

        var sw = Stopwatch.StartNew();
        const int iterations = 100;
        for (int i = 0; i < iterations; i++)
        {
            CardComboFinder.FindAllPlayableCombos(hand, null);
        }
        sw.Stop();

        double avgMs = sw.Elapsed.TotalMilliseconds / iterations;
        Assert.True(avgMs < 10,
            $"17 张手牌平均搜索时间 {avgMs:F2}ms，超过 10ms 阈值");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void FindAllPlayableCombos_BeatingStraight_Under20ms()
    {
        var hand = CreateWorstCaseHand();
        var target = new CardCombo(CardComboType.Straight, Rank.Seven, 5,
            new List<Card>
            {
                new Card(Suit.Clubs, Rank.Three),
                new Card(Suit.Clubs, Rank.Four),
                new Card(Suit.Clubs, Rank.Five),
                new Card(Suit.Clubs, Rank.Six),
                new Card(Suit.Clubs, Rank.Seven),
            });

        // 预热
        CardComboFinder.FindAllPlayableCombos(hand, target);

        var sw = Stopwatch.StartNew();
        const int iterations = 100;
        for (int i = 0; i < iterations; i++)
        {
            CardComboFinder.FindAllPlayableCombos(hand, target);
        }
        sw.Stop();

        double avgMs = sw.Elapsed.TotalMilliseconds / iterations;
        Assert.True(avgMs < 20,
            $"压顺子平均搜索时间 {avgMs:F2}ms，超过 20ms 阈值");
    }

    // ========== RulesEngine 性能 ==========

    [Fact]
    [Trait("Category", "Performance")]
    public void ClassifyPlay_SingleCard_Under01ms()
    {
        var cards = new List<Card> { new Card(Suit.Diamonds, Rank.Ace) };

        // 预热
        RulesEngine.ClassifyPlay(cards);

        var sw = Stopwatch.StartNew();
        const int iterations = 10_000;
        for (int i = 0; i < iterations; i++)
        {
            RulesEngine.ClassifyPlay(cards);
        }
        sw.Stop();

        double avgUs = (sw.Elapsed.TotalMilliseconds / iterations) * 1000;
        Assert.True(avgUs < 100,
            $"单张 ClassifyPlay 平均 {avgUs:F1}μs，超过 100μs 阈值");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void ClassifyPlay_ComplexAirplane_Under1ms()
    {
        // 飞机带两张单牌：333-444 + 5, 6
        var cards = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),  new Card(Suit.Hearts, Rank.Three),
            new Card(Suit.Spades, Rank.Three),
            new Card(Suit.Diamonds, Rank.Four),   new Card(Suit.Hearts, Rank.Four),
            new Card(Suit.Spades, Rank.Four),
            new Card(Suit.Diamonds, Rank.Five),   new Card(Suit.Hearts, Rank.Six),
        };

        // 预热
        RulesEngine.ClassifyPlay(cards);

        var sw = Stopwatch.StartNew();
        const int iterations = 10_000;
        for (int i = 0; i < iterations; i++)
        {
            RulesEngine.ClassifyPlay(cards);
        }
        sw.Stop();

        double avgUs = (sw.Elapsed.TotalMilliseconds / iterations) * 1000;
        Assert.True(avgUs < 100,
            $"飞机 ClassifyPlay 平均 {avgUs:F1}μs，超过 100μs 阈值");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void CanBeat_100000Comparisons_Under1Second()
    {
        var bombA = RulesEngine.ClassifyPlay(new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Five), new Card(Suit.Hearts, Rank.Five),
            new Card(Suit.Spades, Rank.Five), new Card(Suit.Clubs, Rank.Five),
        });
        var bombB = RulesEngine.ClassifyPlay(new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Six), new Card(Suit.Hearts, Rank.Six),
            new Card(Suit.Spades, Rank.Six), new Card(Suit.Clubs, Rank.Six),
        });

        // 预热
        for (int i = 0; i < 100; i++) RulesEngine.CanBeat(bombB, bombA);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100_000; i++)
        {
            RulesEngine.CanBeat(bombB, bombA);
        }
        sw.Stop();

        Assert.True(sw.Elapsed.TotalSeconds < 1,
            $"100,000 次 CanBeat 耗时 {sw.Elapsed.TotalSeconds:F2}s，超过 1s 阈值");
    }

    // ========== GameManager 完整对局性能 ==========

    [Fact]
    [Trait("Category", "Performance")]
    public void GameManager_FullAIGame_Under100ms()
    {
        // 预热
        PlayOneFullGame();

        var sw = Stopwatch.StartNew();
        PlayOneFullGame();
        sw.Stop();

        Assert.True(sw.Elapsed.TotalMilliseconds < 100,
            $"单局 AI 对局耗时 {sw.Elapsed.TotalMilliseconds:F1}ms，超过 100ms 阈值");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void GameManager_100FullAIGames_Under10Seconds()
    {
        // 预热
        PlayOneFullGame();

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            PlayOneFullGame();
        }
        sw.Stop();

        Assert.True(sw.Elapsed.TotalSeconds < 10,
            $"100 局 AI 对局耗时 {sw.Elapsed.TotalSeconds:F1}s，超过 10s 阈值");
    }

    // ========== DeckPool 性能 ==========

    [Fact]
    [Trait("Category", "Performance")]
    public void DeckPool_GetShuffledDeck_Under1ms()
    {
        // 预热填充池
        var warmup = DeckPool.GetShuffledDeck();
        DeckPool.Return(warmup);

        var sw = Stopwatch.StartNew();
        const int iterations = 1000;
        for (int i = 0; i < iterations; i++)
        {
            var deck = DeckPool.GetShuffledDeck();
            DeckPool.Return(deck);
        }
        sw.Stop();

        double avgUs = (sw.Elapsed.TotalMilliseconds / iterations) * 1000;
        Assert.True(avgUs < 1000,
            $"DeckPool 获取归还平均 {avgUs:F1}μs，超过 1ms 阈值");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void Deck_Deal_Under1ms()
    {
        // 预热
        {
            var d = new Deck();
            d.Deal();
            d.ReturnToPool();
        }

        var sw = Stopwatch.StartNew();
        const int iterations = 1000;
        for (int i = 0; i < iterations; i++)
        {
            var deck = new Deck();
            deck.Deal();
            deck.ReturnToPool();
        }
        sw.Stop();

        double avgUs = (sw.Elapsed.TotalMilliseconds / iterations) * 1000;
        Assert.True(avgUs < 1000,
            $"Deck.Deal 平均 {avgUs:F1}μs，超过 1ms 阈值");
    }

    // ========== SimpleAIPlayer 决策性能 ==========

    [Fact]
    [Trait("Category", "Performance")]
    public void SimpleAIPlayer_DecidePlay_Lead_Under20ms()
    {
        var ai = new SimpleAIPlayer();
        var hand = CreateWorstCaseHand();

        // 预热
        ai.DecidePlay(hand, null, true, true, null, 0, 0);

        var sw = Stopwatch.StartNew();
        const int iterations = 100;
        for (int i = 0; i < iterations; i++)
        {
            ai.DecidePlay(hand, null, true, true, null, 0, 0);
        }
        sw.Stop();

        double avgMs = sw.Elapsed.TotalMilliseconds / iterations;
        Assert.True(avgMs < 20,
            $"AI 首次出牌平均 {avgMs:F2}ms，超过 20ms 阈值");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void SimpleAIPlayer_DecideBid_Under1ms()
    {
        var ai = new SimpleAIPlayer();
        var hand = CreateTypicalHand();

        // 预热
        ai.DecideBid(hand, 0);

        var sw = Stopwatch.StartNew();
        const int iterations = 10_000;
        for (int i = 0; i < iterations; i++)
        {
            ai.DecideBid(hand, 0);
        }
        sw.Stop();

        double avgUs = (sw.Elapsed.TotalMilliseconds / iterations) * 1000;
        Assert.True(avgUs < 100,
            $"AI 叫分决策平均 {avgUs:F1}μs，超过 100μs 阈值");
    }
}
