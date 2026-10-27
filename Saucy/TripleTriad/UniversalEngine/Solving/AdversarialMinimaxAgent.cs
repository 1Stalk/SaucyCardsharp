using Saucy.TripleTriad.GameLogic;
using System;

namespace Saucy.TripleTriad.UniversalEngine;

public class AdversarialMinimaxAgent
{
    private const int MaxSearchDepth = 10;

    public float EvaluateMove(
        TriadGameSolver solver,
        TriadGameSimulationState gameState,
        int cardIdx,
        int boardPos,
        ETriadCardOwner localPlayerColor = ETriadCardOwner.Blue,
        int depthLimit = 6)
    {
        var nextState = new TriadGameSimulationState(gameState);
        var turnOwner = (nextState.state == ETriadGameState.InProgressBlue) ? ETriadCardOwner.Blue : ETriadCardOwner.Red;
        var useDeck = (turnOwner == ETriadCardOwner.Blue) ? nextState.deckBlue : nextState.deckRed;

        var isPlaced = solver.simulation.PlaceCard(nextState, cardIdx, useDeck, turnOwner, boardPos);
        if (!isPlaced)
        {
            return 0.0f;
        }

        if (IsTerminal(nextState, localPlayerColor, out var terminalScore))
        {
            return terminalScore;
        }

        nextState.forcedCardIdx = -1;
        var hasOrder = solver.simulation.modifiers.Exists(m => m.IsDeckOrderImportant());
        return Minimax(solver, nextState, 1, depthLimit, 0.0f, 1.0f, hasOrder, localPlayerColor);
    }

    private float Minimax(
        TriadGameSolver solver,
        TriadGameSimulationState state,
        int currentDepth,
        int depthLimit,
        float alpha,
        float beta,
        bool hasOrder,
        ETriadCardOwner localPlayerColor)
    {
        if (IsTerminal(state, localPlayerColor, out var terminalScore))
        {
            return terminalScore;
        }

        if (currentDepth >= depthLimit || currentDepth >= MaxSearchDepth)
        {
            return EvaluateHeuristic(state, localPlayerColor);
        }

        solver.FindAvailableActions(state, out var availBoardMask, out var numAvailBoard, out var availCardsMask, out var numAvailCards);
        if (state.deckBlue.availableCardMask == 0 && state.deckRed.availableCardMask == 0)
        {
            return EvaluateHeuristic(state, localPlayerColor);
        }

        var isBlue = (state.state == ETriadGameState.InProgressBlue);
        var activeDeck = isBlue ? state.deckBlue : state.deckRed;
        if (hasOrder)
        {
            var firstIdx = activeDeck.GetFirstAvailableCardFast();
            if (firstIdx >= 0)
            {
                availCardsMask = (1 << firstIdx);
                numAvailCards = 1;
            }
        }

        if (numAvailCards == 0 || numAvailBoard == 0)
        {
            return EvaluateHeuristic(state, localPlayerColor);
        }
        var turnOwner = isBlue ? ETriadCardOwner.Blue : ETriadCardOwner.Red;
        var isMaximizing = (turnOwner == localPlayerColor);

        if (isMaximizing)
        {
            // Maximizing player (Local Player)
            var maxScore = 0.0f;
            for (var cIdx = 0; cIdx < TriadDeckInstance.maxAvailableCards; cIdx++)
            {
                if ((availCardsMask & (1 << cIdx)) == 0)
                {
                    continue;
                }

                for (var bIdx = 0; bIdx < state.board.Length; bIdx++)
                {
                    if ((availBoardMask & (1 << bIdx)) == 0)
                    {
                        continue;
                    }

                    var copyState = new TriadGameSimulationState(state);
                    var copyDeck = isBlue ? copyState.deckBlue : copyState.deckRed;
                    if (!solver.simulation.PlaceCard(copyState, cIdx, copyDeck, turnOwner, bIdx))
                    {
                        continue;
                    }

                    copyState.forcedCardIdx = -1;
                    var score = Minimax(solver, copyState, currentDepth + 1, depthLimit, alpha, beta, hasOrder, localPlayerColor);
                    if (score > maxScore)
                    {
                        maxScore = score;
                    }
                    if (score > alpha)
                    {
                        alpha = score;
                    }
                    if (alpha >= beta || alpha >= 1.0f)
                    {
                        return maxScore;
                    }
                }
            }

            return maxScore;
        }
        else
        {
            // Minimizing player (Opponent)
            // Separate into capturing moves (where Opponent attacks Player) vs peaceful placements
            var minScore = 1.0f;
            var sumPeacefulScore = 0.0f;
            var peacefulCount = 0;
            var hasCapturingMove = false;

            for (var cIdx = 0; cIdx < TriadDeckInstance.maxAvailableCards; cIdx++)
            {
                if ((availCardsMask & (1 << cIdx)) == 0)
                {
                    continue;
                }

                for (var bIdx = 0; bIdx < state.board.Length; bIdx++)
                {
                    if ((availBoardMask & (1 << bIdx)) == 0)
                    {
                        continue;
                    }

                    var copyState = new TriadGameSimulationState(state);
                    var prevLocalCount = CountBoardOwner(state, localPlayerColor);
                    var copyDeck = isBlue ? copyState.deckBlue : copyState.deckRed;
                    if (!solver.simulation.PlaceCard(copyState, cIdx, copyDeck, turnOwner, bIdx))
                    {
                        continue;
                    }

                    var newLocalCount = CountBoardOwner(copyState, localPlayerColor);
                    var isCapture = newLocalCount < prevLocalCount;

                    copyState.forcedCardIdx = -1;
                    var score = Minimax(solver, copyState, currentDepth + 1, depthLimit, alpha, beta, hasOrder, localPlayerColor);

                    if (isCapture)
                    {
                        hasCapturingMove = true;
                        if (score < minScore)
                        {
                            minScore = score;
                        }
                        if (score < beta)
                        {
                            beta = score;
                        }
                        if (beta <= alpha || beta <= 0.0f)
                        {
                            return minScore;
                        }
                    }
                    else
                    {
                        sumPeacefulScore += score;
                        peacefulCount++;
                        if (score < minScore)
                        {
                            minScore = score;
                        }
                    }
                }
            }

            // If opponent has a direct capture, opponent executes their best counter-move (Min).
            // If opponent has no capturing moves, use realistic average expected outcome (Expectimax).
            if (hasCapturingMove)
            {
                return minScore;
            }

            return peacefulCount > 0 ? (sumPeacefulScore / peacefulCount) : minScore;
        }
    }

