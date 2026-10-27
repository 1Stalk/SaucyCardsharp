using Saucy.TripleTriad.UniversalEngine;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
namespace Saucy.TripleTriad.UI;

public partial class TriadSession
{
    private Task<(int bestCardIdx, int bestBoardPos, float ev, int evalMs)>? _evalTask;

    public void UpdateGame(UIStateTriadGame? stateOb)
    {
        status = Status.NoErrors;

        if (stateOb == null || !TriadUiState.IsBoardVisible())
        {
            if (UniversalTriadManager.Status != UniversalEngineStatus.Idle &&
                TriadRunSession.ModuleEnabled &&
                !C.UniversalTriadAutoplayEnabled &&
                !C.UniversalTriadTournamentEnabled)
            {
                UniversalTriadManager.Reset();
            }
        }

        TriadBoardScanner.GameState? screenOb = null;
        if (stateOb != null)
        {
            var parseCtx = new GameUIParser();
            screenOb = stateOb.ToTriadScreenState(parseCtx);
            currentNpc = stateOb.ToTriadNpc(parseCtx);

            if (parseCtx.HasErrors)
            {
                currentNpc = null;
                status =
                    parseCtx.hasFailedCard ? Status.FailedToParseCards :
                    parseCtx.hasFailedModifier ? Status.FailedToParseRules :
                    parseCtx.hasFailedNpc ? Status.FailedToParseNpc :
                    Status.NoErrors;
            }
        }
        else
        {
            currentNpc = null;
        }

        if (currentNpc != null)
        {
            lastGameNpc = currentNpc;
        }

        _pauseOptimizerForActiveTriad = stateOb != null && TriadUiState.IsBoardVisible();
        if (_pauseOptimizerForActiveTriad)
        {
            if (!activeTriadBoardWorkSuspended)
            {
                activeTriadBoardWorkSuspended = true;
                SuspendBackgroundDeckWorkForActiveMatch();
            }
        }
        else
        {
            if (activeTriadBoardWorkSuspended)
            {
                activeTriadBoardWorkSuspended = false;
                FlushDeferredOptimizerProfileWrite();
            }
        }

        UpdateDeckOptimizerPause();

        if (currentNpc != null &&
            screenOb != null &&
            stateOb != null &&
            IsBlueTurnReadyForSolver(stateOb))
        {
            if (_evalTask != null)
            {
                if (!_evalTask.IsCompleted)
                {
                    return;
                }

                if (_evalTask.IsFaulted)
                {
                    Svc.Log.Error(_evalTask.Exception, "[Saucy] Live move solver task faulted");
                    _evalTask = null;
                    ClearMove();
                    return;
                }

                var (bestCardIdx, bestBoardPos, ev, evalMs) = _evalTask.Result;
                _evalTask = null;

                var hadMove = hasMove;
                var newCardIdx = bestCardIdx;
                var newBoardIdx = newCardIdx < 0 ? -1 : bestBoardPos;

                var forcedCardIdx = DebugScreenMemory.gameState?.forcedCardIdx ?? -1;
                if (forcedCardIdx >= 0 && newCardIdx != forcedCardIdx)
                {
                    newCardIdx = forcedCardIdx;
                }

                moveCardIdx = newCardIdx;
                moveBoardIdx = newBoardIdx;
                hasMove = moveCardIdx >= 0 && moveBoardIdx >= 0;
                if (hasMove && !hadMove)
                {
                    _moveReadyUtc = DateTime.UtcNow;
                }

                if (hasMove)
                {
                    if (CanReportNpcStatus)
                    {
                        UniversalTriadManager.SetStatus(
                            UniversalEngineStatus.MoveReady,
                            $"Ready [Blue]: Card #{moveCardIdx + 1} -> ({moveBoardIdx % 3}, {moveBoardIdx / 3}), EV: {ev:P0} ({evalMs}ms)");
                    }
                }
                else
                {
                    if (CanReportNpcStatus)
                    {
                        UniversalTriadManager.SetStatus(
                            UniversalEngineStatus.WaitingTurn,
                            "No legal moves available");
                    }
                }
                return;
            }

            if (!hasMove)
            {
                var updateFlags = DebugScreenMemory.OnNewScan(screenOb, currentNpc);
                if (updateFlags != TriadGameScreenMemory.EUpdateFlags.None)
                {
                    if (DebugScreenMemory.deckBlue != null &&
                        DebugScreenMemory.gameState != null &&
                        DebugScreenMemory.gameSolver != null)
                    {
                        StartSolverTask();
                    }
                    else
                    {
                        ClearMove();
                    }
                }
            }
        }
        else
        {
            if (hasMove && (stateOb == null || !TriadTurnState.CanBlueAct(stateOb.move, stateOb.isPlayerTurn)))
            {
                ClearMove();
            }

            if (stateOb != null && TriadUiState.IsBoardVisible() && !hasMove && _evalTask == null)
            {
                if (CanReportNpcStatus && UniversalTriadManager.Status != UniversalEngineStatus.Executing)
                {
                    UniversalTriadManager.SetStatus(
                        UniversalEngineStatus.WaitingTurn,
                        "Waiting for opponent (Red)...");
                }
            }
        }
    }

