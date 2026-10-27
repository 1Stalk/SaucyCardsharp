using Saucy.TripleTriad.Data;
using Saucy.TripleTriad.GameLogic;
using System;
using System.Collections.Generic;
using System.Linq;

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
            TriadGameAgentCardsharp.SyncCardDatabase();
            db = TriadCardDB.Get();
        }

        foreach (var r in Enum.GetValues<ETriadCardRarity>())
        {
            if (!CardsByRarity.ContainsKey(r))
            {
                CardsByRarity[r] = [];
            }
            else
            {
                CardsByRarity[r].Clear();
            }
        }

        foreach (var card in db.cards)
        {
            if (card != null && card.IsValid() && card.Id > 0 && card.Sides[0] > 0)
            {
                CardsByRarity[card.Rarity].Add(card);
            }
        }

        if (db.cards.Count >= 50)
        {
            _initialized = true;
        }
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

        // Cards that we know the opponent has in hand, but which are currently hidden (e.g. from Swap)
        var unplacedKnownCards = new List<TriadCard>();
        foreach (var card in knownOpponentCards)
        {
            if (card == null || !card.IsValid()) continue;

            var onBoard = false;
            for (var b = 0; b < 9; b++)
            {
                if (state.BoardCards[b] != null && state.BoardCards[b]!.Id == card.Id)
                {
                    onBoard = true;
                    break;
                }
            }
            if (onBoard) continue;

            var inRevealedHand = false;
            for (var h = 0; h < 5; h++)
            {
                if (opponentRevealed[h] && opponentHand[h] != null && opponentHand[h]!.Id == card.Id)
                {
                    inRevealedHand = true;
                    break;
                }
            }

            if (!inRevealedHand && !unplacedKnownCards.Any(c => c.Id == card.Id))
            {
                unplacedKnownCards.Add(card);
            }
        }

        var swappedTiers = (mode == UniversalEngineMode.OpenTournamentDraft)
            ? RarityBudgetTracker.DetectDraftSwap(state)
            : default;
        var tracker = new RarityBudgetTracker(mode, knownOpponentCards, swappedTiers, state.InitialPlayerCards);

        for (var sampleIdx = 0; sampleIdx < sampleCount; sampleIdx++)
        {
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

            var availableHidden = new List<int>(hiddenIndices);

            // 1. Assign known unplaced opponent cards directly to hidden slots
            foreach (var knownCard in unplacedKnownCards)
            {
                if (availableHidden.Count == 0) break;
                var randPos = Random.Shared.Next(availableHidden.Count);
                var slot = availableHidden[randPos];
                availableHidden.RemoveAt(randPos);

                sampleCards[slot] = knownCard;
                usedCardIds.Add(knownCard.Id);
            }

            // 2. Sample remaining unknown slots according to rarity budget
            var neededTiers = (mode == UniversalEngineMode.OpenTournamentDraft)
                ? tracker.GetDraftRemainingTiers(availableHidden.Count)
                : tracker.GetUniversalRemainingTiers(availableHidden.Count, Random.Shared);

            for (var k = neededTiers.Count - 1; k > 0; k--)
            {
                var r = Random.Shared.Next(k + 1);
                (neededTiers[k], neededTiers[r]) = (neededTiers[r], neededTiers[k]);
            }

            for (var slotIdx = 0; slotIdx < availableHidden.Count; slotIdx++)
            {
                var handIndex = availableHidden[slotIdx];
                var tier = (slotIdx < neededTiers.Count) ? neededTiers[slotIdx] : ETriadCardRarity.Common;
                var pool = CardsByRarity.TryGetValue(tier, out var list) && list.Count > 0
                    ? list
                    : (CardsByRarity.Values.FirstOrDefault(l => l.Count > 0) ?? []);

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
