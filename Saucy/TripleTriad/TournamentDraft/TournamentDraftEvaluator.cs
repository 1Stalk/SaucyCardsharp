using System;
using System.Collections.Generic;
using System.Linq;

namespace Saucy.TripleTriad.TournamentDraft;

public static class TournamentDraftEvaluator
{
    public static TournamentDraftResult Evaluate(TournamentDraftState draftState)
    {
        var rules = draftState.Rules;
        var isReverse = rules.Any(r => r.Contains("Reverse", StringComparison.OrdinalIgnoreCase));
        var isFallenAce = rules.Any(r => r.Contains("Fallen", StringComparison.OrdinalIgnoreCase));
        var isSame = rules.Any(r => r.Contains("Same", StringComparison.OrdinalIgnoreCase));
        var isPlus = rules.Any(r => r.Contains("Plus", StringComparison.OrdinalIgnoreCase));
        var isAscension = rules.Any(r => r.Contains("Ascension", StringComparison.OrdinalIgnoreCase));

        int best1 = 0, best2 = 0, best3 = 0;
        var bestScore = float.MinValue;
        List<TournamentDraftCard> bestDeck = [];

        var set1 = draftState.Set1Options;
        var set2 = draftState.Set2Options;
        var set3 = draftState.Set3Options;

        for (var i = 0; i < set1.Count; i++)
        {
            for (var j = 0; j < set2.Count; j++)
            {
                for (var k = 0; k < set3.Count; k++)
                {
                    var deck = new List<TournamentDraftCard>(5);
                    deck.AddRange(set1[i].Cards);
                    deck.AddRange(set2[j].Cards);
                    deck.AddRange(set3[k].Cards);

                    var score = ScoreDeck(deck, isReverse, isFallenAce, isSame, isPlus, isAscension);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best1 = i;
                        best2 = j;
                        best3 = k;
                        bestDeck = deck;
                    }
                }
            }
        }

        string summary;
        if (isReverse)
        {
            var ones = bestDeck.Sum(c => c.CountVal(1));
            var twos = bestDeck.Sum(c => c.CountVal(2));
            summary = $"[Reverse Mode] Prioritized Low Numbers: {ones} Ones, {twos} Twos (Score: {bestScore:F0})";
        }
        else if (isAscension)
        {
            summary = $"[Ascension Mode] Optimized Tribe Synergy (Score: {bestScore:F0})";
        }
        else if (isFallenAce)
        {
            summary = $"[Fallen Ace Mode] Balanced Corner Anchors & 1s (Score: {bestScore:F0})";
        }
        else
        {
            summary = $"[Standard Mode] Optimized Corner Defense & Raw Power (Score: {bestScore:F0})";
        }

        return new TournamentDraftResult
        {
            Set1Option = best1,
            Set2Option = best2,
            Set3Option = best3,
            Score = bestScore,
            Summary = summary,
            SelectedCards = bestDeck,
            Set1Cards = [.. set1[best1].Cards],
            Set2Cards = [.. set2[best2].Cards],
            Set3Cards = [.. set3[best3].Cards]
        };
    }

    private static float ScoreDeck(
        List<TournamentDraftCard> deck,
        bool isReverse,
        bool isFallenAce,
        bool isSame,
        bool isPlus,
        bool isAscension)
    {
        var score = 0f;

        if (isReverse)
        {
            foreach (var card in deck)
            {
                foreach (var s in card.Sides)
                {
                    score += s switch
                    {
                        1 => 100f,
                        2 => 50f,
                        3 => 25f,
                        4 => 10f,
                        5 => 0f,
                        6 => -10f,
                        7 => -30f,
                        8 => -55f,
                        _ => -85f // 9 or 10 (A)
                    };
                }

                // In Reverse, low numbers on adjacent sides form impregnable corners
                if (card.Up <= 2 && card.Right <= 2) score += 40f;
                if (card.Right <= 2 && card.Down <= 2) score += 40f;
                if (card.Down <= 2 && card.Left <= 2) score += 40f;
                if (card.Left <= 2 && card.Up <= 2) score += 40f;
            }
        }
        else
        {
            foreach (var card in deck)
            {
                foreach (var s in card.Sides)
                {
                    score += s switch
                    {
                        10 => 80f, // A
                        9 => 65f,
                        8 => 50f,
                        7 => 35f,
                        6 => 20f,
                        5 => 10f,
                        _ => 0f
                    };
                }

                // Standard corner anchors: strong numbers on adjacent sides
                if (card.Up >= 8 && card.Right >= 8) score += 50f;
                if (card.Right >= 8 && card.Down >= 8) score += 50f;
                if (card.Down >= 8 && card.Left >= 8) score += 50f;
                if (card.Left >= 8 && card.Up >= 8) score += 50f;

                if (isFallenAce && card.Sides.Contains(10))
                {
                    score -= 25f; // Aces can be flipped by 1s
                }

                if (isFallenAce && card.Sides.Contains(1))
                {
                    score += 20f; // 1s can capture Aces
                }
            }

            if (isAscension)
            {
                var tribeCounts = deck
                    .Where(c => !string.IsNullOrEmpty(c.Tribe))
                    .GroupBy(c => c.Tribe);

                foreach (var g in tribeCounts)
                {
                    if (g.Count() >= 3)
                    {
                        score += g.Count() * 30f;
                    }
                }
            }

            if (isSame || isPlus)
            {
                // Reward number frequency pairs for combos
                var allSides = deck.SelectMany(c => c.Sides);
                var freq = allSides.GroupBy(v => v).Where(g => g.Count() >= 3);
                foreach (var g in freq)
                {
                    score += g.Count() * 8f;
                }
            }
        }

        return score;
    }
}
