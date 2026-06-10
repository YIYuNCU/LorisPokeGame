using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Game;

namespace LolitaPoker.Core.Audio;

/// <summary>
/// Maps game events and card combinations to sound-effect file names.
/// </summary>
public static class SoundEffectMapper
{
    public const string VictoryFileName = "victory.mp3";
    public const string DefeatFileName = "defeat.mp3";
    public const string PassFileName = "pass.mp3";

    public static string? GetCardPlaySoundFileName(CardCombo? combo)
    {
        if (combo is null || !combo.IsValid)
            return null;

        return combo.Type switch
        {
            CardComboType.Single => GetRankKey(combo.PrimaryRank) is { } key ? $"single_{key}.mp3" : null,
            CardComboType.Pair => GetNonJokerRankKey(combo.PrimaryRank) is { } key ? $"pair_{key}.mp3" : null,
            CardComboType.Triple => "triple.mp3",
            CardComboType.TriplePlusOne => "triple_plus_one.mp3",
            CardComboType.TriplePlusPair => "triple_plus_pair.mp3",
            CardComboType.Straight => "straight.mp3",
            CardComboType.ConsecutivePairs => "consecutive_pairs.mp3",
            CardComboType.Airplane => "airplane.mp3",
            CardComboType.AirplaneWithSingles => "airplane_with_singles.mp3",
            CardComboType.AirplaneWithPairs => "airplane_with_pairs.mp3",
            CardComboType.FourPlusTwo => "four_plus_two.mp3",
            CardComboType.FourPlusTwoPairs => "four_plus_two_pairs.mp3",
            CardComboType.Bomb => "bomb.mp3",
            CardComboType.Rocket => "rocket.mp3",
            _ => null
        };
    }

    private static string? GetNonJokerRankKey(Rank rank)
        => rank is Rank.SmallJoker or Rank.BigJoker ? null : GetRankKey(rank);

    private static string? GetRankKey(Rank rank)
        => rank switch
        {
            Rank.Three => "3",
            Rank.Four => "4",
            Rank.Five => "5",
            Rank.Six => "6",
            Rank.Seven => "7",
            Rank.Eight => "8",
            Rank.Nine => "9",
            Rank.Ten => "10",
            Rank.Jack => "j",
            Rank.Queen => "q",
            Rank.King => "k",
            Rank.Ace => "a",
            Rank.Two => "2",
            Rank.SmallJoker => "small_joker",
            Rank.BigJoker => "big_joker",
            _ => null
        };
}
