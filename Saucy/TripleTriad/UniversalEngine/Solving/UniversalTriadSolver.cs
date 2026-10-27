using Saucy.TripleTriad.GameLogic;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Saucy.TripleTriad.UniversalEngine;

public class UniversalTriadSolver
{
    public UniversalEngineMode Mode { get; }
    public int SampleCount { get; }
    public int MaxThreads { get; }

    public UniversalTriadSolver(UniversalEngineMode mode, int sampleCount = 300, int maxThreads = 0)
    {
        Mode = mode;
        SampleCount = Math.Max(1, sampleCount);
        MaxThreads = maxThreads;
    }

    public struct CandidateMove
    {
        public int CardIdx;
        public int BoardPos;
        public float EV;
        public float PositionalScore;
    }

    public (int bestCardIdx, int bestBoardPos, float bestEv, List<CandidateMove> candidates) Solve(UniversalTriadState state)
    {
        var legalMoves = GetLegalMoves(state);
        if (legalMoves.Count == 0)
        {
            return (-1, -1, 0.0f, []);
        }

        if (legalMoves.Count == 1)
        {
            return (legalMoves[0].CardIdx, legalMoves[0].BoardPos, 0.5f, []);
        }

        // Determine search depth:
        // Turns 1-2 (<= 1 placed): Depth-4 Minimax (Player -> Opponent Best Counter -> Player -> Opponent + Heuristic)
        // Turns 3-4 (2-3 placed): Depth-6 Minimax
        // Turns 5-9 (>= 4 placed): Depth-10 Exhaustive Terminal Minimax
        var isEndgame = state.NumCardsPlaced >= 4;
        var searchDepth = isEndgame ? 10 : (state.NumCardsPlaced <= 1 ? 4 : 6);

        var moveScores = new ConcurrentDictionary<(int CardIdx, int BoardPos), float>();
        foreach (var move in legalMoves)
        {
            moveScores[(move.CardIdx, move.BoardPos)] = 0.0f;
        }

        var minimaxAgent = new AdversarialMinimaxAgent();

        if (state.HiddenOpponentCount <= 0)
        {
            // Deterministic single solve (All Open or endgame)
            var baseSolver = CreateSolver(state);
            var baseGameState = BuildInitialGameState(state, BuildDeckInstance(state.OpponentHand, state.OpponentCardPlaced));

            foreach (var move in legalMoves)
            {
                var score = minimaxAgent.EvaluateMove(baseSolver, baseGameState, move.CardIdx, move.BoardPos, state.LocalPlayerColor, depthLimit: searchDepth);
                moveScores[(move.CardIdx, move.BoardPos)] = score;
            }

            return SelectBestMove(state, legalMoves, moveScores, 1);
        }

        // PIMC: Sample N plausible opponent decks and evaluate in parallel
        var sampledDecks = UniversalDeckSampler.SampleOpponentDecks(state, Mode, SampleCount);
        var baseSolverInstance = CreateSolver(state);

        using var threadSolvers = new ThreadLocal<TriadGameSolver>(baseSolverInstance.CreateWorkerCopy);

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxThreads <= 0 ? Environment.ProcessorCount : Math.Clamp(MaxThreads, 1, Environment.ProcessorCount)
        };

        Parallel.ForEach(sampledDecks, parallelOptions, sampledDeck =>
        {
            var solver = threadSolvers.Value!;
            var gameState = BuildInitialGameState(state, sampledDeck);

            foreach (var move in legalMoves)
            {
                var score = minimaxAgent.EvaluateMove(solver, gameState, move.CardIdx, move.BoardPos, state.LocalPlayerColor, depthLimit: searchDepth);

                moveScores.AddOrUpdate(
                    (move.CardIdx, move.BoardPos),
                    score,
                    (_, current) => current + score);
            }
        });

