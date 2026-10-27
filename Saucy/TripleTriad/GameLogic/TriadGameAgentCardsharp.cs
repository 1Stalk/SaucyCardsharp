using System;
using System.Collections.Generic;
using XIVcardsharp.Core.Data;
using XIVcardsharp.Core.Engine;
using XIVcardsharp.Core.Solving;
using XIVcardsharp.Core.State;
using CoreCard = XIVcardsharp.Core.Data.TriadCard;
using CoreOwner = XIVcardsharp.Core.Data.ETriadCardOwner;
using CoreModifier = XIVcardsharp.Core.Engine.TriadGameModifier;

namespace Saucy.TripleTriad.GameLogic;

public class TriadGameAgentCardsharp : TriadGameAgent
{
    private static bool _cardDbSynced;
    private XIVcardsharp.Core.Solving.UniversalTriadSolver? _solver;
    public int SampleCount { get; }
    public int MaxThreads { get; }

    public TriadGameAgentCardsharp(int sampleCount = 300, int maxThreads = 0)
    {
        SampleCount = sampleCount;
        MaxThreads = maxThreads;
    }

    private XIVcardsharp.Core.Solving.UniversalTriadSolver GetSolver()
    {
        var sampleCount = C != null ? C.UniversalTriadSampleCount : SampleCount;
        var maxThreads = SaucyParallelism.UniversalTriadThreads;
        if (_solver == null || _solver.SampleCount != sampleCount || _solver.MaxThreads != maxThreads)
        {
            _solver = new XIVcardsharp.Core.Solving.UniversalTriadSolver(
                XIVcardsharp.Core.State.UniversalEngineMode.UniversalMatch,
                sampleCount,
                maxThreads);
        }

        return _solver;
    }

    public static void SyncCardDatabase()
    {
        if (_cardDbSynced)
        {
            return;
        }

        var saucyCards = Data.TriadCardDB.Get().cards;
        if (saucyCards == null || saucyCards.Count == 0)
        {
            return;
        }

        var coreDb = XIVcardsharp.Core.Data.TriadCardDB.Get();
        coreDb.cards.Clear();
        coreDb.sameNumberMap.Clear();

        foreach (var card in saucyCards)
        {
            if (card == null)
            {
                coreDb.cards.Add(null);
                continue;
            }

            var coreCard = new CoreCard(
                card.Id,
                (XIVcardsharp.Core.Data.ETriadCardRarity)(int)card.Rarity,
                (XIVcardsharp.Core.Data.ETriadCardType)(int)card.Type,
                card.Sides[(int)ETriadGameSide.Up],
                card.Sides[(int)ETriadGameSide.Down],
                card.Sides[(int)ETriadGameSide.Left],
                card.Sides[(int)ETriadGameSide.Right],
                card.SortOrder,
                card.Group)
            {
                Name = card.Name ?? string.Empty,
                SameNumberId = card.SameNumberId,
                OptimizerScore = card.OptimizerScore
            };

            coreDb.cards.Add(coreCard);

            if (coreCard.SameNumberId >= 0)
            {
                if (!coreDb.sameNumberMap.TryGetValue(coreCard.SameNumberId, out var group))
                {
                    group = [];
                    coreDb.sameNumberMap[coreCard.SameNumberId] = group;
                }
                group.Add(coreCard);
            }
        }

        _cardDbSynced = true;
    }

    public override bool FindNextMove(
        TriadGameSolver solver,
        TriadGameSimulationState gameState,
        out int cardIdx,
        out int boardPos,
        out SolverResult solverResult)
    {
        cardIdx = -1;
        boardPos = -1;
        solverResult = SolverResult.Zero;

        if (gameState == null)
        {
            return false;
        }

        SyncCardDatabase();

        var state = ConvertToUniversalState(solver, gameState);
        var (bestCard, bestPos, ev, _) = GetSolver().Solve(state);

        cardIdx = bestCard;
        boardPos = bestPos;
        solverResult = new SolverResult(ev, 1f - ev, 1);

        return cardIdx >= 0 && boardPos >= 0;
    }

