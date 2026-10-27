using Saucy.TripleTriad.GameLogic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Saucy.TripleTriad.UniversalEngine;

public class RarityBudgetTracker
{
    public UniversalEngineMode Mode { get; }
    public Dictionary<ETriadCardRarity, int> KnownCounts { get; } = [];
    public (ETriadCardRarity? GivenTier, ETriadCardRarity? TakenTier) SwappedTiers { get; }
    public IReadOnlyList<TriadCard> InitialPlayerCards { get; }
    public IReadOnlyList<TriadCard> InitialBlueCards => InitialPlayerCards;

    public RarityBudgetTracker(
        UniversalEngineMode mode,
        IEnumerable<TriadCard> knownOpponentCards,
        (ETriadCardRarity? given, ETriadCardRarity? taken) swappedTiers = default,
        IReadOnlyList<TriadCard>? initialPlayerCards = null)
    {
        Mode = mode;
        SwappedTiers = swappedTiers;
        InitialPlayerCards = initialPlayerCards ?? [];
        foreach (var r in Enum.GetValues<ETriadCardRarity>())
        {
            KnownCounts[r] = 0;
        }

        foreach (var card in knownOpponentCards)
        {
            if (card != null && card.IsValid())
            {
                KnownCounts[card.Rarity]++;
            }
        }
    }

    public static (ETriadCardRarity? givenTier, ETriadCardRarity? takenTier) DetectDraftSwap(UniversalTriadState state)
    {
        var hasSwap = state.Modifiers.Exists(m => m is TriadGameModifierSwap) ||
            state.RuleNames.Exists(r =>
                r.Contains("Swap", StringComparison.OrdinalIgnoreCase) ||
                r.Contains("Échange", StringComparison.OrdinalIgnoreCase) ||
                r.Contains("Tausch", StringComparison.OrdinalIgnoreCase) ||
                r.Contains("スワップ", StringComparison.OrdinalIgnoreCase) ||
                r.Contains("交换", StringComparison.OrdinalIgnoreCase));

        if (!hasSwap)
        {
            return (null, null);
        }

        var sourceCards = state.InitialPlayerCards.Count == 5
            ? state.InitialPlayerCards
            : [.. state.LocalPlayerHand.Where(c => c != null).Select(c => c!)];

        if (sourceCards.Count != 5)
        {
            return (null, null);
        }

        var counts = new Dictionary<ETriadCardRarity, int>();
        foreach (var r in Enum.GetValues<ETriadCardRarity>())
        {
            counts[r] = 0;
        }

        foreach (var card in sourceCards)
        {
            if (card != null && card.IsValid())
            {
                counts[card.Rarity]++;
            }
        }

        ETriadCardRarity? given = null;
        ETriadCardRarity? taken = null;

        foreach (var (rarity, count) in counts)
        {
            if (count == 0)
            {
                given = rarity;
            }
            else if (count > 1)
            {
                taken = rarity;
            }
        }

        return (given, taken);
    }

    public List<ETriadCardRarity> GetDraftRemainingTiers(int hiddenCount)
    {
        var expectedRedCounts = new Dictionary<ETriadCardRarity, int>();
        var draftTiers = new[]
        {
            ETriadCardRarity.Legendary,
            ETriadCardRarity.Epic,
            ETriadCardRarity.Rare,
            ETriadCardRarity.Uncommon,
            ETriadCardRarity.Common
        };

        if (InitialPlayerCards.Count == 5)
        {
            // Mathematical invariant of Open Tournament Draft:
            // Across both players, there are exactly 2 cards of each tier in the match (total 10 cards).
            // Therefore, at round start (Normal, Swap, or Sudden Death Round 2):
            // OpponentExpected(tier) = 2 - PlayerInitial(tier).
            foreach (var rarity in draftTiers)
            {
                var playerCount = InitialPlayerCards.Count(c => c != null && c.Rarity == rarity);
                expectedRedCounts[rarity] = Math.Max(0, 2 - playerCount);
            }
        }
        else
        {
            // Fallback when 5 initial cards weren't snapshotted
            foreach (var rarity in draftTiers)
            {
                expectedRedCounts[rarity] = 1;
            }

            if (SwappedTiers.GivenTier.HasValue && SwappedTiers.TakenTier.HasValue)
            {
                expectedRedCounts[SwappedTiers.GivenTier.Value]++;
                expectedRedCounts[SwappedTiers.TakenTier.Value]--;
            }
        }

        var tiers = new List<ETriadCardRarity>();
        foreach (var rarity in draftTiers)
        {
            var remaining = expectedRedCounts[rarity] - KnownCounts[rarity];
            for (var k = 0; k < remaining; k++)
            {
                tiers.Add(rarity);
            }
        }

        // Adjust if known count exceeds or differs from expected
        while (tiers.Count > hiddenCount)
        {
            tiers.RemoveAt(tiers.Count - 1);
        }
        while (tiers.Count < hiddenCount)
        {
            tiers.Add(ETriadCardRarity.Common);
        }

        return tiers;
    }

    public List<ETriadCardRarity> GetUniversalRemainingTiers(int hiddenCount, Random? rng = null)
    {
        var tiers = new List<ETriadCardRarity>();
        var canHave5 = KnownCounts[ETriadCardRarity.Legendary] == 0;
        var canHave4 = (KnownCounts[ETriadCardRarity.Legendary] + KnownCounts[ETriadCardRarity.Epic]) < 2;

        for (var i = 0; i < hiddenCount; i++)
        {
            if (canHave5)
            {
                tiers.Add(ETriadCardRarity.Legendary);
                canHave5 = false;
            }
            else if (canHave4)
            {
                tiers.Add(ETriadCardRarity.Epic);
                canHave4 = false;
            }
            else
            {
                var random = rng ?? Random.Shared;
                var roll = random.Next(100);
                if (roll < 80)
                {
                    tiers.Add(ETriadCardRarity.Rare);
                }
                else if (roll < 95)
                {
                    tiers.Add(ETriadCardRarity.Uncommon);
                }
                else
                {
                    tiers.Add(ETriadCardRarity.Common);
                }
            }
        }

        return tiers;
    }
}
