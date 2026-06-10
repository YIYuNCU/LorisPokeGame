using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LolitaPoker.App;
using LolitaPoker.Core.Audio;
using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Game;
using LolitaPoker.Core.Models;
using LolitaPoker.Core.ViewModels;
using Xunit;

namespace LolitaPoker.Tests;

public class SoundEffectTests
{
    [Theory]
    [InlineData(Rank.Ace, "single_a.mp3")]
    [InlineData(Rank.Two, "single_2.mp3")]
    [InlineData(Rank.SmallJoker, "single_small_joker.mp3")]
    [InlineData(Rank.BigJoker, "single_big_joker.mp3")]
    public void GetCardPlaySoundFileName_Single_MapsRank(Rank rank, string expected)
    {
        var combo = RulesEngine.ClassifyPlay([CardOf(rank)]);

        Assert.Equal(expected, SoundEffectMapper.GetCardPlaySoundFileName(combo));
    }

    [Theory]
    [InlineData(Rank.Ace, "pair_a.mp3")]
    [InlineData(Rank.Two, "pair_2.mp3")]
    public void GetCardPlaySoundFileName_Pair_MapsRank(Rank rank, string expected)
    {
        var combo = RulesEngine.ClassifyPlay([
            new Card(Suit.Diamonds, rank),
            new Card(Suit.Hearts, rank)
        ]);

        Assert.Equal(expected, SoundEffectMapper.GetCardPlaySoundFileName(combo));
    }

    [Theory]
    [MemberData(nameof(SpecialCombos))]
    public void GetCardPlaySoundFileName_SpecialCombos_MapToGenericFile(List<Card> cards, string expected)
    {
        var combo = RulesEngine.ClassifyPlay(cards);

        Assert.Equal(expected, SoundEffectMapper.GetCardPlaySoundFileName(combo));
    }

    [Fact]
    public void GameEnded_LocalPlayerWin_PlaysVictorySound()
    {
        var soundEffects = new RecordingSoundEffectService();
        var vm = new GameViewModel(soundEffectService: soundEffects);
        vm.PlayerBottom.Role = PlayerRole.Landlord;

        InvokeOnGameEnded(vm, winnerIndex: 0, multiplier: 2);

        Assert.Contains(SoundEffectMapper.VictoryFileName, soundEffects.PlayedFiles);
        Assert.DoesNotContain(SoundEffectMapper.DefeatFileName, soundEffects.PlayedFiles);
    }

    [Fact]
    public void GameEnded_LocalPlayerLoss_PlaysDefeatSound()
    {
        var soundEffects = new RecordingSoundEffectService();
        var vm = new GameViewModel(soundEffectService: soundEffects);
        vm.PlayerBottom.Role = PlayerRole.Farmer;
        vm.PlayerRight.Role = PlayerRole.Landlord;

        InvokeOnGameEnded(vm, winnerIndex: 1, multiplier: 2);

        Assert.Contains(SoundEffectMapper.DefeatFileName, soundEffects.PlayedFiles);
        Assert.DoesNotContain(SoundEffectMapper.VictoryFileName, soundEffects.PlayedFiles);
    }

    [Fact]
    public async Task SoundEffectServiceImpl_MissingFile_DoesNotThrow()
    {
        using var service = new SoundEffectServiceImpl();

        await service.PlayAsync($"missing_{Guid.NewGuid():N}.mp3");
    }

    [Fact]
    public void PassSound_LocalMode_PlayerPasses_SetsLastAction()
    {
        var soundEffects = new RecordingSoundEffectService();
        var vm = new GameViewModel(soundEffectService: soundEffects);

        InvokeOnPlayerPlayed(vm, playerIndex: 0, combo: null);

        Assert.Contains(SoundEffectMapper.PassFileName, soundEffects.PlayedFiles);
    }

    [Fact]
    public void PassFileName_Constant_IsPassMp3()
    {
        Assert.Equal("pass.mp3", SoundEffectMapper.PassFileName);
    }

    public static IEnumerable<object[]> SpecialCombos()
    {
        yield return [
            new List<Card>
            {
                new(Suit.Diamonds, Rank.Five),
                new(Suit.Hearts, Rank.Five),
                new(Suit.Spades, Rank.Five),
                new(Suit.Diamonds, Rank.Three),
            },
            "triple_plus_one.mp3"
        ];

        yield return [
            new List<Card>
            {
                new(Suit.Diamonds, Rank.Five),
                new(Suit.Hearts, Rank.Five),
                new(Suit.Spades, Rank.Five),
                new(Suit.Diamonds, Rank.Nine),
                new(Suit.Hearts, Rank.Nine),
            },
            "triple_plus_pair.mp3"
        ];

        yield return [
            new List<Card>
            {
                new(Suit.Diamonds, Rank.Eight),
                new(Suit.Hearts, Rank.Eight),
                new(Suit.Spades, Rank.Eight),
                new(Suit.Clubs, Rank.Eight),
            },
            "bomb.mp3"
        ];

        yield return [
            new List<Card>
            {
                new(Suit.None, Rank.SmallJoker),
                new(Suit.None, Rank.BigJoker),
            },
            "rocket.mp3"
        ];
    }

    private static Card CardOf(Rank rank)
        => rank is Rank.SmallJoker or Rank.BigJoker
            ? new Card(Suit.None, rank)
            : new Card(Suit.Diamonds, rank);

    private static void InvokeOnGameEnded(GameViewModel vm, int? winnerIndex, int multiplier)
    {
        var method = typeof(GameViewModel).GetMethod(
            "OnGameEnded",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        method.Invoke(vm, [winnerIndex, multiplier]);
    }

    private static void InvokeOnPlayerPlayed(GameViewModel vm, int playerIndex, CardCombo? combo)
    {
        var method = typeof(GameViewModel).GetMethod(
            "OnPlayerPlayed",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        method.Invoke(vm, [playerIndex, combo]);
    }

    private sealed class RecordingSoundEffectService : ISoundEffectService
    {
        public List<string> PlayedFiles { get; } = new();

        public Task PlayAsync(string soundFileName, CancellationToken cancellationToken = default)
        {
            PlayedFiles.Add(soundFileName);
            return Task.CompletedTask;
        }
    }
}