    public void ResetForNewMatch()
    {
        ClearMove();
        if (CanReportNpcStatus)
        {
            UniversalTriadManager.Reset();
        }
    }

    private static bool CanReportNpcStatus =>
        TriadRunSession.ModuleEnabled && !C.UniversalTriadAutoplayEnabled && !C.UniversalTriadTournamentEnabled;

    private void StartSolverTask()
    {
        _pauseOptimizerForSolver = true;
        UpdateDeckOptimizerPause();

        if (CanReportNpcStatus)
        {
            UniversalTriadManager.SetStatus(
                UniversalEngineStatus.Evaluating,
                "Thinking (Blue)...");
        }

        var snapshotState = new TriadGameSimulationState(DebugScreenMemory.gameState);
        var solver = DebugScreenMemory.gameSolver;

        _evalTask = Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            solver.FindNextMove(
                snapshotState,
                out var cardIdx,
                out var boardPos,
                out var solverResult);
            sw.Stop();
            return (cardIdx, boardPos, solverResult.winChance, (int)sw.ElapsedMilliseconds);
        });
    }

    private static bool IsBlueTurnReadyForSolver(UIStateTriadGame? stateOb) =>
        stateOb != null &&
        !stateOb.isPvP &&
        TriadTurnState.CanBlueAct(stateOb.move, stateOb.isPlayerTurn) &&
        !(TriadTurnState.IsBoardPickPhase(stateOb.move) && stateOb.turnBannerVisible);

    private void ClearMove()
    {
        _evalTask = null;
        hasMove = false;
        moveCardIdx = -1;
        moveBoardIdx = -1;
        _moveReadyUtc = null;
        _pauseOptimizerForSolver = false;
        UpdateDeckOptimizerPause();
    }

    internal void InvalidatePendingMoveCalc() => ClearMove();

    internal TriadNpc? ResolveNpcForGame(UIStateTriadGame? stateOb)
    {
        if (currentNpc != null)
        {
            lastGameNpc = currentNpc;
            return currentNpc;
        }

        if (stateOb != null)
        {
            var ctx = new GameUIParser();
            var fromGame = stateOb.ToTriadNpc(ctx);
            if (fromGame != null)
            {
                lastGameNpc = fromGame;
                currentNpc = fromGame;
                return fromGame;
            }
        }

        if (!string.IsNullOrEmpty(uiReaderPrep.cachedState.npc))
        {
            var ctx = new GameUIParser();
            var fromPrep = ctx.ParseNpc(uiReaderPrep.cachedState.npc, false) ??
                           ctx.ParseNpcNameStart(uiReaderPrep.cachedState.npc, false);
            if (fromPrep != null)
            {
                lastGameNpc = fromPrep;
                preGameNpc ??= fromPrep;
                return fromPrep;
            }
        }

        return null;
    }
}