    private static XIVcardsharp.Core.State.UniversalTriadState ConvertToUniversalState(TriadGameSolver solver, TriadGameSimulationState gameState)
    {
        var localColor = (gameState.state == ETriadGameState.InProgressBlue) ? CoreOwner.Blue : CoreOwner.Red;
        var coreDb = XIVcardsharp.Core.Data.TriadCardDB.Get();

        var state = new XIVcardsharp.Core.State.UniversalTriadState
        {
            LocalPlayerColor = localColor,
            ForcedCardIdx = gameState.forcedCardIdx,
            IsPlayerTurn = true
        };

        // Convert Blue Hand
        for (var i = 0; i < 5; i++)
        {
            var card = gameState.deckBlue.GetCard(i);
            var isAvailable = (gameState.deckBlue.availableCardMask & (1 << i)) != 0;
            state.BlueCardPlaced[i] = !isAvailable;

            if (card != null && card.Id > 0 && card.Id < coreDb.cards.Count)
            {
                state.BlueHand[i] = coreDb.cards[card.Id];
                state.BlueCardRevealed[i] = true;
                if (!state.KnownBlueCards.Contains(state.BlueHand[i]!))
                {
                    state.KnownBlueCards.Add(state.BlueHand[i]!);
                }
            }
            else
            {
                state.BlueHand[i] = null;
                state.BlueCardRevealed[i] = false;
                if (isAvailable)
                {
                    state.HiddenBlueCount++;
                }
            }
        }

        // Convert Red Hand (e.g. NPC)
        for (var i = 0; i < 5; i++)
        {
            var card = gameState.deckRed.GetCard(i);
            var isAvailable = (gameState.deckRed.availableCardMask & (1 << i)) != 0;
            state.RedCardPlaced[i] = !isAvailable;

            if (card != null && card.Id > 0 && card.Id < coreDb.cards.Count)
            {
                state.RedHand[i] = coreDb.cards[card.Id];
                state.RedCardRevealed[i] = true;
                if (!state.KnownRedCards.Contains(state.RedHand[i]!))
                {
                    state.KnownRedCards.Add(state.RedHand[i]!);
                }
            }
            else
            {
                state.RedHand[i] = null;
                state.RedCardRevealed[i] = false;
                if (isAvailable)
                {
                    state.HiddenRedCount++;
                }
            }
        }

        // Ingest NPC known deck pool if available
        if (gameState.deckRed?.deck != null)
        {
            foreach (var c in gameState.deckRed.deck.knownCards)
            {
                if (c != null && c.Id > 0 && c.Id < coreDb.cards.Count)
                {
                    var coreC = coreDb.cards[c.Id];
                    if (coreC != null && !state.KnownRedCards.Contains(coreC))
                    {
                        state.KnownRedCards.Add(coreC);
                    }
                }
            }
            foreach (var c in gameState.deckRed.deck.unknownCardPool)
            {
                if (c != null && c.Id > 0 && c.Id < coreDb.cards.Count)
                {
                    var coreC = coreDb.cards[c.Id];
                    if (coreC != null && !state.KnownRedCards.Contains(coreC))
                    {
                        state.KnownRedCards.Add(coreC);
                    }
                }
            }
        }

        // Snapshot initial player cards for swap detection
        var playerHand = (localColor == CoreOwner.Blue) ? state.BlueHand : state.RedHand;
        for (var i = 0; i < 5; i++)
        {
            if (playerHand[i] != null)
            {
                state.InitialPlayerCards.Add(playerHand[i]!);
            }
        }

        // Convert Board
        for (var i = 0; i < 9; i++)
        {
            var cardInstance = gameState.board[i];
            if (cardInstance?.card != null && cardInstance.card.Id > 0 && cardInstance.card.Id < coreDb.cards.Count)
            {
                state.BoardCards[i] = coreDb.cards[cardInstance.card.Id];
                state.BoardOwners[i] = (CoreOwner)cardInstance.owner;
                state.NumCardsPlaced++;
            }
            else
            {
                state.BoardCards[i] = null;
                state.BoardOwners[i] = CoreOwner.Unknown;
            }
        }

        // Convert Modifiers
        if (solver.simulation.modifiers != null)
        {
            foreach (var mod in solver.simulation.modifiers)
            {
                var coreMod = CreateCoreModifier(mod);
                if (coreMod != null)
                {
                    state.Modifiers.Add(coreMod);
                    state.RuleNames.Add(coreMod.GetCodeName());
                }
            }
        }

        return state;
    }

    private static CoreModifier? CreateCoreModifier(TriadGameModifier mod)
    {
        var name = mod.GetCodeName()?.ToLowerInvariant() ?? "";
        if (name.Contains("same") && name.Contains("wall")) return new XIVcardsharp.Core.Engine.TriadGameModifierSame();
        if (name.Contains("same")) return new XIVcardsharp.Core.Engine.TriadGameModifierSame();
        if (name.Contains("plus")) return new XIVcardsharp.Core.Engine.TriadGameModifierPlus();
        if (name.Contains("reverse")) return new XIVcardsharp.Core.Engine.TriadGameModifierReverse();
        if (name.Contains("fallen")) return new XIVcardsharp.Core.Engine.TriadGameModifierFallenAce();
        if (name.Contains("order")) return new XIVcardsharp.Core.Engine.TriadGameModifierOrder();
        if (name.Contains("chaos")) return new XIVcardsharp.Core.Engine.TriadGameModifierChaos();
        if (name.Contains("ascension")) return new XIVcardsharp.Core.Engine.TriadGameModifierAscension();
        if (name.Contains("descension")) return new XIVcardsharp.Core.Engine.TriadGameModifierDescension();
        if (name.Contains("swap")) return new XIVcardsharp.Core.Engine.TriadGameModifierSwap();
        if (name.Contains("draft")) return new XIVcardsharp.Core.Engine.TriadGameModifierDraft();
        if (name.Contains("three open")) return new XIVcardsharp.Core.Engine.TriadGameModifierThreeOpen();
        if (name.Contains("all open")) return new XIVcardsharp.Core.Engine.TriadGameModifierAllOpen();
        return null;
    }
}
