using Saucy.TripleTriad.GameLogic;
using System;

namespace Saucy.TripleTriad.UniversalEngine;

public class TerminalRolloutAgent
{
    private const int RolloutsPerCandidatePerSample = 32;

    public float EvaluateMove(
        TriadGameSolver solver,
        TriadGameSimulationState gameState,
        int cardIdx,
        int boardPos,
        ETriadCardOwner localPlayerColor = ETriadCardOwner.Blue,
        int rolloutCount = RolloutsPerCandidatePerSample)
    {
        var nextState = new TriadGameSimulationState(gameState);
        var opponentColor = (localPlayerColor == ETriadCardOwner.Blue) ? ETriadCardOwner.Red : ETriadCardOwner.Blue;
        var turnOwner = (nextState.state == ETriadGameState.InProgressBlue) ? ETriadCardOwner.Blue : ETriadCardOwner.Red;
        var useDeck = (turnOwner == ETriadCardOwner.Blue) ? nextState.deckBlue : nextState.deckRed;

        var isPlaced = solver.simulation.PlaceCard(nextState, cardIdx, useDeck, turnOwner, boardPos);
        if (!isPlaced)
        {
            return 0.0f;
        }

        var winState = (localPlayerColor == ETriadCardOwner.Blue) ? ETriadGameState.BlueWins : ETriadGameState.BlueLost;
        var lossState = (localPlayerColor == ETriadCardOwner.Blue) ? ETriadGameState.BlueLost : ETriadGameState.BlueWins;

        if (nextState.state == winState)
        {
            return 1.0f;
        }
        if (nextState.state == ETriadGameState.BlueDraw)
        {
            return 0.5f;
        }
        if (nextState.state == lossState)
        {
            return 0.0f;
        }

        // Anti-blunder immediate response check:
        // If opponent can immediately capture player's placed card via Same/Plus or high card,
        // test if opponent has an overwhelming counter-move.
        solver.FindAvailableActions(nextState, out var availBoardMask, out _, out var availCardsMask, out _);
        var oppBestFlipCount = 0;
        var oppHasCombo = false;
        var oppTurnDeck = (opponentColor == ETriadCardOwner.Blue) ? nextState.deckBlue : nextState.deckRed;

        for (var oCard = 0; oCard < TriadDeckInstance.maxAvailableCards; oCard++)
        {
            if ((availCardsMask & (1 << oCard)) == 0)
            {
                continue;
            }

            for (var oPos = 0; oPos < nextState.board.Length; oPos++)
            {
                if ((availBoardMask & (1 << oPos)) == 0)
                {
                    continue;
                }

                // Check if this position is adjacent to our newly placed card
                var neis = TriadGameSimulation.cachedNeis[oPos];
                var isAdjacent = false;
                for (var s = 0; s < 4; s++)
                {
                    if (neis[s] == boardPos)
                    {
                        isAdjacent = true;
                        break;
                    }
                }

                if (!isAdjacent)
                {
                    continue;
                }

                var testState = new TriadGameSimulationState(nextState);
                var prevLocalCount = CountBoardOwner(testState, localPlayerColor);
                if (solver.simulation.PlaceCard(testState, oCard, oppTurnDeck, opponentColor, oPos))
                {
                    var newLocalCount = CountBoardOwner(testState, localPlayerColor);
                    var flipped = prevLocalCount - newLocalCount;
                    if (flipped > oppBestFlipCount)
                    {
                        oppBestFlipCount = flipped;
                    }
                    if (flipped >= 2)
                    {
                        oppHasCombo = true;
                    }
                }
            }
        }

        // Severe anti-blunder penalty: if player's move allows opponent an immediate combo flipping 2+ cards
        var penalty = 0.0f;
        if (oppHasCombo)
        {
            penalty = 0.20f;
        }
        else if (oppBestFlipCount >= 1 && nextState.numCardsPlaced <= 2)
        {
            // Early opening blunder (lost card on turn 1 or 2)
            penalty = 0.08f;
        }

        // Execute rapid Monte Carlo rollouts to terminal (depth 9)
        var winCount = 0;
        var drawCount = 0;
        var randAgent = new TriadGameAgentRandom(solver, Random.Shared.Next());

        for (var r = 0; r < rolloutCount; r++)
        {
            var rolloutState = new TriadGameSimulationState(nextState);
            solver.RunSimulation(rolloutState, randAgent, randAgent);

            if (rolloutState.state == winState)
            {
                winCount++;
            }
            else if (rolloutState.state == ETriadGameState.BlueDraw)
            {
                drawCount++;
            }
        }

        var ev = (winCount + (drawCount * 0.5f)) / rolloutCount;
        return Math.Max(0.0f, ev - penalty);
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