    private static bool IsTerminal(TriadGameSimulationState state, ETriadCardOwner localPlayerColor, out float score)
    {
        var winState = (localPlayerColor == ETriadCardOwner.Blue) ? ETriadGameState.BlueWins : ETriadGameState.BlueLost;
        var lossState = (localPlayerColor == ETriadCardOwner.Blue) ? ETriadGameState.BlueLost : ETriadGameState.BlueWins;

        if (state.state == winState)
        {
            score = 1.0f;
            return true;
        }
        if (state.state == ETriadGameState.BlueDraw)
        {
            score = 0.5f;
            return true;
        }
        if (state.state == lossState)
        {
            score = 0.0f;
            return true;
        }

        score = 0.0f;
        return false;
    }

    private static float EvaluateHeuristic(TriadGameSimulationState state, ETriadCardOwner localPlayerColor)
    {
        var opponentColor = (localPlayerColor == ETriadCardOwner.Blue) ? ETriadCardOwner.Red : ETriadCardOwner.Blue;
        var localCount = 0;
        var oppCount = 0;

        for (var i = 0; i < state.board.Length; i++)
        {
            if (state.board[i] == null)
            {
                continue;
            }

            if (state.board[i].owner == localPlayerColor)
            {
                localCount++;
            }
            else if (state.board[i].owner == opponentColor)
            {
                oppCount++;
            }
        }

        var availLocal = CountBits(localPlayerColor == ETriadCardOwner.Blue ? state.deckBlue.availableCardMask : state.deckRed.availableCardMask);
        var availOpp = CountBits(opponentColor == ETriadCardOwner.Blue ? state.deckBlue.availableCardMask : state.deckRed.availableCardMask);
        var localTotal = localCount + availLocal;
        var oppTotal = oppCount + availOpp;

        if (localTotal > oppTotal)
        {
            return 0.6f + Math.Min(0.35f, (localTotal - oppTotal) * 0.1f);
        }
        if (localTotal < oppTotal)
        {
            return 0.4f - Math.Min(0.35f, (oppTotal - localTotal) * 0.1f);
        }

        return 0.5f;
    }

    private static int CountBits(int value)
    {
        var count = 0;
        while (value != 0)
        {
            count += (value & 1);
            value >>= 1;
        }
        return count;
    }

    private static int CountBoardOwner(TriadGameSimulationState state, ETriadCardOwner owner)
    {
        var count = 0;
        for (var i = 0; i < state.board.Length; i++)
        {
            if (state.board[i] != null && state.board[i].owner == owner)
            {
                count++;
            }
        }
        return count;
    }
}
