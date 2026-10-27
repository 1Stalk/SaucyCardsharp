using Saucy.TripleTriad.Data;
using Saucy.TripleTriad.GameLogic;
using System;
using System.Collections.Generic;

namespace Saucy.TripleTriad.UniversalEngine;

public class UniversalDeckSampler
{
    private static readonly Dictionary<ETriadCardRarity, List<TriadCard>> CardsByRarity = [];
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        var db = TriadCardDB.Get();
        if (db.cards.Count < 50)
        {
            return;
        }

        foreach (var r in Enum.GetValues<ETriadCardRarity>())
        {
            CardsByRarity[r] = [];
        }

        foreach (var card in db.cards)
        {
            if (card != null && card.IsValid() && card.Id > 0 && card.Sides[0] > 0)
            {
                CardsByRarity[card.Rarity].Add(card);
            }
        }

        _initialized = true;
    }

    public static List<TriadDeckInstanceScreen> SampleOpponentDecks(
        UniversalTriadState state,
        UniversalEngineMode mode,
        int sampleCount)
    {
        EnsureInitialized();

        var sampledDecks = new List<TriadDeckInstanceScreen>(sampleCount);
        var opponentHand = state.OpponentHand;
        var opponentPlaced = state.OpponentCardPlaced;
        var opponentRevealed = state.OpponentCardRevealed;
        var knownOpponentCards = state.KnownOpponentCards;

        if (state.HiddenOpponentCount <= 0)
        {
            // Fully known opponent deck (e.g. All Open)
            sampledDecks.Add(BuildDeckInstance(opponentHand, opponentPlaced));
            return sampledDecks;
        }

        var hiddenIndices = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            if (!opponentPlaced[i] && !opponentRevealed[i])
            {
                hiddenIndices.Add(i);
            }
        }

        var swappedTiers = (mode == UniversalEngineMode.OpenTournamentDraft)
            ? RarityBudgetTracker.DetectDraftSwap(state)
            : default;
        var tracker = new RarityBudgetTracker(mode, knownOpponentCards, swappedTiers, state.InitialPlayerCards);

        for (var sampleIdx = 0; sampleIdx < sampleCount; sampleIdx++)
        {
            List<ETriadCardRarity> neededTiers = mode == UniversalEngineMode.OpenTournamentDraft
                ? tracker.GetDraftRemainingTiers(hiddenIndices.Count)
                : tracker.GetUniversalRemainingTiers(hiddenIndices.Count, Random.Shared);

            // Permute neededTiers so rarities are randomly distributed among hidden slots
            for (var k = neededTiers.Count - 1; k > 0; k--)
            {
                var r = Random.Shared.Next(k + 1);
                (neededTiers[k], neededTiers[r]) = (neededTiers[r], neededTiers[k]);
            }

            var sampleCards = new TriadCard?[5];
            var usedCardIds = new HashSet<int>();

            // Copy known/revealed cards into sample
            for (var i = 0; i < 5; i++)
            {
                if (opponentRevealed[i] && opponentHand[i] != null)
                {
                    sampleCards[i] = opponentHand[i];
                    usedCardIds.Add(opponentHand[i]!.Id);
                }
            }

            // Also exclude known placed board cards
            foreach (var card in knownOpponentCards)
            {
                if (card != null)
                {
                    usedCardIds.Add(card.Id);
                }
            }

            // Sample hidden slots
            for (var slotIdx = 0; slotIdx < hiddenIndices.Count; slotIdx++)
            {
                var handIndex = hiddenIndices[slotIdx];
                var tier = (slotIdx < neededTiers.Count) ? neededTiers[slotIdx] : ETriadCardRarity.Common;
                var pool = CardsByRarity[tier];

                TriadCard? pickedCard = null;
                if (pool.Count > 0)
                {
                    // Try to pick a non-duplicate card
                    for (var attempt = 0; attempt < 10; attempt++)
                    {
                        var candidate = pool[Random.Shared.Next(pool.Count)];
                        if (!usedCardIds.Contains(candidate.Id))
                        {
                            pickedCard = candidate;
                            break;
                        }
                    }

                    // Fallback: pick any card from the tier
                    pickedCard ??= pool[Random.Shared.Next(pool.Count)];
                }

                if (pickedCard != null)
                {
                    sampleCards[handIndex] = pickedCard;
                    usedCardIds.Add(pickedCard.Id);
                }
            }

            sampledDecks.Add(BuildDeckInstance(sampleCards, opponentPlaced));
        }

        return sampledDecks;
    }

    private static TriadDeckInstanceScreen BuildDeckInstance(TriadCard?[] cards, bool[] placed)
    {
        var deck = new TriadDeckInstanceScreen();
        for (var i = 0; i < 5; i++)
        {
            if (cards[i] != null && !placed[i])
            {
                deck.cards[i] = cards[i]!;
                deck.availableCardMask |= (1 << i);
            }
            else
            {
                deck.cards[i] = cards[i];
                if (placed[i])
                {
                    deck.numPlaced++;
                }
            }
        }

        return deck;
    }
}