        return SelectBestMove(state, legalMoves, moveScores, sampledDecks.Count);
    }

    private static (int bestCardIdx, int bestBoardPos, float bestEv, List<CandidateMove> candidates) SelectBestMove(
        UniversalTriadState state,
        List<(int CardIdx, int BoardPos)> legalMoves,
        ConcurrentDictionary<(int CardIdx, int BoardPos), float> moveScores,
        int totalSamples)
    {
        var candidates = new List<CandidateMove>(legalMoves.Count);
        var hand = state.LocalPlayerHand;

        foreach (var move in legalMoves)
        {
            var sumScore = moveScores[(move.CardIdx, move.BoardPos)];
            var ev = sumScore / totalSamples;
            var posScore = PositionalSafetyEvaluator.Evaluate(state, hand[move.CardIdx], move.BoardPos);

            candidates.Add(new CandidateMove
            {
                CardIdx = move.CardIdx,
                BoardPos = move.BoardPos,
                EV = ev,
                PositionalScore = posScore
            });
        }

        var maxEv = candidates.Max(c => c.EV);
        var isEndgame = state.NumCardsPlaced >= 4;
        var evTieThreshold = isEndgame ? 0.005f : 0.03f;

        // Candidate moves sharing highest EV within threshold
        var topTier = candidates.Where(c => c.EV >= maxEv - evTieThreshold).ToList();

        // Sort by PositionalScore descending FIRST to break EV ties with strategic corner/wall safety,
        // then by EV descending
        var best = topTier
            .OrderByDescending(c => c.PositionalScore)
            .ThenByDescending(c => c.EV)
            .First();

        return (best.CardIdx, best.BoardPos, best.EV, candidates);
    }

    private static List<(int CardIdx, int BoardPos)> GetLegalMoves(UniversalTriadState state)
    {
        var moves = new List<(int CardIdx, int BoardPos)>();

        var hasOrder = state.Modifiers.Exists(m => m.IsDeckOrderImportant()) ||
            state.RuleNames.Exists(r =>
                r.Equals("Order", StringComparison.OrdinalIgnoreCase) ||
                r.Equals("Ordre", StringComparison.OrdinalIgnoreCase) ||
                r.Equals("Reihenfolge", StringComparison.OrdinalIgnoreCase) ||
                r.Equals("オーダー", StringComparison.OrdinalIgnoreCase) ||
                r.Equals("顺序", StringComparison.OrdinalIgnoreCase));

        var validCardIndices = new List<int>();
        var hand = state.LocalPlayerHand;
        var placed = state.LocalCardPlaced;

        if (state.ForcedCardIdx >= 0 &&
            state.ForcedCardIdx < 5 &&
            hand[state.ForcedCardIdx] != null &&
            !placed[state.ForcedCardIdx])
        {
            validCardIndices.Add(state.ForcedCardIdx);
        }
        else if (hasOrder)
        {
            for (var i = 0; i < 5; i++)
            {
                if (hand[i] != null && !placed[i])
                {
                    validCardIndices.Add(i);
                    break;
                }
            }
        }
        else
        {
            for (var i = 0; i < 5; i++)
            {
                if (hand[i] != null && !placed[i])
                {
                    validCardIndices.Add(i);
                }
            }
        }

        var emptyBoardIndices = new List<int>();
        for (var i = 0; i < 9; i++)
        {
            if (state.BoardCards[i] == null)
            {
                emptyBoardIndices.Add(i);
            }
        }

        foreach (var cIdx in validCardIndices)
        {
            foreach (var bIdx in emptyBoardIndices)
            {
                moves.Add((cIdx, bIdx));
            }
        }

        return moves;
    }

    private static TriadGameSolver CreateSolver(UniversalTriadState state)
    {
        var solver = new TriadGameSolver(new TriadGameAgentRandom(null, 0));
        solver.InitializeSimulation(state.Modifiers);
        return solver;
    }

    private static TriadGameSimulationState BuildInitialGameState(
        UniversalTriadState state,
        TriadDeckInstanceScreen opponentDeckInstance)
    {
        var isPlayerBlue = state.LocalPlayerColor == ETriadCardOwner.Blue;
        var playerDeck = BuildDeckInstance(state.LocalPlayerHand, state.LocalCardPlaced);

        var gameState = new TriadGameSimulationState
        {
            state = isPlayerBlue ? ETriadGameState.InProgressBlue : ETriadGameState.InProgressRed,
            deckBlue = isPlayerBlue ? playerDeck : opponentDeckInstance,
            deckRed = isPlayerBlue ? opponentDeckInstance : playerDeck,
            forcedCardIdx = state.ForcedCardIdx,
            numCardsPlaced = state.NumCardsPlaced
        };

        for (var i = 0; i < 9; i++)
        {
            if (state.BoardCards[i] != null)
            {
                gameState.board[i] = new TriadCardInstance(state.BoardCards[i]!, state.BoardOwners[i]);
            }
            else
            {
                gameState.board[i] = null;
            }
        }

        // Apply board-dependent modifier updates (e.g. Ascension/Descension)
        foreach (var mod in state.Modifiers)
        {
            mod.OnScreenUpdate(gameState);
        }

        return gameState;
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
